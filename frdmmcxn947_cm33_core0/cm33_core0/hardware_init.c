/*
 * Copyright 2022 NXP
 * All rights reserved.
 *
 * SPDX-License-Identifier: BSD-3-Clause
 */

/*${header:start}*/
#include "pin_mux.h"
#include "peripherals.h"
#include "board.h"
/*${header:end}*/

static void BOARD_EnableSai1Mclk(void)
{
    sai_master_clock_t sai1MasterClockConfig = {
        .mclkOutputEnable = true,
        .mclkSourceClkHz  = SAI1_MCLK_SOURCE_CLOCK_HZ,
        .mclkHz           = SAI1_USER_MCLK_HZ,
    };

    SAI_SetMasterClockConfig(SAI1_PERIPHERAL, &sai1MasterClockConfig);
}

/*${function:start}*/
void BOARD_InitHardware(void)
{
    CLOCK_EnableClock(kCLOCK_Gpio0);
    BOARD_InitPins();
    BOARD_PowerMode_OD();
    BOARD_InitBootClocks();
    BOARD_InitBootPeripherals();
    BOARD_EnableSai1Mclk();
    LED_RED_INIT(LOGIC_LED_OFF);
}
/*${function:end}*/
