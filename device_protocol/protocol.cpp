#include "device_protocol/protocol_c_api.h"
#include "device_protocol/protocol_core.hpp"
#include "device_protocol/protocol_dispatch.hpp"

namespace {

device_protocol::ProtocolState g_protocolState{};
device_protocol::TxQueue g_txQueue{};
uint32_t g_oversizedOutboundDropCount = 0U;

void QueueEvent(device_protocol::EventId eventId, const void *payload, uint16_t payloadLength)
{
    const uint32_t frameLength = static_cast<uint32_t>(sizeof(device_protocol::FrameHeader)) + payloadLength;

    if (frameLength > g_protocolState.maxCommandPacketSize)
    {
        g_oversizedOutboundDropCount++;
        return;
    }

    (void)g_txQueue.Enqueue(eventId, payload, payloadLength);
}

}  // namespace

extern "C" void DeviceProtocol_Init(void)
{
    g_protocolState.maxCommandPacketSize = device_protocol::kDefaultCommandPacketSize;
    g_oversizedOutboundDropCount = 0U;
    g_txQueue.Reset();
}

extern "C" void DeviceProtocol_OnTransportReset(void)
{
    g_protocolState.maxCommandPacketSize = device_protocol::kDefaultCommandPacketSize;
    g_oversizedOutboundDropCount = 0U;
    g_txQueue.Reset();
}

extern "C" void DeviceProtocol_OnTransportReady(uint16_t maxCommandPacketSize)
{
    device_protocol::DeviceReadyEventPayload payload{};

    g_protocolState.maxCommandPacketSize = maxCommandPacketSize;

    payload.capabilities = device_protocol::kProtocolCapabilities;
    payload.maxCommandPacketSize = maxCommandPacketSize;
    payload.reserved = 0U;

    QueueEvent(device_protocol::EventId::DeviceReady, &payload, sizeof(payload));
}

extern "C" void DeviceProtocol_NotifyStreamStarted(uint32_t sampleRateHz, uint8_t channelCount, uint8_t bitsPerSample)
{
    device_protocol::StreamStartedEventPayload payload{};

    payload.sampleRateHz = sampleRateHz;
    payload.channelCount = channelCount;
    payload.bitsPerSample = bitsPerSample;
    payload.reserved = 0U;

    QueueEvent(device_protocol::EventId::StreamStarted, &payload, sizeof(payload));
}

extern "C" void DeviceProtocol_NotifyStreamStopped(uint32_t reason)
{
    device_protocol::StreamStoppedEventPayload payload{};

    payload.reason = reason;

    QueueEvent(device_protocol::EventId::StreamStopped, &payload, sizeof(payload));
}

extern "C" void DeviceProtocol_NotifyFault(uint32_t faultCode, uint32_t detail)
{
    device_protocol::FaultEventPayload payload{};

    payload.faultCode = faultCode;
    payload.detail = detail;

    QueueEvent(device_protocol::EventId::Fault, &payload, sizeof(payload));
}

extern "C" uint32_t DeviceProtocol_GetOversizedOutboundDropCount(void)
{
    return g_oversizedOutboundDropCount;
}

extern "C" uint32_t DeviceProtocol_HandleCommand(const uint8_t *request,
                                                   uint32_t requestLength,
                                                   uint8_t *responseBuffer,
                                                   uint32_t responseCapacity)
{
    return device_protocol::ParseAndDispatchCommand(g_protocolState,
                                                    request,
                                                    requestLength,
                                                    responseBuffer,
                                                    responseCapacity);
}

extern "C" uint32_t DeviceProtocol_GetNextTxFrame(uint8_t *txBuffer, uint32_t txCapacity)
{
    return g_txQueue.Dequeue(txBuffer, txCapacity);
}