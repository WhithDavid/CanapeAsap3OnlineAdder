using System;
using System.IO;
using System.Reflection;

internal static class Bootstrap
{
    private static string _vectorBin;

    private static int Main(string[] args)
    {
        _vectorBin = Environment.GetEnvironmentVariable("ASAP2_STUDIO_BIN");
        if (string.IsNullOrWhiteSpace(_vectorBin))
        {
            _vectorBin = @"C:\Path\To\ASAP2Studio\Bin";
        }

        AppDomain.CurrentDomain.AssemblyResolve += ResolveVectorAssembly;

        try
        {
            string corePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CanapeA2LUpdater.Core.dll");
            Assembly core = Assembly.LoadFrom(corePath);
            Type entryPoint = core.GetType("CanapeA2LUpdater.EntryPoint", true);
            MethodInfo run = entryPoint.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            return (int)run.Invoke(null, new object[] { args });
        }
        catch (TargetInvocationException ex)
        {
            Console.Error.WriteLine(ex.InnerException == null ? ex.ToString() : ex.InnerException.ToString());
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static Assembly ResolveVectorAssembly(object sender, ResolveEventArgs args)
    {
        string name = new AssemblyName(args.Name).Name + ".dll";
        string path = Path.Combine(_vectorBin, name);
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
}
