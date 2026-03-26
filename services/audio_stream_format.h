#ifndef AUDIO_STREAM_FORMAT_H
#define AUDIO_STREAM_FORMAT_H

#include <stdint.h>

#if defined(__GNUC__)
#define AUDIO_STREAM_PACKED __attribute__((packed))
#else
#define AUDIO_STREAM_PACKED
#endif

#if defined(__cplusplus)
extern "C" {
#endif

enum
{
    AUDIO_STREAM_HEADER_MAGIC = 0x4155U,
    AUDIO_STREAM_HEADER_VERSION = 1U,
};

enum
{
    AUDIO_STREAM_FLAG_START_OF_STREAM = (1U << 0U),
    AUDIO_STREAM_FLAG_END_OF_STREAM = (1U << 1U),
    AUDIO_STREAM_FLAG_DISCONTINUITY = (1U << 2U),
    AUDIO_STREAM_FLAG_FORMAT_CHANGE = (1U << 3U),
};

typedef struct AUDIO_STREAM_PACKED _audio_stream_header
{
    uint16_t magic;
    uint8_t version;
    uint8_t headerSize;
    uint16_t flags;
    uint16_t reserved0;
    uint32_t sequenceNumber;
    uint32_t timestamp;
    uint16_t payloadBytes;
    uint8_t channelCount;
    uint8_t bitsPerSample;
    uint32_t sampleRateHz;
} audio_stream_header_t;

#if defined(__cplusplus)
}

static_assert(sizeof(audio_stream_header_t) == 24U, "audio_stream_header_t must remain 24 bytes.");
#endif

#endif