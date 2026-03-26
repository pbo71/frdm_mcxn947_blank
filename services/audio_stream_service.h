#ifndef AUDIO_STREAM_SERVICE_H
#define AUDIO_STREAM_SERVICE_H

#include <stdbool.h>
#include <stdint.h>

#include "services/audio_stream_format.h"

#if defined(__cplusplus)
extern "C" {
#endif

enum
{
    AUDIO_STREAM_SERVICE_STOP_REASON_TRANSPORT_RESET = 1U,
    AUDIO_STREAM_SERVICE_STOP_REASON_END_OF_STREAM = 2U,
    AUDIO_STREAM_SERVICE_STOP_REASON_CONTROL_PLANE = 3U,
};

enum
{
    AUDIO_STREAM_SERVICE_SOURCE_HOST_RX_LOOPBACK = 0U,
    AUDIO_STREAM_SERVICE_SOURCE_DEVICE_GENERATED_SINE = 1U,
    AUDIO_STREAM_SERVICE_SOURCE_DEVICE_GENERATED_CHIRP = 2U,
    AUDIO_STREAM_SERVICE_SOURCE_DEVICE_GENERATED_NOISE = 3U,
};

enum
{
    AUDIO_STREAM_SERVICE_NOISE_TYPE_WHITE = 0U,
    AUDIO_STREAM_SERVICE_NOISE_TYPE_SAMPLE_HOLD = 1U,
    AUDIO_STREAM_SERVICE_NOISE_TYPE_BINARY = 2U,
};

enum
{
    AUDIO_STREAM_SERVICE_AMPLITUDE_ENVELOPE_CONSTANT = 0U,
    AUDIO_STREAM_SERVICE_AMPLITUDE_ENVELOPE_FADE_IN = 1U,
    AUDIO_STREAM_SERVICE_AMPLITUDE_ENVELOPE_FADE_OUT = 2U,
    AUDIO_STREAM_SERVICE_AMPLITUDE_ENVELOPE_TRIANGLE = 3U,
};

void AudioStreamService_Init(void);
void AudioStreamService_OnTransportReset(void);
void AudioStreamService_OnTransportReady(uint16_t maxAudioPacketSize);
bool AudioStreamService_Start(uint32_t sampleRateHz, uint8_t channelCount, uint8_t bitsPerSample, uint8_t source);
bool AudioStreamService_Stop(uint32_t stopReason);
bool AudioStreamService_SetGeneratorConfig(uint8_t source,
                                           uint32_t primaryFrequencyHz,
                                           uint32_t secondaryFrequencyHz,
                                           uint32_t modulationPeriodMs,
                                           uint16_t amplitude,
                                           uint8_t noiseType,
                                           uint8_t amplitudeEnvelope,
                                           uint32_t noiseSeed);
bool AudioStreamService_HandleRxPacket(const uint8_t *rxBuffer,
                                       uint32_t rxLength,
                                       uint8_t *txBuffer,
                                       uint32_t txCapacity,
                                       uint32_t *txLength);
bool AudioStreamService_TryBuildGeneratedPacket(uint8_t *txBuffer,
                                                uint32_t txCapacity,
                                                uint32_t *txLength);

#if defined(__cplusplus)
}
#endif

#endif