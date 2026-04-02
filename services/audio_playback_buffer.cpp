#include "services/audio_playback_buffer.h"

#include <string.h>

namespace {

struct AudioPlaybackBufferState
{
    bool configured;
    uint32_t sampleRateHz;
    uint32_t writeIndex;
    uint32_t readIndex;
    uint32_t fillLevelBytes;
    uint32_t minFillLevelBytes;
    uint32_t maxFillLevelBytes;
    uint32_t droppedBytes;
    uint32_t underrunCount;
    uint32_t overrunCount;
    uint32_t bytesPerFrame;
    uint8_t channelCount;
    uint8_t bitsPerSample;
};

AudioPlaybackBufferState g_audioPlaybackBufferState{};

constexpr uint32_t kPlaybackBufferCapacityBytes = 64U * 1024U;
constexpr uint32_t kPlaybackStartThresholdBytes = kPlaybackBufferCapacityBytes / 2U;

uint8_t g_audioPlaybackBuffer[kPlaybackBufferCapacityBytes]{};

uint32_t AudioPlaybackBuffer_TrimToWholeFrames(uint32_t length)
{
    if (g_audioPlaybackBufferState.bytesPerFrame == 0U)
    {
        return 0U;
    }

    return length - (length % g_audioPlaybackBufferState.bytesPerFrame);
}

void AudioPlaybackBuffer_UpdateFillExtrema(void)
{
    if (g_audioPlaybackBufferState.fillLevelBytes < g_audioPlaybackBufferState.minFillLevelBytes)
    {
        g_audioPlaybackBufferState.minFillLevelBytes = g_audioPlaybackBufferState.fillLevelBytes;
    }

    if (g_audioPlaybackBufferState.fillLevelBytes > g_audioPlaybackBufferState.maxFillLevelBytes)
    {
        g_audioPlaybackBufferState.maxFillLevelBytes = g_audioPlaybackBufferState.fillLevelBytes;
    }
}

}

extern "C" void AudioPlaybackBuffer_Init(void)
{
    g_audioPlaybackBufferState = {};
}

extern "C" void AudioPlaybackBuffer_Reset(void)
{
    g_audioPlaybackBufferState.writeIndex = 0U;
    g_audioPlaybackBufferState.readIndex = 0U;
    g_audioPlaybackBufferState.fillLevelBytes = 0U;
    g_audioPlaybackBufferState.minFillLevelBytes = 0U;
    g_audioPlaybackBufferState.maxFillLevelBytes = 0U;
    g_audioPlaybackBufferState.droppedBytes = 0U;
    g_audioPlaybackBufferState.underrunCount = 0U;
    g_audioPlaybackBufferState.overrunCount = 0U;
}

extern "C" bool AudioPlaybackBuffer_Configure(uint32_t sampleRateHz, uint8_t channelCount, uint8_t bitsPerSample)
{
    if ((sampleRateHz == 0U) || (channelCount == 0U) || (channelCount > 2U) || (bitsPerSample == 0U) || ((bitsPerSample % 8U) != 0U))
    {
        g_audioPlaybackBufferState.configured = false;
        g_audioPlaybackBufferState.sampleRateHz = 0U;
        g_audioPlaybackBufferState.channelCount = 0U;
        g_audioPlaybackBufferState.bitsPerSample = 0U;
        g_audioPlaybackBufferState.bytesPerFrame = 0U;
        AudioPlaybackBuffer_Reset();
        return false;
    }

    g_audioPlaybackBufferState.configured = true;
    g_audioPlaybackBufferState.sampleRateHz = sampleRateHz;
    g_audioPlaybackBufferState.channelCount = channelCount;
    g_audioPlaybackBufferState.bitsPerSample = bitsPerSample;
    g_audioPlaybackBufferState.bytesPerFrame = static_cast<uint32_t>((bitsPerSample / 8U) * channelCount);
    AudioPlaybackBuffer_Reset();
    return true;
}

extern "C" bool AudioPlaybackBuffer_IsConfigured(void)
{
    return g_audioPlaybackBufferState.configured;
}

extern "C" uint32_t AudioPlaybackBuffer_GetSampleRateHz(void)
{
    return g_audioPlaybackBufferState.sampleRateHz;
}

extern "C" uint32_t AudioPlaybackBuffer_GetBytesPerFrame(void)
{
    return g_audioPlaybackBufferState.bytesPerFrame;
}

extern "C" uint32_t AudioPlaybackBuffer_Write(const uint8_t *data, uint32_t length)
{
    uint32_t writableBytes;
    uint32_t firstChunkBytes;
    uint32_t secondChunkBytes;

    if ((data == nullptr) || !g_audioPlaybackBufferState.configured)
    {
        return 0U;
    }

    length = AudioPlaybackBuffer_TrimToWholeFrames(length);
    if (length == 0U)
    {
        return 0U;
    }

    writableBytes = kPlaybackBufferCapacityBytes - g_audioPlaybackBufferState.fillLevelBytes;
    writableBytes = AudioPlaybackBuffer_TrimToWholeFrames((length < writableBytes) ? length : writableBytes);

    if (writableBytes < length)
    {
        g_audioPlaybackBufferState.overrunCount += 1U;
        g_audioPlaybackBufferState.droppedBytes += (length - writableBytes);
    }

    if (writableBytes == 0U)
    {
        return 0U;
    }

    firstChunkBytes = kPlaybackBufferCapacityBytes - g_audioPlaybackBufferState.writeIndex;
    if (firstChunkBytes > writableBytes)
    {
        firstChunkBytes = writableBytes;
    }
    secondChunkBytes = writableBytes - firstChunkBytes;

    memcpy(&g_audioPlaybackBuffer[g_audioPlaybackBufferState.writeIndex], data, firstChunkBytes);
    if (secondChunkBytes > 0U)
    {
        memcpy(g_audioPlaybackBuffer, data + firstChunkBytes, secondChunkBytes);
    }

    g_audioPlaybackBufferState.writeIndex = (g_audioPlaybackBufferState.writeIndex + writableBytes) % kPlaybackBufferCapacityBytes;
    g_audioPlaybackBufferState.fillLevelBytes += writableBytes;
    AudioPlaybackBuffer_UpdateFillExtrema();

    return writableBytes;
}

extern "C" uint32_t AudioPlaybackBuffer_Read(uint8_t *data, uint32_t capacity)
{
    uint32_t readableBytes;
    uint32_t firstChunkBytes;
    uint32_t secondChunkBytes;

    if ((data == nullptr) || !g_audioPlaybackBufferState.configured)
    {
        return 0U;
    }

    capacity = AudioPlaybackBuffer_TrimToWholeFrames(capacity);
    readableBytes = AudioPlaybackBuffer_TrimToWholeFrames((capacity < g_audioPlaybackBufferState.fillLevelBytes) ? capacity : g_audioPlaybackBufferState.fillLevelBytes);

    if ((capacity > 0U) && (readableBytes == 0U))
    {
        g_audioPlaybackBufferState.underrunCount += 1U;
        return 0U;
    }

    firstChunkBytes = kPlaybackBufferCapacityBytes - g_audioPlaybackBufferState.readIndex;
    if (firstChunkBytes > readableBytes)
    {
        firstChunkBytes = readableBytes;
    }
    secondChunkBytes = readableBytes - firstChunkBytes;

    memcpy(data, &g_audioPlaybackBuffer[g_audioPlaybackBufferState.readIndex], firstChunkBytes);
    if (secondChunkBytes > 0U)
    {
        memcpy(data + firstChunkBytes, g_audioPlaybackBuffer, secondChunkBytes);
    }

    g_audioPlaybackBufferState.readIndex = (g_audioPlaybackBufferState.readIndex + readableBytes) % kPlaybackBufferCapacityBytes;
    g_audioPlaybackBufferState.fillLevelBytes -= readableBytes;
    AudioPlaybackBuffer_UpdateFillExtrema();

    return readableBytes;
}

extern "C" uint32_t AudioPlaybackBuffer_FillLevelBytes(void)
{
    return g_audioPlaybackBufferState.fillLevelBytes;
}

extern "C" uint32_t AudioPlaybackBuffer_CapacityBytes(void)
{
    return kPlaybackBufferCapacityBytes;
}

extern "C" uint32_t AudioPlaybackBuffer_GetMinFillLevelBytes(void)
{
    return g_audioPlaybackBufferState.minFillLevelBytes;
}

extern "C" uint32_t AudioPlaybackBuffer_GetMaxFillLevelBytes(void)
{
    return g_audioPlaybackBufferState.maxFillLevelBytes;
}

extern "C" uint32_t AudioPlaybackBuffer_GetUnderrunCount(void)
{
    return g_audioPlaybackBufferState.underrunCount;
}

extern "C" uint32_t AudioPlaybackBuffer_GetOverrunCount(void)
{
    return g_audioPlaybackBufferState.overrunCount;
}

extern "C" uint32_t AudioPlaybackBuffer_GetDroppedBytes(void)
{
    return g_audioPlaybackBufferState.droppedBytes;
}

extern "C" bool AudioPlaybackBuffer_StartThresholdReached(void)
{
    return g_audioPlaybackBufferState.fillLevelBytes >= kPlaybackStartThresholdBytes;
}