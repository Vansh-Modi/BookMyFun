using System;
using System.IO;

namespace BookMyFun.Tests;

public static class TestPathHelper
{
    public static string FindRepoRoot()
    {
        var current = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "BookMyFun.csproj")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return Directory.GetCurrentDirectory();
    }

    public static string GetPath(params string[] paths)
    {
        var all = new string[paths.Length + 1];
        all[0] = FindRepoRoot();
        Array.Copy(paths, 0, all, 1, paths.Length);
        return Path.Combine(all);
    }
}
