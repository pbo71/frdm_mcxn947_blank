#include "device_protocol/protocol_led_commands.hpp"

extern "C" {
#include "board.h"
}

namespace device_protocol {

uint32_t HandleLedCommand(CommandId commandId,
                          const uint8_t *payload,
                          uint16_t payloadLength,
                          uint16_t sequence,
                          uint8_t *responseBuffer,
                          uint32_t responseCapacity)
{
    SetLedResponsePayload responsePayload{};

    if (commandId != CommandId::SetLed)
    {
        return BuildErrorResponse(StatusCode::UnsupportedCommand,
                                  sequence,
                                  static_cast<uint8_t>(commandId),
                                  responseBuffer,
                                  responseCapacity);
    }

    if (payloadLength != 1U)
    {
        return BuildErrorResponse(StatusCode::InvalidLength,
                                  sequence,
                                  static_cast<uint8_t>(commandId),
                                  responseBuffer,
                                  responseCapacity);
    }

    switch (static_cast<LedAction>(payload[0]))
    {
        case LedAction::Off:
            LED_RED_OFF();
            break;

        case LedAction::On:
            LED_RED_ON();
            break;

        case LedAction::Toggle:
            LED_RED_TOGGLE();
            break;

        default:
            return BuildErrorResponse(StatusCode::InvalidArgument,
                                      sequence,
                                      static_cast<uint8_t>(commandId),
                                      responseBuffer,
                                      responseCapacity);
    }

    responsePayload.appliedAction = payload[0];
    responsePayload.reserved0 = 0U;
    responsePayload.reserved1 = 0U;

    return BuildFrame(FrameType::Response,
                      static_cast<uint8_t>(commandId),
                      StatusCode::Ok,
                      sequence,
                      &responsePayload,
                      sizeof(responsePayload),
                      responseBuffer,
                      responseCapacity);
}

}  // namespace device_protocol