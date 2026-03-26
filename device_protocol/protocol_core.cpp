#include "device_protocol/protocol_core.hpp"

#include <cstring>

namespace device_protocol {

uint32_t BuildFrame(FrameType type,
                    uint8_t opcode,
                    StatusCode status,
                    uint16_t sequence,
                    const void *payload,
                    uint16_t payloadLength,
                    uint8_t *output,
                    uint32_t outputCapacity)
{
    const uint32_t frameLength = static_cast<uint32_t>(sizeof(FrameHeader)) + payloadLength;
    FrameHeader header{};

    if ((output == nullptr) || (frameLength > outputCapacity))
    {
        return 0U;
    }

    header.magic = kFrameMagic;
    header.version = kProtocolVersion;
    header.type = static_cast<uint8_t>(type);
    header.opcode = opcode;
    header.status = static_cast<uint8_t>(status);
    header.sequence = sequence;
    header.payloadLength = payloadLength;

    std::memcpy(output, &header, sizeof(header));
    if ((payloadLength > 0U) && (payload != nullptr))
    {
        std::memcpy(output + sizeof(header), payload, payloadLength);
    }

    return frameLength;
}

uint32_t BuildErrorResponse(StatusCode status,
                            uint16_t sequence,
                            uint8_t opcode,
                            uint8_t *output,
                            uint32_t outputCapacity)
{
    return BuildFrame(FrameType::Response, opcode, status, sequence, nullptr, 0U, output, outputCapacity);
}

void TxQueue::Reset()
{
    lengths_.fill(0U);
    readIndex_ = 0U;
    writeIndex_ = 0U;
    count_ = 0U;
}

bool TxQueue::EnqueueFrame(FrameType type,
                           uint8_t opcode,
                           StatusCode status,
                           uint16_t sequence,
                           const void *payload,
                           uint16_t payloadLength)
{
    uint32_t frameLength;

    if (count_ >= kEventQueueDepth)
    {
        return false;
    }

    frameLength = BuildFrame(type,
                             opcode,
                             status,
                             sequence,
                             payload,
                             payloadLength,
                             slots_[writeIndex_].data(),
                             slots_[writeIndex_].size());
    if (frameLength == 0U)
    {
        return false;
    }

    lengths_[writeIndex_] = static_cast<uint16_t>(frameLength);
    writeIndex_ = (writeIndex_ + 1U) % kEventQueueDepth;
    count_++;

    return true;
}

bool TxQueue::Enqueue(EventId eventId, const void *payload, uint16_t payloadLength)
{
    return EnqueueFrame(FrameType::Event,
                        static_cast<uint8_t>(eventId),
                        StatusCode::Ok,
                        0U,
                        payload,
                        payloadLength);
}

uint32_t TxQueue::Dequeue(uint8_t *txBuffer, uint32_t txCapacity)
{
    uint16_t frameLength;

    if ((txBuffer == nullptr) || (count_ == 0U))
    {
        return 0U;
    }

    frameLength = lengths_[readIndex_];
    if (frameLength > txCapacity)
    {
        return 0U;
    }

    std::memcpy(txBuffer, slots_[readIndex_].data(), frameLength);
    lengths_[readIndex_] = 0U;
    readIndex_ = (readIndex_ + 1U) % kEventQueueDepth;
    count_--;

    return frameLength;
}

}  // namespace device_protocol