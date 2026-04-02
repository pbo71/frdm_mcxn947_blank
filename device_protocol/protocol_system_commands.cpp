#include "device_protocol/protocol_system_commands.hpp"

#include <array>

extern "C" {
#include "frdmmcxn947_cm33_core0/frdmmcxn947/board.h"
#include "fsl_lpflexcomm.h"
#include "device_protocol/protocol_c_api.h"
#include "services/audio_playback_buffer.h"
#include "services/audio_stream_service.h"
#include "usb_vendor_bulk.h"
}

#include "fsl_lpi2c.h"

namespace device_protocol {

namespace {

constexpr uint32_t kDefaultGeneratorModulationPeriodMs = 1000U;
constexpr uint8_t kI2cMinRegisterAddressSize = 1U;
constexpr uint8_t kI2cMaxRegisterAddressSize = 4U;

bool g_codecI2cInitialized = false;

bool IsValidI2cRegisterAddressSize(uint8_t registerAddressSize)
{
    return (registerAddressSize >= kI2cMinRegisterAddressSize) && (registerAddressSize <= kI2cMaxRegisterAddressSize);
}

void EnsureCodecI2cInitialized()
{
    if (!g_codecI2cInitialized)
    {
        lpi2c_master_config_t codecI2cConfig = {0};

        LP_FLEXCOMM_Init(BOARD_CODEC_I2C_INSTANCE, LP_FLEXCOMM_PERIPH_LPI2C);
        LPI2C_MasterGetDefaultConfig(&codecI2cConfig);
        LPI2C_MasterInit(BOARD_CODEC_I2C_BASEADDR, &codecI2cConfig, BOARD_CODEC_I2C_CLOCK_FREQ);
        g_codecI2cInitialized = true;
    }
}

status_t CodecI2cTransfer(uint8_t deviceAddress,
                          uint32_t registerAddress,
                          uint8_t registerAddressSize,
                          lpi2c_direction_t direction,
                          uint8_t *data,
                          uint8_t dataLength)
{
    lpi2c_master_transfer_t transfer = {};

    transfer.flags = kLPI2C_TransferDefaultFlag;
    transfer.slaveAddress = deviceAddress;
    transfer.direction = direction;
    transfer.subaddress = registerAddress;
    transfer.subaddressSize = registerAddressSize;
    transfer.data = data;
    transfer.dataSize = dataLength;

    return LPI2C_MasterTransferBlocking(BOARD_CODEC_I2C_BASEADDR, &transfer);
}

uint32_t HandleGetInfo(const ProtocolState &state, uint16_t sequence, uint8_t *responseBuffer, uint32_t responseCapacity)
{
    GetInfoResponsePayload payload{};

    payload.protocolVersion = kProtocolVersion;
    payload.maxCommandPacketSize = state.maxCommandPacketSize;
    payload.capabilities = kProtocolCapabilities;

    return BuildFrame(FrameType::Response,
                      static_cast<uint8_t>(CommandId::GetInfo),
                      StatusCode::Ok,
                      sequence,
                      &payload,
                      sizeof(payload),
                      responseBuffer,
                      responseCapacity);
}

uint32_t HandleGetUsbDebugState(uint16_t sequence, uint8_t *responseBuffer, uint32_t responseCapacity)
{
    GetUsbDebugStateResponsePayload payload{};

    payload.stage = g_UsbVendorBulkDebug.stage;
    payload.lastEvent = g_UsbVendorBulkDebug.lastEvent;
    payload.lastStatus = g_UsbVendorBulkDebug.lastStatus;
    payload.usbSpeed = g_UsbVendorBulkDebug.usbSpeed;
    payload.oversizedOutboundDropCount = DeviceProtocol_GetOversizedOutboundDropCount();
    payload.setupBmRequestType = g_UsbVendorBulkDebug.setupBmRequestType;
    payload.setupBRequest = g_UsbVendorBulkDebug.setupBRequest;
    payload.setupWValue = g_UsbVendorBulkDebug.setupWValue;
    payload.setupWIndex = g_UsbVendorBulkDebug.setupWIndex;
    payload.setupWLength = g_UsbVendorBulkDebug.setupWLength;
    payload.playbackFillLevelBytes = AudioPlaybackBuffer_FillLevelBytes();
    payload.playbackMinFillLevelBytes = AudioPlaybackBuffer_GetMinFillLevelBytes();
    payload.playbackMaxFillLevelBytes = AudioPlaybackBuffer_GetMaxFillLevelBytes();
    payload.playbackUnderrunCount = AudioPlaybackBuffer_GetUnderrunCount();
    payload.playbackOverrunCount = AudioPlaybackBuffer_GetOverrunCount();
    payload.playbackDroppedBytes = AudioPlaybackBuffer_GetDroppedBytes();

    return BuildFrame(FrameType::Response,
                      static_cast<uint8_t>(CommandId::GetUsbDebugState),
                      StatusCode::Ok,
                      sequence,
                      &payload,
                      sizeof(payload),
                      responseBuffer,
                      responseCapacity);
}

uint32_t HandlePing(const uint8_t *payload,
                    uint16_t payloadLength,
                    uint16_t sequence,
                    uint8_t *responseBuffer,
                    uint32_t responseCapacity)
{
    return BuildFrame(FrameType::Response,
                      static_cast<uint8_t>(CommandId::Ping),
                      StatusCode::Ok,
                      sequence,
                      payload,
                      payloadLength,
                      responseBuffer,
                      responseCapacity);
}

uint32_t HandleStartStream(const uint8_t *payload,
                           uint16_t payloadLength,
                           uint16_t sequence,
                           uint8_t *responseBuffer,
                           uint32_t responseCapacity)
{
    StartStreamCommandPayload requestPayload{};
    StartStreamResponsePayload responsePayload{};

    if (payloadLength != sizeof(requestPayload))
    {
        return BuildErrorResponse(StatusCode::InvalidLength,
                                  sequence,
                                  static_cast<uint8_t>(CommandId::StartStream),
                                  responseBuffer,
                                  responseCapacity);
    }

    memcpy(&requestPayload, payload, sizeof(requestPayload));
    if (!AudioStreamService_Start(requestPayload.sampleRateHz,
                                  requestPayload.channelCount,
                                  requestPayload.bitsPerSample,
                                  requestPayload.source))
    {
        return BuildErrorResponse(StatusCode::InvalidArgument,
                                  sequence,
                                  static_cast<uint8_t>(CommandId::StartStream),
                                  responseBuffer,
                                  responseCapacity);
    }

    responsePayload.sampleRateHz = requestPayload.sampleRateHz;
    responsePayload.channelCount = requestPayload.channelCount;
    responsePayload.bitsPerSample = requestPayload.bitsPerSample;
    responsePayload.source = requestPayload.source;
    responsePayload.reserved = 0U;

    return BuildFrame(FrameType::Response,
                      static_cast<uint8_t>(CommandId::StartStream),
                      StatusCode::Ok,
                      sequence,
                      &responsePayload,
                      sizeof(responsePayload),
                      responseBuffer,
                      responseCapacity);
}

uint32_t HandleStopStream(uint16_t sequence,
                          uint8_t *responseBuffer,
                          uint32_t responseCapacity)
{
    StopStreamResponsePayload responsePayload{};

    if (!AudioStreamService_Stop(AUDIO_STREAM_SERVICE_STOP_REASON_CONTROL_PLANE))
    {
        return BuildErrorResponse(StatusCode::InvalidArgument,
                                  sequence,
                                  static_cast<uint8_t>(CommandId::StopStream),
                                  responseBuffer,
                                  responseCapacity);
    }

    responsePayload.stopReason = AUDIO_STREAM_SERVICE_STOP_REASON_CONTROL_PLANE;

    return BuildFrame(FrameType::Response,
                      static_cast<uint8_t>(CommandId::StopStream),
                      StatusCode::Ok,
                      sequence,
                      &responsePayload,
                      sizeof(responsePayload),
                      responseBuffer,
                      responseCapacity);
}

uint32_t HandleSetGeneratorConfig(const uint8_t *payload,
                                  uint16_t payloadLength,
                                  uint16_t sequence,
                                  uint8_t *responseBuffer,
                                  uint32_t responseCapacity)
{
    SetGeneratorConfigCommandPayload requestPayload{};
    SetGeneratorConfigResponsePayload responsePayload{};

    if (payloadLength != sizeof(requestPayload))
    {
        return BuildErrorResponse(StatusCode::InvalidLength,
                                  sequence,
                                  static_cast<uint8_t>(CommandId::SetGeneratorConfig),
                                  responseBuffer,
                                  responseCapacity);
    }

    memcpy(&requestPayload, payload, sizeof(requestPayload));
    if (!AudioStreamService_SetGeneratorConfig(requestPayload.source,
                                               requestPayload.primaryFrequencyHz,
                                               requestPayload.secondaryFrequencyHz,
                                               requestPayload.modulationPeriodMs,
                                               requestPayload.amplitude,
                                               requestPayload.noiseType,
                                               requestPayload.amplitudeEnvelope,
                                               requestPayload.noiseSeed))
    {
        return BuildErrorResponse(StatusCode::InvalidArgument,
                                  sequence,
                                  static_cast<uint8_t>(CommandId::SetGeneratorConfig),
                                  responseBuffer,
                                  responseCapacity);
    }

    responsePayload.primaryFrequencyHz = requestPayload.primaryFrequencyHz;
    responsePayload.secondaryFrequencyHz = requestPayload.secondaryFrequencyHz;
    responsePayload.modulationPeriodMs =
        (requestPayload.modulationPeriodMs == 0U) ? kDefaultGeneratorModulationPeriodMs : requestPayload.modulationPeriodMs;
    responsePayload.amplitude = requestPayload.amplitude;
    responsePayload.source = requestPayload.source;
    responsePayload.noiseType = requestPayload.noiseType;
    responsePayload.amplitudeEnvelope = requestPayload.amplitudeEnvelope;
    responsePayload.reserved = 0U;
    responsePayload.noiseSeed = requestPayload.noiseSeed;

    return BuildFrame(FrameType::Response,
                      static_cast<uint8_t>(CommandId::SetGeneratorConfig),
                      StatusCode::Ok,
                      sequence,
                      &responsePayload,
                      sizeof(responsePayload),
                      responseBuffer,
                      responseCapacity);
}

uint32_t HandleI2cWriteRegister(const uint8_t *payload,
                                uint16_t payloadLength,
                                uint16_t sequence,
                                uint8_t *responseBuffer,
                                uint32_t responseCapacity)
{
    I2cWriteRegisterCommandHeader requestHeader{};
    I2cTransferStatusPayload responsePayload{};
    status_t status;

    if ((payload == nullptr) || (payloadLength < sizeof(requestHeader)))
    {
        return BuildErrorResponse(StatusCode::InvalidLength,
                                  sequence,
                                  static_cast<uint8_t>(CommandId::I2cWriteRegister),
                                  responseBuffer,
                                  responseCapacity);
    }

    memcpy(&requestHeader, payload, sizeof(requestHeader));

    if ((requestHeader.writeLength == 0U) ||
        (requestHeader.writeLength > kMaxI2cRegisterTransferBytes) ||
        !IsValidI2cRegisterAddressSize(requestHeader.registerAddressSize) ||
        (requestHeader.deviceAddress == 0U) ||
        (payloadLength != (sizeof(requestHeader) + requestHeader.writeLength)))
    {
        return BuildErrorResponse(StatusCode::InvalidArgument,
                                  sequence,
                                  static_cast<uint8_t>(CommandId::I2cWriteRegister),
                                  responseBuffer,
                                  responseCapacity);
    }

    EnsureCodecI2cInitialized();
    status = CodecI2cTransfer(requestHeader.deviceAddress,
                              requestHeader.registerAddress,
                              requestHeader.registerAddressSize,
                              kLPI2C_Write,
                              const_cast<uint8_t *>(payload + sizeof(requestHeader)),
                              requestHeader.writeLength);

    responsePayload.driverStatus = static_cast<uint32_t>(status);

    return BuildFrame(FrameType::Response,
                      static_cast<uint8_t>(CommandId::I2cWriteRegister),
                      (status == kStatus_Success) ? StatusCode::Ok : StatusCode::InvalidArgument,
                      sequence,
                      &responsePayload,
                      sizeof(responsePayload),
                      responseBuffer,
                      responseCapacity);
}

uint32_t HandleI2cReadRegister(const uint8_t *payload,
                               uint16_t payloadLength,
                               uint16_t sequence,
                               uint8_t *responseBuffer,
                               uint32_t responseCapacity)
{
    I2cReadRegisterCommandPayload requestPayload{};
    std::array<uint8_t, sizeof(I2cReadRegisterResponseHeader) + kMaxI2cRegisterTransferBytes> responsePayload{};
    auto *responseHeader = reinterpret_cast<I2cReadRegisterResponseHeader *>(responsePayload.data());
    status_t status;

    if ((payload == nullptr) || (payloadLength != sizeof(requestPayload)))
    {
        return BuildErrorResponse(StatusCode::InvalidLength,
                                  sequence,
                                  static_cast<uint8_t>(CommandId::I2cReadRegister),
                                  responseBuffer,
                                  responseCapacity);
    }

    memcpy(&requestPayload, payload, sizeof(requestPayload));

    if ((requestPayload.readLength == 0U) ||
        (requestPayload.readLength > kMaxI2cRegisterTransferBytes) ||
        !IsValidI2cRegisterAddressSize(requestPayload.registerAddressSize) ||
        (requestPayload.deviceAddress == 0U))
    {
        return BuildErrorResponse(StatusCode::InvalidArgument,
                                  sequence,
                                  static_cast<uint8_t>(CommandId::I2cReadRegister),
                                  responseBuffer,
                                  responseCapacity);
    }

    EnsureCodecI2cInitialized();
    status = CodecI2cTransfer(requestPayload.deviceAddress,
                              requestPayload.registerAddress,
                              requestPayload.registerAddressSize,
                              kLPI2C_Read,
                              responsePayload.data() + sizeof(I2cReadRegisterResponseHeader),
                              requestPayload.readLength);

    responseHeader->driverStatus = static_cast<uint32_t>(status);
    responseHeader->readLength = requestPayload.readLength;
    responseHeader->reserved0 = 0U;
    responseHeader->reserved1 = 0U;

    return BuildFrame(FrameType::Response,
                      static_cast<uint8_t>(CommandId::I2cReadRegister),
                      (status == kStatus_Success) ? StatusCode::Ok : StatusCode::InvalidArgument,
                      sequence,
                      responsePayload.data(),
                      static_cast<uint16_t>(sizeof(I2cReadRegisterResponseHeader) + requestPayload.readLength),
                      responseBuffer,
                      responseCapacity);
}

}  // namespace

uint32_t DispatchSystemCommand(const ProtocolState &state,
                               CommandId commandId,
                               const uint8_t *payload,
                               uint16_t payloadLength,
                               uint16_t sequence,
                               uint8_t *responseBuffer,
                               uint32_t responseCapacity)
{
    switch (commandId)
    {
        case CommandId::GetInfo:
            if (payloadLength != 0U)
            {
                return BuildErrorResponse(StatusCode::InvalidLength,
                                          sequence,
                                          static_cast<uint8_t>(commandId),
                                          responseBuffer,
                                          responseCapacity);
            }
            return HandleGetInfo(state, sequence, responseBuffer, responseCapacity);

        case CommandId::GetUsbDebugState:
            if (payloadLength != 0U)
            {
                return BuildErrorResponse(StatusCode::InvalidLength,
                                          sequence,
                                          static_cast<uint8_t>(commandId),
                                          responseBuffer,
                                          responseCapacity);
            }
            return HandleGetUsbDebugState(sequence, responseBuffer, responseCapacity);

        case CommandId::Ping:
            return HandlePing(payload, payloadLength, sequence, responseBuffer, responseCapacity);

        case CommandId::StartStream:
            return HandleStartStream(payload, payloadLength, sequence, responseBuffer, responseCapacity);

        case CommandId::StopStream:
            if (payloadLength != 0U)
            {
                return BuildErrorResponse(StatusCode::InvalidLength,
                                          sequence,
                                          static_cast<uint8_t>(commandId),
                                          responseBuffer,
                                          responseCapacity);
            }
            return HandleStopStream(sequence, responseBuffer, responseCapacity);

        case CommandId::SetGeneratorConfig:
            return HandleSetGeneratorConfig(payload,
                                            payloadLength,
                                            sequence,
                                            responseBuffer,
                                            responseCapacity);

        case CommandId::I2cWriteRegister:
            return HandleI2cWriteRegister(payload,
                                          payloadLength,
                                          sequence,
                                          responseBuffer,
                                          responseCapacity);

        case CommandId::I2cReadRegister:
            return HandleI2cReadRegister(payload,
                                         payloadLength,
                                         sequence,
                                         responseBuffer,
                                         responseCapacity);

        default:
            return BuildErrorResponse(StatusCode::UnsupportedCommand,
                                      sequence,
                                      static_cast<uint8_t>(commandId),
                                      responseBuffer,
                                      responseCapacity);
    }
}

}  // namespace device_protocol