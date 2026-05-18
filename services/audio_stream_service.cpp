#include "services/audio_stream_service.h"

#include <cmath>
#include <stddef.h>
#include <string.h>

extern "C" {
#include "services/audio_codec.h"
#include "services/audio_playback_buffer.h"
#include "services/control_plane_service.h"
}

namespace {

struct AudioStreamServiceState
{
    bool configured;
    bool streaming;
    bool generatedFirstPacketPending;
    uint16_t maxAudioPacketSize;
    uint16_t generatedPayloadBytes;
    uint32_t nextExpectedSequenceNumber;
    uint32_t generatedTimestamp;
    uint32_t sampleRateHz;
    float generatedPhase;
    float generatedPhaseStep;
    uint32_t generatedPrimaryFrequencyHz;
    uint32_t generatedSecondaryFrequencyHz;
    uint32_t generatedModulationPeriodMs;
    uint32_t generatedNoiseSeed;
    uint32_t generatedNoiseState;
    int32_t generatedHeldNoiseSample;
    uint16_t generatedAmplitude;
    uint8_t channelCount;
    uint8_t bitsPerSample;
    uint8_t source;
    uint8_t generatedNoiseType;
    uint8_t generatedAmplitudeEnvelope;
    uint32_t discontinuityCount;
};

AudioStreamServiceState g_audioStreamServiceState{};

constexpr uint32_t kGeneratedToneFrequencyHz = 1000U;
constexpr uint32_t kGeneratedChirpStartFrequencyHz = 500U;
constexpr uint32_t kGeneratedChirpEndFrequencyHz = 4000U;
constexpr uint32_t kGeneratedMaxPayloadBytes = 256U;
constexpr uint32_t kGeneratedNoiseSeed = 0x13579BDFU;
constexpr uint32_t kGeneratedDefaultModulationPeriodMs = 1000U;
constexpr uint32_t kGeneratedMinModulationSamples = 16U;
constexpr float kTwoPi = 6.28318530717958647692F;
constexpr uint16_t kGeneratedDefaultAmplitude = 12000U;
constexpr int32_t kGeneratedMaxSample24 = 0x7FFFFF;
constexpr int32_t kGeneratedMinSample24 = -0x800000;
constexpr uint32_t kAudioStreamFaultCodecEnable = 0xA001U;
constexpr uint32_t kAudioStreamFaultCodecRecover = 0xA002U;

uint32_t AudioStreamService_PackCodecEnableFaultDetail(status_t codecStatus)
{
    const uint32_t codecStep = AudioCodec_GetLastPlaybackEnableStep() & 0xFFFFU;
    const uint32_t codecDriverStatus = static_cast<uint32_t>(static_cast<uint16_t>(codecStatus));
    return (codecStep << 16) | codecDriverStatus;
}

uint32_t AudioStreamService_PackCodecRecoverFaultDetail(status_t recoverStatus)
{
    const uint32_t attempts = AudioCodec_GetLastRecoveryAttemptCount() & 0xFFFFU;
    const uint32_t codecDriverStatus = static_cast<uint32_t>(static_cast<uint16_t>(recoverStatus));
    return (attempts << 16) | codecDriverStatus;
}

bool AudioStreamService_IsSourceValid(uint8_t source)
{
    return (source == AUDIO_STREAM_SERVICE_SOURCE_HOST_RX_LOOPBACK) ||
           (source == AUDIO_STREAM_SERVICE_SOURCE_DEVICE_GENERATED_SINE) ||
           (source == AUDIO_STREAM_SERVICE_SOURCE_DEVICE_GENERATED_CHIRP) ||
           (source == AUDIO_STREAM_SERVICE_SOURCE_DEVICE_GENERATED_NOISE);
}

bool AudioStreamService_IsContainerBitsValid(uint8_t bitsPerSample)
{
    return (bitsPerSample == 16U) || (bitsPerSample == 32U);
}

bool AudioStreamService_IsFormatValid(uint32_t sampleRateHz,
                                      uint8_t channelCount,
                                      uint8_t bitsPerSample,
                                      uint8_t source)
{
    if ((sampleRateHz == 0U) || (channelCount == 0U) || (bitsPerSample == 0U) || !AudioStreamService_IsSourceValid(source))
    {
        return false;
    }

    if (source != AUDIO_STREAM_SERVICE_SOURCE_HOST_RX_LOOPBACK)
    {
        return AudioStreamService_IsContainerBitsValid(bitsPerSample) &&
               ((channelCount == 1U) || (channelCount == 2U));
    }

    return AudioStreamService_IsContainerBitsValid(bitsPerSample) &&
           ((channelCount == 1U) || (channelCount == 2U));
}

bool AudioStreamService_IsHeaderValid(const audio_stream_header_t &header, uint32_t rxLength)
{
    const uint16_t bytesPerFrame = static_cast<uint16_t>((header.bitsPerSample / 8U) * header.channelCount);

    if (header.magic != AUDIO_STREAM_HEADER_MAGIC)
    {
        return false;
    }

    if (header.version != AUDIO_STREAM_HEADER_VERSION)
    {
        return false;
    }

    if (header.headerSize != sizeof(audio_stream_header_t))
    {
        return false;
    }

    if (header.sampleRateHz == 0U)
    {
        return false;
    }

    if ((header.channelCount == 0U) || (header.bitsPerSample == 0U))
    {
        return false;
    }

    if (!AudioStreamService_IsContainerBitsValid(header.bitsPerSample))
    {
        return false;
    }

    if ((header.channelCount > 2U) || (bytesPerFrame == 0U) || ((header.payloadBytes % bytesPerFrame) != 0U))
    {
        return false;
    }

    return (static_cast<uint32_t>(header.headerSize) + header.payloadBytes) == rxLength;
}

bool AudioStreamService_IsFormatChanged(const audio_stream_header_t &header)
{
    return (g_audioStreamServiceState.sampleRateHz != header.sampleRateHz) ||
           (g_audioStreamServiceState.channelCount != header.channelCount) ||
           (g_audioStreamServiceState.bitsPerSample != header.bitsPerSample);
}

void AudioStreamService_UpdateFormat(const audio_stream_header_t &header)
{
    g_audioStreamServiceState.sampleRateHz = header.sampleRateHz;
    g_audioStreamServiceState.channelCount = header.channelCount;
    g_audioStreamServiceState.bitsPerSample = header.bitsPerSample;
}

uint16_t AudioStreamService_GetBytesPerFrame(void)
{
    return static_cast<uint16_t>((g_audioStreamServiceState.bitsPerSample / 8U) * g_audioStreamServiceState.channelCount);
}

uint16_t AudioStreamService_GetGeneratedPayloadBytes(void)
{
    uint32_t payloadCapacity;
    uint16_t bytesPerFrame;
    uint32_t payloadBytes;

    if (g_audioStreamServiceState.maxAudioPacketSize <= sizeof(audio_stream_header_t))
    {
        return 0U;
    }

    bytesPerFrame = AudioStreamService_GetBytesPerFrame();
    if (bytesPerFrame == 0U)
    {
        return 0U;
    }

    payloadCapacity = g_audioStreamServiceState.maxAudioPacketSize - sizeof(audio_stream_header_t);
    payloadBytes = (payloadCapacity > kGeneratedMaxPayloadBytes) ? kGeneratedMaxPayloadBytes : payloadCapacity;
    payloadBytes -= (payloadBytes % bytesPerFrame);

    return static_cast<uint16_t>(payloadBytes);
}

void AudioStreamService_ResetGeneratedState(void)
{
    g_audioStreamServiceState.generatedFirstPacketPending = false;
    g_audioStreamServiceState.generatedPayloadBytes = 0U;
    g_audioStreamServiceState.generatedTimestamp = 0U;
    g_audioStreamServiceState.generatedPhase = 0.0F;
    g_audioStreamServiceState.generatedPhaseStep = 0.0F;
    g_audioStreamServiceState.generatedHeldNoiseSample = 0;
}

uint32_t AudioStreamService_GetModulationPeriodSamples(void)
{
    uint64_t periodSamples;

    if (g_audioStreamServiceState.sampleRateHz == 0U)
    {
        return kGeneratedMinModulationSamples;
    }

    if (g_audioStreamServiceState.generatedModulationPeriodMs == 0U)
    {
        return g_audioStreamServiceState.sampleRateHz;
    }

    periodSamples = (static_cast<uint64_t>(g_audioStreamServiceState.sampleRateHz) *
                     static_cast<uint64_t>(g_audioStreamServiceState.generatedModulationPeriodMs)) /
                    1000ULL;

    if (periodSamples < kGeneratedMinModulationSamples)
    {
        periodSamples = kGeneratedMinModulationSamples;
    }

    if (periodSamples > 0xFFFFFFFFULL)
    {
        periodSamples = 0xFFFFFFFFULL;
    }

    return static_cast<uint32_t>(periodSamples);
}

float AudioStreamService_ComputePhaseStep(uint32_t frequencyHz)
{
    if ((frequencyHz == 0U) || (g_audioStreamServiceState.sampleRateHz == 0U))
    {
        return 0.0F;
    }

    return kTwoPi * (static_cast<float>(frequencyHz) / static_cast<float>(g_audioStreamServiceState.sampleRateHz));
}

void AudioStreamService_ConfigureGeneratedState(void)
{
    g_audioStreamServiceState.generatedFirstPacketPending = true;
    g_audioStreamServiceState.generatedPayloadBytes = AudioStreamService_GetGeneratedPayloadBytes();
    g_audioStreamServiceState.generatedTimestamp = 0U;
    g_audioStreamServiceState.generatedPhase = 0.0F;
    g_audioStreamServiceState.generatedPhaseStep = AudioStreamService_ComputePhaseStep(
        g_audioStreamServiceState.generatedPrimaryFrequencyHz);
    g_audioStreamServiceState.generatedNoiseState = g_audioStreamServiceState.generatedNoiseSeed;
    g_audioStreamServiceState.generatedHeldNoiseSample = 0;
}

bool AudioStreamService_IsGeneratorSource(uint8_t source)
{
    return source == AUDIO_STREAM_SERVICE_SOURCE_DEVICE_GENERATED_SINE ||
           source == AUDIO_STREAM_SERVICE_SOURCE_DEVICE_GENERATED_CHIRP ||
           source == AUDIO_STREAM_SERVICE_SOURCE_DEVICE_GENERATED_NOISE;
}

bool AudioStreamService_IsGeneratorConfigValid(uint8_t source,
                                               uint32_t primaryFrequencyHz,
                                               uint32_t secondaryFrequencyHz,
                                               uint32_t modulationPeriodMs,
                                               uint16_t amplitude,
                                               uint8_t noiseType,
                                               uint8_t amplitudeEnvelope,
                                               uint32_t noiseSeed)
{
    (void)noiseSeed;

    if (!AudioStreamService_IsGeneratorSource(source))
    {
        return false;
    }

    if ((amplitude == 0U) || (amplitude > 32767U))
    {
        return false;
    }

    if (modulationPeriodMs > 60000U)
    {
        return false;
    }

    if (noiseType > AUDIO_STREAM_SERVICE_NOISE_TYPE_BINARY)
    {
        return false;
    }

    if (amplitudeEnvelope > AUDIO_STREAM_SERVICE_AMPLITUDE_ENVELOPE_TRIANGLE)
    {
        return false;
    }

    switch (source)
    {
        case AUDIO_STREAM_SERVICE_SOURCE_DEVICE_GENERATED_SINE:
            return primaryFrequencyHz > 0U;

        case AUDIO_STREAM_SERVICE_SOURCE_DEVICE_GENERATED_CHIRP:
            return (primaryFrequencyHz > 0U) && (secondaryFrequencyHz > 0U);

        case AUDIO_STREAM_SERVICE_SOURCE_DEVICE_GENERATED_NOISE:
            return true;

        default:
            return false;
    }
}

float AudioStreamService_GetEnvelopeScale(uint32_t sampleIndex)
{
    const uint32_t periodSamples = AudioStreamService_GetModulationPeriodSamples();
    const uint32_t periodPosition = (periodSamples == 0U) ? 0U : (sampleIndex % periodSamples);
    const float ratio = (periodSamples <= 1U) ? 1.0F : (static_cast<float>(periodPosition) / static_cast<float>(periodSamples - 1U));

    switch (g_audioStreamServiceState.generatedAmplitudeEnvelope)
    {
        case AUDIO_STREAM_SERVICE_AMPLITUDE_ENVELOPE_FADE_IN:
            return ratio;

        case AUDIO_STREAM_SERVICE_AMPLITUDE_ENVELOPE_FADE_OUT:
            return 1.0F - ratio;

        case AUDIO_STREAM_SERVICE_AMPLITUDE_ENVELOPE_TRIANGLE:
            return (ratio <= 0.5F) ? (ratio * 2.0F) : ((1.0F - ratio) * 2.0F);

        case AUDIO_STREAM_SERVICE_AMPLITUDE_ENVELOPE_CONSTANT:
        default:
            return 1.0F;
    }
}

int32_t AudioStreamService_ClampGeneratedSample24(int32_t sampleValue)
{
    if (sampleValue > kGeneratedMaxSample24)
    {
        return kGeneratedMaxSample24;
    }

    if (sampleValue < kGeneratedMinSample24)
    {
        return kGeneratedMinSample24;
    }

    return sampleValue;
}

int32_t AudioStreamService_PackSample24ToMsbAligned32(int32_t sampleValue)
{
    return AudioStreamService_ClampGeneratedSample24(sampleValue) << 8U;
}

int32_t AudioStreamService_GetScaledAmplitude24(void)
{
    return static_cast<int32_t>((static_cast<int64_t>(g_audioStreamServiceState.generatedAmplitude) *
                                 static_cast<int64_t>(kGeneratedMaxSample24)) /
                                32767LL);
}

int32_t AudioStreamService_ScaleGeneratedSample(int32_t sampleValue, uint32_t sampleIndex)
{
    const float scale = AudioStreamService_GetEnvelopeScale(sampleIndex);
    return AudioStreamService_ClampGeneratedSample24(static_cast<int32_t>(static_cast<float>(sampleValue) * scale));
}

int32_t AudioStreamService_GenerateSineSample(void)
{
    const uint32_t sampleIndex = g_audioStreamServiceState.generatedTimestamp;
    const int32_t amplitude24 = AudioStreamService_GetScaledAmplitude24();
    const int32_t sampleValue = static_cast<int32_t>(sinf(g_audioStreamServiceState.generatedPhase) * static_cast<float>(amplitude24));

    g_audioStreamServiceState.generatedPhase += g_audioStreamServiceState.generatedPhaseStep;
    if (g_audioStreamServiceState.generatedPhase >= kTwoPi)
    {
        g_audioStreamServiceState.generatedPhase -= kTwoPi;
    }
    return AudioStreamService_ScaleGeneratedSample(sampleValue, sampleIndex);
}

int32_t AudioStreamService_GenerateChirpSample(void)
{
    const uint32_t sampleIndex = g_audioStreamServiceState.generatedTimestamp;
    const uint32_t sweepSamples = AudioStreamService_GetModulationPeriodSamples();
    const uint32_t sweepPosition = g_audioStreamServiceState.generatedTimestamp % sweepSamples;
    const float ratio = (sweepSamples <= 1U) ? 1.0F : (static_cast<float>(sweepPosition) / static_cast<float>(sweepSamples - 1U));
    const float currentFrequency = static_cast<float>(g_audioStreamServiceState.generatedPrimaryFrequencyHz) +
                                   (static_cast<float>(g_audioStreamServiceState.generatedSecondaryFrequencyHz) -
                                    static_cast<float>(g_audioStreamServiceState.generatedPrimaryFrequencyHz)) * ratio;
    const int32_t amplitude24 = AudioStreamService_GetScaledAmplitude24();
    const int32_t sampleValue = static_cast<int32_t>(sinf(g_audioStreamServiceState.generatedPhase) * static_cast<float>(amplitude24));

    g_audioStreamServiceState.generatedPhaseStep = AudioStreamService_ComputePhaseStep(static_cast<uint32_t>(currentFrequency));
    g_audioStreamServiceState.generatedPhase += g_audioStreamServiceState.generatedPhaseStep;
    if (g_audioStreamServiceState.generatedPhase >= kTwoPi)
    {
        g_audioStreamServiceState.generatedPhase -= kTwoPi;
    }
    return AudioStreamService_ScaleGeneratedSample(sampleValue, sampleIndex);
}

int32_t AudioStreamService_GenerateWhiteNoiseSample(void)
{
    const int32_t centered = static_cast<int32_t>((g_audioStreamServiceState.generatedNoiseState >> 8U) & 0x00FFFFFFU) - 0x00800000;
    const int32_t amplitude24 = AudioStreamService_GetScaledAmplitude24();
    return static_cast<int32_t>((static_cast<int64_t>(centered) * static_cast<int64_t>(amplitude24)) /
                                static_cast<int64_t>(kGeneratedMaxSample24));
}

int32_t AudioStreamService_GenerateNoiseSample(void)
{
    const uint32_t sampleIndex = g_audioStreamServiceState.generatedTimestamp;
    const uint32_t holdSamples = AudioStreamService_GetModulationPeriodSamples();
    const int32_t amplitude24 = AudioStreamService_GetScaledAmplitude24();

    g_audioStreamServiceState.generatedNoiseState = (g_audioStreamServiceState.generatedNoiseState * 1664525U) + 1013904223U;

    int32_t sampleValue;

    switch (g_audioStreamServiceState.generatedNoiseType)
    {
        case AUDIO_STREAM_SERVICE_NOISE_TYPE_SAMPLE_HOLD:
            if ((sampleIndex % holdSamples) == 0U)
            {
                g_audioStreamServiceState.generatedHeldNoiseSample = AudioStreamService_GenerateWhiteNoiseSample();
            }
            sampleValue = g_audioStreamServiceState.generatedHeldNoiseSample;
            break;

        case AUDIO_STREAM_SERVICE_NOISE_TYPE_BINARY:
            sampleValue = ((g_audioStreamServiceState.generatedNoiseState & 0x80000000U) != 0U) ?
                              amplitude24 :
                              -amplitude24;
            break;

        case AUDIO_STREAM_SERVICE_NOISE_TYPE_WHITE:
        default:
            sampleValue = AudioStreamService_GenerateWhiteNoiseSample();
            break;
    }

    return AudioStreamService_ScaleGeneratedSample(sampleValue, sampleIndex);
}

int32_t AudioStreamService_GenerateSample(void)
{
    switch (g_audioStreamServiceState.source)
    {
        case AUDIO_STREAM_SERVICE_SOURCE_DEVICE_GENERATED_SINE:
            return AudioStreamService_GenerateSineSample();

        case AUDIO_STREAM_SERVICE_SOURCE_DEVICE_GENERATED_CHIRP:
            return AudioStreamService_GenerateChirpSample();

        case AUDIO_STREAM_SERVICE_SOURCE_DEVICE_GENERATED_NOISE:
            return AudioStreamService_GenerateNoiseSample();

        default:
            return 0;
    }
}

}  // namespace

extern "C" void AudioStreamService_Init(void)
{
    AudioPlaybackBuffer_Reset();
    g_audioStreamServiceState.configured = false;
    g_audioStreamServiceState.streaming = false;
    g_audioStreamServiceState.maxAudioPacketSize = 0U;
    g_audioStreamServiceState.nextExpectedSequenceNumber = 0U;
    g_audioStreamServiceState.sampleRateHz = 0U;
    g_audioStreamServiceState.channelCount = 0U;
    g_audioStreamServiceState.bitsPerSample = 0U;
    g_audioStreamServiceState.source = AUDIO_STREAM_SERVICE_SOURCE_HOST_RX_LOOPBACK;
    g_audioStreamServiceState.generatedPrimaryFrequencyHz = kGeneratedToneFrequencyHz;
    g_audioStreamServiceState.generatedSecondaryFrequencyHz = kGeneratedChirpEndFrequencyHz;
    g_audioStreamServiceState.generatedModulationPeriodMs = kGeneratedDefaultModulationPeriodMs;
    g_audioStreamServiceState.generatedAmplitude = kGeneratedDefaultAmplitude;
    g_audioStreamServiceState.generatedNoiseSeed = kGeneratedNoiseSeed;
    g_audioStreamServiceState.generatedNoiseState = kGeneratedNoiseSeed;
    g_audioStreamServiceState.generatedHeldNoiseSample = 0;
    g_audioStreamServiceState.generatedNoiseType = AUDIO_STREAM_SERVICE_NOISE_TYPE_WHITE;
    g_audioStreamServiceState.generatedAmplitudeEnvelope = AUDIO_STREAM_SERVICE_AMPLITUDE_ENVELOPE_CONSTANT;
    g_audioStreamServiceState.discontinuityCount = 0U;
    AudioStreamService_ResetGeneratedState();
}

extern "C" void AudioStreamService_OnTransportReset(void)
{
    AudioPlaybackBuffer_Reset();
    if (g_audioStreamServiceState.streaming)
    {
        ControlPlaneService_NotifyStreamStopped(AUDIO_STREAM_SERVICE_STOP_REASON_TRANSPORT_RESET);
    }

    g_audioStreamServiceState.configured = false;
    g_audioStreamServiceState.streaming = false;
    g_audioStreamServiceState.maxAudioPacketSize = 0U;
    g_audioStreamServiceState.nextExpectedSequenceNumber = 0U;
    g_audioStreamServiceState.sampleRateHz = 0U;
    g_audioStreamServiceState.channelCount = 0U;
    g_audioStreamServiceState.bitsPerSample = 0U;
    g_audioStreamServiceState.source = AUDIO_STREAM_SERVICE_SOURCE_HOST_RX_LOOPBACK;
    g_audioStreamServiceState.generatedPrimaryFrequencyHz = kGeneratedToneFrequencyHz;
    g_audioStreamServiceState.generatedSecondaryFrequencyHz = kGeneratedChirpEndFrequencyHz;
    g_audioStreamServiceState.generatedModulationPeriodMs = kGeneratedDefaultModulationPeriodMs;
    g_audioStreamServiceState.generatedAmplitude = kGeneratedDefaultAmplitude;
    g_audioStreamServiceState.generatedNoiseSeed = kGeneratedNoiseSeed;
    g_audioStreamServiceState.generatedNoiseState = kGeneratedNoiseSeed;
    g_audioStreamServiceState.generatedHeldNoiseSample = 0;
    g_audioStreamServiceState.generatedNoiseType = AUDIO_STREAM_SERVICE_NOISE_TYPE_WHITE;
    g_audioStreamServiceState.generatedAmplitudeEnvelope = AUDIO_STREAM_SERVICE_AMPLITUDE_ENVELOPE_CONSTANT;
    g_audioStreamServiceState.discontinuityCount = 0U;
    AudioStreamService_ResetGeneratedState();
}

extern "C" void AudioStreamService_OnTransportReady(uint16_t maxAudioPacketSize)
{
    AudioPlaybackBuffer_Reset();
    g_audioStreamServiceState.configured = true;
    g_audioStreamServiceState.streaming = false;
    g_audioStreamServiceState.maxAudioPacketSize = maxAudioPacketSize;
    g_audioStreamServiceState.nextExpectedSequenceNumber = 0U;
    g_audioStreamServiceState.sampleRateHz = 0U;
    g_audioStreamServiceState.channelCount = 0U;
    g_audioStreamServiceState.bitsPerSample = 0U;
    g_audioStreamServiceState.source = AUDIO_STREAM_SERVICE_SOURCE_HOST_RX_LOOPBACK;
    g_audioStreamServiceState.generatedNoiseSeed = kGeneratedNoiseSeed;
    g_audioStreamServiceState.generatedNoiseState = kGeneratedNoiseSeed;
    g_audioStreamServiceState.discontinuityCount = 0U;
    AudioStreamService_ResetGeneratedState();
}

extern "C" bool AudioStreamService_Start(uint32_t sampleRateHz,
                                           uint8_t channelCount,
                                           uint8_t bitsPerSample,
                                           uint8_t source)
{
    if (!g_audioStreamServiceState.configured)
    {
        return false;
    }

    if (!AudioStreamService_IsFormatValid(sampleRateHz, channelCount, bitsPerSample, source))
    {
        return false;
    }

    if (!AudioPlaybackBuffer_Configure(sampleRateHz, channelCount, bitsPerSample))
    {
        return false;
    }

    const status_t codecStatus = AudioCodec_EnablePlaybackDigitalPath();
    if (codecStatus != kStatus_Success)
    {
        ControlPlaneService_NotifyFault(kAudioStreamFaultCodecEnable,
                                        AudioStreamService_PackCodecEnableFaultDetail(codecStatus));
    }

    g_audioStreamServiceState.streaming = true;
    g_audioStreamServiceState.nextExpectedSequenceNumber = 0U;
    g_audioStreamServiceState.sampleRateHz = sampleRateHz;
    g_audioStreamServiceState.channelCount = channelCount;
    g_audioStreamServiceState.bitsPerSample = bitsPerSample;
    g_audioStreamServiceState.source = source;
    g_audioStreamServiceState.discontinuityCount = 0U;
    AudioStreamService_ResetGeneratedState();

    if (AudioStreamService_IsGeneratorSource(source))
    {
        AudioStreamService_ConfigureGeneratedState();
        if (g_audioStreamServiceState.generatedPayloadBytes == 0U)
        {
            g_audioStreamServiceState.streaming = false;
            g_audioStreamServiceState.source = AUDIO_STREAM_SERVICE_SOURCE_HOST_RX_LOOPBACK;
            return false;
        }
    }

    ControlPlaneService_NotifyStreamStarted(sampleRateHz, channelCount, bitsPerSample);
    return true;
}

extern "C" bool AudioStreamService_Stop(uint32_t stopReason)
{
    if (!g_audioStreamServiceState.streaming)
    {
        return false;
    }

    AudioPlaybackBuffer_ClearData();
    const status_t codecRecoverStatus = AudioCodec_DisablePlaybackDigitalPath();
    if (codecRecoverStatus != kStatus_Success)
    {
        ControlPlaneService_NotifyFault(kAudioStreamFaultCodecRecover,
                                        AudioStreamService_PackCodecRecoverFaultDetail(codecRecoverStatus));
    }
    g_audioStreamServiceState.streaming = false;
    g_audioStreamServiceState.nextExpectedSequenceNumber = 0U;
    g_audioStreamServiceState.source = AUDIO_STREAM_SERVICE_SOURCE_HOST_RX_LOOPBACK;
    AudioStreamService_ResetGeneratedState();
    ControlPlaneService_NotifyStreamStopped(stopReason);
    return true;
}

extern "C" bool AudioStreamService_SetGeneratorConfig(uint8_t source,
                                                        uint32_t primaryFrequencyHz,
                                                        uint32_t secondaryFrequencyHz,
                                                        uint32_t modulationPeriodMs,
                                                        uint16_t amplitude,
                                                        uint8_t noiseType,
                                                        uint8_t amplitudeEnvelope,
                                                        uint32_t noiseSeed)
{
    if (!AudioStreamService_IsGeneratorConfigValid(source,
                                                   primaryFrequencyHz,
                                                   secondaryFrequencyHz,
                                                   modulationPeriodMs,
                                                   amplitude,
                                                   noiseType,
                                                   amplitudeEnvelope,
                                                   noiseSeed))
    {
        return false;
    }

    g_audioStreamServiceState.generatedPrimaryFrequencyHz = primaryFrequencyHz;
    g_audioStreamServiceState.generatedSecondaryFrequencyHz = (secondaryFrequencyHz == 0U) ? primaryFrequencyHz : secondaryFrequencyHz;
    g_audioStreamServiceState.generatedModulationPeriodMs =
        (modulationPeriodMs == 0U) ? kGeneratedDefaultModulationPeriodMs : modulationPeriodMs;
    g_audioStreamServiceState.generatedAmplitude = amplitude;
    g_audioStreamServiceState.generatedNoiseType = noiseType;
    g_audioStreamServiceState.generatedAmplitudeEnvelope = amplitudeEnvelope;
    g_audioStreamServiceState.generatedNoiseSeed = noiseSeed;

    if (g_audioStreamServiceState.streaming && (g_audioStreamServiceState.source == source))
    {
        AudioStreamService_ConfigureGeneratedState();
    }

    return true;
}

extern "C" bool AudioStreamService_HandleRxPacket(const uint8_t *rxBuffer,
                                                    uint32_t rxLength,
                                                    uint8_t *txBuffer,
                                                    uint32_t txCapacity,
                                                    uint32_t *txLength)
{
    audio_stream_header_t header{};
    audio_stream_header_t txHeader{};
    bool wasStreaming;
    bool formatChanged = false;

    if ((rxBuffer == nullptr) || (txBuffer == nullptr) || (txLength == nullptr))
    {
        return false;
    }

    *txLength = 0U;

    if (!g_audioStreamServiceState.configured || !g_audioStreamServiceState.streaming ||
        (g_audioStreamServiceState.source != AUDIO_STREAM_SERVICE_SOURCE_HOST_RX_LOOPBACK))
    {
        return false;
    }

    if ((rxLength < sizeof(audio_stream_header_t)) ||
        (rxLength > txCapacity) ||
        (rxLength > g_audioStreamServiceState.maxAudioPacketSize))
    {
        return false;
    }

    memcpy(&header, rxBuffer, sizeof(header));
    if (!AudioStreamService_IsHeaderValid(header, rxLength))
    {
        return false;
    }

    wasStreaming = g_audioStreamServiceState.streaming;

    if ((header.sampleRateHz != g_audioStreamServiceState.sampleRateHz) ||
        (header.channelCount != g_audioStreamServiceState.channelCount) ||
        (header.bitsPerSample != g_audioStreamServiceState.bitsPerSample))
    {
        return false;
    }

    if (AudioStreamService_IsFormatChanged(header) ||
        ((header.flags & AUDIO_STREAM_FLAG_FORMAT_CHANGE) != 0U))
    {
        formatChanged = true;
        AudioStreamService_UpdateFormat(header);
    }

    txHeader = header;

    if (formatChanged)
    {
        txHeader.flags |= AUDIO_STREAM_FLAG_FORMAT_CHANGE;
    }

    if (wasStreaming && (header.sequenceNumber != g_audioStreamServiceState.nextExpectedSequenceNumber))
    {
        txHeader.flags |= AUDIO_STREAM_FLAG_DISCONTINUITY;
        g_audioStreamServiceState.discontinuityCount += 1U;
    }

    g_audioStreamServiceState.nextExpectedSequenceNumber = header.sequenceNumber + 1U;

    (void)AudioPlaybackBuffer_Write(rxBuffer + sizeof(header), header.payloadBytes);

    memcpy(txBuffer, &txHeader, sizeof(txHeader));
    memcpy(txBuffer + sizeof(txHeader), rxBuffer + sizeof(txHeader), header.payloadBytes);
    *txLength = rxLength;

    if ((header.flags & AUDIO_STREAM_FLAG_END_OF_STREAM) != 0U)
    {
        (void)AudioStreamService_Stop(AUDIO_STREAM_SERVICE_STOP_REASON_END_OF_STREAM);
    }

    return true;
}

extern "C" uint32_t AudioStreamService_GetDiscontinuityCount(void)
{
    return g_audioStreamServiceState.discontinuityCount;
}

extern "C" bool AudioStreamService_TryBuildGeneratedPacket(uint8_t *txBuffer,
                                                             uint32_t txCapacity,
                                                             uint32_t *txLength)
{
    audio_stream_header_t header{};
    uint16_t payloadBytes;
    uint16_t bytesPerFrame;
    uint32_t frameCount;
    uint32_t packetTimestamp;
    uint8_t *payloadBuffer;

    if ((txBuffer == nullptr) || (txLength == nullptr))
    {
        return false;
    }

    *txLength = 0U;

    if (!g_audioStreamServiceState.configured || !g_audioStreamServiceState.streaming ||
        !AudioStreamService_IsGeneratorSource(g_audioStreamServiceState.source))
    {
        return false;
    }

    payloadBytes = g_audioStreamServiceState.generatedPayloadBytes;
    bytesPerFrame = AudioStreamService_GetBytesPerFrame();
    if ((payloadBytes == 0U) || (bytesPerFrame == 0U) || (txCapacity < (sizeof(audio_stream_header_t) + payloadBytes)))
    {
        return false;
    }

    header.magic = AUDIO_STREAM_HEADER_MAGIC;
    header.version = AUDIO_STREAM_HEADER_VERSION;
    header.headerSize = sizeof(audio_stream_header_t);
    header.flags = g_audioStreamServiceState.generatedFirstPacketPending ? AUDIO_STREAM_FLAG_START_OF_STREAM : 0U;
    header.reserved0 = 0U;
    header.sequenceNumber = g_audioStreamServiceState.nextExpectedSequenceNumber;
    packetTimestamp = g_audioStreamServiceState.generatedTimestamp;
    header.timestamp = packetTimestamp;
    header.payloadBytes = payloadBytes;
    header.channelCount = g_audioStreamServiceState.channelCount;
    header.bitsPerSample = g_audioStreamServiceState.bitsPerSample;
    header.sampleRateHz = g_audioStreamServiceState.sampleRateHz;

    memcpy(txBuffer, &header, sizeof(header));
    payloadBuffer = txBuffer + sizeof(header);
    frameCount = payloadBytes / bytesPerFrame;

    for (uint32_t frameIndex = 0U; frameIndex < frameCount; ++frameIndex)
    {
        const int32_t sampleValue = AudioStreamService_GenerateSample();

        for (uint8_t channelIndex = 0U; channelIndex < g_audioStreamServiceState.channelCount; ++channelIndex)
        {
            const uint32_t sampleOffset = ((frameIndex * g_audioStreamServiceState.channelCount) + channelIndex) *
                                          (g_audioStreamServiceState.bitsPerSample / 8U);

            if (g_audioStreamServiceState.bitsPerSample == 16U)
            {
                const int16_t sample16 = static_cast<int16_t>(sampleValue >> 8U);
                memcpy(payloadBuffer + sampleOffset, &sample16, sizeof(sample16));
            }
            else
            {
                const int32_t sample32 = AudioStreamService_PackSample24ToMsbAligned32(sampleValue);
                memcpy(payloadBuffer + sampleOffset, &sample32, sizeof(sample32));
            }
        }

        g_audioStreamServiceState.generatedTimestamp += 1U;
    }

    g_audioStreamServiceState.generatedFirstPacketPending = false;
    g_audioStreamServiceState.nextExpectedSequenceNumber += 1U;

    if (AudioPlaybackBuffer_IsConfigured())
    {
        (void)AudioPlaybackBuffer_Write(payloadBuffer, payloadBytes);
    }

    *txLength = sizeof(audio_stream_header_t) + payloadBytes;

    return true;
}