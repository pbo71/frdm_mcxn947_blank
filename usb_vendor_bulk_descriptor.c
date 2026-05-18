#include "usb_device_config.h"
#include "usb.h"
#include "usb_device.h"
#include "usb_device_class.h"
#include "usb_vendor_bulk_descriptor.h"

#define USB_VENDOR_BULK_MAX_POWER (0x32U)

#define USB_VENDOR_BULK_MS_OS_20_SET_TOTAL_LENGTH    (0x00B2U)
#define USB_VENDOR_BULK_MS_OS_20_CONFIG_TOTAL_LENGTH (0x00A8U)
#define USB_VENDOR_BULK_MS_OS_20_FUNC_TOTAL_LENGTH   (0x00A0U)
#define USB_VENDOR_BULK_MS_OS_20_REG_PROP_NAME_LEN   (0x002AU)
#define USB_VENDOR_BULK_MS_OS_20_REG_PROP_DATA_LEN   (0x0050U)

USB_DMA_INIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_deviceDescriptor[] = {
    USB_DESCRIPTOR_LENGTH_DEVICE,
    USB_DESCRIPTOR_TYPE_DEVICE,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_DEVICE_SPEC_BCD),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_DEVICE_SPEC_BCD),
    0x00U,
    0x00U,
    0x00U,
    USB_CONTROL_MAX_PACKET_SIZE,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_VID),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_VID),
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_PID),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_PID),
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_DEVICE_BCD),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_DEVICE_BCD),
    0x01U,
    0x02U,
    0x00U,
    USB_VENDOR_BULK_CONFIG_COUNT,
};

USB_DMA_INIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_configDescriptorFs[] = {
    USB_DESCRIPTOR_LENGTH_CONFIGURE,
    USB_DESCRIPTOR_TYPE_CONFIGURE,
    USB_SHORT_GET_LOW(53U),
    USB_SHORT_GET_HIGH(53U),
    USB_VENDOR_BULK_INTERFACE_CNT,
    USB_VENDOR_BULK_CONFIG_INDEX,
    0x00U,
    USB_DESCRIPTOR_CONFIGURE_ATTRIBUTE_D7_MASK |
#if defined(USB_DEVICE_CONFIG_SELF_POWER) && (USB_DEVICE_CONFIG_SELF_POWER > 0U)
        (1U << USB_DESCRIPTOR_CONFIGURE_ATTRIBUTE_SELF_POWERED_SHIFT) |
#endif
#if defined(USB_DEVICE_CONFIG_REMOTE_WAKEUP) && (USB_DEVICE_CONFIG_REMOTE_WAKEUP > 0U)
        (1U << USB_DESCRIPTOR_CONFIGURE_ATTRIBUTE_REMOTE_WAKEUP_SHIFT) |
#endif
        0U,
    USB_VENDOR_BULK_MAX_POWER,

    USB_DESCRIPTOR_LENGTH_INTERFACE,
    USB_DESCRIPTOR_TYPE_INTERFACE,
    USB_VENDOR_BULK_INTERFACE_IDX,
    0x00U,
    USB_VENDOR_BULK_ENDPOINT_COUNT,
    0xFFU,
    0x00U,
    0x00U,
    0x03U,

    USB_DESCRIPTOR_LENGTH_ENDPOINT,
    USB_DESCRIPTOR_TYPE_ENDPOINT,
    USB_VENDOR_BULK_EP_CMD_OUT,
    USB_ENDPOINT_BULK,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_CMD_FS_MPS),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_CMD_FS_MPS),
    0x00U,

    USB_DESCRIPTOR_LENGTH_ENDPOINT,
    USB_DESCRIPTOR_TYPE_ENDPOINT,
    USB_VENDOR_BULK_EP_CMD_IN,
    USB_ENDPOINT_BULK,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_CMD_FS_MPS),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_CMD_FS_MPS),
    0x00U,

    USB_DESCRIPTOR_LENGTH_ENDPOINT,
    USB_DESCRIPTOR_TYPE_ENDPOINT,
    USB_VENDOR_BULK_EP_AUDIO_OUT,
    USB_ENDPOINT_BULK,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_AUDIO_FS_MPS),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_AUDIO_FS_MPS),
    0x00U,

    USB_DESCRIPTOR_LENGTH_ENDPOINT,
    USB_DESCRIPTOR_TYPE_ENDPOINT,
    USB_VENDOR_BULK_EP_AUDIO_IN,
    USB_ENDPOINT_BULK,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_AUDIO_FS_MPS),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_AUDIO_FS_MPS),
    0x00U,

    /* Isochronous async IN – custom audio stream (device → host) */
    USB_DESCRIPTOR_LENGTH_ENDPOINT,
    USB_DESCRIPTOR_TYPE_ENDPOINT,
    USB_VENDOR_BULK_EP_ISO_AUDIO_IN,
    USB_ENDPOINT_ISOCHRONOUS | 0x04U, /* bmAttributes: iso + async (bits 3:2 = 01) */
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_ISO_AUDIO_FS_MPS),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_ISO_AUDIO_FS_MPS),
    0x01U, /* bInterval = 1 → every 1 ms frame (FS) */
};

USB_DMA_INIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_configDescriptorHs[] = {
    USB_DESCRIPTOR_LENGTH_CONFIGURE,
    USB_DESCRIPTOR_TYPE_CONFIGURE,
    USB_SHORT_GET_LOW(53U),
    USB_SHORT_GET_HIGH(53U),
    USB_VENDOR_BULK_INTERFACE_CNT,
    USB_VENDOR_BULK_CONFIG_INDEX,
    0x00U,
    USB_DESCRIPTOR_CONFIGURE_ATTRIBUTE_D7_MASK |
#if defined(USB_DEVICE_CONFIG_SELF_POWER) && (USB_DEVICE_CONFIG_SELF_POWER > 0U)
        (1U << USB_DESCRIPTOR_CONFIGURE_ATTRIBUTE_SELF_POWERED_SHIFT) |
#endif
#if defined(USB_DEVICE_CONFIG_REMOTE_WAKEUP) && (USB_DEVICE_CONFIG_REMOTE_WAKEUP > 0U)
        (1U << USB_DESCRIPTOR_CONFIGURE_ATTRIBUTE_REMOTE_WAKEUP_SHIFT) |
#endif
        0U,
    USB_VENDOR_BULK_MAX_POWER,

    USB_DESCRIPTOR_LENGTH_INTERFACE,
    USB_DESCRIPTOR_TYPE_INTERFACE,
    USB_VENDOR_BULK_INTERFACE_IDX,
    0x00U,
    USB_VENDOR_BULK_ENDPOINT_COUNT,
    0xFFU,
    0x00U,
    0x00U,
    0x03U,

    USB_DESCRIPTOR_LENGTH_ENDPOINT,
    USB_DESCRIPTOR_TYPE_ENDPOINT,
    USB_VENDOR_BULK_EP_CMD_OUT,
    USB_ENDPOINT_BULK,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_CMD_HS_MPS),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_CMD_HS_MPS),
    0x00U,

    USB_DESCRIPTOR_LENGTH_ENDPOINT,
    USB_DESCRIPTOR_TYPE_ENDPOINT,
    USB_VENDOR_BULK_EP_CMD_IN,
    USB_ENDPOINT_BULK,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_CMD_HS_MPS),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_CMD_HS_MPS),
    0x00U,

    USB_DESCRIPTOR_LENGTH_ENDPOINT,
    USB_DESCRIPTOR_TYPE_ENDPOINT,
    USB_VENDOR_BULK_EP_AUDIO_OUT,
    USB_ENDPOINT_BULK,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_AUDIO_HS_MPS),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_AUDIO_HS_MPS),
    0x00U,

    USB_DESCRIPTOR_LENGTH_ENDPOINT,
    USB_DESCRIPTOR_TYPE_ENDPOINT,
    USB_VENDOR_BULK_EP_AUDIO_IN,
    USB_ENDPOINT_BULK,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_AUDIO_HS_MPS),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_AUDIO_HS_MPS),
    0x00U,

    /* Isochronous async IN – custom audio stream (device → host) */
    USB_DESCRIPTOR_LENGTH_ENDPOINT,
    USB_DESCRIPTOR_TYPE_ENDPOINT,
    USB_VENDOR_BULK_EP_ISO_AUDIO_IN,
    USB_ENDPOINT_ISOCHRONOUS | 0x04U, /* bmAttributes: iso + async (bits 3:2 = 01) */
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_ISO_AUDIO_HS_MPS),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_ISO_AUDIO_HS_MPS),
    0x04U, /* bInterval = 4 → 2^(4-1) = 8 microframes = 1 ms period (HS) */
};

USB_DMA_INIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_bosDescriptor[] = {
    0x05U,
    USB_DESCRIPTOR_TYPE_BOS,
    0x21U,
    0x00U,
    0x01U,

    0x1CU,
    USB_DESCRIPTOR_TYPE_DEVICE_CAPABILITY,
    0x05U,
    0x00U,
    0xD8U,
    0xDDU,
    0x60U,
    0xDFU,
    0x45U,
    0x89U,
    0xC7U,
    0x4CU,
    0x9CU,
    0xD2U,
    0x65U,
    0x9DU,
    0x9EU,
    0x64U,
    0x8AU,
    0x9FU,
    0x00U,
    0x00U,
    0x03U,
    0x06U,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_MS_OS_20_SET_TOTAL_LENGTH),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_MS_OS_20_SET_TOTAL_LENGTH),
    USB_VENDOR_BULK_MS_VENDOR_CODE,
    0x00U,
};

USB_DMA_INIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_msOs20DescriptorSet[] = {
    0x0AU,
    0x00U,
    0x00U,
    0x00U,
    0x00U,
    0x00U,
    0x03U,
    0x06U,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_MS_OS_20_SET_TOTAL_LENGTH),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_MS_OS_20_SET_TOTAL_LENGTH),

    0x08U,
    0x00U,
    0x01U,
    0x00U,
    USB_VENDOR_BULK_CONFIG_INDEX,
    0x00U,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_MS_OS_20_CONFIG_TOTAL_LENGTH),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_MS_OS_20_CONFIG_TOTAL_LENGTH),

    0x08U,
    0x00U,
    0x02U,
    0x00U,
    USB_VENDOR_BULK_INTERFACE_IDX,
    0x00U,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_MS_OS_20_FUNC_TOTAL_LENGTH),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_MS_OS_20_FUNC_TOTAL_LENGTH),

    0x14U,
    0x00U,
    0x03U,
    0x00U,
    'W',
    'I',
    'N',
    'U',
    'S',
    'B',
    0x00U,
    0x00U,
    0x00U,
    0x00U,
    0x00U,
    0x00U,
    0x00U,
    0x00U,

    0x84U,
    0x00U,
    0x04U,
    0x00U,
    0x07U,
    0x00U,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_MS_OS_20_REG_PROP_NAME_LEN),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_MS_OS_20_REG_PROP_NAME_LEN),
    'D',
    0x00U,
    'e',
    0x00U,
    'v',
    0x00U,
    'i',
    0x00U,
    'c',
    0x00U,
    'e',
    0x00U,
    'I',
    0x00U,
    'n',
    0x00U,
    't',
    0x00U,
    'e',
    0x00U,
    'r',
    0x00U,
    'f',
    0x00U,
    'a',
    0x00U,
    'c',
    0x00U,
    'e',
    0x00U,
    'G',
    0x00U,
    'U',
    0x00U,
    'I',
    0x00U,
    'D',
    0x00U,
    's',
    0x00U,
    0x00U,
    0x00U,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_MS_OS_20_REG_PROP_DATA_LEN),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_MS_OS_20_REG_PROP_DATA_LEN),
    '{',
    0x00U,
    'D',
    0x00U,
    '5',
    0x00U,
    '9',
    0x00U,
    '5',
    0x00U,
    '9',
    0x00U,
    '8',
    0x00U,
    '0',
    0x00U,
    '1',
    0x00U,
    '-',
    0x00U,
    '4',
    0x00U,
    '5',
    0x00U,
    'C',
    0x00U,
    '1',
    0x00U,
    '-',
    0x00U,
    '4',
    0x00U,
    '9',
    0x00U,
    'D',
    0x00U,
    'B',
    0x00U,
    '-',
    0x00U,
    '9',
    0x00U,
    '0',
    0x00U,
    '5',
    0x00U,
    '3',
    0x00U,
    '-',
    0x00U,
    '8',
    0x00U,
    '9',
    0x00U,
    'B',
    0x00U,
    '5',
    0x00U,
    '3',
    0x00U,
    '6',
    0x00U,
    '5',
    0x00U,
    'C',
    0x00U,
    '2',
    0x00U,
    '8',
    0x00U,
    '0',
    0x00U,
    '0',
    0x00U,
    '}',
    0x00U,
    0x00U,
    0x00U,
    0x00U,
};

#if (defined(USB_DEVICE_CONFIG_CV_TEST) && (USB_DEVICE_CONFIG_CV_TEST > 0U))
USB_DMA_INIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_deviceQualifierDescriptor[] = {
    USB_DESCRIPTOR_LENGTH_DEVICE_QUALITIER,
    USB_DESCRIPTOR_TYPE_DEVICE_QUALITIER,
    USB_SHORT_GET_LOW(USB_VENDOR_BULK_DEVICE_SPEC_BCD),
    USB_SHORT_GET_HIGH(USB_VENDOR_BULK_DEVICE_SPEC_BCD),
    0x00U,
    0x00U,
    0x00U,
    USB_CONTROL_MAX_PACKET_SIZE,
    USB_VENDOR_BULK_CONFIG_COUNT,
    0x00U,
};
#endif

USB_DMA_INIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_string0[] = {4U, USB_DESCRIPTOR_TYPE_STRING, 0x09U, 0x04U};

USB_DMA_INIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_string1[] = {
    2U + 2U * 12U, USB_DESCRIPTOR_TYPE_STRING,
    'P', 0, 'e', 0, 'r', 0, 'B', 0, 'o', 0, ' ', 0,
    'L', 0, 'a', 0, 'b', 0, 's', 0, ' ', 0, 'A', 0,
};

USB_DMA_INIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_string2[] = {
    2U + 2U * 17U, USB_DESCRIPTOR_TYPE_STRING,
    'V', 0, 'e', 0, 'n', 0, 'd', 0, 'o', 0, 'r', 0, ' ', 0,
    'B', 0, 'u', 0, 'l', 0, 'k', 0, ' ', 0, 'A', 0,
    'u', 0, 'd', 0, 'i', 0, 'o', 0,
};

USB_DMA_INIT_DATA_ALIGN(USB_DATA_ALIGN_SIZE)
static uint8_t s_string3[] = {
    2U + 2U * 20U, USB_DESCRIPTOR_TYPE_STRING,
    'A', 0, 'u', 0, 'd', 0, 'i', 0, 'o', 0, ' ', 0,
    'B', 0, 'u', 0, 'l', 0, 'k', 0, ' ', 0,
    'I', 0, 'n', 0, 't', 0, 'e', 0, 'r', 0, 'f', 0, 'a', 0, 'c', 0, 'e', 0,
};

static uint32_t s_stringLengths[USB_VENDOR_BULK_STRING_COUNT] = {
    sizeof(s_string0),
    sizeof(s_string1),
    sizeof(s_string2),
    sizeof(s_string3),
};

static uint8_t *s_stringTable[USB_VENDOR_BULK_STRING_COUNT] = {
    s_string0,
    s_string1,
    s_string2,
    s_string3,
};

static usb_language_t s_languageList[USB_VENDOR_BULK_LANGUAGE_COUNT] = {{
    s_stringTable,
    s_stringLengths,
    0x0409U,
}};

static usb_language_list_t s_language = {
    s_string0,
    sizeof(s_string0),
    s_languageList,
    USB_VENDOR_BULK_LANGUAGE_COUNT,
};

static uint8_t s_currentSpeed = USB_SPEED_FULL;

usb_status_t USB_VendorBulkSetSpeed(uint8_t speed)
{
    s_currentSpeed = speed;
    return kStatus_USB_Success;
}

usb_status_t USB_VendorBulkGetDeviceDescriptor(usb_device_handle handle,
                                               usb_device_get_device_descriptor_struct_t *deviceDescriptor)
{
    (void)handle;
    deviceDescriptor->buffer = s_deviceDescriptor;
    deviceDescriptor->length = sizeof(s_deviceDescriptor);
    return kStatus_USB_Success;
}

usb_status_t USB_VendorBulkGetConfigurationDescriptor(
    usb_device_handle handle,
    usb_device_get_configuration_descriptor_struct_t *configurationDescriptor)
{
    (void)handle;
    if (configurationDescriptor->configuration > 0U)
    {
        return kStatus_USB_InvalidRequest;
    }

    if (s_currentSpeed == USB_SPEED_HIGH)
    {
        configurationDescriptor->buffer = s_configDescriptorHs;
        configurationDescriptor->length = sizeof(s_configDescriptorHs);
    }
    else
    {
        configurationDescriptor->buffer = s_configDescriptorFs;
        configurationDescriptor->length = sizeof(s_configDescriptorFs);
    }

    return kStatus_USB_Success;
}

usb_status_t USB_VendorBulkGetStringDescriptor(usb_device_handle handle,
                                               usb_device_get_string_descriptor_struct_t *stringDescriptor)
{
    (void)handle;

    if (stringDescriptor->stringIndex == 0U)
    {
        stringDescriptor->buffer = s_language.languageString;
        stringDescriptor->length = s_language.stringLength;
        return kStatus_USB_Success;
    }

    if ((stringDescriptor->stringIndex >= USB_VENDOR_BULK_STRING_COUNT) ||
        (stringDescriptor->languageId != s_language.languageList[0].languageId))
    {
        return kStatus_USB_InvalidRequest;
    }

    stringDescriptor->buffer = s_language.languageList[0].string[stringDescriptor->stringIndex];
    stringDescriptor->length = s_language.languageList[0].length[stringDescriptor->stringIndex];
    return kStatus_USB_Success;
}

usb_status_t USB_VendorBulkGetDeviceQualifierDescriptor(
    usb_device_handle handle,
    usb_device_get_device_qualifier_descriptor_struct_t *deviceQualifierDescriptor)
{
    (void)handle;
#if (defined(USB_DEVICE_CONFIG_CV_TEST) && (USB_DEVICE_CONFIG_CV_TEST > 0U))
    deviceQualifierDescriptor->buffer = s_deviceQualifierDescriptor;
    deviceQualifierDescriptor->length = sizeof(s_deviceQualifierDescriptor);
    return kStatus_USB_Success;
#else
    (void)deviceQualifierDescriptor;
    return kStatus_USB_InvalidRequest;
#endif
}

usb_status_t USB_VendorBulkGetBOSDescriptor(usb_device_handle handle,
                                            usb_device_get_bos_descriptor_struct_t *bosDescriptor)
{
    (void)handle;
    bosDescriptor->buffer = s_bosDescriptor;
    bosDescriptor->length = sizeof(s_bosDescriptor);
    return kStatus_USB_Success;
}

usb_status_t USB_VendorBulkHandleVendorRequest(usb_device_control_request_struct_t *controlRequest)
{
    usb_setup_struct_t *setup;

    if ((controlRequest == NULL) || (controlRequest->setup == NULL))
    {
        return kStatus_USB_InvalidRequest;
    }

    if (!controlRequest->isSetup)
    {
        return kStatus_USB_InvalidRequest;
    }

    setup = controlRequest->setup;

    if (((setup->bmRequestType & USB_REQUEST_TYPE_TYPE_MASK) != USB_REQUEST_TYPE_TYPE_VENDOR) ||
        ((setup->bmRequestType & USB_REQUEST_TYPE_DIR_MASK) != USB_REQUEST_TYPE_DIR_IN) ||
        ((setup->bmRequestType & USB_REQUEST_TYPE_RECIPIENT_MASK) != USB_REQUEST_TYPE_RECIPIENT_DEVICE))
    {
        return kStatus_USB_InvalidRequest;
    }

    if ((setup->bRequest != USB_VENDOR_BULK_MS_VENDOR_CODE) || (setup->wIndex != USB_VENDOR_BULK_MS_OS_20_INDEX))
    {
        return kStatus_USB_InvalidRequest;
    }

    controlRequest->buffer = s_msOs20DescriptorSet;
    controlRequest->length = sizeof(s_msOs20DescriptorSet);
    if (controlRequest->length > setup->wLength)
    {
        controlRequest->length = setup->wLength;
    }
    return kStatus_USB_Success;
}
