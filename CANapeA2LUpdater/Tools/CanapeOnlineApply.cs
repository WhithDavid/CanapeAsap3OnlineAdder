using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class CanapeOnlineApply
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

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3Init5(
        out IntPtr handle,
        uint responseTimeout,
        string project,
        uint fifoSize,
        uint sampleSize,
        [MarshalAs(UnmanagedType.I1)] bool debugMode,
        [MarshalAs(UnmanagedType.I1)] bool clearDeviceList,
        [MarshalAs(UnmanagedType.I1)] bool hexMode,
        [MarshalAs(UnmanagedType.I1)] bool modalMode);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3Exit2(
        IntPtr handle,
        [MarshalAs(UnmanagedType.I1)] bool closeCanape);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall)]
    private static extern ushort Asap3GetLastError(IntPtr handle);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3GetMeasurementState(IntPtr handle, out int state);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3GetModuleHandle(
        IntPtr handle,
        string moduleName,
        out ushort moduleHandle);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3GetDatabaseInfo(
        IntPtr handle,
        ushort moduleHandle,
        out DatabaseFileInfo info);

    [DllImport(RuntimeDiscovery.CanapeApiDllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Asap3ExecuteScript(
        IntPtr handle,
        ushort module,
        [MarshalAs(UnmanagedType.I1)] bool scriptFile,
        string script);

    public static int Main(string[] args)
    {
        bool restoreMode = args.Length == 4
            && string.Equals(args[2], "--restore", StringComparison.OrdinalIgnoreCase);
        if (args.Length != 3 && !restoreMode)
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  CanapeOnlineApply.exe <project.cna> <module name> <validated candidate.a2l>");
            Console.Error.WriteLine("  CanapeOnlineApply.exe <project.cna> <module name> --restore <backup.a2l>");
            return 2;
        }

        try
        {
            string apiPath = RuntimeDiscovery.PrepareCanapeApi(
                "Asap3Init5",
                "Asap3Exit2",
                "Asap3GetLastError",
                "Asap3GetMeasurementState",
                "Asap3GetModuleHandle",
                "Asap3GetDatabaseInfo",
                "Asap3ExecuteScript");
            RuntimeDiscovery.WriteCanapeDetection(apiPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("CANape API discovery failed: " + ex.Message);
            return 5;
        }

        string project = Path.GetFullPath(args[0]);
        string moduleName = args[1];
        string candidate = Path.GetFullPath(restoreMode ? args[3] : args[2]);
        if (!File.Exists(project) || !File.Exists(candidate))
        {
            Console.Error.WriteLine("Project or candidate A2L does not exist.");
            return 2;
        }

        using (Mutex sessionLock = AcquireSessionLock())
        {
            IntPtr handle;
            if (!Asap3Init5(out handle, 20000, project, 256, 256, true, false, false, true))
            {
                Console.Error.WriteLine("ASAP3 connection failed, error=" + Asap3GetLastError(IntPtr.Zero));
                return 1;
            }

            try
            {
                int state;
                Require(Asap3GetMeasurementState(handle, out state), handle, "read measurement state");
                if (state != 0)
                {
                    throw new InvalidOperationException(
                        "CANape measurement must be stopped. Current state=" + state);
                }

                ushort moduleHandle;
                Require(Asap3GetModuleHandle(handle, moduleName, out moduleHandle), handle, "find module " + moduleName);
                DatabaseFileInfo before;
                Require(Asap3GetDatabaseInfo(handle, moduleHandle, out before), handle, "read active database");
                string active = Path.GetFullPath(Path.Combine(before.Directory, before.FileName));
                if (!File.Exists(active)) throw new FileNotFoundException("Active A2L does not exist.", active);
                if (!string.Equals(Path.GetExtension(active), ".a2l", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Active database is not an A2L file: " + active);

                if (restoreMode)
                {
                    File.Copy(candidate, active, true);
                    ReloadDatabase(handle, moduleName, active);
                    Console.WriteLine("Rollback succeeded.");
                    Console.WriteLine("ActiveA2L=" + active);
                    return 0;
                }

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                string backup = active + ".backup-" + stamp;
                string staged = active + ".staged-" + Guid.NewGuid().ToString("N");
                File.Copy(candidate, staged, true);
                bool replaced = false;
                try
                {
                    File.Replace(staged, active, backup, true);
                    replaced = true;
                    ReloadDatabase(handle, moduleName, active);
                    DatabaseFileInfo after;
                    Require(Asap3GetDatabaseInfo(handle, moduleHandle, out after), handle, "verify active database");
                    string activeAfter = Path.GetFullPath(Path.Combine(after.Directory, after.FileName));
                    if (!string.Equals(active, activeAfter, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Database path changed unexpectedly: " + activeAfter);
                    Console.WriteLine("Online update succeeded.");
                    Console.WriteLine("ActiveA2L=" + active);
                    Console.WriteLine("BackupA2L=" + backup);
                    return 0;
                }
                catch
                {
                    if (replaced && File.Exists(backup))
                    {
                        File.Copy(backup, active, true);
                        ReloadDatabase(handle, moduleName, active);
                        Console.Error.WriteLine("Update failed; original A2L restored from " + backup);
                    }
                    throw;
                }
                finally
                {
                    if (File.Exists(staged)) File.Delete(staged);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
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

    private static void ReloadDatabase(IntPtr handle, string moduleName, string activeA2l)
    {
        string scriptPath = Path.Combine(
            Path.GetTempPath(),
            "CanapeOnlineApply_" + Guid.NewGuid().ToString("N") + ".cns");
        try
        {
            string escapedPath = activeA2l.Replace("\\", "\\\\").Replace("\"", "\\\"");
            File.WriteAllText(
                scriptPath,
                moduleName + ".ReplaceDatabase(\"" + escapedPath + "\");\r\n",
                Encoding.ASCII);
            Require(
                Asap3ExecuteScript(handle, ushort.MaxValue, true, scriptPath),
                handle,
                "reload current database");
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    private static void Require(bool succeeded, IntPtr handle, string operation)
    {
        if (!succeeded)
        {
            throw new InvalidOperationException(
                "Failed to " + operation + ", ASAP3 error=" + Asap3GetLastError(handle));
        }
    }
}
