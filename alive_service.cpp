extern "C" {
#include "FreeRTOS.h"
#include "task.h"
#include "board.h"
}

#include "alive_service.h"

namespace {

constexpr uint32_t kAliveTaskStackSize = configMINIMAL_STACK_SIZE + 128U;
constexpr UBaseType_t kAliveTaskPriority = tskIDLE_PRIORITY + 1U;

void AliveServiceTask(void *taskParameter)
{
    (void)taskParameter;

    for (;;)
    {
        LED_RED_TOGGLE();
        vTaskDelay(pdMS_TO_TICKS(1000U));
    }
}

}  // namespace

extern "C" int AliveService_Start(void)
{
    BaseType_t status = xTaskCreate(AliveServiceTask, "alive", kAliveTaskStackSize, nullptr, kAliveTaskPriority, nullptr);

    return (status == pdPASS) ? 0 : -1;
}