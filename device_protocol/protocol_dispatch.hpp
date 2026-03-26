#ifndef DEVICE_PROTOCOL_PROTOCOL_DISPATCH_HPP
#define DEVICE_PROTOCOL_PROTOCOL_DISPATCH_HPP

#include <cstdint>

#include "device_protocol/protocol_core.hpp"

namespace device_protocol {

uint32_t ParseAndDispatchCommand(const ProtocolState &state,
                                 const uint8_t *request,
                                 uint32_t requestLength,
                                 uint8_t *responseBuffer,
                                 uint32_t responseCapacity);

}  // namespace device_protocol

#endif