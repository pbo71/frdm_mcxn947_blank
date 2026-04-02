extern "C" {
#include "FreeRTOS.h"
#include "task.h"
#include "services/audio_playback_buffer.h"
#include "services/control_plane_service.h"
#include "usb_vendor_bulk.h"
}

#include "audio_tx_service.h"

namespace {

constexpr uint32_t kAudioTxTaskStackSize = configMINIMAL_STACK_SIZE + 192U;
constexpr UBaseType_t kAudioTxTaskPriority = tskIDLE_PRIORITY + 2U;
constexpr TickType_t kAudioTxTaskPeriodTicks = pdMS_TO_TICKS(2U);
constexpr uint32_t kAudioTxTaskPeriodMs = 2U;
constexpr uint32_t kAudioDrainRateScalePermille = 1000U;
constexpr uint32_t kAudioDrainScratchBufferBytes = 1024U;

uint8_t g_audioDrainScratchBuffer[kAudioDrainScratchBufferBytes]{};

void AudioTxService_DrainPlaybackBuffer(void)
{
    static bool playbackDrainActive = false;
    static uint32_t byteAccumulator = 0U;

    if (ControlPlaneService_GetState() != kControlPlaneServiceStateStreaming)
    {
        playbackDrainActive = false;
        byteAccumulator = 0U;
        return;
    }

    if (!AudioPlaybackBuffer_IsConfigured())
    {
        return;
    }

    if (!playbackDrainActive)
    {
        playbackDrainActive = AudioPlaybackBuffer_StartThresholdReached();
        if (!playbackDrainActive)
        {
            return;
        }
    }

    const uint32_t bytesPerFrame = AudioPlaybackBuffer_GetBytesPerFrame();
    const uint32_t sampleRateHz = AudioPlaybackBuffer_GetSampleRateHz();
    if ((bytesPerFrame == 0U) || (sampleRateHz == 0U))
    {
        return;
    }

    byteAccumulator += (sampleRateHz * bytesPerFrame * kAudioTxTaskPeriodMs * kAudioDrainRateScalePermille);

    uint32_t bytesToConsume = byteAccumulator / 1000000U;
    byteAccumulator %= 1000000U;

    if (bytesToConsume == 0U)
    {
        return;
    }

    const uint32_t chunkSize = bytesToConsume > kAudioDrainScratchBufferBytes ? kAudioDrainScratchBufferBytes : bytesToConsume;
    const uint32_t consumedBytes = AudioPlaybackBuffer_Read(g_audioDrainScratchBuffer, chunkSize);
    (void)consumedBytes;
}

void AudioTxServiceTask(void *taskParameter)
{
    (void)taskParameter;

    for (;;)
    {
        USB_VendorBulkNotifyAudioTxPending();
        AudioTxService_DrainPlaybackBuffer();
        vTaskDelay(kAudioTxTaskPeriodTicks);
    }
}

}  // namespace

extern "C" int AudioTxService_Start(void)
{
    const BaseType_t status = xTaskCreate(AudioTxServiceTask,
                                          "audio_tx",
                                          kAudioTxTaskStackSize,
                                          nullptr,
                                          kAudioTxTaskPriority,
                                          nullptr);

    return (status == pdPASS) ? 0 : -1;
}