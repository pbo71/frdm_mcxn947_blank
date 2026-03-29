#ifndef DEVICE_PROTOCOL_PROTOCOL_CORE_HPP
#define DEVICE_PROTOCOL_PROTOCOL_CORE_HPP

#include <array>
#include <cstddef>
#include <cstdint>

#include "device_protocol/protocol.hpp"

namespace device_protocol {

constexpr std::size_t kMaxFrameSize = 512U;
constexpr std::size_t kEventQueueDepth = 4U;
constexpr uint16_t kDefaultCommandPacketSize = 64U;

constexpr uint32_t kProtocolCapabilities = kCapabilityCommandResponse |
                                           kCapabilityAsyncEvents |
                                           kCapabilityLedControl |
                                           kCapabilityUsbDebug |
                                           kCapabilityAudioBulkPipe |
                                           kCapabilityAudioStreamControl |
                                           kCapabilityGeneratedAudioSource |
                                           kCapabilityGeneratorConfig |
                                           kCapabilityI2cRegisterAccess;

struct ProtocolState
{
    uint16_t maxCommandPacketSize;
};

uint32_t BuildFrame(FrameType type,
                    uint8_t opcode,
                    StatusCode status,
                    uint16_t sequence,
                    const void *payload,
                    uint16_t payloadLength,
                    uint8_t *output,
                    uint32_t outputCapacity);

uint32_t BuildErrorResponse(StatusCode status,
                            uint16_t sequence,
                            uint8_t opcode,
                            uint8_t *output,
                            uint32_t outputCapacity);

class TxQueue
{
  public:
    void Reset();
    bool EnqueueFrame(FrameType type,
                      uint8_t opcode,
                      StatusCode status,
                      uint16_t sequence,
                      const void *payload,
                      uint16_t payloadLength);
    bool Enqueue(EventId eventId, const void *payload, uint16_t payloadLength);
    uint32_t Dequeue(uint8_t *txBuffer, uint32_t txCapacity);

  private:
    std::array<std::array<uint8_t, kMaxFrameSize>, kEventQueueDepth> slots_{};
    std::array<uint16_t, kEventQueueDepth> lengths_{};
    std::size_t readIndex_ = 0U;
    std::size_t writeIndex_ = 0U;
    std::size_t count_ = 0U;
};

}  // namespace device_protocol

#endif