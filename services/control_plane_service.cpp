#include "services/control_plane_service.h"

extern "C" {
#include "device_protocol/protocol_c_api.h"
#include "usb_vendor_bulk.h"
}

namespace {

control_plane_service_state_t g_controlPlaneServiceState = kControlPlaneServiceStateIdle;
bool g_controlPlaneServiceCommandInProgress = false;

void ControlPlaneService_NotifyTransportIfPending(void)
{
    if (g_controlPlaneServiceCommandInProgress)
    {
        return;
    }

    USB_VendorBulkNotifyTxPending();
}

}  // namespace

extern "C" void ControlPlaneService_Init(void)
{
    g_controlPlaneServiceState = kControlPlaneServiceStateIdle;
    DeviceProtocol_Init();
}

extern "C" void ControlPlaneService_OnTransportReset(void)
{
    g_controlPlaneServiceState = kControlPlaneServiceStateIdle;
    DeviceProtocol_OnTransportReset();
}

extern "C" void ControlPlaneService_OnTransportReady(uint16_t maxCommandPacketSize)
{
    g_controlPlaneServiceState = kControlPlaneServiceStateConfigured;
    DeviceProtocol_OnTransportReady(maxCommandPacketSize);
    ControlPlaneService_NotifyTransportIfPending();
}

extern "C" void ControlPlaneService_NotifyStreamStarted(uint32_t sampleRateHz,
                                                         uint8_t channelCount,
                                                         uint8_t bitsPerSample)
{
    g_controlPlaneServiceState = kControlPlaneServiceStateStreaming;
    DeviceProtocol_NotifyStreamStarted(sampleRateHz, channelCount, bitsPerSample);
    ControlPlaneService_NotifyTransportIfPending();
}

extern "C" void ControlPlaneService_NotifyStreamStopped(uint32_t reason)
{
    g_controlPlaneServiceState = kControlPlaneServiceStateConfigured;
    DeviceProtocol_NotifyStreamStopped(reason);
    ControlPlaneService_NotifyTransportIfPending();
}

extern "C" void ControlPlaneService_NotifyFault(uint32_t faultCode, uint32_t detail)
{
    DeviceProtocol_NotifyFault(faultCode, detail);
    ControlPlaneService_NotifyTransportIfPending();
}

extern "C" uint32_t ControlPlaneService_HandleCommandPacket(const uint8_t *request,
                                                             uint32_t requestLength,
                                                             uint8_t *responseBuffer,
                                                             uint32_t responseCapacity)
{
    uint32_t responseLength;

    g_controlPlaneServiceCommandInProgress = true;
    responseLength = DeviceProtocol_HandleCommand(request, requestLength, responseBuffer, responseCapacity);
    g_controlPlaneServiceCommandInProgress = false;

    return responseLength;
}

extern "C" uint32_t ControlPlaneService_GetNextOutboundPacket(uint8_t *txBuffer, uint32_t txCapacity)
{
    return DeviceProtocol_GetNextTxFrame(txBuffer, txCapacity);
}

extern "C" control_plane_service_state_t ControlPlaneService_GetState(void)
{
    return g_controlPlaneServiceState;
}