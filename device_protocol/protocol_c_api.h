#ifndef DEVICE_PROTOCOL_PROTOCOL_C_API_H
#define DEVICE_PROTOCOL_PROTOCOL_C_API_H

#include <stdint.h>

#if defined(__cplusplus)
extern "C" {
#endif

void DeviceProtocol_Init(void);
void DeviceProtocol_OnTransportReset(void);
void DeviceProtocol_OnTransportReady(uint16_t maxCommandPacketSize);
void DeviceProtocol_NotifyStreamStarted(uint32_t sampleRateHz, uint8_t channelCount, uint8_t bitsPerSample);
void DeviceProtocol_NotifyStreamStopped(uint32_t reason);
void DeviceProtocol_NotifyFault(uint32_t faultCode, uint32_t detail);
uint32_t DeviceProtocol_GetOversizedOutboundDropCount(void);
uint32_t DeviceProtocol_HandleCommand(const uint8_t *request,
                                      uint32_t requestLength,
                                      uint8_t *responseBuffer,
                                      uint32_t responseCapacity);
uint32_t DeviceProtocol_GetNextTxFrame(uint8_t *txBuffer, uint32_t txCapacity);

#if defined(__cplusplus)
}
#endif

#endif