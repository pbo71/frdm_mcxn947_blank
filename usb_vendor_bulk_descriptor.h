/*
 * USB vendor bulk descriptors for command/event + audio bulk streams.
 */
#ifndef USB_VENDOR_BULK_DESCRIPTOR_H
#define USB_VENDOR_BULK_DESCRIPTOR_H

#include "usb_device_class.h"
#include "usb.h"

#define USB_VENDOR_BULK_DEVICE_SPEC_BCD (0x0200U)
#define USB_VENDOR_BULK_DEVICE_BCD      (0x0101U)

#define USB_VENDOR_BULK_VID (0x1FC9U)
#define USB_VENDOR_BULK_PID (0x00B0U)

#define USB_VENDOR_BULK_CONFIG_COUNT   (1U)
#define USB_VENDOR_BULK_STRING_COUNT   (4U)
#define USB_VENDOR_BULK_LANGUAGE_COUNT (1U)

#define USB_VENDOR_BULK_CONFIG_INDEX   (1U)
#define USB_VENDOR_BULK_INTERFACE_IDX  (0U)
#define USB_VENDOR_BULK_INTERFACE_CNT  (1U)
#define USB_VENDOR_BULK_ENDPOINT_COUNT (5U)

#define USB_VENDOR_BULK_EP_CMD_OUT      (0x01U)
#define USB_VENDOR_BULK_EP_CMD_IN       (0x81U)
#define USB_VENDOR_BULK_EP_AUDIO_OUT    (0x02U)
#define USB_VENDOR_BULK_EP_AUDIO_IN     (0x82U)
#define USB_VENDOR_BULK_EP_ISO_AUDIO_IN (0x83U)

#define USB_VENDOR_BULK_CMD_FS_MPS       (64U)
#define USB_VENDOR_BULK_CMD_HS_MPS       (512U)
#define USB_VENDOR_BULK_AUDIO_FS_MPS     (64U)
#define USB_VENDOR_BULK_AUDIO_HS_MPS     (512U)
/* Isochronous IN: 192 bytes/ms fits 48 kHz 16-bit stereo; 512 gives headroom.
 * HS wMaxPacketSize bits 12:11 = 00 → 1 transaction per microframe.
 * bInterval = 4 → 2^(4-1) = 8 microframes = 1 ms period. */
#define USB_VENDOR_BULK_ISO_AUDIO_FS_MPS (192U)
#define USB_VENDOR_BULK_ISO_AUDIO_HS_MPS (512U)

#define USB_VENDOR_BULK_MS_VENDOR_CODE (0x20U)
#define USB_VENDOR_BULK_MS_OS_20_INDEX (0x0007U)

usb_status_t USB_VendorBulkSetSpeed(uint8_t speed);

usb_status_t USB_VendorBulkGetDeviceDescriptor(usb_device_handle handle,
                                               usb_device_get_device_descriptor_struct_t *deviceDescriptor);
usb_status_t USB_VendorBulkGetConfigurationDescriptor(
    usb_device_handle handle,
    usb_device_get_configuration_descriptor_struct_t *configurationDescriptor);
usb_status_t USB_VendorBulkGetStringDescriptor(usb_device_handle handle,
                                               usb_device_get_string_descriptor_struct_t *stringDescriptor);
usb_status_t USB_VendorBulkGetDeviceQualifierDescriptor(
    usb_device_handle handle,
    usb_device_get_device_qualifier_descriptor_struct_t *deviceQualifierDescriptor);
usb_status_t USB_VendorBulkGetBOSDescriptor(usb_device_handle handle,
                                            usb_device_get_bos_descriptor_struct_t *bosDescriptor);
usb_status_t USB_VendorBulkHandleVendorRequest(usb_device_control_request_struct_t *controlRequest);

#endif
