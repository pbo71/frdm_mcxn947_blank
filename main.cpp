/*
 * Copyright 2019 NXP
 * All rights reserved.
 *
 * SPDX-License-Identifier: BSD-3-Clause
 */

extern "C" {
#include "board.h"
#include "app.h"
#include "fsl_common.h"
#include "FreeRTOS.h"
#include "task.h"
#include "alive_service.h"
#include "audio_tx_service.h"
#include "services/audio_codec.h"
#include "services/audio_playback_buffer.h"
#include "services/audio_stream_service.h"
#include "services/control_plane_service.h"
#include "usb_vendor_bulk.h"

void USB_DeviceClockInit(void);
}

namespace
{
[[noreturn]] void BootFailureResetLoop(void)
{
    for (;;)
    {
        // Blink quickly to indicate a boot-stage failure, then reset and retry.
        for (int i = 0; i < 6; ++i)
        {
            LED_RED_TOGGLE();
            SDK_DelayAtLeastUs(80000U, SDK_DEVICE_MAXIMUM_CPU_CLOCK_FREQUENCY);
        }

        NVIC_SystemReset();
    }
}
}

int main(void)
{
    BOARD_InitHardware();

    //constexpr int kCodecInitRetries = 4;
    //status_t codecStatus = kStatus_Fail;
    //for (int attempt = 0; attempt < kCodecInitRetries; ++attempt)
    {
        //codecStatus = AudioCodec_Init();
        //if (codecStatus == kStatus_Success)
        //{
        //    break;
        //}

        //SDK_DelayAtLeastUs(120000U, SDK_DEVICE_MAXIMUM_CPU_CLOCK_FREQUENCY);
    }

    // Codec bring-up can be timing sensitive on cold boot.
    // Continue boot even if codec init fails so USB/control plane still comes up.

    AudioPlaybackBuffer_Init();
    AudioStreamService_Init();
    ControlPlaneService_Init();
    USB_DeviceClockInit();
    USB_VendorBulkApplicationInit();

    if (AliveService_Start() != 0)
    {
        BootFailureResetLoop();
    }

    if (AudioTxService_Start() != 0)
    {
        BootFailureResetLoop();
    }

    vTaskStartScheduler();

    BootFailureResetLoop();
}