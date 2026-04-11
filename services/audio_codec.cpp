extern "C" {
#include "board.h"
#include "peripherals.h"
#include "fsl_sgtl5000.h"
#include "fsl_lpflexcomm.h"
}

#include "audio_codec.h"
#include "fsl_lpi2c.h"

namespace
{
constexpr uint8_t kCodecRegisterAddressSize = 2U;
constexpr uint16_t kExpectedChipId = 0xA011U;
constexpr uint32_t kCodecInitSettlingDelayUs = 20000U;
constexpr uint32_t kCodecInitRetryDelayUs = 5000U;
constexpr uint32_t kCodecInitMaxAttempts = 5U;

enum AudioCodecInitStep : uint32_t
{
    kAudioCodecInitStepIdle = 0U,
    kAudioCodecInitStepReadChipId,
    kAudioCodecInitStepCompleted,
};

bool g_codecI2cInitialized = false;
uint32_t g_audioCodecInitAttemptCount = 0U;
uint32_t g_audioCodecLastInitStep = kAudioCodecInitStepIdle;

void EnsureCodecI2cInitialized(void)
{
    if (g_codecI2cInitialized)
    {
        return;
    }

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

status_t g_audioCodecInitStatus = kStatus_Fail;
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