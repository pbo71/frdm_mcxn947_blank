using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private const string DefaultRunMode = "full";
    private const string GeneratedSmokeRunMode = "generated-smoke";
    private const string LoopbackStressRunMode = "loopback-stress";
    private const string CodecProbeRunMode = "codec-probe";
    private const string CodecWriteSmokeRunMode = "codec-write-smoke";
    private const string CodecAnalogSmokeRunMode = "codec-analog-smoke";
    private const string CodecAnalogStepSmokeRunMode = "codec-analog-step-smoke";
    private const string CodecPlaybackSequenceRunMode = "codec-playback-sequence-smoke";
    private const string StreamCodecProbeRunMode = "stream-codec-probe";
    private const string PlaybackAudibleRunMode = "playback-audible";
    private const string IsoAudioRunMode = "iso-audio";
    private const byte IsoAudioInPipe = 0x83;
    private const int IsoPacketsPerTransfer = 16;   // 16 ms per WinUSB call (1 packet per 1 ms at HS)
    private const int IsoTransferRounds = 62;       // ~1 second total
    private const int IsoPacketDescriptorSize = 12; // sizeof(USBD_ISO_PACKET_DESCRIPTOR)
    private const int NativeOverlappedSize = 32;    // sizeof(OVERLAPPED) on Windows x64
    private const uint WaitObject0 = 0x00000000;
    private const uint TransferTimeoutMs = 5000;
    private const int ErrorIoPending = 997;
    private const uint CodecEnableFaultCode = 0xA001;
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileFlagOverlapped = 0x40000000;
    private const byte PipeTransferTimeoutPolicy = 0x03;
    private const uint DefaultTimeoutMs = 2000;
    private const int CmdOutPipe = 0x01;
    private const int CmdInPipe = 0x81;
    private const int AudioOutPipe = 0x02;
    private const int AudioInPipe = 0x82;
    private const ushort FrameMagic = 0x4153;
    private const byte ProtocolVersion = 1;
    private const ushort AudioHeaderMagic = 0x4155;
    private const byte AudioHeaderVersion = 1;
    private const byte AudioHeaderSize = 24;
    private const ushort AudioFlagStartOfStream = 1 << 0;
    private const ushort AudioFlagEndOfStream = 1 << 1;
    private const ushort AudioFlagDiscontinuity = 1 << 2;
    private const byte AudioSourceHostRxLoopback = 0;
    private const byte AudioSourceDeviceGeneratedSine = 1;
    private const byte AudioSourceDeviceGeneratedChirp = 2;
    private const byte AudioSourceDeviceGeneratedNoise = 3;
    private const byte NoiseTypeWhite = 0;
    private const byte NoiseTypeSampleHold = 1;
    private const byte NoiseTypeBinary = 2;
    private const byte AmplitudeEnvelopeConstant = 0;
    private const byte AmplitudeEnvelopeFadeIn = 1;
    private const byte AmplitudeEnvelopeFadeOut = 2;
    private const byte AmplitudeEnvelopeTriangle = 3;
    private const uint DefaultNoiseSeed = 0x13579BDF;
    private const uint DemoNoiseSeed = 0x2468ACE1;
    private const int DrainEventAttemptsAfterStop = 4;
    private const uint LoopbackSampleRateHz = 48000;
    private const byte LoopbackChannelCount = 2;
    private const byte LoopbackContainerBitsPerSample = 32;
    private const int LoopbackValidBitsPerSample = 24;
    private const uint GeneratedSampleRateHz = 48000;
    private const byte GeneratedChannelCount = 2;
    private const byte GeneratedContainerBitsPerSample = 32;
    private const int GeneratedValidBitsPerSample = 24;
    private const byte Sgtl5000DeviceAddress = 0x0A;
    private const byte Sgtl5000RegisterAddressSize = 2;
    private const ushort Sgtl5000ChipIdRegister = 0x0000;
    private const ushort Sgtl5000DigPowerRegister = 0x0002;
    private const ushort Sgtl5000ClkCtrlRegister = 0x0004;
    private const ushort Sgtl5000I2sCtrlRegister = 0x0006;
    private const ushort Sgtl5000SssCtrlRegister = 0x000A;
    private const ushort Sgtl5000AdcDacCtrlRegister = 0x000E;
    private const ushort Sgtl5000AnaCtrlRegister = 0x0024;
    private const ushort Sgtl5000RefCtrlRegister = 0x0028;
    private const ushort Sgtl5000AnaHpCtrlRegister = 0x0022;
    private const ushort Sgtl5000AnaPowerRegister = 0x0030;
    private const ushort Sgtl5000AnaStatusRegister = 0x0036;
    private const ushort Sgtl5000ShortCtrlRegister = 0x003C;
    private const ushort Sgtl5000DacVolRegister = 0x0010;
    private const ushort Sgtl5000ExpectedAnaPowerValue = 0x7060;
    private const ushort Sgtl5000ExpectedAdcDacCtrlBaseline = 0x323C;
    private const ushort Sgtl5000ExpectedAdcDacCtrlPlayback = 0x3230;
    private const ushort Sgtl5000ExpectedAnaCtrlBaseline = 0x0111;
    private const ushort Sgtl5000ExpectedAnaCtrlPlayback = 0x0001;
    private const byte Sgtl5000ChipIdLength = 2;
    private const int LoopbackStressBurstCount = 18;
    private const int LoopbackStressPacketsPerBurst = 8;
    private const int LoopbackStressFramesPerPacket = 32;
    private const int LoopbackStressShortPauseMs = 2;
    private const int LoopbackStressLongPauseMs = 12;
    private const int LoopbackStressDebugSnapshotPeriod = 6;
    private const int PlaybackAudiblePacketCount = 3000;
    private const int PlaybackAudibleFramesPerPacket = 60;
    private const int PlaybackAudibleSnapshotPacketIndex = 200;

    private static readonly Guid DefaultInterfaceGuid = new("D5959801-45C1-49DB-9053-89B5365C2800");

    private static int Main(string[] args)
    {
        try
        {
            var (interfaceGuid, runMode) = ParseArguments(args);
            Console.WriteLine($"Using interface GUID: {interfaceGuid}");
            Console.WriteLine($"Run mode: {runMode}");

            var devicePath = FindDevicePath(interfaceGuid);
            if (devicePath is null && !args.Any(argument => Guid.TryParse(argument, out _)))
            {
                var registeredGuid = FindRegisteredWinUsbInterfaceGuid();
                if (registeredGuid is Guid fallbackGuid)
                {
                    Console.WriteLine($"Falling back to registered WinUSB GUID: {fallbackGuid}");
                    devicePath = FindDevicePath(fallbackGuid);
                }
            }

            if (devicePath is null)
            {
                Console.Error.WriteLine("No matching WinUSB device interface found.");
                return 1;
            }

            var candidatePaths = new List<string> { devicePath };
            var symbolicName = FindRegistrySymbolicName();
            if (!string.IsNullOrWhiteSpace(symbolicName) && !candidatePaths.Contains(symbolicName, StringComparer.OrdinalIgnoreCase))
            {
                candidatePaths.Add(symbolicName);
            }

            using var deviceHandle = OpenWinUsbDevice(candidatePaths, out var winUsbHandle);

            try
            {
                var endpoints = QueryBulkEndpoints(winUsbHandle);
                PrintEndpoints(endpoints);
                RunProtocolDemo(winUsbHandle, endpoints, runMode);
            }
            finally
            {
                WinUsb_Free(winUsbHandle);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static (Guid InterfaceGuid, string RunMode) ParseArguments(string[] args)
    {
        var interfaceGuid = DefaultInterfaceGuid;
        var runMode = DefaultRunMode;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            if (Guid.TryParse(argument, out var parsedGuid))
            {
                interfaceGuid = parsedGuid;
                continue;
            }

            if (string.Equals(argument, "--smoke", StringComparison.OrdinalIgnoreCase))
            {
                runMode = GeneratedSmokeRunMode;
                continue;
            }

            if (argument.StartsWith("--mode=", StringComparison.OrdinalIgnoreCase))
            {
                runMode = argument.Substring("--mode=".Length);
                continue;
            }

            if (string.Equals(argument, "--mode", StringComparison.OrdinalIgnoreCase) && (index + 1) < args.Length)
            {
                runMode = args[index + 1];
                index++;
                continue;
            }

            throw new ArgumentException($"Unsupported argument: {argument}");
        }

        if (!string.Equals(runMode, DefaultRunMode, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(runMode, GeneratedSmokeRunMode, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(runMode, LoopbackStressRunMode, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(runMode, CodecProbeRunMode, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(runMode, CodecWriteSmokeRunMode, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(runMode, CodecAnalogSmokeRunMode, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(runMode, CodecAnalogStepSmokeRunMode, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(runMode, CodecPlaybackSequenceRunMode, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(runMode, StreamCodecProbeRunMode, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(runMode, PlaybackAudibleRunMode, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(runMode, IsoAudioRunMode, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Unsupported run mode: {runMode}");
        }

        return (interfaceGuid, runMode);
    }

    private static string? FindDevicePath(Guid interfaceGuid)
    {
        var deviceInfoSet = SetupDiGetClassDevs(ref interfaceGuid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs failed");
        }

        try
        {
            var interfaceData = new SpDeviceInterfaceData
            {
                cbSize = Marshal.SizeOf<SpDeviceInterfaceData>()
            };

            for (uint index = 0; SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref interfaceGuid, index, ref interfaceData); index++)
            {
                SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
                if (requiredSize == 0)
                {
                    continue;
                }

                var detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    var cbSize = IntPtr.Size == 8 ? 8 : 6;
                    Marshal.WriteInt32(detailBuffer, cbSize);

                    if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detailBuffer, requiredSize, out _, IntPtr.Zero))
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetDeviceInterfaceDetail failed");
                    }

                    var pathPtr = detailBuffer + 4;
                    return Marshal.PtrToStringAuto(pathPtr);
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }

            var error = Marshal.GetLastWin32Error();
            if (error != 259)
            {
                throw new Win32Exception(error, "SetupDiEnumDeviceInterfaces failed");
            }

            return null;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private static Guid? FindRegisteredWinUsbInterfaceGuid()
    {
        const string enumRoot = @"SYSTEM\CurrentControlSet\Enum\USB";

        using var usbRoot = Registry.LocalMachine.OpenSubKey(enumRoot);
        if (usbRoot is null)
        {
            return null;
        }

        foreach (var deviceKeyName in usbRoot.GetSubKeyNames().Where(name => name.StartsWith("VID_1FC9&PID_00B0", StringComparison.OrdinalIgnoreCase)))
        {
            using var deviceKey = usbRoot.OpenSubKey(deviceKeyName);
            if (deviceKey is null)
            {
                continue;
            }

            foreach (var instanceKeyName in deviceKey.GetSubKeyNames())
            {
                using var instanceKey = deviceKey.OpenSubKey(instanceKeyName + "\\Device Parameters");
                if (instanceKey is null)
                {
                    continue;
                }

                var rawGuids = instanceKey.GetValue("DeviceInterfaceGUIDs") as string[];
                if (rawGuids is null || rawGuids.Length == 0)
                {
                    continue;
                }

                foreach (var rawGuid in rawGuids)
                {
                    var trimmed = rawGuid.Trim('{', '}');
                    if (Guid.TryParse(trimmed, out var guid))
                    {
                        return guid;
                    }
                }
            }
        }

        return null;
    }

    private static string? FindRegistrySymbolicName()
    {
        const string enumRoot = @"SYSTEM\CurrentControlSet\Enum\USB";

        using var usbRoot = Registry.LocalMachine.OpenSubKey(enumRoot);
        if (usbRoot is null)
        {
            return null;
        }

        foreach (var deviceKeyName in usbRoot.GetSubKeyNames().Where(name => name.StartsWith("VID_1FC9&PID_00B0", StringComparison.OrdinalIgnoreCase)))
        {
            using var deviceKey = usbRoot.OpenSubKey(deviceKeyName);
            if (deviceKey is null)
            {
                continue;
            }

            foreach (var instanceKeyName in deviceKey.GetSubKeyNames())
            {
                using var instanceKey = deviceKey.OpenSubKey(instanceKeyName + "\\Device Parameters");
                var symbolicName = instanceKey?.GetValue("SymbolicName") as string;
                if (!string.IsNullOrWhiteSpace(symbolicName))
                {
                    return symbolicName;
                }
            }
        }

        return null;
    }

    private static SafeFileHandle OpenWinUsbDevice(IEnumerable<string> candidatePaths, out IntPtr winUsbHandle)
    {
        var errors = new List<string>();

        foreach (var candidatePath in candidatePaths)
        {
            Console.WriteLine($"Opening device: {candidatePath}");
            var deviceHandle = CreateFile(candidatePath, GenericRead | GenericWrite, FileShareRead | FileShareWrite,
                IntPtr.Zero, OpenExisting, FileAttributeNormal | FileFlagOverlapped, IntPtr.Zero);
            if (deviceHandle.IsInvalid)
            {
                errors.Add($"CreateFile failed for {candidatePath} (error {Marshal.GetLastWin32Error()})");
                deviceHandle.Dispose();
                continue;
            }

            if (WinUsb_Initialize(deviceHandle, out winUsbHandle))
            {
                return deviceHandle;
            }

            errors.Add($"WinUsb_Initialize failed for {candidatePath} (error {Marshal.GetLastWin32Error()})");
            deviceHandle.Dispose();
        }

        throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
    }

    private static Dictionary<byte, WinUsbPipeInformation> QueryBulkEndpoints(IntPtr winUsbHandle)
    {
        if (!WinUsb_QueryInterfaceSettings(winUsbHandle, 0, out var interfaceDescriptor))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_QueryInterfaceSettings failed");
        }

        var endpoints = new Dictionary<byte, WinUsbPipeInformation>();
        for (byte pipeIndex = 0; pipeIndex < interfaceDescriptor.bNumEndpoints; pipeIndex++)
        {
            if (!WinUsb_QueryPipe(winUsbHandle, 0, pipeIndex, out var pipeInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"WinUsb_QueryPipe failed for pipe index {pipeIndex}");
            }

            endpoints[pipeInfo.PipeId] = pipeInfo;
            // PIPE_TRANSFER_TIMEOUT policy is not supported for isochronous pipes
            if (pipeInfo.PipeType != UsbdPipeType.Isochronous)
            {
                SetTimeout(winUsbHandle, pipeInfo.PipeId, DefaultTimeoutMs);
            }
        }

        return endpoints;
    }

    private static void PrintEndpoints(Dictionary<byte, WinUsbPipeInformation> endpoints)
    {
        Console.WriteLine("Discovered bulk pipes:");
        foreach (var pipeInfo in endpoints.Values.OrderBy(p => p.PipeId))
        {
            var direction = UsbEndpointDirectionIn(pipeInfo.PipeId) ? "IN" : "OUT";
            Console.WriteLine($"  Pipe 0x{pipeInfo.PipeId:X2} {direction} type={pipeInfo.PipeType} maxPacket={pipeInfo.MaximumPacketSize}");
        }
    }

    private static void RunProtocolDemo(IntPtr winUsbHandle, Dictionary<byte, WinUsbPipeInformation> endpoints, string runMode)
    {
        if (!endpoints.ContainsKey(CmdOutPipe) || !endpoints.ContainsKey(CmdInPipe))
        {
            Console.WriteLine("Skipping protocol demo: command pipes 0x01/0x81 not both present.");
            return;
        }

        var maxPacket = endpoints[(byte)CmdInPipe].MaximumPacketSize;

        if (TryReadProtocolFrame(winUsbHandle, CmdInPipe, maxPacket, out var readyFrame))
        {
            var initialFrame = readyFrame ?? throw new InvalidOperationException("Initial protocol read returned no frame data.");
            Console.WriteLine($"Initial frame: {DescribeFrame(initialFrame)}");
        }
        else
        {
            Console.WriteLine("No initial event was received before timeout.");
        }

        var debugStateResponse = ExecuteCommand(winUsbHandle, maxPacket, 1, 0x03, Array.Empty<byte>());
        Console.WriteLine($"GetUsbDebugState response: {DescribeFrame(debugStateResponse)}");

        var getInfoResponse = ExecuteCommand(winUsbHandle, maxPacket, 2, 0x01, Array.Empty<byte>());
        Console.WriteLine($"GetInfo response: {DescribeFrame(getInfoResponse)}");

        var pingPayload = Encoding.ASCII.GetBytes("hello-from-pc");
        var pingResponse = ExecuteCommand(winUsbHandle, maxPacket, 3, 0x04, pingPayload);
        Console.WriteLine($"Ping response: {DescribeFrame(pingResponse)}");

        if (string.Equals(runMode, GeneratedSmokeRunMode, StringComparison.OrdinalIgnoreCase))
        {
            RunGeneratedSmokeDemo(winUsbHandle, endpoints, maxPacket);
            return;
        }

        if (string.Equals(runMode, LoopbackStressRunMode, StringComparison.OrdinalIgnoreCase))
        {
            RunLoopbackStressDemo(winUsbHandle, endpoints, maxPacket);
            return;
        }

        if (string.Equals(runMode, CodecProbeRunMode, StringComparison.OrdinalIgnoreCase))
        {
            RunCodecProbeDemo(winUsbHandle, maxPacket);
            return;
        }

        if (string.Equals(runMode, CodecWriteSmokeRunMode, StringComparison.OrdinalIgnoreCase))
        {
            RunCodecWriteSmokeDemo(winUsbHandle, maxPacket);
            return;
        }

        if (string.Equals(runMode, CodecAnalogSmokeRunMode, StringComparison.OrdinalIgnoreCase))
        {
            RunCodecAnalogSmokeDemo(winUsbHandle, maxPacket);
            return;
        }

        if (string.Equals(runMode, CodecAnalogStepSmokeRunMode, StringComparison.OrdinalIgnoreCase))
        {
            RunCodecAnalogStepSmokeDemo(winUsbHandle, maxPacket);
            return;
        }

        if (string.Equals(runMode, CodecPlaybackSequenceRunMode, StringComparison.OrdinalIgnoreCase))
        {
            RunCodecPlaybackSequenceSmokeDemo(winUsbHandle, maxPacket);
            return;
        }

        if (string.Equals(runMode, StreamCodecProbeRunMode, StringComparison.OrdinalIgnoreCase))
        {
            RunStreamCodecProbeDemo(winUsbHandle, endpoints, maxPacket);
            return;
        }

        if (string.Equals(runMode, PlaybackAudibleRunMode, StringComparison.OrdinalIgnoreCase))
        {
            RunPlaybackAudibleDemo(winUsbHandle, endpoints, maxPacket);
            return;
        }

        if (string.Equals(runMode, IsoAudioRunMode, StringComparison.OrdinalIgnoreCase))
        {
            RunIsoAudioDemo(winUsbHandle, endpoints, maxPacket);
            return;
        }

        if (endpoints.ContainsKey(AudioOutPipe) && endpoints.ContainsKey(AudioInPipe))
        {
            var startStreamResponse = ExecuteCommand(winUsbHandle,
                                                     maxPacket,
                                                     5,
                                                     0x05,
                                                     BuildStartStreamPayload(LoopbackSampleRateHz,
                                                                             LoopbackChannelCount,
                                                                             LoopbackContainerBitsPerSample,
                                                                             AudioSourceHostRxLoopback));
            ValidateResponseStatus(startStreamResponse, expectedStatus: 0, "StartStream");
            Console.WriteLine($"StartStream response: {DescribeFrame(startStreamResponse)}");

            var audioMaxPacket = endpoints[(byte)AudioInPipe].MaximumPacketSize;
            RunAudioLoopbackDemo(winUsbHandle, audioMaxPacket);
            PrintDebugStateSnapshot(winUsbHandle, maxPacket, 6, "Debug state during host loopback");

            var stopStreamResponse = ExecuteCommand(winUsbHandle, maxPacket, 7, 0x06, Array.Empty<byte>());
            ValidateResponseStatus(stopStreamResponse, expectedStatus: 0, "StopStream");
            Console.WriteLine($"StopStream response: {DescribeFrame(stopStreamResponse)}");
            DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Post-stop");
            PrintDebugStateSnapshot(winUsbHandle, maxPacket, 8, "Debug state after stop");

            var restartStreamResponse = ExecuteCommand(winUsbHandle,
                                                       maxPacket,
                                                       9,
                                                       0x05,
                                                       BuildStartStreamPayload(LoopbackSampleRateHz,
                                                                               LoopbackChannelCount,
                                                                               LoopbackContainerBitsPerSample,
                                                                               AudioSourceHostRxLoopback));
            ValidateResponseStatus(restartStreamResponse, expectedStatus: 0, "StartStream (EOS phase)");
            Console.WriteLine($"RestartStream response: {DescribeFrame(restartStreamResponse)}");

            RunEndOfStreamLoopbackDemo(winUsbHandle, audioMaxPacket);
            DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Post-EOS");

            var stopAfterEosResponse = ExecuteCommand(winUsbHandle, maxPacket, 10, 0x06, Array.Empty<byte>());
            ValidateResponseStatus(stopAfterEosResponse, expectedStatus: 6, "StopStream after EOS");
            Console.WriteLine($"StopStream after EOS response: {DescribeFrame(stopAfterEosResponse)}");

            var mismatchStartStreamResponse = ExecuteCommand(winUsbHandle,
                                                             maxPacket,
                                                             11,
                                                             0x05,
                                                             BuildStartStreamPayload(LoopbackSampleRateHz,
                                                                                     LoopbackChannelCount,
                                                                                     LoopbackContainerBitsPerSample,
                                                                                     AudioSourceHostRxLoopback));
            ValidateResponseStatus(mismatchStartStreamResponse, expectedStatus: 0, "StartStream (format mismatch phase)");
            Console.WriteLine($"Mismatch phase StartStream response: {DescribeFrame(mismatchStartStreamResponse)}");

            RunFormatMismatchDropDemo(winUsbHandle, audioMaxPacket);

            var stopAfterMismatchResponse = ExecuteCommand(winUsbHandle, maxPacket, 12, 0x06, Array.Empty<byte>());
            ValidateResponseStatus(stopAfterMismatchResponse, expectedStatus: 0, "StopStream after format mismatch");
            Console.WriteLine($"StopStream after mismatch response: {DescribeFrame(stopAfterMismatchResponse)}");
            DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Post-mismatch stop");

            var setSineConfigResponse = ExecuteCommand(winUsbHandle,
                                                       maxPacket,
                                                       13,
                                                       0x07,
                                                       BuildSetGeneratorConfigPayload(1200U,
                                                                                      1200U,
                                                                                      750U,
                                                                                      (ushort)9000,
                                                                                      AudioSourceDeviceGeneratedSine,
                                                                                      NoiseTypeWhite,
                                                                                      AmplitudeEnvelopeTriangle,
                                                                                      DefaultNoiseSeed));
            ValidateResponseStatus(setSineConfigResponse, expectedStatus: 0, "SetGeneratorConfig (sine)");
            Console.WriteLine($"SetGeneratorConfig sine response: {DescribeFrame(setSineConfigResponse)}");

            var generatedSineStartStreamResponse = ExecuteCommand(winUsbHandle,
                                                                  maxPacket,
                                                                  14,
                                                                  0x05,
                                                                  BuildStartStreamPayload(GeneratedSampleRateHz,
                                                                                          GeneratedChannelCount,
                                                                                          GeneratedContainerBitsPerSample,
                                                                                          AudioSourceDeviceGeneratedSine));
            ValidateResponseStatus(generatedSineStartStreamResponse, expectedStatus: 0, "StartStream (generated sine)");
            Console.WriteLine($"Generated sine StartStream response: {DescribeFrame(generatedSineStartStreamResponse)}");

            RunGeneratedAudioDemo(winUsbHandle, audioMaxPacket, AudioSourceDeviceGeneratedSine, "Generated sine");

            var stopAfterGeneratedSineResponse = ExecuteCommand(winUsbHandle, maxPacket, 15, 0x06, Array.Empty<byte>());
            ValidateResponseStatus(stopAfterGeneratedSineResponse, expectedStatus: 0, "StopStream after generated sine");
            Console.WriteLine($"StopStream after generated sine response: {DescribeFrame(stopAfterGeneratedSineResponse)}");
            DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Post-generated sine stop");
            DrainAudioPackets(winUsbHandle, audioMaxPacket, "Post-generated sine audio drain");

            var setChirpConfigResponse = ExecuteCommand(winUsbHandle,
                                                        maxPacket,
                                                        16,
                                                        0x07,
                                                        BuildSetGeneratorConfigPayload(500U,
                                                                                       3500U,
                                                                                       1500U,
                                                                                       (ushort)10000,
                                                                                       AudioSourceDeviceGeneratedChirp,
                                                                                       NoiseTypeWhite,
                                                                                       AmplitudeEnvelopeFadeIn,
                                                                                       DefaultNoiseSeed));
            ValidateResponseStatus(setChirpConfigResponse, expectedStatus: 0, "SetGeneratorConfig (chirp)");
            Console.WriteLine($"SetGeneratorConfig chirp response: {DescribeFrame(setChirpConfigResponse)}");

            var generatedChirpStartStreamResponse = ExecuteCommand(winUsbHandle,
                                                                   maxPacket,
                                                                   17,
                                                                   0x05,
                                                                   BuildStartStreamPayload(GeneratedSampleRateHz,
                                                                                           GeneratedChannelCount,
                                                                                           GeneratedContainerBitsPerSample,
                                                                                           AudioSourceDeviceGeneratedChirp));
            ValidateResponseStatus(generatedChirpStartStreamResponse, expectedStatus: 0, "StartStream (generated chirp)");
            Console.WriteLine($"Generated chirp StartStream response: {DescribeFrame(generatedChirpStartStreamResponse)}");

            RunGeneratedAudioDemo(winUsbHandle, audioMaxPacket, AudioSourceDeviceGeneratedChirp, "Generated chirp");

            var stopAfterGeneratedChirpResponse = ExecuteCommand(winUsbHandle, maxPacket, 18, 0x06, Array.Empty<byte>());
            ValidateResponseStatus(stopAfterGeneratedChirpResponse, expectedStatus: 0, "StopStream after generated chirp");
            Console.WriteLine($"StopStream after generated chirp response: {DescribeFrame(stopAfterGeneratedChirpResponse)}");
            DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Post-generated chirp stop");
            DrainAudioPackets(winUsbHandle, audioMaxPacket, "Post-generated chirp audio drain");

            var setNoiseConfigResponse = ExecuteCommand(winUsbHandle,
                                                        maxPacket,
                                                        19,
                                                        0x07,
                                                        BuildSetGeneratorConfigPayload(0U,
                                                                                       0U,
                                                                                       32U,
                                                                                       (ushort)8000,
                                                                                       AudioSourceDeviceGeneratedNoise,
                                                                                       NoiseTypeBinary,
                                                                                       AmplitudeEnvelopeTriangle,
                                                                                       DemoNoiseSeed));
            ValidateResponseStatus(setNoiseConfigResponse, expectedStatus: 0, "SetGeneratorConfig (noise)");
            Console.WriteLine($"SetGeneratorConfig noise response: {DescribeFrame(setNoiseConfigResponse)}");

            var generatedNoiseStartStreamResponse = ExecuteCommand(winUsbHandle,
                                                                   maxPacket,
                                                                   20,
                                                                   0x05,
                                                                   BuildStartStreamPayload(GeneratedSampleRateHz,
                                                                                           GeneratedChannelCount,
                                                                                           GeneratedContainerBitsPerSample,
                                                                                           AudioSourceDeviceGeneratedNoise));
            ValidateResponseStatus(generatedNoiseStartStreamResponse, expectedStatus: 0, "StartStream (generated noise)");
            Console.WriteLine($"Generated noise StartStream response: {DescribeFrame(generatedNoiseStartStreamResponse)}");

            RunGeneratedAudioDemo(winUsbHandle, audioMaxPacket, AudioSourceDeviceGeneratedNoise, "Generated noise");

            var stopAfterGeneratedNoiseResponse = ExecuteCommand(winUsbHandle, maxPacket, 21, 0x06, Array.Empty<byte>());
            ValidateResponseStatus(stopAfterGeneratedNoiseResponse, expectedStatus: 0, "StopStream after generated noise");
            Console.WriteLine($"StopStream after generated noise response: {DescribeFrame(stopAfterGeneratedNoiseResponse)}");
            DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Post-generated noise stop");
            DrainAudioPackets(winUsbHandle, audioMaxPacket, "Post-generated noise audio drain");
        }
        else
        {
            Console.WriteLine("Skipping audio demo: audio pipes 0x02/0x82 not both present.");
        }
    }

    private static void RunIsoAudioDemo(IntPtr winUsbHandle, Dictionary<byte, WinUsbPipeInformation> endpoints, ushort cmdMaxPacket)
    {
        if (!endpoints.ContainsKey(IsoAudioInPipe))
        {
            Console.WriteLine("Skipping iso audio demo: iso IN pipe 0x83 not present.");
            return;
        }

        Console.WriteLine("=== Isochronous Audio Demo (EP 0x83) ===");

        // Configure 1 kHz sine with constant amplitude
        var setConfigResp = ExecuteCommand(winUsbHandle, cmdMaxPacket, 900, 0x07,
            BuildSetGeneratorConfigPayload(1000U, 1000U, 0U, 12000,
                AudioSourceDeviceGeneratedSine, NoiseTypeWhite, AmplitudeEnvelopeConstant, DefaultNoiseSeed));
        ValidateResponseStatus(setConfigResp, 0, "SetGeneratorConfig (iso)");
        Console.WriteLine($"SetGeneratorConfig: {DescribeFrame(setConfigResp)}");

        var startResp = ExecuteCommand(winUsbHandle, cmdMaxPacket, 901, 0x05,
            BuildStartStreamPayload(GeneratedSampleRateHz, GeneratedChannelCount,
                GeneratedContainerBitsPerSample, AudioSourceDeviceGeneratedSine));
        ValidateResponseStatus(startResp, 0, "StartStream (iso)");
        Console.WriteLine($"StartStream: {DescribeFrame(startResp)}");

        var isoMaxPacket = endpoints[IsoAudioInPipe].MaximumPacketSize;
        var bytesPerTransfer = (uint)IsoPacketsPerTransfer * isoMaxPacket;

        // Pin the data buffer in memory so its address remains valid for WinUSB DMA
        var dataBuffer = new byte[bytesPerTransfer];
        var gcHandle = GCHandle.Alloc(dataBuffer, GCHandleType.Pinned);
        IntPtr bufPtr = gcHandle.AddrOfPinnedObject();
        IntPtr isochHandle = IntPtr.Zero;
        IntPtr descsMem = Marshal.AllocHGlobal(IsoPacketsPerTransfer * IsoPacketDescriptorSize);
        // OVERLAPPED layout (x64): Internal(8) + InternalHigh(8) + Offset(4) + OffsetHigh(4) + hEvent(8) = 32 bytes
        IntPtr overlappedMem = Marshal.AllocHGlobal(NativeOverlappedSize);
        IntPtr eventHandle = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);

        try
        {
            if (!WinUsb_RegisterIsochBuffer(winUsbHandle, IsoAudioInPipe, bufPtr, bytesPerTransfer, out isochHandle))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_RegisterIsochBuffer failed");

            uint totalDataBytes = 0u;
            int totalDataPackets = 0;

            for (int round = 0; round < IsoTransferRounds; round++)
            {
                // Zero OVERLAPPED and set hEvent at byte offset 24
                for (int b = 0; b < NativeOverlappedSize; b++)
                    Marshal.WriteByte(overlappedMem, b, 0);
                Marshal.WriteIntPtr(overlappedMem, 24, eventHandle);

                // Zero packet descriptor array
                for (int b = 0; b < IsoPacketsPerTransfer * IsoPacketDescriptorSize; b++)
                    Marshal.WriteByte(descsMem, b, 0);

                ResetEvent(eventHandle);

                bool submitted = WinUsb_ReadIsochPipeAsap(
                    isochHandle, 0u, bytesPerTransfer,
                    round > 0,
                    (uint)IsoPacketsPerTransfer,
                    descsMem, overlappedMem);
                int submitErr = Marshal.GetLastWin32Error();
                if (!submitted && submitErr != ErrorIoPending)
                    throw new Win32Exception(submitErr, $"WinUsb_ReadIsochPipeAsap failed (round {round})");

                uint waitResult = WaitForSingleObject(eventHandle, TransferTimeoutMs);
                if (waitResult != WaitObject0)
                    throw new TimeoutException($"Isochronous transfer timed out on round {round}");

                // Parse USBD_ISO_PACKET_DESCRIPTOR array: Offset(+0), Length(+4), Status(+8)
                int roundDataPackets = 0;
                uint roundDataBytes = 0u;
                for (int p = 0; p < IsoPacketsPerTransfer; p++)
                {
                    IntPtr desc = IntPtr.Add(descsMem, p * IsoPacketDescriptorSize);
                    uint pktLen = (uint)Marshal.ReadInt32(desc, 4);
                    int pktStatus = Marshal.ReadInt32(desc, 8);
                    if (pktStatus == 0 && pktLen >= AudioHeaderSize)
                    {
                        roundDataPackets++;
                        roundDataBytes += pktLen;
                    }
                }

                totalDataPackets += roundDataPackets;
                totalDataBytes += roundDataBytes;

                if (round % 10 == 0 || round == IsoTransferRounds - 1)
                {
                    Console.WriteLine(
                        $"  Round {round + 1,3}/{IsoTransferRounds}: " +
                        $"{roundDataPackets}/{IsoPacketsPerTransfer} packets with audio, " +
                        $"{roundDataBytes} bytes");
                }
            }

            Console.WriteLine(
                $"Total: {totalDataPackets}/{IsoTransferRounds * IsoPacketsPerTransfer} packets with data, " +
                $"{totalDataBytes} bytes (~{totalDataBytes / 1000} kB)");
        }
        finally
        {
            if (isochHandle != IntPtr.Zero)
                WinUsb_UnregisterIsochBuffer(isochHandle);
            Marshal.FreeHGlobal(descsMem);
            Marshal.FreeHGlobal(overlappedMem);
            CloseHandle(eventHandle);
            gcHandle.Free();
        }

        var stopResp = ExecuteCommand(winUsbHandle, cmdMaxPacket, 902, 0x06, Array.Empty<byte>());
        ValidateResponseStatus(stopResp, 0, "StopStream (iso)");
        Console.WriteLine($"StopStream: {DescribeFrame(stopResp)}");
        DrainProtocolEvents(winUsbHandle, cmdMaxPacket, DrainEventAttemptsAfterStop, "Iso post-stop");
    }

    private static void RunGeneratedSmokeDemo(IntPtr winUsbHandle, Dictionary<byte, WinUsbPipeInformation> endpoints, ushort maxPacket)
    {
        if (!endpoints.ContainsKey(AudioInPipe))
        {
            Console.WriteLine("Skipping generated smoke test: audio IN pipe 0x82 not present.");
            return;
        }

        var audioMaxPacket = endpoints[(byte)AudioInPipe].MaximumPacketSize;

        DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Smoke pre-drain");
        DrainAudioPackets(winUsbHandle, audioMaxPacket, "Smoke pre-drain audio");

        RunGeneratedSourceScenario(winUsbHandle,
                                   maxPacket,
                                   audioMaxPacket,
                                   setConfigSequence: 100,
                                   startSequence: 101,
                                   stopSequence: 102,
                                   source: AudioSourceDeviceGeneratedChirp,
                                   label: "Generated chirp smoke",
                                   configPayload: BuildSetGeneratorConfigPayload(500U,
                                                                                3500U,
                                                                                1500U,
                                                                                (ushort)10000,
                                                                                AudioSourceDeviceGeneratedChirp,
                                                                                NoiseTypeWhite,
                                                                                AmplitudeEnvelopeFadeIn,
                                                                                DefaultNoiseSeed));

        RunGeneratedSourceScenario(winUsbHandle,
                                   maxPacket,
                                   audioMaxPacket,
                                   setConfigSequence: 110,
                                   startSequence: 111,
                                   stopSequence: 112,
                                   source: AudioSourceDeviceGeneratedNoise,
                                   label: "Generated noise smoke",
                                   configPayload: BuildSetGeneratorConfigPayload(0U,
                                                                                0U,
                                                                                32U,
                                                                                (ushort)8000,
                                                                                AudioSourceDeviceGeneratedNoise,
                                                                                NoiseTypeBinary,
                                                                                AmplitudeEnvelopeTriangle,
                                                                                DemoNoiseSeed));
    }

    private static void RunLoopbackStressDemo(IntPtr winUsbHandle, Dictionary<byte, WinUsbPipeInformation> endpoints, ushort maxPacket)
    {
        if (!endpoints.ContainsKey(AudioOutPipe) || !endpoints.ContainsKey(AudioInPipe))
        {
            Console.WriteLine("Skipping loopback stress test: audio pipes 0x02/0x82 not both present.");
            return;
        }

        var audioMaxPacket = endpoints[(byte)AudioInPipe].MaximumPacketSize;
        var startStreamResponse = ExecuteCommand(winUsbHandle,
                                                 maxPacket,
                                                 200,
                                                 0x05,
                                                 BuildStartStreamPayload(LoopbackSampleRateHz,
                                                                         LoopbackChannelCount,
                                                                         LoopbackContainerBitsPerSample,
                                                                         AudioSourceHostRxLoopback));
        ValidateResponseStatus(startStreamResponse, expectedStatus: 0, "StartStream (loopback stress)");
        Console.WriteLine($"Loopback stress StartStream response: {DescribeFrame(startStreamResponse)}");

        RunLoopbackBurstStress(winUsbHandle, audioMaxPacket);
        PrintDebugStateSnapshot(winUsbHandle, maxPacket, 201, "Debug state after loopback stress");

        var stopStreamResponse = ExecuteCommand(winUsbHandle, maxPacket, 202, 0x06, Array.Empty<byte>());
        ValidateResponseStatus(stopStreamResponse, expectedStatus: 0, "StopStream (loopback stress)");
        Console.WriteLine($"Loopback stress StopStream response: {DescribeFrame(stopStreamResponse)}");
        DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Loopback stress post-stop");
        PrintDebugStateSnapshot(winUsbHandle, maxPacket, 203, "Debug state after loopback stress stop");
    }

    private static void RunStreamCodecProbeDemo(IntPtr winUsbHandle, Dictionary<byte, WinUsbPipeInformation> endpoints, ushort maxPacket)
    {
        if (!endpoints.ContainsKey(AudioInPipe))
        {
            Console.WriteLine("Skipping stream codec probe: audio IN pipe 0x82 not present.");
            return;
        }

        var audioMaxPacket = endpoints[(byte)AudioInPipe].MaximumPacketSize;

        DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Stream codec probe pre-drain");
        DrainAudioPackets(winUsbHandle, audioMaxPacket, "Stream codec probe pre-drain audio");

        var beforeDigPower = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 300, Sgtl5000DigPowerRegister, "CHIP_DIG_POWER before StartStream");
        Console.WriteLine($"CHIP_DIG_POWER before StartStream: 0x{beforeDigPower:X4}");

        var setConfigResponse = ExecuteCommand(winUsbHandle,
                                               maxPacket,
                                               301,
                                               0x07,
                                               BuildSetGeneratorConfigPayload(1200U,
                                                                              1200U,
                                                                              750U,
                                                                              (ushort)9000,
                                                                              AudioSourceDeviceGeneratedSine,
                                                                              NoiseTypeWhite,
                                                                              AmplitudeEnvelopeTriangle,
                                                                              DefaultNoiseSeed));
        ValidateResponseStatus(setConfigResponse, expectedStatus: 0, "SetGeneratorConfig (stream codec probe)");
        Console.WriteLine($"Stream codec probe config response: {DescribeFrame(setConfigResponse)}");

        var startStreamResponse = ExecuteCommand(winUsbHandle,
                                                 maxPacket,
                                                 302,
                                                 0x05,
                                                 BuildStartStreamPayload(GeneratedSampleRateHz,
                                                                         GeneratedChannelCount,
                                                                         GeneratedContainerBitsPerSample,
                                                                         AudioSourceDeviceGeneratedSine));
        ValidateResponseStatus(startStreamResponse, expectedStatus: 0, "StartStream (stream codec probe)");
        Console.WriteLine($"Stream codec probe StartStream response: {DescribeFrame(startStreamResponse)}");

        var duringDigPower = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 303, Sgtl5000DigPowerRegister, "CHIP_DIG_POWER during stream");
        Console.WriteLine($"CHIP_DIG_POWER during stream: 0x{duringDigPower:X4}");

        var duringAdcDacCtrl = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 304, Sgtl5000AdcDacCtrlRegister, "CHIP_ADCDAC_CTRL during stream");
        Console.WriteLine($"CHIP_ADCDAC_CTRL during stream: 0x{duringAdcDacCtrl:X4}");

        var duringAnaCtrl = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 305, Sgtl5000AnaCtrlRegister, "CHIP_ANA_CTRL during stream");
        Console.WriteLine($"CHIP_ANA_CTRL during stream: 0x{duringAnaCtrl:X4}");

        RunGeneratedAudioDemo(winUsbHandle, audioMaxPacket, AudioSourceDeviceGeneratedSine, "Stream codec probe audio");
        PrintDebugStateSnapshot(winUsbHandle, maxPacket, 306, "Stream codec probe debug state");

        var stopStreamResponse = ExecuteCommand(winUsbHandle, maxPacket, 307, 0x06, Array.Empty<byte>());
        ValidateResponseStatus(stopStreamResponse, expectedStatus: 0, "StopStream (stream codec probe)");
        Console.WriteLine($"Stream codec probe StopStream response: {DescribeFrame(stopStreamResponse)}");

        DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Stream codec probe post-stop");
        DrainAudioPackets(winUsbHandle, audioMaxPacket, "Stream codec probe post-stop audio");

        var afterDigPower = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 308, Sgtl5000DigPowerRegister, "CHIP_DIG_POWER after StopStream");
        Console.WriteLine($"CHIP_DIG_POWER after StopStream: 0x{afterDigPower:X4}");

        var afterAdcDacCtrl = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 309, Sgtl5000AdcDacCtrlRegister, "CHIP_ADCDAC_CTRL after StopStream");
        Console.WriteLine($"CHIP_ADCDAC_CTRL after StopStream: 0x{afterAdcDacCtrl:X4}");

        var afterAnaCtrl = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 310, Sgtl5000AnaCtrlRegister, "CHIP_ANA_CTRL after StopStream");
        Console.WriteLine($"CHIP_ANA_CTRL after StopStream: 0x{afterAnaCtrl:X4}");

        if (duringDigPower != 0x0021)
        {
            throw new InvalidOperationException($"Expected CHIP_DIG_POWER=0x0021 during active stream, got 0x{duringDigPower:X4}.");
        }

        if (duringAdcDacCtrl != Sgtl5000ExpectedAdcDacCtrlPlayback)
        {
            throw new InvalidOperationException($"Expected CHIP_ADCDAC_CTRL=0x{Sgtl5000ExpectedAdcDacCtrlPlayback:X4} during active stream, got 0x{duringAdcDacCtrl:X4}.");
        }

        if (duringAnaCtrl != Sgtl5000ExpectedAnaCtrlPlayback)
        {
            throw new InvalidOperationException($"Expected CHIP_ANA_CTRL=0x{Sgtl5000ExpectedAnaCtrlPlayback:X4} during active stream, got 0x{duringAnaCtrl:X4}.");
        }

        if (afterDigPower != 0x0000)
        {
            throw new InvalidOperationException($"Expected CHIP_DIG_POWER=0x0000 after StopStream, got 0x{afterDigPower:X4}.");
        }

        if (afterAdcDacCtrl != Sgtl5000ExpectedAdcDacCtrlBaseline)
        {
            throw new InvalidOperationException($"Expected CHIP_ADCDAC_CTRL=0x{Sgtl5000ExpectedAdcDacCtrlBaseline:X4} after StopStream, got 0x{afterAdcDacCtrl:X4}.");
        }

        if (afterAnaCtrl != Sgtl5000ExpectedAnaCtrlBaseline)
        {
            throw new InvalidOperationException($"Expected CHIP_ANA_CTRL=0x{Sgtl5000ExpectedAnaCtrlBaseline:X4} after StopStream, got 0x{afterAnaCtrl:X4}.");
        }
    }

    private static void RunPlaybackAudibleDemo(IntPtr winUsbHandle, Dictionary<byte, WinUsbPipeInformation> endpoints, ushort maxPacket)
    {
        if (!endpoints.ContainsKey(AudioOutPipe) || !endpoints.ContainsKey(AudioInPipe))
        {
            Console.WriteLine("Skipping playback audible demo: audio pipes 0x02/0x82 not both present.");
            return;
        }

        var audioMaxPacket = endpoints[(byte)AudioInPipe].MaximumPacketSize;

        DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Playback audible pre-drain");
        DrainAudioPackets(winUsbHandle, audioMaxPacket, "Playback audible pre-drain audio");

        var startStreamResponse = ExecuteCommand(winUsbHandle,
                                                 maxPacket,
                                                 400,
                                                 0x05,
                                                 BuildStartStreamPayload(LoopbackSampleRateHz,
                                                                         LoopbackChannelCount,
                                                                         LoopbackContainerBitsPerSample,
                                                                         AudioSourceHostRxLoopback));
        ValidateResponseStatus(startStreamResponse, expectedStatus: 0, "StartStream (playback audible)");
        Console.WriteLine($"Playback audible StartStream response: {DescribeFrame(startStreamResponse)}");
        Console.WriteLine("Playing 1 kHz sine for about 2 seconds...");

        var playbackStopwatch = Stopwatch.StartNew();
        var packetDurationTicks = (long)Math.Round((double)Stopwatch.Frequency * PlaybackAudibleFramesPerPacket / LoopbackSampleRateHz);
        var nextPacketDeadlineTicks = packetDurationTicks;
        var activeSnapshotCaptured = false;

        for (var packetIndex = 0; packetIndex < PlaybackAudiblePacketCount; packetIndex++)
        {
            var flags = (packetIndex == 0) ? AudioFlagStartOfStream : (ushort)0;
            var sequenceNumber = (uint)packetIndex;
            var timestamp = (uint)(packetIndex * PlaybackAudibleFramesPerPacket);
            var payload = BuildHostSinePayload(PlaybackAudibleFramesPerPacket,
                                               LoopbackChannelCount,
                                               LoopbackValidBitsPerSample,
                                               LoopbackSampleRateHz,
                                               frequencyHz: 1000.0,
                                               startingFrameIndex: packetIndex * PlaybackAudibleFramesPerPacket);
            var audioPacket = BuildAudioPacket(sequenceNumber,
                                               timestamp,
                                               LoopbackSampleRateHz,
                                               LoopbackChannelCount,
                                               LoopbackContainerBitsPerSample,
                                               flags,
                                               payload);

            var echoedPacket = ExchangeAudioPacket(winUsbHandle, audioMaxPacket, audioPacket);

            if (packetIndex == 0 || packetIndex == PlaybackAudiblePacketCount - 1)
            {
                Console.WriteLine($"Playback audible packet {packetIndex + 1}/{PlaybackAudiblePacketCount}: {DescribeAudioPacket(echoedPacket)}");
            }

            if (!activeSnapshotCaptured && packetIndex >= PlaybackAudibleSnapshotPacketIndex)
            {
                Console.WriteLine("Playback audible codec snapshot during active stream:");
                try
                {
                    Console.WriteLine($"  CHIP_DIG_POWER = 0x{ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 402, Sgtl5000DigPowerRegister, "CHIP_DIG_POWER during playback"):X4}");
                    Console.WriteLine($"  CHIP_ANA_POWER = 0x{ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 403, Sgtl5000AnaPowerRegister, "CHIP_ANA_POWER during playback"):X4}");
                    Console.WriteLine($"  CHIP_CLK_CTRL = 0x{ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 404, Sgtl5000ClkCtrlRegister, "CHIP_CLK_CTRL during playback"):X4}");
                    Console.WriteLine($"  CHIP_I2S_CTRL = 0x{ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 405, Sgtl5000I2sCtrlRegister, "CHIP_I2S_CTRL during playback"):X4}");
                    Console.WriteLine($"  CHIP_SSS_CTRL = 0x{ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 406, Sgtl5000SssCtrlRegister, "CHIP_SSS_CTRL during playback"):X4}");
                    Console.WriteLine($"  CHIP_ANA_HP_CTRL = 0x{ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 407, Sgtl5000AnaHpCtrlRegister, "CHIP_ANA_HP_CTRL during playback"):X4}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  Mid-stream codec snapshot failed: {ex.Message}");
                }

                PrintDebugStateSnapshot(winUsbHandle, maxPacket, 414, "Playback audible debug state during stream");
                activeSnapshotCaptured = true;
            }

            WaitForPlaybackDeadline(playbackStopwatch, nextPacketDeadlineTicks);
            nextPacketDeadlineTicks += packetDurationTicks;
        }

        var stopStreamResponse = ExecuteCommand(winUsbHandle, maxPacket, 408, 0x06, Array.Empty<byte>());
        ValidateResponseStatus(stopStreamResponse, expectedStatus: 0, "StopStream (playback audible)");
        Console.WriteLine($"Playback audible StopStream response: {DescribeFrame(stopStreamResponse)}");
        DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Playback audible post-stop");
        DrainAudioPackets(winUsbHandle, audioMaxPacket, "Playback audible post-stop audio");

        Console.WriteLine("Playback audible codec snapshot after stop:");
        Console.WriteLine($"  CHIP_DIG_POWER = 0x{ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 409, Sgtl5000DigPowerRegister, "CHIP_DIG_POWER after playback"):X4}");
        Console.WriteLine($"  CHIP_ANA_POWER = 0x{ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 410, Sgtl5000AnaPowerRegister, "CHIP_ANA_POWER after playback"):X4}");
        Console.WriteLine($"  CHIP_I2S_CTRL = 0x{ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 411, Sgtl5000I2sCtrlRegister, "CHIP_I2S_CTRL after playback"):X4}");
        Console.WriteLine($"  CHIP_SSS_CTRL = 0x{ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 412, Sgtl5000SssCtrlRegister, "CHIP_SSS_CTRL after playback"):X4}");
        Console.WriteLine($"  CHIP_ANA_HP_CTRL = 0x{ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 413, Sgtl5000AnaHpCtrlRegister, "CHIP_ANA_HP_CTRL after playback"):X4}");
        PrintDebugStateSnapshot(winUsbHandle, maxPacket, 415, "Playback audible debug state after stop");
    }

    private static byte[] BuildHostSinePayload(int frameCount,
                                               byte channelCount,
                                               int validBitsPerSample,
                                               uint sampleRateHz,
                                               double frequencyHz,
                                               int startingFrameIndex)
    {
        var payload = new byte[frameCount * channelCount * sizeof(int)];
        var amplitude = (1 << (validBitsPerSample - 2)) - 1;

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var samplePhase = 2.0 * Math.PI * frequencyHz * (startingFrameIndex + frameIndex) / sampleRateHz;
            var sample24 = (int)(Math.Sin(samplePhase) * amplitude);
            var sample32 = PackSample24ToMsbAligned32(sample24);

            for (var channelIndex = 0; channelIndex < channelCount; channelIndex++)
            {
                var sampleOffset = ((frameIndex * channelCount) + channelIndex) * sizeof(int);
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(sampleOffset, sizeof(int)), sample32);
            }
        }

        return payload;
    }

    private static void RunLoopbackBurstStress(IntPtr winUsbHandle, ushort audioMaxPacket)
    {
        uint sequenceNumber = 0;
        uint timestamp = 0;
        var totalPackets = 0;

        for (var burstIndex = 0; burstIndex < LoopbackStressBurstCount; burstIndex++)
        {
            for (var packetIndex = 0; packetIndex < LoopbackStressPacketsPerBurst; packetIndex++)
            {
                var flags = (sequenceNumber == 0U) ? AudioFlagStartOfStream : (ushort)0;
                var payload = BuildLoopbackPayload(frameCount: LoopbackStressFramesPerPacket,
                                                   seed: burstIndex * 97 + packetIndex * 13,
                                                   channelCount: LoopbackChannelCount);
                var audioPacket = BuildAudioPacket(sequenceNumber,
                                                   timestamp,
                                                   LoopbackSampleRateHz,
                                                   LoopbackChannelCount,
                                                   LoopbackContainerBitsPerSample,
                                                   flags,
                                                   payload);

                var echoedAudioPacket = ExchangeAudioPacket(winUsbHandle, audioMaxPacket, audioPacket);
                var echoedInfo = ParseAudioPacket(echoedAudioPacket);
                ValidateEchoedAudioPacket(echoedInfo,
                                          sequenceNumber,
                                          flags,
                                          expectDiscontinuity: false,
                                          payload);

                sequenceNumber += 1U;
                timestamp += LoopbackStressFramesPerPacket;
                totalPackets += 1;
            }

            if (((burstIndex + 1) % LoopbackStressDebugSnapshotPeriod) == 0)
            {
                Console.WriteLine($"Loopback stress progress: completedBursts={burstIndex + 1} packets={totalPackets}");
            }

            Thread.Sleep(((burstIndex + 1) % 4) == 0 ? LoopbackStressLongPauseMs : LoopbackStressShortPauseMs);
        }

        Console.WriteLine($"Loopback stress summary: bursts={LoopbackStressBurstCount} packets={totalPackets} framesPerPacket={LoopbackStressFramesPerPacket} payloadBytesPerPacket={LoopbackStressFramesPerPacket * LoopbackChannelCount * sizeof(int)}");
    }

    private static void PrintDebugStateSnapshot(IntPtr winUsbHandle, ushort maxPacket, ushort sequence, string label)
    {
        var debugStateResponse = ExecuteCommand(winUsbHandle, maxPacket, sequence, 0x03, Array.Empty<byte>());
        ValidateResponseStatus(debugStateResponse, expectedStatus: 0, label);
        Console.WriteLine($"{label}: {DescribeFrame(debugStateResponse)}");
    }

    private static void RunGeneratedSourceScenario(IntPtr winUsbHandle,
                                                   ushort maxPacket,
                                                   ushort audioMaxPacket,
                                                   ushort setConfigSequence,
                                                   ushort startSequence,
                                                   ushort stopSequence,
                                                   byte source,
                                                   string label,
                                                   byte[] configPayload)
    {
        var setConfigResponse = ExecuteCommand(winUsbHandle, maxPacket, setConfigSequence, 0x07, configPayload);
        ValidateResponseStatus(setConfigResponse, expectedStatus: 0, $"SetGeneratorConfig ({label})");
        Console.WriteLine($"{label} config response: {DescribeFrame(setConfigResponse)}");

        var startStreamResponse = ExecuteCommand(winUsbHandle,
                             maxPacket,
                             startSequence,
                             0x05,
                             BuildStartStreamPayload(GeneratedSampleRateHz,
                                         GeneratedChannelCount,
                                         GeneratedContainerBitsPerSample,
                                         source));
        ValidateResponseStatus(startStreamResponse, expectedStatus: 0, $"StartStream ({label})");
        Console.WriteLine($"{label} StartStream response: {DescribeFrame(startStreamResponse)}");

        RunGeneratedAudioDemo(winUsbHandle, audioMaxPacket, source, label);

        var stopStreamResponse = ExecuteCommand(winUsbHandle, maxPacket, stopSequence, 0x06, Array.Empty<byte>());
        ValidateResponseStatus(stopStreamResponse, expectedStatus: 0, $"StopStream ({label})");
        Console.WriteLine($"{label} StopStream response: {DescribeFrame(stopStreamResponse)}");
        DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, $"{label} post-stop");
        DrainAudioPackets(winUsbHandle, audioMaxPacket, $"{label} post-stop audio");
    }

    private static byte[] BuildStartStreamPayload(uint sampleRateHz, byte channelCount, byte bitsPerSample, byte source)
    {
        var payload = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), sampleRateHz);
        payload[4] = channelCount;
        payload[5] = bitsPerSample;
        payload[6] = source;
        payload[7] = 0;
        return payload;
    }

    private static byte[] BuildSetGeneratorConfigPayload(uint primaryFrequencyHz,
                                                         uint secondaryFrequencyHz,
                                                         uint modulationPeriodMs,
                                                         ushort amplitude,
                                                         byte source,
                                                         byte noiseType,
                                                         byte amplitudeEnvelope,
                                                         uint noiseSeed)
    {
        var payload = new byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), primaryFrequencyHz);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), secondaryFrequencyHz);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), modulationPeriodMs);
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(12, 2), amplitude);
        payload[14] = source;
        payload[15] = noiseType;
        payload[16] = amplitudeEnvelope;
        payload[17] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(18, 4), noiseSeed);
        return payload;
    }

    private static byte[] BuildI2cReadRegisterPayload(byte deviceAddress,
                                                      byte registerAddressSize,
                                                      byte readLength,
                                                      uint registerAddress)
    {
        var payload = new byte[8];
        payload[0] = deviceAddress;
        payload[1] = registerAddressSize;
        payload[2] = readLength;
        payload[3] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), registerAddress);
        return payload;
    }

    private static byte[] BuildI2cWriteRegisterPayload(byte deviceAddress,
                                                       byte registerAddressSize,
                                                       uint registerAddress,
                                                       ReadOnlySpan<byte> data)
    {
        var payload = new byte[8 + data.Length];
        payload[0] = deviceAddress;
        payload[1] = registerAddressSize;
        payload[2] = checked((byte)data.Length);
        payload[3] = 0;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), registerAddress);
        data.CopyTo(payload.AsSpan(8));
        return payload;
    }

    private static byte[] BuildAudioPacket(uint sequenceNumber,
                                           uint timestamp,
                                           uint sampleRateHz,
                                           byte channelCount,
                                           byte bitsPerSample,
                                           ushort flags,
                                           byte[] payload)
    {
        var packet = new byte[AudioHeaderSize + payload.Length];

        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0, 2), AudioHeaderMagic);
        packet[2] = AudioHeaderVersion;
        packet[3] = AudioHeaderSize;
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(4, 2), flags);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(6, 2), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8, 4), sequenceNumber);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12, 4), timestamp);
        BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(16, 2), (ushort)payload.Length);
        packet[18] = channelCount;
        packet[19] = bitsPerSample;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(20, 4), sampleRateHz);
        payload.CopyTo(packet.AsSpan(AudioHeaderSize));

        return packet;
    }

    private static void RunAudioLoopbackDemo(IntPtr winUsbHandle, ushort audioMaxPacket)
    {
        var demoPackets = new (uint SequenceNumber, uint Timestamp, ushort Flags, bool ExpectDiscontinuity)[]
        {
            (0U, 0U, AudioFlagStartOfStream, false),
            (1U, 160U, (ushort)0, false),
            (3U, 320U, (ushort)0, true),
        };

        for (var packetIndex = 0; packetIndex < demoPackets.Length; packetIndex++)
        {
            var demoPacket = demoPackets[packetIndex];
            var payload = BuildLoopbackPayload(frameCount: 4,
                                               seed: packetIndex * 17,
                                               channelCount: LoopbackChannelCount);
            var audioPacket = BuildAudioPacket(sequenceNumber: demoPacket.SequenceNumber,
                                               timestamp: demoPacket.Timestamp,
                                               sampleRateHz: LoopbackSampleRateHz,
                                               channelCount: LoopbackChannelCount,
                                               bitsPerSample: LoopbackContainerBitsPerSample,
                                               flags: demoPacket.Flags,
                                               payload: payload);

            var echoedAudioPacket = ExchangeAudioPacket(winUsbHandle, audioMaxPacket, audioPacket);
            var echoedInfo = ParseAudioPacket(echoedAudioPacket);
            ValidateEchoedAudioPacket(echoedInfo,
                                      demoPacket.SequenceNumber,
                                      demoPacket.Flags,
                                      demoPacket.ExpectDiscontinuity,
                                      payload);
            Console.WriteLine($"Audio echo {packetIndex + 1}/{demoPackets.Length}: {DescribeAudioPacket(echoedAudioPacket)}");
        }
    }

    private static void RunEndOfStreamLoopbackDemo(IntPtr winUsbHandle, ushort audioMaxPacket)
    {
                var payload = BuildLoopbackPayload(frameCount: 4,
                                                                                     seed: 96,
                                                                                     channelCount: LoopbackChannelCount);
        var eosPacket = BuildAudioPacket(sequenceNumber: 0U,
                                         timestamp: 0U,
                                                                                 sampleRateHz: LoopbackSampleRateHz,
                                                                                 channelCount: LoopbackChannelCount,
                                                                                 bitsPerSample: LoopbackContainerBitsPerSample,
                                         flags: (ushort)(AudioFlagStartOfStream | AudioFlagEndOfStream),
                                         payload: payload);

        var echoedAudioPacket = ExchangeAudioPacket(winUsbHandle, audioMaxPacket, eosPacket);
        var echoedInfo = ParseAudioPacket(echoedAudioPacket);
        ValidateEchoedAudioPacket(echoedInfo,
                                  expectedSequenceNumber: 0U,
                                  sentFlags: (ushort)(AudioFlagStartOfStream | AudioFlagEndOfStream),
                                  expectDiscontinuity: false,
                                  expectedPayload: payload);
        Console.WriteLine($"Audio EOS echo: {DescribeAudioPacket(echoedAudioPacket)}");
    }

    private static void RunFormatMismatchDropDemo(IntPtr winUsbHandle, ushort audioMaxPacket)
    {
        var payload = BuildLoopbackPayload(frameCount: 4,
                                           seed: 128,
                                           channelCount: LoopbackChannelCount);
        var mismatchedPacket = BuildAudioPacket(sequenceNumber: 0U,
                                                timestamp: 0U,
                                                sampleRateHz: 44100U,
                                                channelCount: LoopbackChannelCount,
                                                bitsPerSample: LoopbackContainerBitsPerSample,
                                                flags: AudioFlagStartOfStream,
                                                payload: payload);

        WriteAudioPacket(winUsbHandle, mismatchedPacket);

        if (TryReadAudioPacket(winUsbHandle, audioMaxPacket, out var unexpectedEchoPacket))
        {
            throw new InvalidOperationException($"Unexpected audio echo for format mismatch test: {DescribeAudioPacket(unexpectedEchoPacket)}");
        }

        Console.WriteLine("Format mismatch test: no audio echo received before timeout, as expected.");
    }

    private static void RunCodecProbeDemo(IntPtr winUsbHandle, ushort maxPacket)
    {
        Console.WriteLine("Running SGTL5000 codec probe over control-plane I2C...");

        var chipId = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 5, Sgtl5000ChipIdRegister, "CHIP_ID");
        Console.WriteLine($"SGTL5000 CHIP_ID register value: 0x{chipId:X4}");

        Console.WriteLine("SGTL5000 baseline register snapshot:");
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 6, "CHIP_DIG_POWER", Sgtl5000DigPowerRegister, expectedValue: 0x0000);
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 7, "CHIP_CLK_CTRL", Sgtl5000ClkCtrlRegister, expectedValue: 0x0008);
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 8, "CHIP_I2S_CTRL", Sgtl5000I2sCtrlRegister, expectedValue: 0x0010);
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 9, "CHIP_SSS_CTRL", Sgtl5000SssCtrlRegister, expectedValue: 0x0010);
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 10, "CHIP_ADCDAC_CTRL", Sgtl5000AdcDacCtrlRegister, expectedValue: 0x323C);
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 11, "CHIP_ANA_CTRL", Sgtl5000AnaCtrlRegister, expectedValue: 0x0111);
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 12, "CHIP_ANA_POWER", Sgtl5000AnaPowerRegister, expectedValue: Sgtl5000ExpectedAnaPowerValue);
    }

    private static void RunCodecWriteSmokeDemo(IntPtr winUsbHandle, ushort maxPacket)
    {
        Console.WriteLine("Running SGTL5000 codec write smoke test over control-plane I2C...");

        var chipId = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 5, Sgtl5000ChipIdRegister, "CHIP_ID");
        Console.WriteLine($"SGTL5000 CHIP_ID register value: 0x{chipId:X4}");

        var originalDigPower = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 6, Sgtl5000DigPowerRegister, "CHIP_DIG_POWER");
        Console.WriteLine($"Original CHIP_DIG_POWER: 0x{originalDigPower:X4}");

        WriteCodecRegister16(winUsbHandle, maxPacket, sequence: 7, Sgtl5000DigPowerRegister, 0x0021, "CHIP_DIG_POWER enable DAC+I2S_IN");
        var updatedDigPower = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 8, Sgtl5000DigPowerRegister, "CHIP_DIG_POWER");
        Console.WriteLine($"Updated CHIP_DIG_POWER: 0x{updatedDigPower:X4}");

        WriteCodecRegister16(winUsbHandle, maxPacket, sequence: 9, Sgtl5000DigPowerRegister, originalDigPower, "CHIP_DIG_POWER restore baseline");
        var restoredDigPower = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 10, Sgtl5000DigPowerRegister, "CHIP_DIG_POWER");
        Console.WriteLine($"Restored CHIP_DIG_POWER: 0x{restoredDigPower:X4}");

        if (updatedDigPower != 0x0021)
        {
            throw new InvalidOperationException($"CHIP_DIG_POWER write did not stick. Expected 0x0021, got 0x{updatedDigPower:X4}.");
        }

        if (restoredDigPower != originalDigPower)
        {
            throw new InvalidOperationException($"CHIP_DIG_POWER restore did not stick. Expected 0x{originalDigPower:X4}, got 0x{restoredDigPower:X4}.");
        }
    }

    private static void RunCodecAnalogSmokeDemo(IntPtr winUsbHandle, ushort maxPacket)
    {
        Console.WriteLine("Running SGTL5000 codec analog smoke test over control-plane I2C...");

        var chipId = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 5, Sgtl5000ChipIdRegister, "CHIP_ID");
        Console.WriteLine($"SGTL5000 CHIP_ID register value: 0x{chipId:X4}");

        var originalAnaPower = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 6, Sgtl5000AnaPowerRegister, "CHIP_ANA_POWER");
        Console.WriteLine($"Original CHIP_ANA_POWER: 0x{originalAnaPower:X4}");

        const ushort targetAnaPower = Sgtl5000ExpectedAnaPowerValue;
        WriteCodecRegister16(winUsbHandle, maxPacket, sequence: 7, Sgtl5000AnaPowerRegister, targetAnaPower, "CHIP_ANA_POWER staged analog enable");

        var updatedAnaPower = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 8, Sgtl5000AnaPowerRegister, "CHIP_ANA_POWER");
        Console.WriteLine($"Updated CHIP_ANA_POWER: 0x{updatedAnaPower:X4}");

        var chipIdAfterAnalogWrite = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 9, Sgtl5000ChipIdRegister, "CHIP_ID after analog write");
        Console.WriteLine($"CHIP_ID after analog write: 0x{chipIdAfterAnalogWrite:X4}");

        WriteCodecRegister16(winUsbHandle, maxPacket, sequence: 10, Sgtl5000AnaPowerRegister, originalAnaPower, "CHIP_ANA_POWER restore baseline");

        var restoredAnaPower = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 11, Sgtl5000AnaPowerRegister, "CHIP_ANA_POWER");
        Console.WriteLine($"Restored CHIP_ANA_POWER: 0x{restoredAnaPower:X4}");

        var chipIdAfterRestore = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 12, Sgtl5000ChipIdRegister, "CHIP_ID after restore");
        Console.WriteLine($"CHIP_ID after restore: 0x{chipIdAfterRestore:X4}");

        if (updatedAnaPower != targetAnaPower)
        {
            throw new InvalidOperationException($"CHIP_ANA_POWER write did not stick. Expected 0x{targetAnaPower:X4}, got 0x{updatedAnaPower:X4}.");
        }

        if (restoredAnaPower != originalAnaPower)
        {
            throw new InvalidOperationException($"CHIP_ANA_POWER restore did not stick. Expected 0x{originalAnaPower:X4}, got 0x{restoredAnaPower:X4}.");
        }
    }

    private static void RunCodecAnalogStepSmokeDemo(IntPtr winUsbHandle, ushort maxPacket)
    {
        Console.WriteLine("Running SGTL5000 codec analog step smoke test over control-plane I2C...");

        var chipId = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 5, Sgtl5000ChipIdRegister, "CHIP_ID");
        Console.WriteLine($"SGTL5000 CHIP_ID register value: 0x{chipId:X4}");

        var baselineAnaPower = ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 6, Sgtl5000AnaPowerRegister, "CHIP_ANA_POWER baseline");
        Console.WriteLine($"Baseline CHIP_ANA_POWER: 0x{baselineAnaPower:X4}");

        var tests = new (string Label, ushort TargetValue)[]
        {
            ("set VAG powerup", (ushort)(baselineAnaPower | 0x0080)),
            ("set VAG + DAC + capless", (ushort)(baselineAnaPower | 0x008C)),
            ("set VAG + DAC + LINREG_D", (ushort)(baselineAnaPower | 0x0288)),
            ("set VAG + DAC + charge pump", (ushort)(baselineAnaPower | 0x0888)),
            ("set VAG + DAC + capless + LINREG_D", (ushort)(baselineAnaPower | 0x028C)),
            ("set VAG + DAC + capless + charge pump", (ushort)(baselineAnaPower | 0x088C)),
            ("set VAG + headphone powerup", (ushort)(baselineAnaPower | 0x0090)),
            ("set VAG + DAC powerup", (ushort)(baselineAnaPower | 0x0088)),
            ("set VAG + headphone + DAC powerup", (ushort)(baselineAnaPower | 0x0098)),
            ("set VAG + headphone + DAC + capless", (ushort)(baselineAnaPower | 0x009C)),
            ("set headphone powerup", (ushort)(baselineAnaPower | 0x0010)),
            ("set DAC powerup", (ushort)(baselineAnaPower | 0x0008)),
            ("set ADC powerup", (ushort)(baselineAnaPower | 0x0002)),
            ("set lineout powerup", (ushort)(baselineAnaPower | 0x0001)),
            ("set capless headphone powerup", (ushort)(baselineAnaPower | 0x0004)),
            ("set LINREG_D powerup", (ushort)(baselineAnaPower | 0x0200)),
            ("set charge pump powerup", (ushort)(baselineAnaPower | 0x0800)),
            ("clear startup powerup", (ushort)(baselineAnaPower & 0xEFFF)),
        };

        ushort sequence = 7;

        foreach (var test in tests)
        {
            try
            {
                RunCodecAnalogStep(winUsbHandle, maxPacket, ref sequence, baselineAnaPower, test.TargetValue, test.Label);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Analog step '{test.Label}' failed: {ex.Message}");
            }
        }

        var stagedSequences = new (string Label, ushort[] Values)[]
        {
            ("VAG -> HP -> capless -> DAC", new ushort[] { 0x70E0, 0x70F0, 0x70F4, 0x70FC }),
            ("VAG -> DAC -> capless -> HP", new ushort[] { 0x70E0, 0x70E8, 0x70EC, 0x70FC }),
            ("DAC -> VAG -> capless -> HP", new ushort[] { 0x7068, 0x70E8, 0x70EC, 0x70FC }),
        };

        foreach (var stagedSequence in stagedSequences)
        {
            try
            {
                RunCodecAnalogStagedSequence(winUsbHandle, maxPacket, ref sequence, baselineAnaPower, stagedSequence.Label, stagedSequence.Values);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Analog staged sequence '{stagedSequence.Label}' failed: {ex.Message}");
            }
        }
    }

    private static void RunCodecPlaybackSequenceSmokeDemo(IntPtr winUsbHandle, ushort maxPacket)
    {
        Console.WriteLine("Running SGTL5000 playback sequence smoke test over control-plane I2C...");

        const ushort playbackRefCtrl = 0x01F1;
        const ushort playbackAnaPower = 0x70FC;
        const ushort playbackDigPower = 0x0021;
        const ushort playbackHpCtrl = 0x1818;
        const ushort playbackDacVol = 0x5C5C;
        const ushort playbackAdcDacCtrl = 0x3230;
        const ushort playbackAnaCtrl = 0x0001;

        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 60, "CHIP_DIG_POWER baseline", Sgtl5000DigPowerRegister, expectedValue: 0x0000);
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 61, "CHIP_ANA_POWER baseline", Sgtl5000AnaPowerRegister, expectedValue: 0x7060);
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 62, "CHIP_REF_CTRL baseline", Sgtl5000RefCtrlRegister, expectedValue: 0x0000);

        WriteCodecRegister16(winUsbHandle, maxPacket, sequence: 63, Sgtl5000RefCtrlRegister, playbackRefCtrl, "CHIP_REF_CTRL playback bias");
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 64, "CHIP_REF_CTRL after write", Sgtl5000RefCtrlRegister, expectedValue: playbackRefCtrl);

        WriteCodecRegister16(winUsbHandle, maxPacket, sequence: 65, Sgtl5000DigPowerRegister, playbackDigPower, "CHIP_DIG_POWER playback enable");
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 66, "CHIP_DIG_POWER after write", Sgtl5000DigPowerRegister, expectedValue: playbackDigPower);

        WriteCodecRegister16(winUsbHandle, maxPacket, sequence: 67, Sgtl5000AnaPowerRegister, playbackAnaPower, "CHIP_ANA_POWER playback enable");
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 68, "CHIP_ANA_POWER after write", Sgtl5000AnaPowerRegister, expectedValue: playbackAnaPower);

        WriteCodecRegister16(winUsbHandle, maxPacket, sequence: 69, Sgtl5000AnaHpCtrlRegister, playbackHpCtrl, "CHIP_ANA_HP_CTRL playback volume");
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 70, "CHIP_ANA_HP_CTRL after write", Sgtl5000AnaHpCtrlRegister, expectedValue: playbackHpCtrl);

        WriteCodecRegister16(winUsbHandle, maxPacket, sequence: 71, Sgtl5000DacVolRegister, playbackDacVol, "CHIP_DAC_VOL playback volume");
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 72, "CHIP_DAC_VOL after write", Sgtl5000DacVolRegister, expectedValue: playbackDacVol);

        WriteCodecRegister16(winUsbHandle, maxPacket, sequence: 73, Sgtl5000AdcDacCtrlRegister, playbackAdcDacCtrl, "CHIP_ADCDAC_CTRL playback unmute");
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 74, "CHIP_ADCDAC_CTRL after write", Sgtl5000AdcDacCtrlRegister, expectedValue: playbackAdcDacCtrl);

        WriteCodecRegister16(winUsbHandle, maxPacket, sequence: 75, Sgtl5000AnaCtrlRegister, playbackAnaCtrl, "CHIP_ANA_CTRL playback unmute");
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 76, "CHIP_ANA_CTRL after write", Sgtl5000AnaCtrlRegister, expectedValue: playbackAnaCtrl);

        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 77, "CHIP_ID after playback sequence", Sgtl5000ChipIdRegister, expectedValue: 0xA011);

        Thread.Sleep(250);
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 78, "CHIP_DIG_POWER after 250ms", Sgtl5000DigPowerRegister, expectedValue: playbackDigPower);
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 79, "CHIP_ANA_POWER after 250ms", Sgtl5000AnaPowerRegister, expectedValue: playbackAnaPower);
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 80, "CHIP_ADCDAC_CTRL after 250ms", Sgtl5000AdcDacCtrlRegister, expectedValue: playbackAdcDacCtrl);
        DumpCodecRegister16(winUsbHandle, maxPacket, sequence: 81, "CHIP_ANA_CTRL after 250ms", Sgtl5000AnaCtrlRegister, expectedValue: playbackAnaCtrl);
        Console.WriteLine($"  CHIP_REF_CTRL after 250ms = 0x{ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 82, Sgtl5000RefCtrlRegister, "CHIP_REF_CTRL after 250ms"):X4}");
        Console.WriteLine($"  CHIP_ANA_STATUS after 250ms = 0x{ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 83, Sgtl5000AnaStatusRegister, "CHIP_ANA_STATUS after 250ms"):X4}");
        Console.WriteLine($"  CHIP_SHORT_CTRL after 250ms = 0x{ReadCodecRegister16(winUsbHandle, maxPacket, sequence: 84, Sgtl5000ShortCtrlRegister, "CHIP_SHORT_CTRL after 250ms"):X4}");

        WriteCodecRegister16(winUsbHandle, maxPacket, sequence: 85, Sgtl5000AnaCtrlRegister, Sgtl5000ExpectedAnaCtrlBaseline, "CHIP_ANA_CTRL restore baseline");
        WriteCodecRegister16(winUsbHandle, maxPacket, sequence: 86, Sgtl5000AdcDacCtrlRegister, Sgtl5000ExpectedAdcDacCtrlBaseline, "CHIP_ADCDAC_CTRL restore baseline");
        WriteCodecRegister16(winUsbHandle, maxPacket, sequence: 87, Sgtl5000AnaPowerRegister, Sgtl5000ExpectedAnaPowerValue, "CHIP_ANA_POWER restore baseline");
        WriteCodecRegister16(winUsbHandle, maxPacket, sequence: 88, Sgtl5000DigPowerRegister, 0x0000, "CHIP_DIG_POWER restore baseline");
        WriteCodecRegister16(winUsbHandle, maxPacket, sequence: 89, Sgtl5000RefCtrlRegister, 0x0000, "CHIP_REF_CTRL restore baseline");
    }

    private static void RunCodecAnalogStep(IntPtr winUsbHandle,
                                           ushort maxPacket,
                                           ref ushort sequence,
                                           ushort baselineAnaPower,
                                           ushort targetAnaPower,
                                           string label)
    {
        Console.WriteLine($"Testing analog step: {label} target=0x{targetAnaPower:X4}");

        ushort observedAnaPower = 0;
        ushort restoredAnaPower = 0;

        try
        {
            WriteCodecRegister16(winUsbHandle, maxPacket, sequence++, Sgtl5000AnaPowerRegister, targetAnaPower, $"CHIP_ANA_POWER {label}");

            observedAnaPower = ReadCodecRegister16(winUsbHandle, maxPacket, sequence++, Sgtl5000AnaPowerRegister, $"CHIP_ANA_POWER after {label}");
            Console.WriteLine($"Observed CHIP_ANA_POWER after {label}: 0x{observedAnaPower:X4}");

            var chipIdAfterStep = ReadCodecRegister16(winUsbHandle, maxPacket, sequence++, Sgtl5000ChipIdRegister, $"CHIP_ID after {label}");
            Console.WriteLine($"CHIP_ID after {label}: 0x{chipIdAfterStep:X4}");

            Thread.Sleep(250);
            var heldAnaPower = ReadCodecRegister16(winUsbHandle, maxPacket, sequence++, Sgtl5000AnaPowerRegister, $"CHIP_ANA_POWER 250ms after {label}");
            Console.WriteLine($"Held CHIP_ANA_POWER after {label}: 0x{heldAnaPower:X4}");
            Console.WriteLine($"CHIP_ANA_STATUS after {label}: 0x{ReadCodecRegister16(winUsbHandle, maxPacket, sequence++, Sgtl5000AnaStatusRegister, $"CHIP_ANA_STATUS after {label}"):X4}");

            if (observedAnaPower != targetAnaPower)
            {
                throw new InvalidOperationException($"Analog step '{label}' did not stick. Expected 0x{targetAnaPower:X4}, got 0x{observedAnaPower:X4}.");
            }

            if (heldAnaPower != targetAnaPower)
            {
                throw new InvalidOperationException($"Analog step '{label}' did not hold for 250ms. Expected 0x{targetAnaPower:X4}, got 0x{heldAnaPower:X4}.");
            }
        }
        finally
        {
            WriteCodecRegister16(winUsbHandle, maxPacket, sequence++, Sgtl5000AnaPowerRegister, baselineAnaPower, $"CHIP_ANA_POWER restore after {label}");
            restoredAnaPower = ReadCodecRegister16(winUsbHandle, maxPacket, sequence++, Sgtl5000AnaPowerRegister, $"CHIP_ANA_POWER restored after {label}");
            Console.WriteLine($"Restored CHIP_ANA_POWER after {label}: 0x{restoredAnaPower:X4}");
        }

        if (restoredAnaPower != baselineAnaPower)
        {
            throw new InvalidOperationException($"Analog step '{label}' did not restore baseline. Expected 0x{baselineAnaPower:X4}, got 0x{restoredAnaPower:X4}.");
        }
    }

    private static void RunCodecAnalogStagedSequence(IntPtr winUsbHandle,
                                                     ushort maxPacket,
                                                     ref ushort sequence,
                                                     ushort baselineAnaPower,
                                                     string label,
                                                     ushort[] stagedValues)
    {
        Console.WriteLine($"Testing staged analog sequence: {label}");

        try
        {
            for (var index = 0; index < stagedValues.Length; index++)
            {
                var value = stagedValues[index];
                var stepLabel = $"{label} step {index + 1}";

                WriteCodecRegister16(winUsbHandle, maxPacket, sequence++, Sgtl5000AnaPowerRegister, value, $"CHIP_ANA_POWER {stepLabel}");

                var immediateValue = ReadCodecRegister16(winUsbHandle, maxPacket, sequence++, Sgtl5000AnaPowerRegister, $"CHIP_ANA_POWER after {stepLabel}");
                Console.WriteLine($"Immediate CHIP_ANA_POWER after {stepLabel}: 0x{immediateValue:X4}");

                Thread.Sleep(250);
                var heldValue = ReadCodecRegister16(winUsbHandle, maxPacket, sequence++, Sgtl5000AnaPowerRegister, $"CHIP_ANA_POWER held after {stepLabel}");
                Console.WriteLine($"Held CHIP_ANA_POWER after {stepLabel}: 0x{heldValue:X4}");

                if (immediateValue != value)
                {
                    throw new InvalidOperationException($"{stepLabel} did not stick immediately. Expected 0x{value:X4}, got 0x{immediateValue:X4}.");
                }

                if (heldValue != value)
                {
                    throw new InvalidOperationException($"{stepLabel} did not hold for 250ms. Expected 0x{value:X4}, got 0x{heldValue:X4}.");
                }
            }
        }
        finally
        {
            WriteCodecRegister16(winUsbHandle, maxPacket, sequence++, Sgtl5000AnaPowerRegister, baselineAnaPower, $"CHIP_ANA_POWER restore after {label}");
            var restoredValue = ReadCodecRegister16(winUsbHandle, maxPacket, sequence++, Sgtl5000AnaPowerRegister, $"CHIP_ANA_POWER restored after {label}");
            Console.WriteLine($"Restored CHIP_ANA_POWER after {label}: 0x{restoredValue:X4}");
        }
    }

    private static ushort ReadCodecRegister16(IntPtr winUsbHandle,
                                              ushort maxPacket,
                                              ushort sequence,
                                              ushort registerAddress,
                                              string registerName)
    {
        var response = ExecuteCommand(winUsbHandle,
                                      maxPacket,
                                      sequence,
                                      0x09,
                                      BuildI2cReadRegisterPayload(Sgtl5000DeviceAddress,
                                                                  Sgtl5000RegisterAddressSize,
                                                                  readLength: 2,
                                                                  registerAddress));

        Console.WriteLine($"{registerName} response: {DescribeFrame(response)}");

        ValidateResponseStatus(response, expectedStatus: 0, $"I2cReadRegister({registerName})");

        if (response.Payload.Length < 10)
        {
            throw new InvalidOperationException($"{registerName} response was too short: {Convert.ToHexString(response.Payload)}");
        }

        return BinaryPrimitives.ReadUInt16BigEndian(response.Payload.AsSpan(8, 2));
    }

    private static void WriteCodecRegister16(IntPtr winUsbHandle,
                                             ushort maxPacket,
                                             ushort sequence,
                                             ushort registerAddress,
                                             ushort value,
                                             string operationName)
    {
        Span<byte> data = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(data, value);

        var response = ExecuteCommand(winUsbHandle,
                                      maxPacket,
                                      sequence,
                                      0x08,
                                      BuildI2cWriteRegisterPayload(Sgtl5000DeviceAddress,
                                                                   Sgtl5000RegisterAddressSize,
                                                                   registerAddress,
                                                                   data));

        Console.WriteLine($"{operationName} response: {DescribeFrame(response)}");
        ValidateResponseStatus(response, expectedStatus: 0, $"I2cWriteRegister({operationName})");
    }

    private static void DumpCodecRegister16(IntPtr winUsbHandle,
                                            ushort maxPacket,
                                            ushort sequence,
                                            string registerName,
                                            ushort registerAddress,
                                            ushort expectedValue)
    {
        var value = ReadCodecRegister16(winUsbHandle, maxPacket, sequence, registerAddress, registerName);
        var matches = value == expectedValue ? "match" : "MISMATCH";
        Console.WriteLine($"  {registerName} (0x{registerAddress:X4}) = 0x{value:X4} expected=0x{expectedValue:X4} [{matches}]");
    }

    private static void RunGeneratedAudioDemo(IntPtr winUsbHandle, ushort audioMaxPacket, byte source, string label)
    {
        for (uint packetIndex = 0; packetIndex < 3U; packetIndex++)
        {
            if (!TryReadAudioPacket(winUsbHandle, audioMaxPacket, out var audioPacket))
            {
                throw new TimeoutException("Timed out waiting for generated audio packet.");
            }

            var packetInfo = ParseAudioPacket(audioPacket);
            ValidateGeneratedAudioPacket(packetInfo, packetIndex, source);
            Console.WriteLine($"{label} {packetIndex + 1}/3: {DescribeAudioPacket(audioPacket)} {DescribeGeneratedPayload(packetInfo)}");
        }
    }

    private static string DescribeGeneratedPayload(AudioPacketInfo packet)
    {
        var bytesPerSample = packet.BitsPerSample / 8;
        var bytesPerFrame = bytesPerSample * packet.ChannelCount;
        var frameCount = packet.PayloadBytes / bytesPerFrame;
        var minSample = int.MaxValue;
        var maxSample = int.MinValue;
        var previewSamples = new List<int>();

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var leftSampleOffset = frameIndex * bytesPerFrame;
            var sample = UnpackMsbAligned24(BinaryPrimitives.ReadInt32LittleEndian(packet.Payload.AsSpan(leftSampleOffset, bytesPerSample)));

            if (sample < minSample)
            {
                minSample = sample;
            }

            if (sample > maxSample)
            {
                maxSample = sample;
            }

            if (previewSamples.Count < 4)
            {
                previewSamples.Add(sample);
            }
        }

        var preview = string.Join(",", previewSamples);
        return $"frames={frameCount} min={minSample} max={maxSample} firstL=[{preview}]";
    }

    private static byte[] BuildLoopbackPayload(int frameCount, int seed, byte channelCount)
    {
        var payload = new byte[frameCount * channelCount * sizeof(int)];
        var sampleIndex = 0;

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            for (var channelIndex = 0; channelIndex < channelCount; channelIndex++)
            {
                var sample24 = CreateLoopbackSample24(seed, sampleIndex, channelIndex);
                var sample32 = PackSample24ToMsbAligned32(sample24);
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(sampleIndex * sizeof(int), sizeof(int)), sample32);
                sampleIndex++;
            }
        }

        return payload;
    }

    private static int CreateLoopbackSample24(int seed, int sampleIndex, int channelIndex)
    {
        var magnitudeMask = (1 << (LoopbackValidBitsPerSample - 1)) - 1;
        var magnitude = ((seed * 131) + (sampleIndex * 977) + (channelIndex * 4093)) & magnitudeMask;
        if (((sampleIndex + channelIndex) & 1) != 0)
        {
            return -magnitude;
        }

        return magnitude;
    }

    private static int PackSample24ToMsbAligned32(int sample24)
    {
        return sample24 << 8;
    }

    private static int UnpackMsbAligned24(int sample32)
    {
        return sample32 >> 8;
    }

    private static void WaitForPlaybackDeadline(Stopwatch stopwatch, long targetTicks)
    {
        while (true)
        {
            var remainingTicks = targetTicks - stopwatch.ElapsedTicks;
            if (remainingTicks <= 0)
            {
                return;
            }

            var remainingMilliseconds = remainingTicks * 1000.0 / Stopwatch.Frequency;
            if (remainingMilliseconds > 2.0)
            {
                Thread.Sleep((int)remainingMilliseconds - 1);
                continue;
            }

            Thread.SpinWait(500);
        }
    }

    private static byte[] ExchangeAudioPacket(IntPtr winUsbHandle, ushort maxPacket, byte[] audioPacket)
    {
        WriteAudioPacket(winUsbHandle, audioPacket);

        if (!TryReadAudioPacket(winUsbHandle, maxPacket, out var echoedPacket))
        {
            throw new TimeoutException("Timed out waiting for audio echo packet.");
        }

        return echoedPacket;
    }

    private static void WriteAudioPacket(IntPtr winUsbHandle, byte[] audioPacket)
    {
        if (!WinUsb_WritePipe(winUsbHandle, AudioOutPipe, audioPacket, audioPacket.Length, out var bytesWritten, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"WinUsb_WritePipe failed for 0x{AudioOutPipe:X2}");
        }

        if (bytesWritten != audioPacket.Length)
        {
            throw new InvalidOperationException($"Short audio write: expected {audioPacket.Length}, wrote {bytesWritten}.");
        }
    }

    private static bool TryReadAudioPacket(IntPtr winUsbHandle, ushort maxPacket, out byte[] audioPacket)
    {
        var readBuffer = new byte[maxPacket];
        if (!WinUsb_ReadPipe(winUsbHandle, AudioInPipe, readBuffer, readBuffer.Length, out var bytesRead, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 121)
            {
                audioPacket = Array.Empty<byte>();
                return false;
            }

            throw new Win32Exception(error, $"WinUsb_ReadPipe failed for 0x{AudioInPipe:X2}");
        }

        audioPacket = readBuffer.AsSpan(0, bytesRead).ToArray();
        return true;
    }

    private static void DrainProtocolEvents(IntPtr winUsbHandle, ushort maxPacket, int maxAttempts, string phaseLabel)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (!TryReadProtocolFrame(winUsbHandle, CmdInPipe, maxPacket, out var frame))
            {
                if (attempt == 0)
                {
                    Console.WriteLine($"{phaseLabel} event drain: no additional frames before timeout.");
                }

                return;
            }

            var receivedFrame = frame ?? throw new InvalidOperationException("Protocol read returned no frame data.");
            Console.WriteLine($"{phaseLabel} frame: {DescribeFrame(receivedFrame)}");
        }

        Console.WriteLine($"{phaseLabel} event drain stopped after {maxAttempts} frame attempts.");
    }

    private static void DrainAudioPackets(IntPtr winUsbHandle, ushort audioMaxPacket, string phaseLabel)
    {
        var drainedCount = 0;

        while (TryReadAudioPacket(winUsbHandle, audioMaxPacket, out var audioPacket))
        {
            drainedCount++;
            Console.WriteLine($"{phaseLabel} packet {drainedCount}: {DescribeAudioPacket(audioPacket)}");
        }

        if (drainedCount == 0)
        {
            Console.WriteLine($"{phaseLabel}: no additional audio packets before timeout.");
        }
    }

    private static ProtocolFrame ExecuteCommand(IntPtr winUsbHandle, ushort maxPacket, ushort sequence, byte opcode, byte[] payload)
    {
        var commandFrame = BuildCommandFrame(sequence, opcode, payload);

        if (!WinUsb_WritePipe(winUsbHandle, CmdOutPipe, commandFrame, commandFrame.Length, out var bytesWritten, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"WinUsb_WritePipe failed for 0x{CmdOutPipe:X2}");
        }

        if (bytesWritten != commandFrame.Length)
        {
            throw new InvalidOperationException($"Short write: expected {commandFrame.Length}, wrote {bytesWritten}.");
        }

        while (true)
        {
            if (!TryReadProtocolFrame(winUsbHandle, CmdInPipe, maxPacket, out var frame))
            {
                throw new TimeoutException("Timed out waiting for protocol response frame.");
            }

            var receivedFrame = frame ?? throw new InvalidOperationException("Protocol read returned no frame data.");

            if (receivedFrame.Type == FrameTypeCode.Event)
            {
                Console.WriteLine($"Event: {DescribeFrame(receivedFrame)}");
                continue;
            }

            if (receivedFrame.Sequence == sequence)
            {
                return receivedFrame;
            }

            Console.WriteLine($"Ignoring unmatched response: {DescribeFrame(receivedFrame)}");
        }
    }

    private static byte[] BuildCommandFrame(ushort sequence, byte opcode, byte[] payload)
    {
        var frame = new byte[10 + payload.Length];

        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0, 2), FrameMagic);
        frame[2] = ProtocolVersion;
        frame[3] = (byte)FrameTypeCode.Command;
        frame[4] = opcode;
        frame[5] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), sequence);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(8, 2), (ushort)payload.Length);
        payload.CopyTo(frame.AsSpan(10));

        return frame;
    }

    private static bool TryReadProtocolFrame(IntPtr winUsbHandle, int pipeId, ushort maxPacket, out ProtocolFrame? frame)
    {
        var readBuffer = new byte[maxPacket];

        if (!WinUsb_ReadPipe(winUsbHandle, (byte)pipeId, readBuffer, readBuffer.Length, out var bytesRead, IntPtr.Zero))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 121)
            {
                frame = null;
                return false;
            }

            throw new Win32Exception(error, $"WinUsb_ReadPipe failed for 0x{pipeId:X2}");
        }

        frame = ParseFrame(readBuffer.AsSpan(0, bytesRead));
        return true;
    }

    private static ProtocolFrame ParseFrame(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < 10)
        {
            throw new InvalidOperationException($"Protocol frame too short: {buffer.Length} bytes.");
        }

        var magic = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(0, 2));
        var version = buffer[2];
        var type = (FrameTypeCode)buffer[3];
        var opcode = buffer[4];
        var status = buffer[5];
        var sequence = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(6, 2));
        var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(8, 2));

        if (magic != FrameMagic)
        {
            throw new InvalidOperationException($"Unexpected frame magic 0x{magic:X4}.");
        }

        if (version != ProtocolVersion)
        {
            throw new InvalidOperationException($"Unexpected protocol version {version}.");
        }

        if (buffer.Length != 10 + payloadLength)
        {
            throw new InvalidOperationException($"Frame length mismatch: header says {payloadLength}, total is {buffer.Length}.");
        }

        return new ProtocolFrame(type, opcode, status, sequence, buffer.Slice(10, payloadLength).ToArray());
    }

    private static string DescribeFrame(ProtocolFrame frame)
    {
        return frame.Type switch
        {
            FrameTypeCode.Event => DescribeEvent(frame),
            FrameTypeCode.Response => DescribeResponse(frame),
            _ => $"type={frame.Type} opcode={GetOpcodeName(frame.Opcode)}(0x{frame.Opcode:X2}) status={GetStatusName(frame.Status)}({frame.Status}) sequence={frame.Sequence} payload={frame.Payload.Length} bytes",
        };
    }

    private static string DescribeEvent(ProtocolFrame frame)
    {
        return frame.Opcode switch
        {
            0x01 => DescribeDeviceReady(frame),
            0x02 => DescribeStreamStarted(frame),
            0x03 => DescribeStreamStopped(frame),
            0x04 => DescribeFault(frame),
            _ => $"event {GetOpcodeName(frame.Opcode)}(0x{frame.Opcode:X2}) payload={Convert.ToHexString(frame.Payload)}",
        };
    }

    private static string DescribeResponse(ProtocolFrame frame)
    {
        return frame.Opcode switch
        {
            0x01 => DescribeGetInfo(frame),
            0x05 => DescribeStartStream(frame),
            0x06 => DescribeStopStream(frame),
            0x07 => DescribeSetGeneratorConfig(frame),
            0x08 => DescribeI2cWriteRegister(frame),
            0x09 => DescribeI2cReadRegister(frame),
            0x04 => $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} payload=\"{Encoding.ASCII.GetString(frame.Payload)}\"",
            0x02 => DescribeSetLed(frame),
            0x03 => DescribeGetUsbDebugState(frame),
            _ => $"response {GetOpcodeName(frame.Opcode)}(0x{frame.Opcode:X2}) seq={frame.Sequence} status={GetStatusName(frame.Status)} payload={Convert.ToHexString(frame.Payload)}",
        };
    }

    private static string DescribeGetInfo(ProtocolFrame frame)
    {
        if (frame.Payload.Length != 8)
        {
            return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} payload={Convert.ToHexString(frame.Payload)}";
        }

        var protocolVersion = BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload.AsSpan(0, 2));
        var maxPacket = BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload.AsSpan(2, 2));
        var capabilities = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(4, 4));

        return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} protocolVersion={protocolVersion} maxPacket={maxPacket} capabilities=0x{capabilities:X8}";
    }

    private static string DescribeSetLed(ProtocolFrame frame)
    {
        if (frame.Payload.Length != 4)
        {
            return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} payload={Convert.ToHexString(frame.Payload)}";
        }

        var appliedAction = frame.Payload[0];
        return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} appliedAction={GetLedActionName(appliedAction)}({appliedAction})";
    }

    private static string DescribeGetUsbDebugState(ProtocolFrame frame)
    {
        if (frame.Payload.Length is not (64 or 68))
        {
            return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} payload={Convert.ToHexString(frame.Payload)}";
        }

        var stage = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(0, 4));
        var lastEvent = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(4, 4));
        var lastStatus = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(8, 4));
        var usbSpeed = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(12, 4));
        var oversizedOutboundDropCount = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(16, 4));
        var playbackFillLevelBytes = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(40, 4));
        var playbackMinFillLevelBytes = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(44, 4));
        var playbackMaxFillLevelBytes = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(48, 4));
        var playbackUnderrunCount = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(52, 4));
        var playbackOverrunCount = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(56, 4));
        var playbackDroppedBytes = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(60, 4));
        var discontinuityCount = frame.Payload.Length >= 68
            ? BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(64, 4))
            : 0U;

        return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} stage=0x{stage:X8} lastEvent=0x{lastEvent:X8} lastStatus=0x{lastStatus:X8} usbSpeed={GetUsbSpeedName(usbSpeed)}({usbSpeed}) oversizedOutboundDrops={oversizedOutboundDropCount} playbackFill={playbackFillLevelBytes} playbackMin={playbackMinFillLevelBytes} playbackMax={playbackMaxFillLevelBytes} underruns={playbackUnderrunCount} overruns={playbackOverrunCount} droppedBytes={playbackDroppedBytes} discontinuities={discontinuityCount}";
    }

    private static string DescribeStartStream(ProtocolFrame frame)
    {
        if (frame.Payload.Length != 8)
        {
            return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} payload={Convert.ToHexString(frame.Payload)}";
        }

        var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(0, 4));
        var channels = frame.Payload[4];
        var bitsPerSample = frame.Payload[5];
        var source = frame.Payload[6];
        return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} sampleRate={sampleRate}Hz channels={channels} bitsPerSample={bitsPerSample} source={GetAudioSourceName(source)}({source})";
    }

    private static string DescribeI2cReadRegister(ProtocolFrame frame)
    {
        if (frame.Payload.Length < 8)
        {
            return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} payload={Convert.ToHexString(frame.Payload)}";
        }

        var driverStatus = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(0, 4));
        var readLength = frame.Payload[4];
        var dataLength = Math.Min(readLength, Math.Max(0, frame.Payload.Length - 8));
        var data = Convert.ToHexString(frame.Payload.AsSpan(8, dataLength));

        return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} driverStatus=0x{driverStatus:X8} readLength={readLength} data={data}";
    }

    private static string DescribeI2cWriteRegister(ProtocolFrame frame)
    {
        if (frame.Payload.Length < 4)
        {
            return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} payload={Convert.ToHexString(frame.Payload)}";
        }

        var driverStatus = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(0, 4));
        return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} driverStatus=0x{driverStatus:X8}";
    }

    private static string DescribeStopStream(ProtocolFrame frame)
    {
        if (frame.Payload.Length != 4)
        {
            return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} payload={Convert.ToHexString(frame.Payload)}";
        }

        var stopReason = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(0, 4));
        return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} stopReason={stopReason}";
    }

    private static string DescribeSetGeneratorConfig(ProtocolFrame frame)
    {
        if (frame.Payload.Length != 22)
        {
            return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} payload={Convert.ToHexString(frame.Payload)}";
        }

        var primaryFrequencyHz = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(0, 4));
        var secondaryFrequencyHz = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(4, 4));
        var modulationPeriodMs = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(8, 4));
        var amplitude = BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload.AsSpan(12, 2));
        var source = frame.Payload[14];
        var noiseType = frame.Payload[15];
        var amplitudeEnvelope = frame.Payload[16];
        var noiseSeed = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(18, 4));

        return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} source={GetAudioSourceName(source)}({source}) primaryFrequencyHz={primaryFrequencyHz} secondaryFrequencyHz={secondaryFrequencyHz} modulationPeriodMs={modulationPeriodMs} amplitude={amplitude} noiseType={GetNoiseTypeName(noiseType)}({noiseType}) envelope={GetAmplitudeEnvelopeName(amplitudeEnvelope)}({amplitudeEnvelope}) noiseSeed=0x{noiseSeed:X8}";
    }

    private static string DescribeAudioPacket(byte[] packet)
    {
        var audioPacket = ParseAudioPacket(packet);
        return $"audio magic=0x{audioPacket.Magic:X4} version={audioPacket.Version} headerSize={audioPacket.HeaderSize} flags=0x{audioPacket.Flags:X4} seq={audioPacket.SequenceNumber} ts={audioPacket.Timestamp} payloadBytes={audioPacket.PayloadBytes} channels={audioPacket.ChannelCount} bitsPerSample={audioPacket.BitsPerSample} sampleRate={audioPacket.SampleRateHz}Hz";
    }

    private static AudioPacketInfo ParseAudioPacket(byte[] packet)
    {
        if (packet.Length < AudioHeaderSize)
        {
            throw new InvalidOperationException($"Audio packet too short: {packet.Length} bytes.");
        }

        var magic = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(0, 2));
        var version = packet[2];
        var headerSize = packet[3];
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(4, 2));
        var sequenceNumber = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(8, 4));
        var timestamp = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(12, 4));
        var payloadBytes = BinaryPrimitives.ReadUInt16LittleEndian(packet.AsSpan(16, 2));
        var channelCount = packet[18];
        var bitsPerSample = packet[19];
        var sampleRateHz = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(20, 4));

        if (packet.Length != (AudioHeaderSize + payloadBytes))
        {
            throw new InvalidOperationException($"Audio packet length mismatch: header says {payloadBytes} payload bytes, total is {packet.Length}.");
        }

        return new AudioPacketInfo(magic,
                                   version,
                                   headerSize,
                                   flags,
                                   sequenceNumber,
                                   timestamp,
                                   payloadBytes,
                                   channelCount,
                                   bitsPerSample,
                                   sampleRateHz,
                                   packet.AsSpan(AudioHeaderSize, payloadBytes).ToArray());
    }

    private static void ValidateResponseStatus(ProtocolFrame responseFrame, byte expectedStatus, string operationName)
    {
        if (responseFrame.Status != expectedStatus)
        {
            throw new InvalidOperationException($"Unexpected status for {operationName}: expected {GetStatusName(expectedStatus)}({expectedStatus}), got {GetStatusName(responseFrame.Status)}({responseFrame.Status}).");
        }
    }

    private static void ValidateEchoedAudioPacket(AudioPacketInfo echoedPacket,
                                                  uint expectedSequenceNumber,
                                                  ushort sentFlags,
                                                  bool expectDiscontinuity,
                                                  byte[] expectedPayload)
    {
        if (echoedPacket.Magic != AudioHeaderMagic)
        {
            throw new InvalidOperationException($"Unexpected audio magic 0x{echoedPacket.Magic:X4}.");
        }

        if (echoedPacket.Version != AudioHeaderVersion)
        {
            throw new InvalidOperationException($"Unexpected audio header version {echoedPacket.Version}.");
        }

        if (echoedPacket.SequenceNumber != expectedSequenceNumber)
        {
            throw new InvalidOperationException($"Unexpected echoed sequence {echoedPacket.SequenceNumber}, expected {expectedSequenceNumber}.");
        }

        var discontinuityPresent = (echoedPacket.Flags & AudioFlagDiscontinuity) != 0;
        if (discontinuityPresent != expectDiscontinuity)
        {
            throw new InvalidOperationException($"Unexpected discontinuity flag state for sequence {expectedSequenceNumber}: expected {expectDiscontinuity}, actual {discontinuityPresent}.");
        }

        var expectedNonDiscontinuityFlags = (ushort)(sentFlags & ~AudioFlagDiscontinuity);
        var echoedNonDiscontinuityFlags = (ushort)(echoedPacket.Flags & ~AudioFlagDiscontinuity);
        if (echoedNonDiscontinuityFlags != expectedNonDiscontinuityFlags)
        {
            throw new InvalidOperationException($"Unexpected echoed flags 0x{echoedPacket.Flags:X4} for sequence {expectedSequenceNumber}; expected base flags 0x{expectedNonDiscontinuityFlags:X4}.");
        }

        if (!echoedPacket.Payload.SequenceEqual(expectedPayload))
        {
            throw new InvalidOperationException($"Echoed audio payload mismatch for sequence {expectedSequenceNumber}.");
        }
    }

    private static void ValidateGeneratedAudioPacket(AudioPacketInfo packet, uint expectedSequenceNumber, byte source)
    {
        var bytesPerSample = packet.BitsPerSample / 8;
        var bytesPerFrame = bytesPerSample * packet.ChannelCount;

        if (packet.Magic != AudioHeaderMagic)
        {
            throw new InvalidOperationException($"Unexpected generated audio magic 0x{packet.Magic:X4}.");
        }

        if (packet.Version != AudioHeaderVersion)
        {
            throw new InvalidOperationException($"Unexpected generated audio header version {packet.Version}.");
        }

        if (packet.SequenceNumber != expectedSequenceNumber)
        {
            throw new InvalidOperationException($"Unexpected generated sequence {packet.SequenceNumber}, expected {expectedSequenceNumber}.");
        }

        if ((packet.ChannelCount != GeneratedChannelCount) ||
            (packet.BitsPerSample != GeneratedContainerBitsPerSample) ||
            (packet.SampleRateHz != GeneratedSampleRateHz))
        {
            throw new InvalidOperationException($"Unexpected generated audio format: channels={packet.ChannelCount}, bitsPerSample={packet.BitsPerSample}, sampleRate={packet.SampleRateHz}.");
        }

        if ((bytesPerFrame == 0) || ((packet.PayloadBytes % bytesPerFrame) != 0))
        {
            throw new InvalidOperationException($"Generated payload is not frame-aligned: payloadBytes={packet.PayloadBytes}, bytesPerFrame={bytesPerFrame}.");
        }

        var expectedTimestamp = expectedSequenceNumber * (packet.PayloadBytes / bytesPerFrame);
        if (packet.Timestamp != expectedTimestamp)
        {
            throw new InvalidOperationException($"Unexpected generated timestamp {packet.Timestamp}, expected {expectedTimestamp}.");
        }

        var hasStartFlag = (packet.Flags & AudioFlagStartOfStream) != 0;
        if ((expectedSequenceNumber == 0U) != hasStartFlag)
        {
            throw new InvalidOperationException($"Unexpected START_OF_STREAM flag state for generated packet {expectedSequenceNumber}.");
        }

        if ((source == AudioSourceDeviceGeneratedNoise) && (packet.PayloadBytes == 0))
        {
            throw new InvalidOperationException("Generated noise packet had no payload.");
        }

        ValidateGeneratedPayload(packet, bytesPerSample, bytesPerFrame);
    }

    private static void ValidateGeneratedPayload(AudioPacketInfo packet, int bytesPerSample, int bytesPerFrame)
    {
        if (packet.Payload.Length != packet.PayloadBytes)
        {
            throw new InvalidOperationException($"Generated payload length mismatch: header says {packet.PayloadBytes}, actual {packet.Payload.Length}.");
        }

        for (var frameOffset = 0; frameOffset < packet.Payload.Length; frameOffset += bytesPerFrame)
        {
            int? firstChannelSample = null;

            for (var channelIndex = 0; channelIndex < packet.ChannelCount; channelIndex++)
            {
                var sampleOffset = frameOffset + (channelIndex * bytesPerSample);
                var sampleRaw = BinaryPrimitives.ReadInt32LittleEndian(packet.Payload.AsSpan(sampleOffset, bytesPerSample));
                var sample = UnpackMsbAligned24(sampleRaw);

                ValidateMsbAligned24BitSample(sampleRaw, sample);

                if (!firstChannelSample.HasValue)
                {
                    firstChannelSample = sample;
                }
                else if (sample != firstChannelSample.Value)
                {
                    throw new InvalidOperationException($"Generated stereo channels diverged at payload offset {frameOffset}: left={firstChannelSample.Value}, right={sample}.");
                }
            }
        }
    }

    private static void ValidateMsbAligned24BitSample(int sampleRaw, int sample)
    {
        var minSample = -(1 << (GeneratedValidBitsPerSample - 1));
        var maxSample = (1 << (GeneratedValidBitsPerSample - 1)) - 1;

        if ((sampleRaw & 0xFF) != 0)
        {
            throw new InvalidOperationException($"Generated sample 0x{sampleRaw:X8} is not MSB-aligned in its 32-bit slot.");
        }

        if ((sample < minSample) || (sample > maxSample))
        {
            throw new InvalidOperationException($"Generated sample {sample} is outside the signed 24-bit range.");
        }
    }

    private static string GetUsbSpeedName(uint usbSpeed)
    {
        return usbSpeed switch
        {
            0 => "Low",
            1 => "Full",
            2 => "High",
            _ => "UnknownSpeed",
        };
    }

    private static string GetOpcodeName(byte opcode)
    {
        return opcode switch
        {
            0x01 => "GetInfo/DeviceReady",
            0x02 => "SetLed/StreamStarted",
            0x03 => "GetUsbDebugState/StreamStopped",
            0x04 => "Ping/Fault",
            0x05 => "StartStream",
            0x06 => "StopStream",
            0x07 => "SetGeneratorConfig",
            0x08 => "I2cWriteRegister",
            0x09 => "I2cReadRegister",
            _ => "UnknownOpcode",
        };
    }

    private static string GetStatusName(byte status)
    {
        return status switch
        {
            0 => "Ok",
            1 => "InvalidMagic",
            2 => "InvalidVersion",
            3 => "InvalidType",
            4 => "InvalidLength",
            5 => "UnsupportedCommand",
            6 => "InvalidArgument",
            _ => "UnknownStatus",
        };
    }

    private static string GetLedActionName(byte action)
    {
        return action switch
        {
            0 => "Off",
            1 => "On",
            2 => "Toggle",
            _ => "UnknownAction",
        };
    }

    private static string GetAudioSourceName(byte source)
    {
        return source switch
        {
            AudioSourceHostRxLoopback => "HostRxLoopback",
            AudioSourceDeviceGeneratedSine => "DeviceGeneratedSine",
            AudioSourceDeviceGeneratedChirp => "DeviceGeneratedChirp",
            AudioSourceDeviceGeneratedNoise => "DeviceGeneratedNoise",
            _ => "UnknownSource",
        };
    }

    private static string GetNoiseTypeName(byte noiseType)
    {
        return noiseType switch
        {
            NoiseTypeWhite => "White",
            NoiseTypeSampleHold => "SampleHold",
            NoiseTypeBinary => "Binary",
            _ => "UnknownNoiseType",
        };
    }

    private static string GetAmplitudeEnvelopeName(byte amplitudeEnvelope)
    {
        return amplitudeEnvelope switch
        {
            AmplitudeEnvelopeConstant => "Constant",
            AmplitudeEnvelopeFadeIn => "FadeIn",
            AmplitudeEnvelopeFadeOut => "FadeOut",
            AmplitudeEnvelopeTriangle => "Triangle",
            _ => "UnknownEnvelope",
        };
    }

    private static string DescribeDeviceReady(ProtocolFrame frame)
    {
        if (frame.Payload.Length != 8)
        {
            return $"event DeviceReady payload={Convert.ToHexString(frame.Payload)}";
        }

        var capabilities = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(0, 4));
        var maxPacket = BinaryPrimitives.ReadUInt16LittleEndian(frame.Payload.AsSpan(4, 2));

        return $"event DeviceReady capabilities=0x{capabilities:X8} maxPacket={maxPacket}";
    }

    private static string DescribeStreamStarted(ProtocolFrame frame)
    {
        if (frame.Payload.Length != 8)
        {
            return $"event StreamStarted payload={Convert.ToHexString(frame.Payload)}";
        }

        var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(0, 4));
        var channels = frame.Payload[4];
        var bitsPerSample = frame.Payload[5];

        return $"event StreamStarted sampleRate={sampleRate}Hz channels={channels} bitsPerSample={bitsPerSample}";
    }

    private static string DescribeStreamStopped(ProtocolFrame frame)
    {
        if (frame.Payload.Length != 4)
        {
            return $"event StreamStopped payload={Convert.ToHexString(frame.Payload)}";
        }

        var reason = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload);
        return $"event StreamStopped reason={reason}";
    }

    private static string DescribeFault(ProtocolFrame frame)
    {
        if (frame.Payload.Length != 8)
        {
            return $"event Fault payload={Convert.ToHexString(frame.Payload)}";
        }

        var faultCode = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(0, 4));
        var detail = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(4, 4));

        if (faultCode == CodecEnableFaultCode)
        {
            var codecStep = (ushort)(detail >> 16);
            var codecStatus = (ushort)(detail & 0xFFFF);
            return $"event Fault faultCode={faultCode} detail={detail} codecEnableStep={GetCodecEnableStepName(codecStep)}({codecStep}) codecDriverStatus={GetCodecDriverStatusName(codecStatus)}({codecStatus})";
        }

        return $"event Fault faultCode={faultCode} detail={detail}";
    }

    private static string GetCodecEnableStepName(ushort step)
    {
        return step switch
        {
            0 => "Idle",
            1 => "Init",
            2 => "WriteDigPower",
            3 => "WriteAnaPower",
            4 => "WriteHpVolume",
            5 => "WriteDacVolume",
            6 => "WriteAdcDacCtrl",
            7 => "WriteAnaCtrl",
            8 => "Completed",
            _ => "UnknownCodecEnableStep",
        };
    }

    private static string GetCodecDriverStatusName(ushort driverStatus)
    {
        return driverStatus switch
        {
            0 => "Success",
            902 => "Nak",
            905 => "ArbitrationLost",
            _ => "UnknownDriverStatus",
        };
    }

    private static void SetTimeout(IntPtr winUsbHandle, byte pipeId, uint timeoutMs)
    {
        var timeoutBytes = BitConverter.GetBytes(timeoutMs);
        if (!WinUsb_SetPipePolicy(winUsbHandle, pipeId, PipeTransferTimeoutPolicy, (uint)timeoutBytes.Length, timeoutBytes))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"WinUsb_SetPipePolicy failed for 0x{pipeId:X2}");
        }
    }

    private static bool UsbEndpointDirectionIn(byte pipeId)
    {
        return (pipeId & 0x80) != 0;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
        uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData,
        IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Initialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Free(IntPtr interfaceHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_QueryInterfaceSettings(IntPtr interfaceHandle, byte alternateInterfaceNumber,
        out UsbInterfaceDescriptor usbAltInterfaceDescriptor);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_QueryPipe(IntPtr interfaceHandle, byte alternateInterfaceNumber, byte pipeIndex,
        out WinUsbPipeInformation pipeInformation);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_SetPipePolicy(IntPtr interfaceHandle, byte pipeId, byte policyType, uint valueLength,
        byte[] value);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_WritePipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer, int bufferLength,
        out int lengthTransferred, IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_ReadPipe(IntPtr interfaceHandle, byte pipeId, byte[] buffer, int bufferLength,
        out int lengthTransferred, IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_RegisterIsochBuffer(IntPtr interfaceHandle, byte pipeId,
        IntPtr buffer, uint bufferLength, out IntPtr isochBufferHandle);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_ReadIsochPipeAsap(IntPtr isochBufferHandle, uint offset, uint length,
        [MarshalAs(UnmanagedType.Bool)] bool continueStream, uint numberOfPackets,
        IntPtr isoPacketDescriptors, IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_UnregisterIsochBuffer(IntPtr isochBufferHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpSecAttr, bool bManualReset, bool bInitialState, IntPtr lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ResetEvent(IntPtr hEvent);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UsbInterfaceDescriptor
    {
        public byte bLength;
        public byte bDescriptorType;
        public byte bInterfaceNumber;
        public byte bAlternateSetting;
        public byte bNumEndpoints;
        public byte bInterfaceClass;
        public byte bInterfaceSubClass;
        public byte bInterfaceProtocol;
        public byte iInterface;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinUsbPipeInformation
    {
        public UsbdPipeType PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }

    private enum UsbdPipeType
    {
        Control,
        Isochronous,
        Bulk,
        Interrupt,
    }

    private enum FrameTypeCode : byte
    {
        Command = 1,
        Response = 2,
        Event = 3,
    }

    private readonly record struct ProtocolFrame(FrameTypeCode Type, byte Opcode, byte Status, ushort Sequence, byte[] Payload);

    private readonly record struct AudioPacketInfo(ushort Magic,
                                                   byte Version,
                                                   byte HeaderSize,
                                                   ushort Flags,
                                                   uint SequenceNumber,
                                                   uint Timestamp,
                                                   ushort PayloadBytes,
                                                   byte ChannelCount,
                                                   byte BitsPerSample,
                                                   uint SampleRateHz,
                                                   byte[] Payload);
}