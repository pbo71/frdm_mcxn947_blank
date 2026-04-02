#ifndef DEVICE_PROTOCOL_PROTOCOL_HPP
#define DEVICE_PROTOCOL_PROTOCOL_HPP

#include <cstdint>

#if defined(__GNUC__)
#define DEVICE_PROTOCOL_PACKED __attribute__((packed))
#else
#define DEVICE_PROTOCOL_PACKED
#endif

namespace device_protocol {

constexpr uint16_t kFrameMagic = 0x4153U;
constexpr uint8_t kProtocolVersion = 1U;

enum class FrameType : uint8_t
{
    Command = 1U,
    Response = 2U,
    Event = 3U,
};

enum class StatusCode : uint8_t
{
    Ok = 0U,
    InvalidMagic = 1U,
    InvalidVersion = 2U,
    InvalidType = 3U,
    InvalidLength = 4U,
    UnsupportedCommand = 5U,
    InvalidArgument = 6U,
};

enum class CommandId : uint8_t
{
    GetInfo = 1U,
    SetLed = 2U,
    GetUsbDebugState = 3U,
    Ping = 4U,
    StartStream = 5U,
    StopStream = 6U,
    SetGeneratorConfig = 7U,
    I2cWriteRegister = 8U,
    I2cReadRegister = 9U,
};

enum class EventId : uint8_t
{
    DeviceReady = 1U,
    StreamStarted = 2U,
    StreamStopped = 3U,
    Fault = 4U,
};

enum class FaultCode : uint32_t
{
    StreamUnderrun = 1U,
    UsbTransferError = 2U,
    InternalError = 3U,
};

enum class LedAction : uint8_t
{
    Off = 0U,
    On = 1U,
    Toggle = 2U,
};

enum class StartStreamSource : uint8_t
{
    HostRxLoopback = 0U,
    DeviceGeneratedSine = 1U,
    DeviceGeneratedChirp = 2U,
    DeviceGeneratedNoise = 3U,
};

enum class GeneratorNoiseType : uint8_t
{
    White = 0U,
    SampleHold = 1U,
    Binary = 2U,
};

enum class GeneratorAmplitudeEnvelope : uint8_t
{
    Constant = 0U,
    FadeIn = 1U,
    FadeOut = 2U,
    Triangle = 3U,
};

constexpr const char *kPingPayloadDescription = "Response payload echoes the command payload bytes unchanged.";

constexpr uint32_t kCapabilityCommandResponse = (1UL << 0U);
constexpr uint32_t kCapabilityAsyncEvents = (1UL << 1U);
constexpr uint32_t kCapabilityLedControl = (1UL << 2U);
constexpr uint32_t kCapabilityUsbDebug = (1UL << 3U);
constexpr uint32_t kCapabilityAudioBulkPipe = (1UL << 4U);
constexpr uint32_t kCapabilityAudioStreamControl = (1UL << 5U);
constexpr uint32_t kCapabilityGeneratedAudioSource = (1UL << 6U);
constexpr uint32_t kCapabilityGeneratorConfig = (1UL << 7U);
constexpr uint32_t kCapabilityI2cRegisterAccess = (1UL << 8U);

constexpr uint8_t kMaxI2cRegisterTransferBytes = 32U;

struct DEVICE_PROTOCOL_PACKED FrameHeader
{
    uint16_t magic;
    uint8_t version;
    uint8_t type;
    uint8_t opcode;
    uint8_t status;
    uint16_t sequence;
    uint16_t payloadLength;
};

static_assert(sizeof(FrameHeader) == 10U, "FrameHeader must remain 10 bytes.");

struct DEVICE_PROTOCOL_PACKED GetInfoResponsePayload
{
    uint16_t protocolVersion;
    uint16_t maxCommandPacketSize;
    uint32_t capabilities;
};

struct DEVICE_PROTOCOL_PACKED SetLedCommandPayload
{
    uint8_t action;
};

struct DEVICE_PROTOCOL_PACKED SetLedResponsePayload
{
    uint8_t appliedAction;
    uint8_t reserved0;
    uint16_t reserved1;
};

struct DEVICE_PROTOCOL_PACKED StartStreamCommandPayload
{
    uint32_t sampleRateHz;
    uint8_t channelCount;
    uint8_t bitsPerSample;
    uint8_t source;
    uint8_t reserved;
};

struct DEVICE_PROTOCOL_PACKED StartStreamResponsePayload
{
    uint32_t sampleRateHz;
    uint8_t channelCount;
    uint8_t bitsPerSample;
    uint8_t source;
    uint8_t reserved;
};

struct DEVICE_PROTOCOL_PACKED SetGeneratorConfigCommandPayload
{
    uint32_t primaryFrequencyHz;
    uint32_t secondaryFrequencyHz;
    uint32_t modulationPeriodMs;
    uint16_t amplitude;
    uint8_t source;
    uint8_t noiseType;
    uint8_t amplitudeEnvelope;
    uint8_t reserved;
    uint32_t noiseSeed;
};

struct DEVICE_PROTOCOL_PACKED SetGeneratorConfigResponsePayload
{
    uint32_t primaryFrequencyHz;
    uint32_t secondaryFrequencyHz;
    uint32_t modulationPeriodMs;
    uint16_t amplitude;
    uint8_t source;
    uint8_t noiseType;
    uint8_t amplitudeEnvelope;
    uint8_t reserved;
    uint32_t noiseSeed;
};

struct DEVICE_PROTOCOL_PACKED I2cWriteRegisterCommandHeader
{
    uint8_t deviceAddress;
    uint8_t registerAddressSize;
    uint8_t writeLength;
    uint8_t reserved;
    uint32_t registerAddress;
};

struct DEVICE_PROTOCOL_PACKED I2cReadRegisterCommandPayload
{
    uint8_t deviceAddress;
    uint8_t registerAddressSize;
    uint8_t readLength;
    uint8_t reserved;
    uint32_t registerAddress;
};

struct DEVICE_PROTOCOL_PACKED I2cTransferStatusPayload
{
    uint32_t driverStatus;
};

struct DEVICE_PROTOCOL_PACKED I2cReadRegisterResponseHeader
{
    uint32_t driverStatus;
    uint8_t readLength;
    uint8_t reserved0;
    uint16_t reserved1;
};

struct DEVICE_PROTOCOL_PACKED StopStreamResponsePayload
{
    uint32_t stopReason;
};

struct DEVICE_PROTOCOL_PACKED DeviceReadyEventPayload
{
    uint32_t capabilities;
    uint16_t maxCommandPacketSize;
    uint16_t reserved;
};

struct DEVICE_PROTOCOL_PACKED StreamStartedEventPayload
{
    uint32_t sampleRateHz;
    uint8_t channelCount;
    uint8_t bitsPerSample;
    uint16_t reserved;
};

struct DEVICE_PROTOCOL_PACKED StreamStoppedEventPayload
{
    uint32_t reason;
};

struct DEVICE_PROTOCOL_PACKED FaultEventPayload
{
    uint32_t faultCode;
    uint32_t detail;
};

struct DEVICE_PROTOCOL_PACKED GetUsbDebugStateResponsePayload
{
    uint32_t stage;
    uint32_t lastEvent;
    uint32_t lastStatus;
    uint32_t usbSpeed;
    uint32_t oversizedOutboundDropCount;
    uint32_t setupBmRequestType;
    uint32_t setupBRequest;
    uint32_t setupWValue;
    uint32_t setupWIndex;
    uint32_t setupWLength;
    uint32_t playbackFillLevelBytes;
    uint32_t playbackMinFillLevelBytes;
    uint32_t playbackMaxFillLevelBytes;
    uint32_t playbackUnderrunCount;
    uint32_t playbackOverrunCount;
    uint32_t playbackDroppedBytes;
};

static_assert(sizeof(GetInfoResponsePayload) == 8U, "GetInfo response payload must remain 8 bytes.");
static_assert(sizeof(SetLedCommandPayload) == 1U, "SetLed command payload must remain 1 byte.");
static_assert(sizeof(SetLedResponsePayload) == 4U, "SetLed response payload must remain 4 bytes.");
static_assert(sizeof(StartStreamCommandPayload) == 8U, "StartStream command payload must remain 8 bytes.");
static_assert(sizeof(StartStreamResponsePayload) == 8U, "StartStream response payload must remain 8 bytes.");
static_assert(sizeof(SetGeneratorConfigCommandPayload) == 22U, "SetGeneratorConfig command payload must remain 22 bytes.");
static_assert(sizeof(SetGeneratorConfigResponsePayload) == 22U, "SetGeneratorConfig response payload must remain 22 bytes.");
static_assert(sizeof(StopStreamResponsePayload) == 4U, "StopStream response payload must remain 4 bytes.");
static_assert(sizeof(DeviceReadyEventPayload) == 8U, "DeviceReady event payload must remain 8 bytes.");
static_assert(sizeof(StreamStartedEventPayload) == 8U, "StreamStarted event payload must remain 8 bytes.");
static_assert(sizeof(StreamStoppedEventPayload) == 4U, "StreamStopped event payload must remain 4 bytes.");
static_assert(sizeof(FaultEventPayload) == 8U, "Fault event payload must remain 8 bytes.");
static_assert(sizeof(GetUsbDebugStateResponsePayload) == 64U, "GetUsbDebugState response payload must remain 64 bytes.");

}  // namespace device_protocol

#endif