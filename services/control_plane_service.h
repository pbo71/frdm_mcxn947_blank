#ifndef CONTROL_PLANE_SERVICE_H
#define CONTROL_PLANE_SERVICE_H

#include <stdint.h>

#if defined(__cplusplus)
extern "C" {
#endif

typedef enum _control_plane_service_state
{
    kControlPlaneServiceStateIdle = 0,
    kControlPlaneServiceStateConfigured = 1,
    kControlPlaneServiceStateStreaming = 2,
} control_plane_service_state_t;

void ControlPlaneService_Init(void);
void ControlPlaneService_OnTransportReset(void);
void ControlPlaneService_OnTransportReady(uint16_t maxCommandPacketSize);
void ControlPlaneService_NotifyStreamStarted(uint32_t sampleRateHz, uint8_t channelCount, uint8_t bitsPerSample);
void ControlPlaneService_NotifyStreamStopped(uint32_t reason);
void ControlPlaneService_NotifyFault(uint32_t faultCode, uint32_t detail);
uint32_t ControlPlaneService_HandleCommandPacket(const uint8_t *request,
                                                 uint32_t requestLength,
                                                 uint8_t *responseBuffer,
                                                 uint32_t responseCapacity);
uint32_t ControlPlaneService_GetNextOutboundPacket(uint8_t *txBuffer, uint32_t txCapacity);
control_plane_service_state_t ControlPlaneService_GetState(void);

#if defined(__cplusplus)
}
#endif

#endif