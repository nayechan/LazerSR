using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace LazerSR.Hook;

public static class DependencyResolver
{
    private static int _installed;

    public static void Install()
    {
        if (Interlocked.CompareExchange(ref _installed, 1, 0) != 0)
            return;

        string hookDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) => Resolve(args, hookDir);
    }

    private static Assembly? Resolve(ResolveEventArgs args, string hookDir)
    {
        var name = new AssemblyName(args.Name);

        if (name.Name is not string simpleName)
            return null;
        if (simpleName.StartsWith("osu", StringComparison.Ordinal))
            return null;

        string path = Path.Combine(hookDir, simpleName + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
}
