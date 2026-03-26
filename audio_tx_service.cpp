extern "C" {
#include "FreeRTOS.h"
#include "task.h"
#include "usb_vendor_bulk.h"
}

#include "audio_tx_service.h"

namespace {

constexpr uint32_t kAudioTxTaskStackSize = configMINIMAL_STACK_SIZE + 192U;
constexpr UBaseType_t kAudioTxTaskPriority = tskIDLE_PRIORITY + 2U;
constexpr TickType_t kAudioTxTaskPeriodTicks = pdMS_TO_TICKS(2U);

void AudioTxServiceTask(void *taskParameter)
{
    (void)taskParameter;

    for (;;)
    {
        USB_VendorBulkNotifyAudioTxPending();
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