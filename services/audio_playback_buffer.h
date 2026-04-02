#ifndef AUDIO_PLAYBACK_BUFFER_H
#define AUDIO_PLAYBACK_BUFFER_H

#include <stdbool.h>
#include <stdint.h>

#if defined(__cplusplus)
extern "C" {
#endif

void AudioPlaybackBuffer_Init(void);
void AudioPlaybackBuffer_Reset(void);
bool AudioPlaybackBuffer_Configure(uint32_t sampleRateHz, uint8_t channelCount, uint8_t bitsPerSample);
bool AudioPlaybackBuffer_IsConfigured(void);
uint32_t AudioPlaybackBuffer_GetSampleRateHz(void);
uint32_t AudioPlaybackBuffer_GetBytesPerFrame(void);
uint32_t AudioPlaybackBuffer_Write(const uint8_t *data, uint32_t length);
uint32_t AudioPlaybackBuffer_Read(uint8_t *data, uint32_t capacity);
uint32_t AudioPlaybackBuffer_FillLevelBytes(void);
uint32_t AudioPlaybackBuffer_CapacityBytes(void);
uint32_t AudioPlaybackBuffer_GetMinFillLevelBytes(void);
uint32_t AudioPlaybackBuffer_GetMaxFillLevelBytes(void);
uint32_t AudioPlaybackBuffer_GetUnderrunCount(void);
uint32_t AudioPlaybackBuffer_GetOverrunCount(void);
uint32_t AudioPlaybackBuffer_GetDroppedBytes(void);
bool AudioPlaybackBuffer_StartThresholdReached(void);

#if defined(__cplusplus)
}
#endif

#endif