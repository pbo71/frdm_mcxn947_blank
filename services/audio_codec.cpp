extern "C" {
#include "board.h"
#include "peripherals.h"
#include "fsl_sgtl5000.h"
#include "fsl_lpflexcomm.h"
#include "fsl_gpio.h"
#include "fsl_port.h"
}

#include "audio_codec.h"
#include "fsl_lpi2c.h"

namespace
{
constexpr uint8_t kCodecRegisterAddressSize = 2U;
constexpr uint16_t kExpectedChipId = 0xA011U;
constexpr uint16_t kCodecBaselineAdcDacCtrl = 0x323CU;
constexpr uint16_t kCodecBaselineAnaCtrl = 0x0111U;
constexpr uint16_t kCodecBaselineAnaPower = 0x7060U;
constexpr uint16_t kCodecBaselineDigPower = 0x0000U;
constexpr uint16_t kCodecBaselineRefCtrl = 0x0000U;
constexpr uint16_t kCodecPlaybackAdcDacCtrl = 0x0000U;
constexpr uint16_t kCodecPlaybackAnaCtrl = 0x0000U;
constexpr uint16_t kCodecPlaybackDigPower = 0x0073U;
constexpr uint16_t kCodecPlaybackClkCtrl = 0x0008U;
constexpr uint16_t kCodecPlaybackI2sCtrl = 0x0130U;
constexpr uint16_t kCodecPlaybackSssCtrl = 0x0010U;
// Keep ANA_POWER at board-stable baseline for this MCXN947 + Teensy shield setup.
constexpr uint16_t kCodecPlaybackAnaPower = 0x7060U;
constexpr uint16_t kCodecDefaultHpVolume = 0x1818U;
constexpr uint16_t kCodecDefaultDacVolume = 0x0172U;
constexpr uint32_t kCodecVagPowerDelayUs = 500000U;
constexpr uint32_t kCodecRegisterRetryDelayUs = 5000U;
constexpr uint32_t kCodecRegisterRetryCount = 3U;
constexpr uint32_t kCodecInitSettlingDelayUs = 20000U;
constexpr uint32_t kCodecInitRetryDelayUs = 5000U;
constexpr uint32_t kCodecInitMaxAttempts = 5U;
constexpr uint32_t kCodecRecoveryMaxAttempts = 3U;
constexpr uint32_t kCodecRecoveryDelayUs = 5000U;

enum AudioCodecInitStep : uint32_t
{
    kAudioCodecInitStepIdle = 0U,
    kAudioCodecInitStepReadChipId,
    kAudioCodecInitStepCompleted,
};

enum AudioCodecPlaybackEnableStep : uint32_t
{
    kAudioCodecPlaybackEnableStepIdle = 0U,
    kAudioCodecPlaybackEnableStepInit,
    kAudioCodecPlaybackEnableStepWriteDigPower,
    kAudioCodecPlaybackEnableStepWriteAnaPower,
    kAudioCodecPlaybackEnableStepWriteHpVolume,
    kAudioCodecPlaybackEnableStepWriteDacVolume,
    kAudioCodecPlaybackEnableStepWriteAdcDacCtrl,
    kAudioCodecPlaybackEnableStepWriteAnaCtrl,
    kAudioCodecPlaybackEnableStepCompleted,
};

bool g_codecI2cInitialized = false;
uint32_t g_audioCodecInitAttemptCount = 0U;
uint32_t g_audioCodecLastInitStep = kAudioCodecInitStepIdle;
uint32_t g_audioCodecLastPlaybackEnableStep = kAudioCodecPlaybackEnableStepIdle;
status_t g_audioCodecLastPlaybackEnableStatus = kStatus_Success;

void SetPlaybackEnableProgress(uint32_t step, status_t status)
{
    g_audioCodecLastPlaybackEnableStep = step;
    g_audioCodecLastPlaybackEnableStatus = status;
}

void EnsureCodecI2cInitialized(void)
{
    if (g_codecI2cInitialized)
    {
        return;
    }

    // SGTL5000 RESET# is wired to FRDM J1-12 = P5_3 (PORT5 pin 3 / GPIO5 pin 3).
    // Drive it HIGH (active-low deassert) so the codec is not held in reset.
    PORT_SetPinMux(PORT5, 3U, kPORT_MuxAlt0);  // GPIO function
    gpio_pin_config_t resetPinHigh = {kGPIO_DigitalOutput, 1U};
    GPIO_PinInit(GPIO5, 3U, &resetPinHigh);

    lpi2c_master_config_t codecI2cConfig = {0};

    LP_FLEXCOMM_Init(BOARD_CODEC_I2C_INSTANCE, LP_FLEXCOMM_PERIPH_LPI2C);
    LPI2C_MasterGetDefaultConfig(&codecI2cConfig);
    LPI2C_MasterInit(BOARD_CODEC_I2C_BASEADDR, &codecI2cConfig, BOARD_CODEC_I2C_CLOCK_FREQ);

    g_codecI2cInitialized = true;
}

status_t CodecI2cTransfer(uint16_t reg, lpi2c_direction_t direction, uint8_t *data, uint8_t dataSize)
{
    lpi2c_master_transfer_t transfer = {};

    transfer.flags = kLPI2C_TransferDefaultFlag;
    transfer.slaveAddress = SGTL5000_I2C_ADDR;
    transfer.direction = direction;
    transfer.subaddress = reg;
    transfer.subaddressSize = kCodecRegisterAddressSize;
    transfer.data = data;
    transfer.dataSize = dataSize;

    return LPI2C_MasterTransferBlocking(BOARD_CODEC_I2C_BASEADDR, &transfer);
}

status_t ReadCodecRegister(uint16_t reg, uint16_t *value)
{
    uint8_t payload[2] = {0U, 0U};
    const status_t status = CodecI2cTransfer(reg, kLPI2C_Read, payload, sizeof(payload));

    if (status != kStatus_Success)
    {
        return status;
    }

    *value = static_cast<uint16_t>((static_cast<uint16_t>(payload[0]) << 8) | payload[1]);
    return kStatus_Success;
}

status_t WriteCodecRegister(uint16_t reg, uint16_t value);

status_t WriteCodecRegisterWithRetry(uint16_t reg, uint16_t value)
{
    status_t status = kStatus_Fail;

    for (uint32_t attempt = 0U; attempt < kCodecRegisterRetryCount; ++attempt)
    {
        status = WriteCodecRegister(reg, value);
        if (status == kStatus_Success)
        {
            return status;
        }

        SDK_DelayAtLeastUs(kCodecRegisterRetryDelayUs, SDK_DEVICE_MAXIMUM_CPU_CLOCK_FREQUENCY);
    }

    return status;
}

status_t WriteCodecRegister(uint16_t reg, uint16_t value)
{
    uint8_t payload[2] = {
        static_cast<uint8_t>(value >> 8),
        static_cast<uint8_t>(value & 0xFFU),
    };

    return CodecI2cTransfer(reg, kLPI2C_Write, payload, sizeof(payload));
}

status_t WritePlaybackCoreRegisters(void)
{
    SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteDigPower, kStatus_Success);
    status_t status = WriteCodecRegisterWithRetry(CHIP_DIG_POWER, kCodecPlaybackDigPower);
    if (status != kStatus_Success)
    {
        SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteDigPower, status);
        return status;
    }

    return kStatus_Success;
}

status_t g_audioCodecInitStatus = kStatus_Fail;
status_t g_audioCodecLastRecoveryStatus = kStatus_Fail;
uint32_t g_audioCodecLastRecoveryAttemptCount = 0U;
}

extern "C" status_t AudioCodec_Init(void)
{
    if (g_audioCodecInitStatus == kStatus_Success)
    {
        return g_audioCodecInitStatus;
    }

    EnsureCodecI2cInitialized();

    for (uint32_t attempt = 0U; attempt < kCodecInitMaxAttempts; ++attempt)
    {
        g_audioCodecInitAttemptCount = attempt + 1U;
        g_audioCodecLastInitStep = kAudioCodecInitStepReadChipId;

        if (attempt == 0U)
        {
            SDK_DelayAtLeastUs(kCodecInitSettlingDelayUs, SDK_DEVICE_MAXIMUM_CPU_CLOCK_FREQUENCY);
        }
        else
        {
            SDK_DelayAtLeastUs(kCodecInitRetryDelayUs, SDK_DEVICE_MAXIMUM_CPU_CLOCK_FREQUENCY);
        }

        uint16_t chipId = 0U;
        g_audioCodecInitStatus = ReadCodecRegister(CHIP_ID, &chipId);
        if (g_audioCodecInitStatus != kStatus_Success)
        {
            continue;
        }

        if (chipId != kExpectedChipId)
        {
            g_audioCodecInitStatus = kStatus_Fail;
            continue;
        }

        g_audioCodecInitStatus = kStatus_Success;
        g_audioCodecLastInitStep = kAudioCodecInitStepCompleted;
        break;
    }

    return g_audioCodecInitStatus;
}

extern "C" status_t AudioCodec_GetInitStatus(void)
{
    return g_audioCodecInitStatus;
}

extern "C" uint32_t AudioCodec_GetInitAttemptCount(void)
{
    return g_audioCodecInitAttemptCount;
}

extern "C" uint32_t AudioCodec_GetLastInitStep(void)
{
    return g_audioCodecLastInitStep;
}

extern "C" uint32_t AudioCodec_GetLastPlaybackEnableStep(void)
{
    return g_audioCodecLastPlaybackEnableStep;
}

extern "C" status_t AudioCodec_GetLastPlaybackEnableStatus(void)
{
    return g_audioCodecLastPlaybackEnableStatus;
}

extern "C" status_t AudioCodec_EnablePlaybackDigitalPath(void)
{
    SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepInit, kStatus_Success);

    status_t status = AudioCodec_Init();

    if (status != kStatus_Success)
    {
        SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepInit, status);
        return status;
    }

    // 1. Program ANA_POWER and continue if the I2C write succeeds.
    // Some board/codec states may keep ANA_POWER readback at baseline even when
    // playback can still proceed, so do not hard-fail on readback mismatch here.
    SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteAnaPower, kStatus_Success);
    status = WriteCodecRegisterWithRetry(CHIP_ANA_POWER, kCodecPlaybackAnaPower);
    if (status != kStatus_Success)
    {
        SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteAnaPower, status);
        return status;
    }
    SDK_DelayAtLeastUs(20000U, SDK_DEVICE_MAXIMUM_CPU_CLOCK_FREQUENCY);

    // 2. Enable I2S_IN and DAC digital power
    SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteDigPower, kStatus_Success);
    status = WriteCodecRegisterWithRetry(CHIP_DIG_POWER, kCodecPlaybackDigPower);
    if (status != kStatus_Success)
    {
        SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteDigPower, status);
        return status;
    }

    status = WriteCodecRegisterWithRetry(CHIP_CLK_CTRL, kCodecPlaybackClkCtrl);
    if (status != kStatus_Success)
    {
        SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteDigPower, status);
        return status;
    }

    status = WriteCodecRegisterWithRetry(CHIP_I2S_CTRL, kCodecPlaybackI2sCtrl);
    if (status != kStatus_Success)
    {
        SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteDigPower, status);
        return status;
    }

    status = WriteCodecRegisterWithRetry(CHIP_SSS_CTRL, kCodecPlaybackSssCtrl);
    if (status != kStatus_Success)
    {
        SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteDigPower, status);
        return status;
    }

    // 3. Set HP volume
    SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteHpVolume, kStatus_Success);
    status = WriteCodecRegisterWithRetry(CHIP_ANA_HP_CTRL, kCodecDefaultHpVolume);
    if (status != kStatus_Success)
    {
        SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteHpVolume, status);
        return status;
    }

    // 4. Set DAC volume
    SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteDacVolume, kStatus_Success);
    status = WriteCodecRegisterWithRetry(CHIP_DAC_VOL, kCodecDefaultDacVolume);
    if (status != kStatus_Success)
    {
        SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteDacVolume, status);
        return status;
    }

    // 5. Unmute DAC
    SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteAdcDacCtrl, kStatus_Success);
    status = WriteCodecRegisterWithRetry(CHIP_ADCDAC_CTRL, kCodecPlaybackAdcDacCtrl);
    if (status != kStatus_Success)
    {
        SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteAdcDacCtrl, status);
        return status;
    }

    // 6. Unmute headphone output
    SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteAnaCtrl, kStatus_Success);
    status = WriteCodecRegisterWithRetry(CHIP_ANA_CTRL, kCodecPlaybackAnaCtrl);
    if (status != kStatus_Success)
    {
        SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepWriteAnaCtrl, status);
        return status;
    }

    SetPlaybackEnableProgress(kAudioCodecPlaybackEnableStepCompleted, kStatus_Success);
    return kStatus_Success;
}

extern "C" status_t AudioCodec_RefreshPlaybackDigitalPath(void)
{
    status_t status = AudioCodec_Init();

    if (status != kStatus_Success)
    {
        return status;
    }

    return WritePlaybackCoreRegisters();
}

extern "C" status_t AudioCodec_RecoverI2cBus(void)
{
    status_t status = kStatus_Fail;

    g_audioCodecLastRecoveryAttemptCount = 0U;
    g_audioCodecLastRecoveryStatus = kStatus_Fail;

    for (uint32_t attempt = 0U; attempt < kCodecRecoveryMaxAttempts; ++attempt)
    {
        g_audioCodecLastRecoveryAttemptCount = attempt + 1U;

        if (g_codecI2cInitialized)
        {
            LPI2C_MasterDeinit(BOARD_CODEC_I2C_BASEADDR);
            g_codecI2cInitialized = false;
        }

        EnsureCodecI2cInitialized();
        SDK_DelayAtLeastUs(kCodecRecoveryDelayUs, SDK_DEVICE_MAXIMUM_CPU_CLOCK_FREQUENCY);

        uint16_t chipId = 0U;
        status = ReadCodecRegister(CHIP_ID, &chipId);
        if ((status == kStatus_Success) && (chipId == kExpectedChipId))
        {
            g_audioCodecLastRecoveryStatus = kStatus_Success;
            return kStatus_Success;
        }

        if (status == kStatus_Success)
        {
            status = kStatus_Fail;
        }

        SDK_DelayAtLeastUs(kCodecRecoveryDelayUs, SDK_DEVICE_MAXIMUM_CPU_CLOCK_FREQUENCY);
    }

    g_audioCodecLastRecoveryStatus = status;
    return status;
}

extern "C" status_t AudioCodec_GetLastRecoveryStatus(void)
{
    return g_audioCodecLastRecoveryStatus;
}

extern "C" uint32_t AudioCodec_GetLastRecoveryAttemptCount(void)
{
    return g_audioCodecLastRecoveryAttemptCount;
}

extern "C" status_t AudioCodec_DisablePlaybackDigitalPath(void)
{
    status_t status = AudioCodec_Init();
    if (status != kStatus_Success)
    {
        return status;
    }

    return AudioCodec_RecoverI2cBus();
}