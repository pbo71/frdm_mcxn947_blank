#ifndef DEVICE_PROTOCOL_PROTOCOL_LED_COMMANDS_HPP
#define DEVICE_PROTOCOL_PROTOCOL_LED_COMMANDS_HPP

#include <cstdint>

#include "device_protocol/protocol_core.hpp"

namespace device_protocol {

uint32_t HandleLedCommand(CommandId commandId,
                          const uint8_t *payload,
                          uint16_t payloadLength,
                          uint16_t sequence,
                          uint8_t *responseBuffer,
                          uint32_t responseCapacity);

}  // namespace device_protocol

#endif