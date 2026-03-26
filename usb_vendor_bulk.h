#ifndef USB_VENDOR_BULK_H
#define USB_VENDOR_BULK_H

#include "usb.h"
#include "usb_device.h"

typedef struct _usb_vendor_bulk_debug_state
{
	volatile uint32_t stage;
	volatile uint32_t lastEvent;
	volatile uint32_t lastStatus;
	volatile uint32_t usbSpeed;
	volatile uintptr_t deviceHandle;
	volatile uint32_t ch9Stage;
	volatile uint32_t setupBmRequestType;
	volatile uint32_t setupBRequest;
	volatile uint32_t setupWValue;
	volatile uint32_t setupWIndex;
	volatile uint32_t setupWLength;
} usb_vendor_bulk_debug_state_t;

void USB_VendorBulkApplicationInit(void);
usb_device_handle USB_VendorBulkGetDeviceHandle(void);
void USB_VendorBulkNotifyTxPending(void);
void USB_VendorBulkNotifyAudioTxPending(void);
extern volatile usb_vendor_bulk_debug_state_t g_UsbVendorBulkDebug;

#endif
