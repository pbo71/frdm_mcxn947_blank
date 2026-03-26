#include "device_protocol/protocol_dispatch.hpp"

#include <cstring>

#include "device_protocol/protocol_led_commands.hpp"
#include "device_protocol/protocol_system_commands.hpp"

namespace device_protocol {

namespace {

uint32_t GetEffectiveResponseCapacity(const ProtocolState &state, uint32_t responseCapacity)
{
    return (responseCapacity < state.maxCommandPacketSize) ? responseCapacity : state.maxCommandPacketSize;
}

}  // namespace

uint32_t ParseAndDispatchCommand(const ProtocolState &state,
                                 const uint8_t *request,
                                 uint32_t requestLength,
                                 uint8_t *responseBuffer,
                                 uint32_t responseCapacity)
{
    FrameHeader header{};
    const uint8_t *payload;
    const uint32_t effectiveResponseCapacity = GetEffectiveResponseCapacity(state, responseCapacity);

    if ((request == nullptr) || (responseBuffer == nullptr) || (requestLength < sizeof(FrameHeader)))
    {
        return BuildErrorResponse(StatusCode::InvalidLength, 0U, 0U, responseBuffer, effectiveResponseCapacity);
    }

    if (requestLength > state.maxCommandPacketSize)
    {
        return BuildErrorResponse(StatusCode::InvalidLength, 0U, request[4U], responseBuffer, effectiveResponseCapacity);
    }

    const CommandId commandId = static_cast<CommandId>(request[4U]);

    std::memcpy(&header, request, sizeof(header));

    if (header.magic != kFrameMagic)
    {
        return BuildErrorResponse(StatusCode::InvalidMagic, header.sequence, header.opcode, responseBuffer, effectiveResponseCapacity);
    }

    if (header.version != kProtocolVersion)
    {
        return BuildErrorResponse(StatusCode::InvalidVersion, header.sequence, header.opcode, responseBuffer, effectiveResponseCapacity);
    }

    if (header.type != static_cast<uint8_t>(FrameType::Command))
    {
        return BuildErrorResponse(StatusCode::InvalidType, header.sequence, header.opcode, responseBuffer, effectiveResponseCapacity);
    }

    if ((sizeof(FrameHeader) + header.payloadLength) != requestLength)
    {
        return BuildErrorResponse(StatusCode::InvalidLength, header.sequence, header.opcode, responseBuffer, effectiveResponseCapacity);
    }

    if (requestLength > state.maxCommandPacketSize)
    {
        return BuildErrorResponse(StatusCode::InvalidLength, header.sequence, header.opcode, responseBuffer, effectiveResponseCapacity);
    }

    payload = request + sizeof(FrameHeader);

    switch (commandId)
    {
        case CommandId::GetInfo:
        case CommandId::GetUsbDebugState:
        case CommandId::Ping:
        case CommandId::StartStream:
        case CommandId::StopStream:
        case CommandId::SetGeneratorConfig:
            return DispatchSystemCommand(state,
                                         commandId,
                                         payload,
                                         header.payloadLength,
                                         header.sequence,
                                         responseBuffer,
                                         effectiveResponseCapacity);

        case CommandId::SetLed:
            return HandleLedCommand(commandId,
                                    payload,
                                    header.payloadLength,
                                    header.sequence,
                                    responseBuffer,
                                    effectiveResponseCapacity);

        default:
            return BuildErrorResponse(StatusCode::UnsupportedCommand,
                                      header.sequence,
                                      header.opcode,
                                      responseBuffer,
                                      effectiveResponseCapacity);
    }
}

}  // namespace device_protocol