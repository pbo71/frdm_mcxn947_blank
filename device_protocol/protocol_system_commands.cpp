#include "device_protocol/protocol_system_commands.hpp"

extern "C" {
#include "device_protocol/protocol_c_api.h"
#include "services/audio_stream_service.h"
#include "usb_vendor_bulk.h"
}

namespace device_protocol {

namespace {

constexpr uint32_t kDefaultGeneratorModulationPeriodMs = 1000U;

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

        default:
            return BuildErrorResponse(StatusCode::UnsupportedCommand,
                                      sequence,
                                      static_cast<uint8_t>(commandId),
                                      responseBuffer,
                                      responseCapacity);
    }
}

}  // namespace device_protocol