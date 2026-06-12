namespace ScriptLab;

public static class ScriptPathResolver
{
    public static string Resolve(string scriptPath)
    {
        if (Path.IsPathFullyQualified(scriptPath))
        {
            return scriptPath;
        }

        var currentDirectoryPath = Path.GetFullPath(scriptPath);
        if (File.Exists(currentDirectoryPath))
        {
            return currentDirectoryPath;
        }

        var appDirectoryPath = Path.GetFullPath(scriptPath, AppContext.BaseDirectory);
        if (File.Exists(appDirectoryPath))
        {
            return appDirectoryPath;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.GetFullPath(scriptPath, directory.FullName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return currentDirectoryPath;
    }
}
