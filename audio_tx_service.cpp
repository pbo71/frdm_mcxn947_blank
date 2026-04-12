extern "C" {
#include "FreeRTOS.h"
#include "board/peripherals.h"
#include "fsl_sai.h"
#include "task.h"
#include "services/audio_playback_buffer.h"
#include "services/control_plane_service.h"
#include "usb_vendor_bulk.h"
}

#include "audio_tx_service.h"

namespace {

constexpr uint32_t kAudioTxTaskStackSize = configMINIMAL_STACK_SIZE + 192U;
constexpr UBaseType_t kAudioTxTaskPriority = tskIDLE_PRIORITY + 1U;
constexpr TickType_t kAudioTxIdleDelayTicks = pdMS_TO_TICKS(1U);
constexpr uint32_t kAudioDrainScratchBufferBytes = 256U;

uint8_t g_audioDrainScratchBuffer[kAudioDrainScratchBufferBytes]{};

void AudioTxService_StopSaiTx(void)
{
    SAI_TxEnable(SAI1_PERIPHERAL, false);
    SAI_TxSoftwareReset(SAI1_PERIPHERAL, kSAI_ResetTypeFIFO);
}

void AudioTxService_StartSaiTx(void)
{
    SAI_TxSoftwareReset(SAI1_PERIPHERAL, kSAI_ResetTypeFIFO);
    SAI_TxSetChannelFIFOMask(SAI1_PERIPHERAL, 1U);
    SAI_TxSetFIFOErrorContinue(SAI1_PERIPHERAL, true);
    SAI_TxEnable(SAI1_PERIPHERAL, true);
}

bool AudioTxService_DrainPlaybackBuffer(void)
{
    static bool playbackDrainActive = false;
    static bool saiTxActive = false;

    if (ControlPlaneService_GetState() != kControlPlaneServiceStateStreaming)
    {
        if (saiTxActive)
        {
            AudioTxService_StopSaiTx();
            saiTxActive = false;
        }

        playbackDrainActive = false;
        return false;
    }

    if (!AudioPlaybackBuffer_IsConfigured())
    {
        return false;
    }

    if (!playbackDrainActive)
    {
        playbackDrainActive = AudioPlaybackBuffer_StartThresholdReached();
        if (!playbackDrainActive)
        {
            return false;
        }

        if (!saiTxActive)
        {
            AudioTxService_StartSaiTx();
            saiTxActive = true;
        }
    }

    const uint32_t bytesPerFrame = AudioPlaybackBuffer_GetBytesPerFrame();
    if (bytesPerFrame == 0U)
    {
        return false;
    }

    uint32_t bytesToConsume = AudioPlaybackBuffer_FillLevelBytes();
    bytesToConsume = bytesToConsume > kAudioDrainScratchBufferBytes ? kAudioDrainScratchBufferBytes : bytesToConsume;
    bytesToConsume -= (bytesToConsume % bytesPerFrame);

    if (bytesToConsume == 0U)
    {
        return false;
    }

    const uint32_t consumedBytes = AudioPlaybackBuffer_Read(g_audioDrainScratchBuffer, bytesToConsume);
    if (consumedBytes == 0U)
    {
        return false;
    }

    SAI_WriteBlocking(SAI1_PERIPHERAL, 0U, SAI1_TX_WORD_WIDTH, g_audioDrainScratchBuffer, consumedBytes);
    return true;
}

void AudioTxServiceTask(void *taskParameter)
{
    (void)taskParameter;

    for (;;)
    {
        USB_VendorBulkNotifyAudioTxPending();
        if (!AudioTxService_DrainPlaybackBuffer())
        {
            vTaskDelay(kAudioTxIdleDelayTicks);
        }
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