using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class CanapeReadOnlyProbe
{
    private const string Api = @"C:\Path\To\CANape\CANapeAPI\CANapAPI64.dll";
    private const string Asap3SessionMutexName = @"Local\QTAutoAdder.Asap3Session";

    [DllImport(Api, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
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

    [DllImport(Api, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3Exit2(
        IntPtr handle,
        [MarshalAs(UnmanagedType.I1)] bool closeCanape);

    [DllImport(Api, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3GetMeasurementState(IntPtr handle, out int state);

    [DllImport(Api, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3GetCNAFilename(
        IntPtr handle,
        StringBuilder fileName,
        ref uint size);

    [DllImport(Api, CallingConvention = CallingConvention.Winapi, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3GetModuleHandle(
        IntPtr handle,
        string moduleName,
        out ushort module);

    [DllImport(Api, CallingConvention = CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3GetAsap2(
        IntPtr handle,
        ushort module,
        out IntPtr fileName);

    [DllImport(Api, CallingConvention = CallingConvention.Winapi)]
    private static extern int Asap3GetLastError(IntPtr handle);

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: CanapeReadOnlyProbe.exe <project-or-directory> <module-name>");
            return 2;
        }

        using (Mutex sessionLock = AcquireSessionLock())
        {
            IntPtr handle;
            if (!Asap3Init5(out handle, 20000, args[0], 8000, 320, true, false, false, false))
            {
                Console.Error.WriteLine("Asap3Init5 failed: " + Asap3GetLastError(IntPtr.Zero));
                return 3;
            }
            try
            {
                int state;
                if (!Asap3GetMeasurementState(handle, out state))
                    throw new InvalidOperationException("Asap3GetMeasurementState failed: " + Asap3GetLastError(handle));
                StringBuilder cna = new StringBuilder(1024);
                uint cnaSize = (uint)cna.Capacity;
                if (!Asap3GetCNAFilename(handle, cna, ref cnaSize))
                    throw new InvalidOperationException("Asap3GetCNAFilename failed: " + Asap3GetLastError(handle));
                ushort module;
                if (!Asap3GetModuleHandle(handle, args[1], out module))
                    throw new InvalidOperationException("Asap3GetModuleHandle failed: " + Asap3GetLastError(handle));
                IntPtr asap2Pointer;
                if (!Asap3GetAsap2(handle, module, out asap2Pointer))
                    throw new InvalidOperationException("Asap3GetAsap2 failed: " + Asap3GetLastError(handle));
                Console.WriteLine("MeasurementState=" + state);
                Console.WriteLine("CNA=" + cna);
                Console.WriteLine("Module=" + args[1] + " (" + module + ")");
                Console.WriteLine("A2L=" + Marshal.PtrToStringAnsi(asap2Pointer));
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error.Message);
                return 4;
            }
            finally
            {
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
}
