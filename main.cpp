/*
 * Copyright 2019 NXP
 * All rights reserved.
 *
 * SPDX-License-Identifier: BSD-3-Clause
 */

extern "C" {
#include "board.h"
#include "app.h"
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

int main(void)
{
    BOARD_InitHardware();
    (void)AudioCodec_Init();
    AudioPlaybackBuffer_Init();
    AudioStreamService_Init();
    ControlPlaneService_Init();
    USB_DeviceClockInit();
    USB_VendorBulkApplicationInit();

    if (AliveService_Start() != 0)
    {
        for (;;)
        {
        }
    }

    if (AudioTxService_Start() != 0)
    {
        for (;;)
        {
        }
    }

    vTaskStartScheduler();

    for (;;)
    {
    }
}