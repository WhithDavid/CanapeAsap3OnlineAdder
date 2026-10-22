using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

internal static class RuntimeDiscovery
{
    public const string CanapeApiDllName = "CANapAPI64.dll";
    private const string Asap2ProbeDll = "Vector.MCD.ASAP2.DataModel.dll";
    private static readonly string[] Asap2RequiredFiles =
    {
        "Vector.MCD.ASAP2.DataModel.dll",
        "Vector.MCD.ASAP2.Reader.dll",
        "Vector.MCD.ASAP2.Writer.dll",
        "Vector.MCD.ASAP2.Updater.dll",
        "Vector.MCD.LinkMap.Contract.dll",
        "Vector.MCD.LinkMap.DataModel.dll",
        "Vector.MCD.LinkMap.SymbolsProvider.dll"
    };
    private static IntPtr _canapeApiHandle;
    private static string _preparedCanapeApiPath;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string pathName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string fileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

    public static string PrepareCanapeApi(params string[] requiredExports)
    {
        if (!Environment.Is64BitProcess)
        {
            throw new PlatformNotSupportedException(
                "This build requires a 64-bit process because it uses CANapAPI64.dll.");
        }

        if (_canapeApiHandle != IntPtr.Zero)
        {
            ValidateExports(_canapeApiHandle, requiredExports);
            return _preparedCanapeApiPath;
        }

        string apiPath = FindCanapeApiDll();
        if (string.IsNullOrWhiteSpace(apiPath))
        {
            throw new FileNotFoundException(
                "CANapAPI64.dll was not found. Start CANape first, or set CANAPE_API_DLL to its full path.");
        }

        string directory = Path.GetDirectoryName(apiPath);
        if (!SetDllDirectory(directory))
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Failed to select the CANape API directory: " + directory);
        }

        _canapeApiHandle = LoadLibrary(apiPath);
        if (_canapeApiHandle == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Failed to load CANape API: " + apiPath);
        }

        _preparedCanapeApiPath = apiPath;
        ValidateExports(_canapeApiHandle, requiredExports);
        return apiPath;
    }

    public static string FindCanapeApiDll()
    {
        string configured = ExistingFile(Environment.GetEnvironmentVariable("CANAPE_API_DLL"));
        if (configured != null) return configured;
        string bundled = ExistingFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CanapeApiDllName));
        if (bundled != null) return bundled;

        List<string> candidates = new List<string>();

        string processPath = FindRunningCanapePath();
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            string execDirectory = Path.GetDirectoryName(processPath);
            string installDirectory = Directory.GetParent(execDirectory) == null
                ? null
                : Directory.GetParent(execDirectory).FullName;
            AddFileCandidate(candidates, Path.Combine(execDirectory, CanapeApiDllName));
            if (!string.IsNullOrWhiteSpace(installDirectory))
            {
                AddFileCandidate(candidates, Path.Combine(installDirectory, "CANapeAPI", CanapeApiDllName));
                AddFileCandidate(candidates, Path.Combine(installDirectory, CanapeApiDllName));
            }
            string matchingRunningInstallation = SelectNewestExistingFile(candidates);
            if (matchingRunningInstallation != null) return matchingRunningInstallation;
        }

        foreach (string installDirectory in FindInstalledProductDirectories("CANape"))
        {
            AddFileCandidate(candidates, Path.Combine(installDirectory, "CANapeAPI", CanapeApiDllName));
            AddFileCandidate(candidates, Path.Combine(installDirectory, CanapeApiDllName));
        }

        AddFileCandidate(candidates, @"D:\CANape\CANapeAPI\CANapAPI64.dll");
        AddCanapeProgramFilesCandidates(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddCanapeProgramFilesCandidates(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        return SelectNewestExistingFile(candidates);
    }

    public static string FindAsap2StudioBin()
    {
        string configured = ExistingDirectory(
            Environment.GetEnvironmentVariable("ASAP2_STUDIO_BIN"),
            Asap2RequiredFiles);
        if (configured != null) return configured;
        string bundled = ExistingDirectory(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ASAP2Studio", "Bin"),
            Asap2RequiredFiles);
        if (bundled != null) return bundled;

        List<string> candidates = new List<string>();

        foreach (string installDirectory in FindInstalledProductDirectories("ASAP2 Studio"))
        {
            AddDirectoryCandidate(candidates, installDirectory);
            AddDirectoryCandidate(candidates, Path.Combine(installDirectory, "Bin"));
            AddAsap2VersionDirectories(candidates, installDirectory);
        }

        AddAsap2ProgramFilesCandidates(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        AddAsap2ProgramFilesCandidates(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        AddDirectoryCandidate(candidates, @"C:\Program Files\Vector ASAP2 Studio\ASAP2StudioCANape 1.6\Bin");
        return SelectNewestExistingDirectory(candidates, Asap2RequiredFiles);
    }

    public static string FindRunningCanapePath()
    {
        foreach (string processName in new[] { "CANape64", "CANape" })
        {
            Process[] processes = new Process[0];
            try
            {
                processes = Process.GetProcessesByName(processName);
                foreach (Process process in processes)
                {
                    try
                    {
                        string path = process.MainModule.FileName;
                        if (File.Exists(path)) return Path.GetFullPath(path);
                    }
                    catch
                    {
                        // A protected process can hide MainModule; continue with installation discovery.
                    }
                }
            }
            finally
            {
                foreach (Process process in processes) process.Dispose();
            }
        }
        return null;
    }

    public static string GetFileVersion(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "未检测到";
        try
        {
            FileVersionInfo information = FileVersionInfo.GetVersionInfo(path);
            if (information.FileMajorPart != 0
                || information.FileMinorPart != 0
                || information.FileBuildPart != 0
                || information.FilePrivatePart != 0)
            {
                return information.FileMajorPart + "."
                    + information.FileMinorPart + "."
                    + information.FileBuildPart + "."
                    + information.FilePrivatePart;
            }
            return string.IsNullOrWhiteSpace(information.FileVersion) ? "未知" : information.FileVersion;
        }
        catch
        {
            return "未知";
        }
    }

    public static string GetEnvironmentSummary()
    {
        string canapePath = FindRunningCanapePath();
        string apiPath = FindCanapeApiDll();
        if (string.IsNullOrWhiteSpace(canapePath)) canapePath = FindCanapeNearApi(apiPath);
        string asap2Bin = FindAsap2StudioBin();
        string canapeVersion = GetFileVersion(canapePath);
        string apiVersion = GetFileVersion(apiPath);
        string asap2Version = GetFileVersion(
            string.IsNullOrWhiteSpace(asap2Bin) ? null : Path.Combine(asap2Bin, Asap2ProbeDll));
        return "环境：CANape " + canapeVersion
            + " ｜ API " + apiVersion
            + " ｜ ASAP2 Studio " + GetAsap2DisplayVersion(asap2Bin, asap2Version);
    }

    public static void WriteCanapeDetection(string apiPath)
    {
        string canapePath = FindRunningCanapePath();
        if (string.IsNullOrWhiteSpace(canapePath)) canapePath = FindCanapeNearApi(apiPath);
        Console.WriteLine("DetectedCanapePath=" + (canapePath ?? string.Empty));
        Console.WriteLine("DetectedCanapeVersion=" + GetFileVersion(canapePath));
        Console.WriteLine("DetectedCanapeApi=" + apiPath);
        Console.WriteLine("DetectedCanapeApiVersion=" + GetFileVersion(apiPath));
    }

    private static string FindCanapeNearApi(string apiPath)
    {
        if (string.IsNullOrWhiteSpace(apiPath)) return null;
        DirectoryInfo apiDirectory = Directory.GetParent(Path.GetDirectoryName(apiPath));
        if (apiDirectory == null) return null;
        foreach (string candidate in new[]
        {
            Path.Combine(apiDirectory.FullName, "Exec64", "CANape64.exe"),
            Path.Combine(apiDirectory.FullName, "Exec", "CANape.exe")
        })
        {
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

    private static string GetAsap2DisplayVersion(string asap2Bin, string assemblyVersion)
    {
        if (string.IsNullOrWhiteSpace(asap2Bin)) return assemblyVersion;
        DirectoryInfo bin = new DirectoryInfo(asap2Bin);
        string productDirectory = bin.Parent == null ? string.Empty : bin.Parent.Name;
        const string prefix = "ASAP2StudioCANape";
        if (productDirectory.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            string productVersion = productDirectory.Substring(prefix.Length).Trim();
            if (productVersion.Length > 0) return productVersion + " (API " + assemblyVersion + ")";
        }
        return assemblyVersion;
    }

    private static void ValidateExports(IntPtr module, IEnumerable<string> requiredExports)
    {
        if (requiredExports == null) return;
        List<string> missing = requiredExports
            .Where(name => !string.IsNullOrWhiteSpace(name) && GetProcAddress(module, name) == IntPtr.Zero)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (missing.Count > 0)
        {
            throw new EntryPointNotFoundException(
                "The detected CANape API is incompatible. Missing exports: " + string.Join(", ", missing));
        }
    }

    private static IEnumerable<string> FindInstalledProductDirectories(string productNamePart)
    {
        List<string> results = new List<string>();
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using (RegistryKey localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (RegistryKey uninstall = localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uninstall == null) continue;
                    foreach (string subKeyName in uninstall.GetSubKeyNames())
                    {
                        using (RegistryKey product = uninstall.OpenSubKey(subKeyName))
                        {
                            if (product == null) continue;
                            string displayName = product.GetValue("DisplayName") as string;
                            if (string.IsNullOrWhiteSpace(displayName)
                                || displayName.IndexOf(productNamePart, StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue;
                            }
                            string installLocation = product.GetValue("InstallLocation") as string;
                            if (!string.IsNullOrWhiteSpace(installLocation)) results.Add(installLocation);
                        }
                    }
                }
            }
            catch
            {
                // Registry access is optional; other discovery sources remain available.
            }
        }
        return results;
    }

    private static void AddCanapeProgramFilesCandidates(List<string> candidates, string programFiles)
    {
        if (string.IsNullOrWhiteSpace(programFiles)) return;
        foreach (string root in new[] { programFiles, Path.Combine(programFiles, "Vector") })
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (string directory in Directory.GetDirectories(root, "*CANape*", SearchOption.TopDirectoryOnly))
                {
                    AddFileCandidate(candidates, Path.Combine(directory, "CANapeAPI", CanapeApiDllName));
                    AddFileCandidate(candidates, Path.Combine(directory, CanapeApiDllName));
                }
            }
            catch { }
        }
    }

    private static void AddAsap2ProgramFilesCandidates(List<string> candidates, string programFiles)
    {
        if (string.IsNullOrWhiteSpace(programFiles)) return;
        foreach (string root in new[]
        {
            Path.Combine(programFiles, "Vector ASAP2 Studio"),
            Path.Combine(programFiles, "Vector", "ASAP2 Studio")
        })
        {
            AddAsap2VersionDirectories(candidates, root);
        }
    }

    private static void AddAsap2VersionDirectories(List<string> candidates, string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;
        AddDirectoryCandidate(candidates, Path.Combine(root, "Bin"));
        try
        {
            foreach (string directory in Directory.GetDirectories(root, "ASAP2StudioCANape*", SearchOption.TopDirectoryOnly))
            {
                AddDirectoryCandidate(candidates, Path.Combine(directory, "Bin"));
            }
        }
        catch { }
    }

    private static void AddFileCandidate(List<string> candidates, string path)
    {
        if (!string.IsNullOrWhiteSpace(path)) candidates.Add(Environment.ExpandEnvironmentVariables(path.Trim()));
    }

    private static void AddDirectoryCandidate(List<string> candidates, string path)
    {
        if (!string.IsNullOrWhiteSpace(path)) candidates.Add(Environment.ExpandEnvironmentVariables(path.Trim()));
    }

    private static string ExistingFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        return File.Exists(expanded) ? Path.GetFullPath(expanded) : null;
    }

    private static string ExistingDirectory(string path, IEnumerable<string> requiredFiles)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string expanded = Environment.ExpandEnvironmentVariables(path.Trim());
        return Directory.Exists(expanded) && RequiredFilesExist(expanded, requiredFiles)
            ? Path.GetFullPath(expanded)
            : null;
    }

    private static string SelectNewestExistingFile(IEnumerable<string> candidates)
    {
        return candidates
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(GetFileVersionKey)
            .FirstOrDefault();
    }

    private static string SelectNewestExistingDirectory(
        IEnumerable<string> candidates,
        IEnumerable<string> requiredFiles)
    {
        return candidates
            .Where(path => Directory.Exists(path) && RequiredFilesExist(path, requiredFiles))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => GetFileVersionKey(Path.Combine(path, Asap2ProbeDll)))
            .FirstOrDefault();
    }

    private static bool RequiredFilesExist(string directory, IEnumerable<string> requiredFiles)
    {
        return requiredFiles.All(file => File.Exists(Path.Combine(directory, file)));
    }

    private static Version GetFileVersionKey(string path)
    {
        try
        {
            FileVersionInfo information = FileVersionInfo.GetVersionInfo(path);
            Version version;
            if (Version.TryParse(information.FileVersion, out version)) return version;
            return new Version(
                Math.Max(0, information.FileMajorPart),
                Math.Max(0, information.FileMinorPart),
                Math.Max(0, information.FileBuildPart),
                Math.Max(0, information.FilePrivatePart));
        }
        catch
        {
            return new Version(0, 0);
        }
    }
}
