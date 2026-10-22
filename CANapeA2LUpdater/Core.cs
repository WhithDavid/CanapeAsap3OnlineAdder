using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Vector.MCD.ASAP2.DataModel.Interface;
using Vector.MCD.ASAP2.Reader.Interface;
using Vector.MCD.ASAP2.Updater.Interface;
using Vector.MCD.ASAP2.Writer.Interface;
using Vector.MCD.LinkMap.SymbolsProvider;

namespace CanapeA2LUpdater
{
    public static class EntryPoint
    {
        public static int Run(string[] args)
        {
            Options options;
            if (!Options.TryParse(args, out options))
            {
                Options.PrintUsage();
                return 2;
            }

            ValidateInput(options);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.OutputA2l)));

            ConsoleInformation information = new ConsoleInformation();
            IDatabase database = ReadDatabase(options.InputA2l, information);
            ValidateParsedObjectCounts(options.InputA2l, database);
            IModule module = SelectModule(database, options.ModuleName);
            ElfContext elf = ReadElf(options.ElfPath, options.VectorBin);
            IMapFileProvider mapProvider = CreateMapFileProvider(options.ElfPath, elf);
            IUpdater updater = UpdaterFactory.CreateUpdater(information, new UpdateCallback());

            updater.UseObjectNamesForUpdate = true;
            updater.UseObjectNamesForUpdateIfNoSymbolLinkAvailable = true;
            updater.UpdateDatatypes = true;
            updater.UpdateBaseDataTypes = PerformanceType.Always;
            updater.UpdateBitOffsets = PerformanceType.Always;
            updater.CreateLinkMapReferences = true;

            MethodInfo updateOne = updater.GetType().GetMethod(
                "UpdateVariable",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (updateOne == null)
            {
                throw new MissingMethodException(updater.GetType().FullName, "UpdateVariable");
            }

            List<string> signals = ReadNames(options.SignalsPath);
            List<string> calibrations = ReadNames(options.CalibrationsPath);
            List<string> duplicateNames = signals.Intersect(calibrations, StringComparer.Ordinal).ToList();
            if (duplicateNames.Count > 0)
            {
                throw new InvalidOperationException(
                    "The same object cannot be both measurement and calibration: " +
                    string.Join(", ", duplicateNames));
            }
            List<string> created = new List<string>();
            List<string> updated = new List<string>();
            List<string> createdCalibrations = new List<string>();
            List<string> updatedCalibrations = new List<string>();
            List<string> notFound = new List<string>();
            List<string> failed = new List<string>();

            foreach (string signal in signals)
            {
                IMapSymbolProvider symbol = mapProvider.FindMapSymbol(signal);
                if (symbol == null || !symbol.AddressValid)
                {
                    notFound.Add(signal);
                    continue;
                }

                IMeasurement measurement = module.FindMeasurement(signal);
                bool isNew = measurement == null;
                if (isNew)
                {
                    measurement = DatabaseFactory.CreateMeasurement(signal, string.Empty, 0, module);
                    measurement.SymbolName = signal;
                }

                bool ok = (bool)updateOne.Invoke(
                    updater,
                    new object[] { measurement, module, mapProvider, false, false });

                if (!ok)
                {
                    if (isNew)
                    {
                        measurement.DeleteFromDatabase(false, false);
                    }
                    failed.Add(signal);
                }
                else if (isNew)
                {
                    created.Add(signal);
                }
                else
                {
                    updated.Add(signal);
                }
            }

            foreach (string calibration in calibrations)
            {
                IMapSymbolProvider symbol = mapProvider.FindMapSymbol(calibration);
                if (symbol == null || !symbol.AddressValid)
                {
                    notFound.Add(calibration);
                    continue;
                }

                ICharacteristic characteristic = module.FindCharacteristic(calibration);
                bool isNew = characteristic == null;
                if (isNew)
                {
                    characteristic = DatabaseFactory.CreateParameter(calibration, string.Empty, 0, module);
                    characteristic.SymbolName = calibration;
                }

                bool ok = (bool)updateOne.Invoke(
                    updater,
                    new object[] { characteristic, module, mapProvider, false, false });

                if (!ok)
                {
                    if (isNew)
                    {
                        characteristic.DeleteFromDatabase(false, false);
                    }
                    failed.Add(calibration);
                }
                else if (isNew)
                {
                    createdCalibrations.Add(calibration);
                }
                else
                {
                    updatedCalibrations.Add(calibration);
                }
            }

            DatabaseCounts expectedCounts = DatabaseCounts.From(database);
            WriteDatabase(database, options.OutputA2l, information);
            VerifyOutput(options.OutputA2l, information, expectedCounts);
            PrintSummary(options, module.Name, created, updated, notFound, failed);
            PrintNames("Created calibration list", createdCalibrations);
            PrintNames("Updated calibration list", updatedCalibrations);
            PrintMachineResults("measurement", "created", created);
            PrintMachineResults("measurement", "updated", updated);
            PrintMachineResults("measurement", "not-found", notFound);
            PrintMachineResults("measurement", "failed", failed);
            PrintMachineResults("calibration", "created", createdCalibrations);
            PrintMachineResults("calibration", "updated", updatedCalibrations);
            return failed.Count == 0 && notFound.Count == 0 ? 0 : 3;
        }

        private static void ValidateInput(Options options)
        {
            RequireFile(options.InputA2l, "A2L");
            RequireFile(options.ElfPath, "ELF");
            if (!string.IsNullOrWhiteSpace(options.SignalsPath))
            {
                RequireFile(options.SignalsPath, "measurement list");
            }
            if (!string.IsNullOrWhiteSpace(options.CalibrationsPath))
            {
                RequireFile(options.CalibrationsPath, "calibration list");
            }
            RequireFile(Path.Combine(options.VectorBin, "Vector.MCD.ASAP2.DataModel.dll"), "ASAP2 Studio API");

            if (string.Equals(Path.GetFullPath(options.InputA2l), Path.GetFullPath(options.OutputA2l), StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("输出 A2L 不能与输入 A2L 相同。第一版不会覆盖原文件。");
            }
        }

        private static void RequireFile(string path, string description)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(description + " 文件不存在", path);
            }
        }

        private static IDatabase ReadDatabase(string path, IInformation information)
        {
            IDatabase database = DatabaseFactory.CreateDatabase(information);
            ReaderSettings settings = new ReaderSettings();
            settings.AllowUnresolvedReferences = true;
            settings.KeepLongStrings = true;

            IReader reader = ReaderFactory.CreateReader(information, settings);
            reader.DemoVersion = false;
            ushort version = 0;
            if (!reader.Read(database, path, string.Empty, false, out version))
            {
                throw new InvalidDataException("Vector ASAP2 Reader 无法读取 A2L：" + path);
            }
            return database;
        }

        private static IModule SelectModule(IDatabase database, string requestedName)
        {
            List<IModule> modules = database.Modules.ToList();
            if (!string.IsNullOrWhiteSpace(requestedName))
            {
                IModule requested = modules.FirstOrDefault(
                    item => string.Equals(item.Name, requestedName, StringComparison.OrdinalIgnoreCase));
                if (requested == null)
                {
                    throw new ArgumentException("A2L 中不存在模块：" + requestedName);
                }
                return requested;
            }

            if (modules.Count != 1)
            {
                throw new ArgumentException("A2L 包含多个模块，请使用 --module 指定模块名。");
            }
            return modules[0];
        }

        private static void ValidateParsedObjectCounts(string inputPath, IDatabase database)
        {
            string source = File.ReadAllText(inputPath);
            DatabaseCounts parsed = DatabaseCounts.From(database);
            CheckKeywordCount(source, "MEASUREMENT", parsed.Measurements);
            CheckKeywordCount(source, "CHARACTERISTIC", parsed.Characteristics);
            CheckKeywordCount(source, "AXIS_PTS", parsed.AxisPoints);
            CheckKeywordCount(source, "BLOB", parsed.Blobs);
            CheckKeywordCount(source, "INSTANCE", parsed.Instances);
        }

        private static void CheckKeywordCount(string source, string keyword, int parsedCount)
        {
            int textCount = Regex.Matches(
                source,
                @"(?im)^\s*/begin\s+" + Regex.Escape(keyword) + @"\b").Count;
            if (textCount != parsedCount)
            {
                throw new InvalidDataException(
                    "A2L 解析对象数量不完整：" + keyword +
                    " 文本=" + textCount + "，API=" + parsedCount +
                    "。已停止，未写入输出文件。");
            }
        }

        private static ElfContext ReadElf(string elfPath, string vectorBin)
        {
            Type readerType = typeof(MapFormatNumber).Assembly.GetType(
                "Vector.MCD.LinkMap.SymbolsProvider.LinkerMapFileReader",
                true);
            object reader = Activator.CreateInstance(readerType, true);

            string iniPath = Path.Combine(Path.GetTempPath(), "CanapeA2LUpdater_LinkMapReader.ini");
            readerType.GetProperty("UserDefinedIniFilePath").SetValue(reader, iniPath, null);

            MethodInfo read = readerType.GetMethod(
                "Read",
                new Type[] { typeof(string), typeof(MapFormatNumber), typeof(IInformationHandler) });
            object result = read.Invoke(
                reader,
                new object[] { elfPath, MapFormatNumber.ELF_DWARF_32_Bit, null });

            ILinkerMapFile map = result as ILinkerMapFile;
            if (map == null)
            {
                throw new InvalidDataException("Vector ELF/DWARF Reader 无法读取：" + elfPath);
            }
            MethodInfo getReader = readerType.GetMethod("GetMapFileReader");
            ILinkerMapFileReader mapReader = (ILinkerMapFileReader)getReader.Invoke(reader, null);
            return new ElfContext(map, mapReader);
        }

        private static IMapFileProvider CreateMapFileProvider(string elfPath, ElfContext elf)
        {
            Assembly studio = Assembly.Load("Vector.MCD.ASAP2.Studio");
            Type settingsType = studio.GetType(
                "Vector.MCD.ASAP2Studio.Models.ToolSettings.LinkerMapSettings",
                true);
            object settings = Activator.CreateInstance(settingsType, new object[] { elf.Reader });

            Type mapFileType = studio.GetType("Vector.MCD.ASAP2Studio.Models.Database.MapFile", true);
            object mapFile = Activator.CreateInstance(
                mapFileType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new object[] {
                    elfPath,
                    MapFormatNumber.ELF_DWARF_32_Bit,
                    "ELF/DWARF 32/64 Bit",
                    settings
                },
                null);
            mapFileType.GetProperty("LinkerMapFile").SetValue(mapFile, elf.Map, null);

            Type providerType = studio.GetType(
                "Vector.MCD.ASAP2Studio.Models.MapFileProvider.MapFileProvider",
                true);
            object provider = Activator.CreateInstance(
                providerType,
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new object[] { mapFile },
                null);
            return (IMapFileProvider)provider;
        }

        private static List<string> ReadNames(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return new List<string>();
            return File.ReadAllLines(path, System.Text.Encoding.UTF8)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("#"))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static void WriteDatabase(IDatabase database, string outputPath, IInformation information)
        {
            IWriter writer = WriterFactory.CreateWriter(information);
            writer.DemoVersion = false;
            writer.ASAP2Version = 0x0171;
            writer.Encoding = new UTF8Encoding(false);
            writer.PreserveGlobalUserComments = true;
            if (!writer.Write(database, outputPath, false, "CanapeA2LUpdater", false, 0, string.Empty))
            {
                throw new IOException("Vector ASAP2 Writer 写入失败：" + outputPath);
            }
        }

        private static void VerifyOutput(
            string outputPath,
            IInformation information,
            DatabaseCounts expectedCounts)
        {
            IDatabase verification = ReadDatabase(outputPath, information);
            if (!verification.Modules.Any())
            {
                throw new InvalidDataException("输出 A2L 重新解析后没有模块。");
            }

            DatabaseCounts actualCounts = DatabaseCounts.From(verification);
            if (!expectedCounts.Equals(actualCounts))
            {
                throw new InvalidDataException(
                    "输出 A2L 对象数量校验失败。期望：" + expectedCounts +
                    "；实际：" + actualCounts);
            }
        }

        private static void PrintSummary(
            Options options,
            string moduleName,
            IList<string> created,
            IList<string> updated,
            IList<string> notFound,
            IList<string> failed)
        {
            Console.WriteLine("模块：" + moduleName);
            Console.WriteLine("新增：" + created.Count);
            Console.WriteLine("更新：" + updated.Count);
            Console.WriteLine("ELF 中未找到：" + notFound.Count);
            Console.WriteLine("处理失败：" + failed.Count);
            Console.WriteLine("输出：" + Path.GetFullPath(options.OutputA2l));

            PrintNames("新增列表", created);
            PrintNames("更新列表", updated);
            PrintNames("未找到列表", notFound);
            PrintNames("失败列表", failed);
        }

        private static void PrintNames(string title, IList<string> names)
        {
            if (names.Count == 0) return;
            Console.WriteLine(title + "：");
            foreach (string name in names) Console.WriteLine("  " + name);
        }

        private static void PrintMachineResults(string kind, string state, IList<string> names)
        {
            foreach (string name in names)
            {
                Console.WriteLine("RESULT|" + kind + "|" + state + "|" + name);
            }
        }
    }

    internal sealed class DatabaseCounts
    {
        public int Measurements;
        public int Characteristics;
        public int AxisPoints;
        public int Blobs;
        public int Instances;

        public static DatabaseCounts From(IDatabase database)
        {
            DatabaseCounts counts = new DatabaseCounts();
            foreach (IModule module in database.Modules)
            {
                counts.Measurements += module.Measurements.Count();
                counts.Characteristics += module.Characteristics.Count();
                counts.AxisPoints += module.AxisPoints.Count();
                counts.Blobs += module.Blobs.Count();
                counts.Instances += module.Instances.Count();
            }
            return counts;
        }

        public override bool Equals(object obj)
        {
            DatabaseCounts other = obj as DatabaseCounts;
            return other != null
                && Measurements == other.Measurements
                && Characteristics == other.Characteristics
                && AxisPoints == other.AxisPoints
                && Blobs == other.Blobs
                && Instances == other.Instances;
        }

        public override int GetHashCode()
        {
            return Measurements ^ Characteristics ^ AxisPoints ^ Blobs ^ Instances;
        }

        public override string ToString()
        {
            return "MEASUREMENT=" + Measurements
                + ", CHARACTERISTIC=" + Characteristics
                + ", AXIS_PTS=" + AxisPoints
                + ", BLOB=" + Blobs
                + ", INSTANCE=" + Instances;
        }
    }

    internal sealed class ElfContext
    {
        public readonly ILinkerMapFile Map;
        public readonly ILinkerMapFileReader Reader;

        public ElfContext(ILinkerMapFile map, ILinkerMapFileReader reader)
        {
            Map = map;
            Reader = reader;
        }
    }

    internal sealed class ConsoleInformation : IInformation
    {
        public void Alive() { }
        public void OnAction(string action) { }
        public void OnProgress(int current, int maximum) { }

        public void OnMessage(EventMessage message)
        {
            if (message != null) Console.Error.WriteLine(message.ToString());
        }
    }

    internal sealed class UpdateCallback : IUpdateCallback
    {
        public UpdateActionAnswer CanPerformAction(UpdateActionType action, string objectName)
        {
            return UpdateActionAnswer.YesAll;
        }
    }

    internal sealed class Options
    {
        public string InputA2l;
        public string ElfPath;
        public string SignalsPath;
        public string CalibrationsPath;
        public string OutputA2l;
        public string ModuleName;
        public string VectorBin = Environment.GetEnvironmentVariable("ASAP2_STUDIO_BIN");

        public static bool TryParse(string[] args, out Options options)
        {
            options = new Options();
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < args.Length; index += 2)
            {
                if (!args[index].StartsWith("--") || index + 1 >= args.Length) return false;
                values[args[index]] = args[index + 1];
            }

            values.TryGetValue("--a2l", out options.InputA2l);
            values.TryGetValue("--elf", out options.ElfPath);
            values.TryGetValue("--signals", out options.SignalsPath);
            values.TryGetValue("--calibrations", out options.CalibrationsPath);
            values.TryGetValue("--out", out options.OutputA2l);
            values.TryGetValue("--module", out options.ModuleName);
            string vectorBin;
            if (values.TryGetValue("--vector-bin", out vectorBin)) options.VectorBin = vectorBin;

            return !string.IsNullOrWhiteSpace(options.InputA2l)
                && !string.IsNullOrWhiteSpace(options.ElfPath)
                && (!string.IsNullOrWhiteSpace(options.SignalsPath)
                    || !string.IsNullOrWhiteSpace(options.CalibrationsPath))
                && !string.IsNullOrWhiteSpace(options.OutputA2l);
        }

        public static void PrintUsage()
        {
            Console.WriteLine("用法：");
            Console.WriteLine("  CanapeA2LUpdater.exe --a2l input.a2l --elf app.elf [--signals measurements.txt] [--calibrations parameters.txt] --out output.a2l [--module name]");
        }
    }
}
