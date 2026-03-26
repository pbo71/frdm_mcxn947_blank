using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private const string DefaultRunMode = "full";
    private const string GeneratedSmokeRunMode = "generated-smoke";
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
            !string.Equals(runMode, GeneratedSmokeRunMode, StringComparison.OrdinalIgnoreCase))
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
            SetTimeout(winUsbHandle, pipeInfo.PipeId, DefaultTimeoutMs);
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

        var ledResponse = ExecuteCommand(winUsbHandle, maxPacket, 4, 0x02, new byte[] { 0x02 });
        Console.WriteLine($"SetLed response: {DescribeFrame(ledResponse)}");

        if (string.Equals(runMode, GeneratedSmokeRunMode, StringComparison.OrdinalIgnoreCase))
        {
            RunGeneratedSmokeDemo(winUsbHandle, endpoints, maxPacket);
            return;
        }

        if (endpoints.ContainsKey(AudioOutPipe) && endpoints.ContainsKey(AudioInPipe))
        {
            var startStreamResponse = ExecuteCommand(winUsbHandle, maxPacket, 5, 0x05, BuildStartStreamPayload(48000U, 2, 16, AudioSourceHostRxLoopback));
            ValidateResponseStatus(startStreamResponse, expectedStatus: 0, "StartStream");
            Console.WriteLine($"StartStream response: {DescribeFrame(startStreamResponse)}");

            var audioMaxPacket = endpoints[(byte)AudioInPipe].MaximumPacketSize;
            RunAudioLoopbackDemo(winUsbHandle, audioMaxPacket);

            var stopStreamResponse = ExecuteCommand(winUsbHandle, maxPacket, 6, 0x06, Array.Empty<byte>());
            ValidateResponseStatus(stopStreamResponse, expectedStatus: 0, "StopStream");
            Console.WriteLine($"StopStream response: {DescribeFrame(stopStreamResponse)}");
            DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Post-stop");

            var restartStreamResponse = ExecuteCommand(winUsbHandle, maxPacket, 7, 0x05, BuildStartStreamPayload(48000U, 2, 16, AudioSourceHostRxLoopback));
            ValidateResponseStatus(restartStreamResponse, expectedStatus: 0, "StartStream (EOS phase)");
            Console.WriteLine($"RestartStream response: {DescribeFrame(restartStreamResponse)}");

            RunEndOfStreamLoopbackDemo(winUsbHandle, audioMaxPacket);
            DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Post-EOS");

            var stopAfterEosResponse = ExecuteCommand(winUsbHandle, maxPacket, 8, 0x06, Array.Empty<byte>());
            ValidateResponseStatus(stopAfterEosResponse, expectedStatus: 6, "StopStream after EOS");
            Console.WriteLine($"StopStream after EOS response: {DescribeFrame(stopAfterEosResponse)}");

            var mismatchStartStreamResponse = ExecuteCommand(winUsbHandle, maxPacket, 9, 0x05, BuildStartStreamPayload(48000U, 2, 16, AudioSourceHostRxLoopback));
            ValidateResponseStatus(mismatchStartStreamResponse, expectedStatus: 0, "StartStream (format mismatch phase)");
            Console.WriteLine($"Mismatch phase StartStream response: {DescribeFrame(mismatchStartStreamResponse)}");

            RunFormatMismatchDropDemo(winUsbHandle, audioMaxPacket);

            var stopAfterMismatchResponse = ExecuteCommand(winUsbHandle, maxPacket, 10, 0x06, Array.Empty<byte>());
            ValidateResponseStatus(stopAfterMismatchResponse, expectedStatus: 0, "StopStream after format mismatch");
            Console.WriteLine($"StopStream after mismatch response: {DescribeFrame(stopAfterMismatchResponse)}");
            DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Post-mismatch stop");

            var setSineConfigResponse = ExecuteCommand(winUsbHandle,
                                                       maxPacket,
                                                       11,
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

            var generatedSineStartStreamResponse = ExecuteCommand(winUsbHandle, maxPacket, 12, 0x05, BuildStartStreamPayload(48000U, 2, 16, AudioSourceDeviceGeneratedSine));
            ValidateResponseStatus(generatedSineStartStreamResponse, expectedStatus: 0, "StartStream (generated sine)");
            Console.WriteLine($"Generated sine StartStream response: {DescribeFrame(generatedSineStartStreamResponse)}");

            RunGeneratedAudioDemo(winUsbHandle, audioMaxPacket, AudioSourceDeviceGeneratedSine, "Generated sine");

            var stopAfterGeneratedSineResponse = ExecuteCommand(winUsbHandle, maxPacket, 13, 0x06, Array.Empty<byte>());
            ValidateResponseStatus(stopAfterGeneratedSineResponse, expectedStatus: 0, "StopStream after generated sine");
            Console.WriteLine($"StopStream after generated sine response: {DescribeFrame(stopAfterGeneratedSineResponse)}");
            DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Post-generated sine stop");
            DrainAudioPackets(winUsbHandle, audioMaxPacket, "Post-generated sine audio drain");

            var setChirpConfigResponse = ExecuteCommand(winUsbHandle,
                                                        maxPacket,
                                                        14,
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

            var generatedChirpStartStreamResponse = ExecuteCommand(winUsbHandle, maxPacket, 15, 0x05, BuildStartStreamPayload(48000U, 2, 16, AudioSourceDeviceGeneratedChirp));
            ValidateResponseStatus(generatedChirpStartStreamResponse, expectedStatus: 0, "StartStream (generated chirp)");
            Console.WriteLine($"Generated chirp StartStream response: {DescribeFrame(generatedChirpStartStreamResponse)}");

            RunGeneratedAudioDemo(winUsbHandle, audioMaxPacket, AudioSourceDeviceGeneratedChirp, "Generated chirp");

            var stopAfterGeneratedChirpResponse = ExecuteCommand(winUsbHandle, maxPacket, 16, 0x06, Array.Empty<byte>());
            ValidateResponseStatus(stopAfterGeneratedChirpResponse, expectedStatus: 0, "StopStream after generated chirp");
            Console.WriteLine($"StopStream after generated chirp response: {DescribeFrame(stopAfterGeneratedChirpResponse)}");
            DrainProtocolEvents(winUsbHandle, maxPacket, DrainEventAttemptsAfterStop, "Post-generated chirp stop");
            DrainAudioPackets(winUsbHandle, audioMaxPacket, "Post-generated chirp audio drain");

            var setNoiseConfigResponse = ExecuteCommand(winUsbHandle,
                                                        maxPacket,
                                                        17,
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

            var generatedNoiseStartStreamResponse = ExecuteCommand(winUsbHandle, maxPacket, 18, 0x05, BuildStartStreamPayload(48000U, 2, 16, AudioSourceDeviceGeneratedNoise));
            ValidateResponseStatus(generatedNoiseStartStreamResponse, expectedStatus: 0, "StartStream (generated noise)");
            Console.WriteLine($"Generated noise StartStream response: {DescribeFrame(generatedNoiseStartStreamResponse)}");

            RunGeneratedAudioDemo(winUsbHandle, audioMaxPacket, AudioSourceDeviceGeneratedNoise, "Generated noise");

            var stopAfterGeneratedNoiseResponse = ExecuteCommand(winUsbHandle, maxPacket, 19, 0x06, Array.Empty<byte>());
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

        var startStreamResponse = ExecuteCommand(winUsbHandle, maxPacket, startSequence, 0x05, BuildStartStreamPayload(48000U, 2, 16, source));
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
            var payload = Enumerable.Range(packetIndex * 32, 32).Select(value => (byte)value).ToArray();
            var audioPacket = BuildAudioPacket(sequenceNumber: demoPacket.SequenceNumber,
                                               timestamp: demoPacket.Timestamp,
                                               sampleRateHz: 48000U,
                                               channelCount: 2,
                                               bitsPerSample: 16,
                                               flags: demoPacket.Flags,
                                               payload: payload);

            var echoedAudioPacket = ExchangeAudioPacket(winUsbHandle, audioMaxPacket, audioPacket);
            var echoedInfo = ParseAudioPacket(echoedAudioPacket);
            ValidateEchoedAudioPacket(echoedInfo, demoPacket.SequenceNumber, demoPacket.Flags, demoPacket.ExpectDiscontinuity);
            Console.WriteLine($"Audio echo {packetIndex + 1}/{demoPackets.Length}: {DescribeAudioPacket(echoedAudioPacket)}");
        }
    }

    private static void RunEndOfStreamLoopbackDemo(IntPtr winUsbHandle, ushort audioMaxPacket)
    {
        var payload = Enumerable.Range(96, 32).Select(value => (byte)value).ToArray();
        var eosPacket = BuildAudioPacket(sequenceNumber: 0U,
                                         timestamp: 0U,
                                         sampleRateHz: 48000U,
                                         channelCount: 2,
                                         bitsPerSample: 16,
                                         flags: (ushort)(AudioFlagStartOfStream | AudioFlagEndOfStream),
                                         payload: payload);

        var echoedAudioPacket = ExchangeAudioPacket(winUsbHandle, audioMaxPacket, eosPacket);
        var echoedInfo = ParseAudioPacket(echoedAudioPacket);
        ValidateEchoedAudioPacket(echoedInfo,
                                  expectedSequenceNumber: 0U,
                                  sentFlags: (ushort)(AudioFlagStartOfStream | AudioFlagEndOfStream),
                                  expectDiscontinuity: false);
        Console.WriteLine($"Audio EOS echo: {DescribeAudioPacket(echoedAudioPacket)}");
    }

    private static void RunFormatMismatchDropDemo(IntPtr winUsbHandle, ushort audioMaxPacket)
    {
        var payload = Enumerable.Range(128, 32).Select(value => (byte)value).ToArray();
        var mismatchedPacket = BuildAudioPacket(sequenceNumber: 0U,
                                                timestamp: 0U,
                                                sampleRateHz: 44100U,
                                                channelCount: 2,
                                                bitsPerSample: 16,
                                                flags: AudioFlagStartOfStream,
                                                payload: payload);

        WriteAudioPacket(winUsbHandle, mismatchedPacket);

        if (TryReadAudioPacket(winUsbHandle, audioMaxPacket, out var unexpectedEchoPacket))
        {
            throw new InvalidOperationException($"Unexpected audio echo for format mismatch test: {DescribeAudioPacket(unexpectedEchoPacket)}");
        }

        Console.WriteLine("Format mismatch test: no audio echo received before timeout, as expected.");
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
            Console.WriteLine($"{label} {packetIndex + 1}/3: {DescribeAudioPacket(audioPacket)}");
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
        if (frame.Payload.Length != 40)
        {
            return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} payload={Convert.ToHexString(frame.Payload)}";
        }

        var stage = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(0, 4));
        var lastEvent = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(4, 4));
        var lastStatus = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(8, 4));
        var usbSpeed = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(12, 4));
        var oversizedOutboundDropCount = BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.AsSpan(16, 4));

        return $"response {GetOpcodeName(frame.Opcode)} seq={frame.Sequence} status={GetStatusName(frame.Status)} stage=0x{stage:X8} lastEvent=0x{lastEvent:X8} lastStatus=0x{lastStatus:X8} usbSpeed={GetUsbSpeedName(usbSpeed)}({usbSpeed}) oversizedOutboundDrops={oversizedOutboundDropCount}";
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

        return new AudioPacketInfo(magic, version, headerSize, flags, sequenceNumber, timestamp, payloadBytes, channelCount, bitsPerSample, sampleRateHz);
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
                                                  bool expectDiscontinuity)
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
    }

    private static void ValidateGeneratedAudioPacket(AudioPacketInfo packet, uint expectedSequenceNumber, byte source)
    {
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

        if ((packet.ChannelCount != 2) || (packet.BitsPerSample != 16) || (packet.SampleRateHz != 48000U))
        {
            throw new InvalidOperationException($"Unexpected generated audio format: channels={packet.ChannelCount}, bitsPerSample={packet.BitsPerSample}, sampleRate={packet.SampleRateHz}.");
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
        return $"event Fault faultCode={faultCode} detail={detail}";
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
                                                   uint SampleRateHz);
}