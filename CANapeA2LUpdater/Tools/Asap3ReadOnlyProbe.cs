using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class Asap3ReadOnlyProbe
{
    private const string Asap3SessionMutexName = @"Local\QTAutoAdder.Asap3Session";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct DatabaseFileInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FileName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Directory;

        public byte Type;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct DatabaseObjectInfo
    {
        public int ObjectType;
        public int ValueType;
        public double Minimum;
        public double Maximum;
        public double ExtendedMinimum;
        public double ExtendedMaximum;
        public byte Precision;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string Unit;
    }

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3Init5(
        out IntPtr handle,
        uint responseTimeout,
        string workingDirectory,
        uint fifoSize,
        uint sampleSize,
        [MarshalAs(UnmanagedType.I1)] bool debugMode,
        [MarshalAs(UnmanagedType.I1)] bool clearDeviceList,
        [MarshalAs(UnmanagedType.I1)] bool hexMode,
        [MarshalAs(UnmanagedType.I1)] bool modalMode);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3GetProjectDirectory(
        IntPtr handle,
        StringBuilder directory,
        ref uint size);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall)]
    private static extern ushort Asap3GetLastError(IntPtr handle);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3Exit2(
        IntPtr handle,
        [MarshalAs(UnmanagedType.I1)] bool closeCanape);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3GetModuleHandle(
        IntPtr handle,
        string moduleName,
        out ushort moduleHandle);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3GetAsap2(
        IntPtr handle,
        ushort moduleHandle,
        out IntPtr fileName);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3GetDatabaseInfo(
        IntPtr handle,
        ushort moduleHandle,
        out DatabaseFileInfo info);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3GetMeasurementState(IntPtr handle, out int state);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3GetDBObjectInfo(
        IntPtr handle,
        ushort moduleHandle,
        string objectName,
        out DatabaseObjectInfo info);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3ExecuteScript(
        IntPtr handle,
        ushort module,
        [MarshalAs(UnmanagedType.I1)] bool scriptFile,
        string script);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3ExecuteScriptEx(
        IntPtr handle,
        ushort module,
        [MarshalAs(UnmanagedType.I1)] bool scriptFile,
        string script,
        out UIntPtr scriptHandle);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3GetScriptState(
        IntPtr handle,
        UIntPtr scriptHandle,
        out int scriptState,
        StringBuilder textBuffer,
        ref uint bufferSize);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3GetScriptResultString(
        IntPtr handle,
        UIntPtr scriptHandle,
        StringBuilder result,
        ref uint bufferSize);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3ReleaseScript(IntPtr handle, UIntPtr scriptHandle);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3StartScript(
        IntPtr handle,
        UIntPtr scriptHandle,
        string commandLine,
        ushort moduleHandle);

    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: Asap3ReadOnlyProbe.exe <project> [read-only script] [object names...]");
            return 2;
        }

        try
        {
            string apiPath = RuntimeDiscovery.PrepareCanapeApi(
                "Asap3Init5",
                "Asap3GetProjectDirectory",
                "Asap3GetLastError",
                "Asap3Exit2",
                "Asap3GetModuleHandle",
                "Asap3GetAsap2",
                "Asap3GetDatabaseInfo",
                "Asap3GetMeasurementState",
                "Asap3GetDBObjectInfo",
                "Asap3ExecuteScript",
                "Asap3ExecuteScriptEx",
                "Asap3GetScriptState",
                "Asap3GetScriptResultString",
                "Asap3ReleaseScript",
                "Asap3StartScript");
            RuntimeDiscovery.WriteCanapeDetection(apiPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("CANape API discovery failed: " + ex.Message);
            return 5;
        }

        using (Mutex sessionLock = AcquireSessionLock())
        {
            IntPtr handle;
            bool initialized = Asap3Init5(
                out handle,
                20000,
                args[0],
                256,
                256,
                true,
                false,
                false,
                true);

            if (!initialized)
            {
                Console.Error.WriteLine("Asap3Init5 failed, error=" + Asap3GetLastError(IntPtr.Zero));
                return 1;
            }

            try
            {
                uint size = 0;
                Asap3GetProjectDirectory(handle, null, ref size);
                var directory = new StringBuilder((int)Math.Max(size, 2));
                if (!Asap3GetProjectDirectory(handle, directory, ref size))
                {
                    Console.Error.WriteLine("Asap3GetProjectDirectory failed, error=" + Asap3GetLastError(handle));
                    return 1;
                }

                Console.WriteLine("ConnectedProjectDirectory=" + directory);
                int measurementState;
                if (!Asap3GetMeasurementState(handle, out measurementState))
                {
                    Console.Error.WriteLine(
                        "Asap3GetMeasurementState failed, error=" + Asap3GetLastError(handle));
                    return 1;
                }
                Console.WriteLine("MeasurementState=" + measurementState);
                string moduleName = "XCPsim";
                if (args.Length >= 3 && args[2].StartsWith("--module=", StringComparison.OrdinalIgnoreCase))
                {
                    moduleName = args[2].Substring("--module=".Length);
                }
                if (args.Length >= 2)
                {
                    try
                    {
                        RunReadOnlyScript(handle, args[1], moduleName);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("ScriptError=" + ex.Message);
                        return 1;
                    }
                }
                if (args.Length > 2)
                {
                    int objectStart = 2;
                    if (args[2].StartsWith("--module=", StringComparison.OrdinalIgnoreCase))
                    {
                        moduleName = args[2].Substring("--module=".Length);
                        objectStart = 3;
                    }
                    ushort moduleHandle;
                    if (!Asap3GetModuleHandle(handle, moduleName, out moduleHandle))
                    {
                        Console.Error.WriteLine(
                            "Asap3GetModuleHandle(" + moduleName + ") failed, error=" + Asap3GetLastError(handle));
                        return 1;
                    }
                    for (int index = objectStart; index < args.Length; index++)
                    {
                        string requested = args[index];
                        int expectedType = -1;
                        string objectName = requested;
                        if (requested.StartsWith("M:", StringComparison.Ordinal))
                        {
                            expectedType = 0;
                            objectName = requested.Substring(2);
                        }
                        else if (requested.StartsWith("C:", StringComparison.Ordinal))
                        {
                            expectedType = 1;
                            objectName = requested.Substring(2);
                        }
                        DatabaseObjectInfo objectInfo;
                        if (!Asap3GetDBObjectInfo(handle, moduleHandle, objectName, out objectInfo))
                        {
                            Console.Error.WriteLine(
                                "ObjectNotFound=" + objectName + ", error=" + Asap3GetLastError(handle));
                            return 1;
                        }
                        if (expectedType >= 0 && objectInfo.ObjectType != expectedType)
                        {
                            Console.Error.WriteLine(
                                "ObjectTypeMismatch=" + objectName + ", expected=" + expectedType + ", actual=" + objectInfo.ObjectType);
                            return 1;
                        }
                        Console.WriteLine(
                            "Object=" + objectName +
                            ", ObjectType=" + objectInfo.ObjectType +
                            ", ValueType=" + objectInfo.ValueType);
                    }
                }
                return 0;
            }
            finally
            {
                // Disconnect only; explicitly keep the user's CANape process open.
                Asap3Exit2(handle, false);
            }
        }
    }

    private static Mutex AcquireSessionLock()
    {
        var mutex = new Mutex(false, Asap3SessionMutexName);
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(30)))
            {
                mutex.Dispose();
                throw new InvalidOperationException("Another ASAP3 operation is still running; please wait and retry.");
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous helper exited unexpectedly; the mutex is now owned by this process.
        }
        return mutex;
    }

    private static void RunReadOnlyScript(IntPtr handle, string scriptPath, string moduleName)
    {
        ushort moduleHandle;
        if (!Asap3GetModuleHandle(handle, moduleName, out moduleHandle))
        {
            throw new InvalidOperationException(
                "Asap3GetModuleHandle(" + moduleName + ") failed, error=" + Asap3GetLastError(handle));
        }

        Console.WriteLine("ModuleHandle." + moduleName + "=" + moduleHandle);
        IntPtr databaseFile;
        if (!Asap3GetAsap2(handle, moduleHandle, out databaseFile))
        {
            throw new InvalidOperationException(
                "Asap3GetAsap2 failed, error=" + Asap3GetLastError(handle));
        }
        Console.WriteLine("Database." + moduleName + "=" + Marshal.PtrToStringAnsi(databaseFile));
        DatabaseFileInfo databaseInfo;
        if (!Asap3GetDatabaseInfo(handle, moduleHandle, out databaseInfo))
        {
            throw new InvalidOperationException(
                "Asap3GetDatabaseInfo failed, error=" + Asap3GetLastError(handle));
        }
        Console.WriteLine("DatabaseDirectory." + moduleName + "=" + databaseInfo.Directory);
        Console.WriteLine("DatabaseFile." + moduleName + "=" + databaseInfo.FileName);
        Console.WriteLine("DatabaseType." + moduleName + "=" + databaseInfo.Type);

        if (!Asap3ExecuteScript(handle, ushort.MaxValue, true, scriptPath))
        {
            throw new InvalidOperationException(
                "Asap3ExecuteScript failed, error=" + Asap3GetLastError(handle));
        }
        Console.WriteLine("ScriptExecution=OK");
        return;

#pragma warning disable 0162
        UIntPtr scriptHandle;
        if (!Asap3ExecuteScriptEx(handle, ushort.MaxValue, true, scriptPath, out scriptHandle))
        {
            throw new InvalidOperationException(
                "Asap3ExecuteScriptEx failed, error=" + Asap3GetLastError(handle));
        }

        try
        {
            if (!Asap3StartScript(handle, scriptHandle, null, moduleHandle))
            {
                throw new InvalidOperationException(
                    "Asap3StartScript failed, error=" + Asap3GetLastError(handle));
            }

            int state = 0;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                uint textSize = 0;
                Asap3GetScriptState(handle, scriptHandle, out state, null, ref textSize);
                if (state >= 6)
                {
                    break;
                }
                Thread.Sleep(100);
            }

            if (state != 7 && state != 8)
            {
                uint textSize = 0;
                Asap3GetScriptState(handle, scriptHandle, out state, null, ref textSize);
                var stateText = new StringBuilder((int)Math.Max(textSize, 2));
                Asap3GetScriptState(handle, scriptHandle, out state, stateText, ref textSize);
                throw new InvalidOperationException(
                    "Script did not finish successfully. state=" + state + ", detail=" + stateText);
            }

            uint resultSize = 0;
            Asap3GetScriptResultString(handle, scriptHandle, null, ref resultSize);
            var result = new StringBuilder((int)Math.Max(resultSize, 2));
            if (!Asap3GetScriptResultString(handle, scriptHandle, result, ref resultSize))
            {
                throw new InvalidOperationException(
                    "Asap3GetScriptResultString failed, error=" + Asap3GetLastError(handle));
            }

            Console.WriteLine("ScriptResult=" + result);
        }
        finally
        {
            Asap3ReleaseScript(handle, scriptHandle);
        }
#pragma warning restore 0162
    }
}
