#include "usb_device_config.h"
#include "usb.h"
#include "usb_device.h"
#include "usb_device_ch9.h"
#include "usb_device_class.h"
#include "usb_vendor_bulk.h"
#include "usb_vendor_bulk_descriptor.h"
#include "services/audio_stream_service.h"
#include "services/control_plane_service.h"

#include "fsl_common.h"
#include "clock_config.h"

#ifndef CONTROLLER_ID
#define CONTROLLER_ID kUSB_ControllerEhci0
#endif

#define USB_DEVICE_INTERRUPT_PRIORITY (3U)

typedef struct _usb_vendor_bulk_state
{
    usb_device_handle deviceHandle;
    uint8_t speed;
    uint8_t attach;
    uint8_t currentConfiguration;
    uint8_t cmdInBusy;
    uint8_t audioInBusy;
    uint8_t isoAudioInBusy;
    uint8_t isoLoopbackPending;
    uint8_t pendingCmdResponseValid;
    uint16_t cmdPacketSize;
    uint16_t audioPacketSize;
    uint16_t isoAudioPacketSize;
    uint16_t isoAudioOutPacketSize;
    uint16_t isoLoopbackPendingLength;
    uint16_t pendingCmdResponseLength;
} usb_vendor_bulk_state_t;

USB_DMA_NONINIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_cmdOutBuffer[USB_VENDOR_BULK_CMD_HS_MPS];
USB_DMA_NONINIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_cmdInBuffer[USB_VENDOR_BULK_CMD_HS_MPS];
USB_DMA_NONINIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_cmdPendingResponseBuffer[USB_VENDOR_BULK_CMD_HS_MPS];
USB_DMA_NONINIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_audioOutBuffer[USB_VENDOR_BULK_AUDIO_HS_MPS];
USB_DMA_NONINIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_audioInBuffer[USB_VENDOR_BULK_AUDIO_HS_MPS];
USB_DMA_NONINIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_isoAudioInBuffer[USB_VENDOR_BULK_ISO_AUDIO_HS_MPS];
USB_DMA_NONINIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_isoAudioOutBuffer[USB_VENDOR_BULK_ISO_AUDIO_OUT_HS_MPS];
USB_DMA_NONINIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_isoLoopbackBuffer[USB_VENDOR_BULK_ISO_AUDIO_HS_MPS];

static usb_vendor_bulk_state_t s_vendorBulk;
volatile usb_vendor_bulk_debug_state_t g_UsbVendorBulkDebug;

static usb_status_t USB_VendorBulkDeviceCallback(usb_device_handle handle, uint32_t event, void *param);
static void USB_VendorBulkTrySendProtocolFrame(void);
static void USB_VendorBulkTrySendPendingCommandResponse(void);
static void USB_VendorBulkTrySendGeneratedAudioFrame(void);
static void USB_VendorBulkTrySendIsoAudioFrame(void);

static usb_device_class_config_list_struct_t s_vendorBulkClassConfigList = {
    .config = NULL,
    .deviceCallback = USB_VendorBulkDeviceCallback,
    .count = 0U,
};

extern void USB_DeviceIsrEnable(void);
#if USB_DEVICE_CONFIG_USE_TASK
extern void USB_DeviceTaskFn(void *deviceHandle);
#endif

static void USB_VendorBulkTrySendPendingCommandResponse(void)
{
    if (!s_vendorBulk.attach || s_vendorBulk.cmdInBusy || !s_vendorBulk.pendingCmdResponseValid)
    {
        return;
    }

    s_vendorBulk.cmdInBusy = 1U;
    s_vendorBulk.pendingCmdResponseValid = 0U;
    (void)USB_DeviceSendRequest(s_vendorBulk.deviceHandle,
                                USB_VENDOR_BULK_EP_CMD_IN,
                                s_cmdPendingResponseBuffer,
                                s_vendorBulk.pendingCmdResponseLength);
}

/* Bulk IN fallback for generated audio so legacy host tools (reading EP 0x82)
 * still receive frames when stream source is device-generated. */
static void USB_VendorBulkTrySendGeneratedAudioFrame(void)
{
    uint32_t txLen = 0U;

    if (!s_vendorBulk.attach || s_vendorBulk.audioInBusy)
    {
        return;
    }

    if (!AudioStreamService_TryBuildGeneratedPacket(s_audioInBuffer, sizeof(s_audioInBuffer), &txLen) ||
        (txLen == 0U))
    {
        return;
    }

    s_vendorBulk.audioInBusy = 1U;
    (void)USB_DeviceSendRequest(s_vendorBulk.deviceHandle,
                                USB_VENDOR_BULK_EP_AUDIO_IN,
                                s_audioInBuffer,
                                txLen);
}

/* Isochronous IN endpoint – re-primed every callback regardless of data availability.
 * A zero-length packet is sent when no audio data is ready so the host always
 * receives something in each scheduled microframe interval. A loopback packet
 * built from the iso OUT endpoint takes priority over generated audio so the
 * iso path can be exercised symmetrically (host -> device -> host), matching
 * the bulk AUDIO_OUT/AUDIO_IN loopback behavior. */
static void USB_VendorBulkTrySendIsoAudioFrame(void)
{
    uint32_t txLen = 0U;

    if (!s_vendorBulk.attach || s_vendorBulk.isoAudioInBusy)
    {
        return;
    }

    if (s_vendorBulk.isoLoopbackPending)
    {
        memcpy(s_isoAudioInBuffer, s_isoLoopbackBuffer, s_vendorBulk.isoLoopbackPendingLength);
        txLen = s_vendorBulk.isoLoopbackPendingLength;
        s_vendorBulk.isoLoopbackPending = 0U;
    }
    else
    {
        (void)AudioStreamService_TryBuildGeneratedPacket(s_isoAudioInBuffer, sizeof(s_isoAudioInBuffer), &txLen);
    }

    s_vendorBulk.isoAudioInBusy = 1U;
    (void)USB_DeviceSendRequest(s_vendorBulk.deviceHandle,
                                USB_VENDOR_BULK_EP_ISO_AUDIO_IN,
                                s_isoAudioInBuffer,
                                txLen);
}

/* Command endpoints are reserved for the control plane: commands in, responses/events out. */
static void USB_VendorBulkHandleCommandOutPacket(uint32_t messageLength)
{
    uint32_t txLen;

    if ((messageLength > 0U) && (messageLength <= sizeof(s_cmdOutBuffer)))
    {
        txLen = ControlPlaneService_HandleCommandPacket(s_cmdOutBuffer, messageLength, s_cmdInBuffer, sizeof(s_cmdInBuffer));

        if ((txLen > 0U) && !s_vendorBulk.cmdInBusy)
        {
            s_vendorBulk.cmdInBusy = 1U;
            (void)USB_DeviceSendRequest(s_vendorBulk.deviceHandle, USB_VENDOR_BULK_EP_CMD_IN, s_cmdInBuffer, txLen);
        }
        else if (txLen > 0U)
        {
            memcpy(s_cmdPendingResponseBuffer, s_cmdInBuffer, txLen);
            s_vendorBulk.pendingCmdResponseLength = (uint16_t)txLen;
            s_vendorBulk.pendingCmdResponseValid = 1U;
        }
        else if (txLen == 0U)
        {
            USB_VendorBulkTrySendProtocolFrame();
        }
    }

    (void)USB_DeviceRecvRequest(s_vendorBulk.deviceHandle, USB_VENDOR_BULK_EP_CMD_OUT, s_cmdOutBuffer,
                                s_vendorBulk.cmdPacketSize);
}

/* Audio endpoints are reserved for the data plane and must not carry command/event protocol frames. */
static void USB_VendorBulkHandleAudioOutPacket(uint32_t messageLength)
{
    uint32_t txLen;

    if (AudioStreamService_HandleRxPacket(s_audioOutBuffer,
                                          messageLength,
                                          s_audioInBuffer,
                                          sizeof(s_audioInBuffer),
                                          &txLen) && !s_vendorBulk.audioInBusy)
    {
        s_vendorBulk.audioInBusy = 1U;
        (void)USB_DeviceSendRequest(s_vendorBulk.deviceHandle, USB_VENDOR_BULK_EP_AUDIO_IN, s_audioInBuffer, txLen);
    }

    (void)USB_DeviceRecvRequest(s_vendorBulk.deviceHandle, USB_VENDOR_BULK_EP_AUDIO_OUT, s_audioOutBuffer,
                                s_vendorBulk.audioPacketSize);
}

/* Isochronous OUT endpoint – mirrors USB_VendorBulkHandleAudioOutPacket but stages the
 * loopback response in s_isoLoopbackBuffer for the next iso IN send instead of sending
 * immediately, since the iso IN endpoint must always stay pre-armed on its own schedule. */
static void USB_VendorBulkHandleIsoAudioOutPacket(uint32_t messageLength)
{
    uint32_t txLen;

    if (AudioStreamService_HandleRxPacket(s_isoAudioOutBuffer,
                                          messageLength,
                                          s_isoLoopbackBuffer,
                                          sizeof(s_isoLoopbackBuffer),
                                          &txLen))
    {
        s_vendorBulk.isoLoopbackPendingLength = (uint16_t)txLen;
        s_vendorBulk.isoLoopbackPending = 1U;
    }

    (void)USB_DeviceRecvRequest(s_vendorBulk.deviceHandle, USB_VENDOR_BULK_EP_ISO_AUDIO_OUT, s_isoAudioOutBuffer,
                                s_vendorBulk.isoAudioOutPacketSize);
}

static void USB_VendorBulkTrySendProtocolFrame(void)
{
    uint32_t txLen;

    if (!s_vendorBulk.attach || s_vendorBulk.cmdInBusy)
    {
        return;
    }

    txLen = ControlPlaneService_GetNextOutboundPacket(s_cmdInBuffer, sizeof(s_cmdInBuffer));
    if (txLen == 0U)
    {
        return;
    }

    s_vendorBulk.cmdInBusy = 1U;
    (void)USB_DeviceSendRequest(s_vendorBulk.deviceHandle, USB_VENDOR_BULK_EP_CMD_IN, s_cmdInBuffer, txLen);
}

static usb_status_t USB_VendorBulkEndpointCallback(usb_device_handle handle,
                                                   usb_device_endpoint_callback_message_struct_t *message,
                                                   void *callbackParam)
{
    uint8_t ep = (uint8_t)(uintptr_t)callbackParam;

    (void)handle;

    if (!s_vendorBulk.attach)
    {
        return kStatus_USB_Success;
    }

    switch (ep)
    {
        case USB_VENDOR_BULK_EP_CMD_OUT:
            USB_VendorBulkHandleCommandOutPacket(message->length);
            break;

        case USB_VENDOR_BULK_EP_AUDIO_OUT:
            USB_VendorBulkHandleAudioOutPacket(message->length);
            break;

        case USB_VENDOR_BULK_EP_ISO_AUDIO_OUT:
            USB_VendorBulkHandleIsoAudioOutPacket(message->length);
            break;

        case USB_VENDOR_BULK_EP_CMD_IN:
            s_vendorBulk.cmdInBusy = 0U;
            USB_VendorBulkTrySendPendingCommandResponse();
            if (!s_vendorBulk.cmdInBusy)
            {
                USB_VendorBulkTrySendProtocolFrame();
            }
            USB_VendorBulkTrySendGeneratedAudioFrame();
            break;

        case USB_VENDOR_BULK_EP_AUDIO_IN:
            s_vendorBulk.audioInBusy = 0U;
            USB_VendorBulkTrySendGeneratedAudioFrame();
            break;

        case USB_VENDOR_BULK_EP_ISO_AUDIO_IN:
            s_vendorBulk.isoAudioInBusy = 0U;
            USB_VendorBulkTrySendIsoAudioFrame();
            break;

        default:
            break;
    }

    return kStatus_USB_Success;
}

static usb_status_t USB_VendorBulkConfigureEndpoints(void)
{
    g_UsbVendorBulkDebug.stage = 0x40U;

    usb_device_endpoint_init_struct_t epInit;
    usb_device_endpoint_callback_struct_t epCb;

    epCb.callbackFn = USB_VendorBulkEndpointCallback;
    epCb.isBusy     = 0U;

    s_vendorBulk.cmdPacketSize   = (s_vendorBulk.speed == USB_SPEED_HIGH) ? USB_VENDOR_BULK_CMD_HS_MPS        : USB_VENDOR_BULK_CMD_FS_MPS;
    s_vendorBulk.audioPacketSize  = (s_vendorBulk.speed == USB_SPEED_HIGH) ? USB_VENDOR_BULK_AUDIO_HS_MPS      : USB_VENDOR_BULK_AUDIO_FS_MPS;
    s_vendorBulk.isoAudioPacketSize = (s_vendorBulk.speed == USB_SPEED_HIGH) ? USB_VENDOR_BULK_ISO_AUDIO_HS_MPS : USB_VENDOR_BULK_ISO_AUDIO_FS_MPS;
    s_vendorBulk.isoAudioOutPacketSize = (s_vendorBulk.speed == USB_SPEED_HIGH) ? USB_VENDOR_BULK_ISO_AUDIO_OUT_HS_MPS : USB_VENDOR_BULK_ISO_AUDIO_OUT_FS_MPS;

    epInit.zlt = 0U;
    epInit.interval = 0U;

    epInit.transferType = USB_ENDPOINT_BULK;

    epInit.endpointAddress = USB_VENDOR_BULK_EP_CMD_OUT;
    epInit.maxPacketSize = s_vendorBulk.cmdPacketSize;
    epCb.callbackParam = (void *)(uintptr_t)USB_VENDOR_BULK_EP_CMD_OUT;
    if (USB_DeviceInitEndpoint(s_vendorBulk.deviceHandle, &epInit, &epCb) != kStatus_USB_Success)
    {
        g_UsbVendorBulkDebug.lastStatus = (uint32_t)kStatus_USB_Error;
        return kStatus_USB_Error;
    }

    epInit.endpointAddress = USB_VENDOR_BULK_EP_CMD_IN;
    epInit.maxPacketSize = s_vendorBulk.cmdPacketSize;
    epCb.callbackParam = (void *)(uintptr_t)USB_VENDOR_BULK_EP_CMD_IN;
    if (USB_DeviceInitEndpoint(s_vendorBulk.deviceHandle, &epInit, &epCb) != kStatus_USB_Success)
    {
        g_UsbVendorBulkDebug.lastStatus = (uint32_t)kStatus_USB_Error;
        return kStatus_USB_Error;
    }

    epInit.endpointAddress = USB_VENDOR_BULK_EP_AUDIO_OUT;
    epInit.maxPacketSize = s_vendorBulk.audioPacketSize;
    epCb.callbackParam = (void *)(uintptr_t)USB_VENDOR_BULK_EP_AUDIO_OUT;
    if (USB_DeviceInitEndpoint(s_vendorBulk.deviceHandle, &epInit, &epCb) != kStatus_USB_Success)
    {
        g_UsbVendorBulkDebug.lastStatus = (uint32_t)kStatus_USB_Error;
        return kStatus_USB_Error;
    }

    epInit.endpointAddress = USB_VENDOR_BULK_EP_AUDIO_IN;
    epInit.maxPacketSize = s_vendorBulk.audioPacketSize;
    epCb.callbackParam = (void *)(uintptr_t)USB_VENDOR_BULK_EP_AUDIO_IN;
    if (USB_DeviceInitEndpoint(s_vendorBulk.deviceHandle, &epInit, &epCb) != kStatus_USB_Success)
    {
        g_UsbVendorBulkDebug.lastStatus = (uint32_t)kStatus_USB_Error;
        return kStatus_USB_Error;
    }

    /* Isochronous async IN endpoint */
    epInit.transferType    = USB_ENDPOINT_ISOCHRONOUS;
    epInit.endpointAddress = USB_VENDOR_BULK_EP_ISO_AUDIO_IN;
    epInit.maxPacketSize   = s_vendorBulk.isoAudioPacketSize;
    epInit.interval        = (s_vendorBulk.speed == USB_SPEED_HIGH) ? 5U : 1U;
    epCb.callbackParam     = (void *)(uintptr_t)USB_VENDOR_BULK_EP_ISO_AUDIO_IN;
    if (USB_DeviceInitEndpoint(s_vendorBulk.deviceHandle, &epInit, &epCb) != kStatus_USB_Success)
    {
        g_UsbVendorBulkDebug.lastStatus = (uint32_t)kStatus_USB_Error;
        return kStatus_USB_Error;
    }

    /* Isochronous async OUT endpoint */
    epInit.transferType    = USB_ENDPOINT_ISOCHRONOUS;
    epInit.endpointAddress = USB_VENDOR_BULK_EP_ISO_AUDIO_OUT;
    epInit.maxPacketSize   = s_vendorBulk.isoAudioOutPacketSize;
    epInit.interval        = (s_vendorBulk.speed == USB_SPEED_HIGH) ? 5U : 1U;
    epCb.callbackParam     = (void *)(uintptr_t)USB_VENDOR_BULK_EP_ISO_AUDIO_OUT;
    if (USB_DeviceInitEndpoint(s_vendorBulk.deviceHandle, &epInit, &epCb) != kStatus_USB_Success)
    {
        g_UsbVendorBulkDebug.lastStatus = (uint32_t)kStatus_USB_Error;
        return kStatus_USB_Error;
    }

    (void)USB_DeviceRecvRequest(s_vendorBulk.deviceHandle, USB_VENDOR_BULK_EP_CMD_OUT, s_cmdOutBuffer,
                                s_vendorBulk.cmdPacketSize);
    (void)USB_DeviceRecvRequest(s_vendorBulk.deviceHandle, USB_VENDOR_BULK_EP_AUDIO_OUT, s_audioOutBuffer,
                                s_vendorBulk.audioPacketSize);
    (void)USB_DeviceRecvRequest(s_vendorBulk.deviceHandle, USB_VENDOR_BULK_EP_ISO_AUDIO_OUT, s_isoAudioOutBuffer,
                                s_vendorBulk.isoAudioOutPacketSize);

    /* Prime the isochronous IN endpoint – must always be pre-queued */
    USB_VendorBulkTrySendIsoAudioFrame();

    g_UsbVendorBulkDebug.stage = 0x4FU;

    return kStatus_USB_Success;
}

static usb_status_t USB_VendorBulkDeviceCallback(usb_device_handle handle, uint32_t event, void *param)
{
    uint8_t *temp8 = (uint8_t *)param;
    uint16_t *temp16 = (uint16_t *)param;

    g_UsbVendorBulkDebug.lastEvent = event;
    g_UsbVendorBulkDebug.deviceHandle = (uintptr_t)handle;

    switch (event)
    {
        case kUSB_DeviceEventBusReset:
            g_UsbVendorBulkDebug.stage = 0x20U;
            g_UsbVendorBulkDebug.stage = 0x21U;
            s_vendorBulk.attach = 0U;
            s_vendorBulk.currentConfiguration = 0U;
            s_vendorBulk.cmdInBusy = 0U;
            s_vendorBulk.audioInBusy = 0U;
            s_vendorBulk.isoAudioInBusy = 0U;
            s_vendorBulk.isoLoopbackPending = 0U;
            s_vendorBulk.isoLoopbackPendingLength = 0U;
            s_vendorBulk.pendingCmdResponseValid = 0U;
            s_vendorBulk.pendingCmdResponseLength = 0U;
            AudioStreamService_OnTransportReset();
            ControlPlaneService_OnTransportReset();
            if (USB_DeviceGetStatus(handle, kUSB_DeviceStatusSpeed, &s_vendorBulk.speed) == kStatus_USB_Success)
            {
                g_UsbVendorBulkDebug.usbSpeed = s_vendorBulk.speed;
                (void)USB_VendorBulkSetSpeed(s_vendorBulk.speed);
            }
            g_UsbVendorBulkDebug.stage = 0x2FU;
            return kStatus_USB_Success;

        case kUSB_DeviceEventSetConfiguration:
            g_UsbVendorBulkDebug.stage = 0x30U;
            if (param == NULL)
            {
                return kStatus_USB_InvalidRequest;
            }

            if (*temp8 == 0U)
            {
                s_vendorBulk.attach = 0U;
                s_vendorBulk.currentConfiguration = 0U;
                s_vendorBulk.pendingCmdResponseValid = 0U;
                s_vendorBulk.pendingCmdResponseLength = 0U;
                AudioStreamService_OnTransportReset();
                ControlPlaneService_OnTransportReset();
                return kStatus_USB_Success;
            }

            if (*temp8 == USB_VENDOR_BULK_CONFIG_INDEX)
            {
                usb_status_t status;

                s_vendorBulk.attach = 1U;
                s_vendorBulk.currentConfiguration = *temp8;
                status = USB_VendorBulkConfigureEndpoints();
                if (status == kStatus_USB_Success)
                {
                    AudioStreamService_OnTransportReady(s_vendorBulk.audioPacketSize);
                    ControlPlaneService_OnTransportReady(s_vendorBulk.cmdPacketSize);
                    USB_VendorBulkTrySendProtocolFrame();
                }
                return status;
            }
            return kStatus_USB_InvalidRequest;

        case kUSB_DeviceEventSetInterface:
            (void)temp16;
            return kStatus_USB_Success;

        case kUSB_DeviceEventGetConfiguration:
            if (param != NULL)
            {
                *temp8 = s_vendorBulk.currentConfiguration;
                return kStatus_USB_Success;
            }
            return kStatus_USB_InvalidRequest;

        case kUSB_DeviceEventGetInterface:
            if (param != NULL)
            {
                *temp16 = (*temp16 & 0xFF00U);
                return kStatus_USB_Success;
            }
            return kStatus_USB_InvalidRequest;

        case kUSB_DeviceEventGetDeviceDescriptor:
            g_UsbVendorBulkDebug.stage = 0x61U;
            return USB_VendorBulkGetDeviceDescriptor(handle, (usb_device_get_device_descriptor_struct_t *)param);

        case kUSB_DeviceEventGetConfigurationDescriptor:
            g_UsbVendorBulkDebug.stage = 0x62U;
            return USB_VendorBulkGetConfigurationDescriptor(handle,
                (usb_device_get_configuration_descriptor_struct_t *)param);

        case kUSB_DeviceEventGetStringDescriptor:
            g_UsbVendorBulkDebug.stage = 0x63U;
            return USB_VendorBulkGetStringDescriptor(handle, (usb_device_get_string_descriptor_struct_t *)param);

        case kUSB_DeviceEventGetDeviceQualifierDescriptor:
            return USB_VendorBulkGetDeviceQualifierDescriptor(handle,
                (usb_device_get_device_qualifier_descriptor_struct_t *)param);

        case kUSB_DeviceEventGetBOSDescriptor:
            return USB_VendorBulkGetBOSDescriptor(handle, (usb_device_get_bos_descriptor_struct_t *)param);

        case kUSB_DeviceEventVendorRequest:
            return USB_VendorBulkHandleVendorRequest((usb_device_control_request_struct_t *)param);

        default:
            return kStatus_USB_Success;
    }
}

void USB_VendorBulkApplicationInit(void)
{
    usb_status_t status;

    g_UsbVendorBulkDebug.stage = 0x01U;
    g_UsbVendorBulkDebug.lastEvent = 0U;
    g_UsbVendorBulkDebug.lastStatus = 0U;
    g_UsbVendorBulkDebug.usbSpeed = USB_SPEED_FULL;
    g_UsbVendorBulkDebug.deviceHandle = 0U;
    g_UsbVendorBulkDebug.ch9Stage = 0U;
    g_UsbVendorBulkDebug.setupBmRequestType = 0U;
    g_UsbVendorBulkDebug.setupBRequest = 0U;
    g_UsbVendorBulkDebug.setupWValue = 0U;
    g_UsbVendorBulkDebug.setupWIndex = 0U;
    g_UsbVendorBulkDebug.setupWLength = 0U;

    s_vendorBulk.speed = USB_SPEED_FULL;
    s_vendorBulk.attach = 0U;
    s_vendorBulk.currentConfiguration = 0U;
    s_vendorBulk.cmdInBusy = 0U;
    s_vendorBulk.audioInBusy = 0U;
    s_vendorBulk.pendingCmdResponseValid = 0U;
    s_vendorBulk.pendingCmdResponseLength = 0U;
    s_vendorBulk.deviceHandle = NULL;

    g_UsbVendorBulkDebug.stage = 0x02U;
    status = USB_DeviceClassInit(CONTROLLER_ID, &s_vendorBulkClassConfigList, &s_vendorBulk.deviceHandle);
    g_UsbVendorBulkDebug.lastStatus = (uint32_t)status;
    g_UsbVendorBulkDebug.deviceHandle = (uintptr_t)s_vendorBulk.deviceHandle;
    if (status != kStatus_USB_Success)
    {
        g_UsbVendorBulkDebug.stage = 0xE1U;
        return;
    }

    g_UsbVendorBulkDebug.stage = 0x03U;
    USB_DeviceIsrEnable();
    SDK_DelayAtLeastUs(5000U, SDK_DEVICE_MAXIMUM_CPU_CLOCK_FREQUENCY);
    g_UsbVendorBulkDebug.stage = 0x04U;
    status = USB_DeviceRun(s_vendorBulk.deviceHandle);
    g_UsbVendorBulkDebug.lastStatus = (uint32_t)status;
    if (status != kStatus_USB_Success)
    {
        g_UsbVendorBulkDebug.stage = 0xE2U;
        return;
    }

    g_UsbVendorBulkDebug.stage = 0x05U;
}

usb_device_handle USB_VendorBulkGetDeviceHandle(void)
{
    return s_vendorBulk.deviceHandle;
}

void USB_VendorBulkNotifyTxPending(void)
{
    USB_VendorBulkTrySendProtocolFrame();
}

void USB_VendorBulkNotifyAudioTxPending(void)
{
    USB_VendorBulkTrySendIsoAudioFrame();
}
