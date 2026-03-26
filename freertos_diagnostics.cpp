extern "C" {
#include "FreeRTOS.h"
#include "task.h"
}

#include <stdint.h>

extern "C" {

volatile uint32_t g_freertosFaultCode = 0U;
volatile uintptr_t g_freertosFaultTaskHandle = 0U;
volatile const char *g_freertosFaultTaskName = nullptr;

void vApplicationMallocFailedHook(void)
{
    g_freertosFaultCode = 1U;
    g_freertosFaultTaskHandle = 0U;
    g_freertosFaultTaskName = "malloc_failed";
    taskDISABLE_INTERRUPTS();
    for (;;)
    {
    }
}

void vApplicationStackOverflowHook(TaskHandle_t xTask, char *pcTaskName)
{
    g_freertosFaultCode = 2U;
    g_freertosFaultTaskHandle = (uintptr_t)xTask;
    g_freertosFaultTaskName = pcTaskName;
    taskDISABLE_INTERRUPTS();
    for (;;)
    {
    }
}

}