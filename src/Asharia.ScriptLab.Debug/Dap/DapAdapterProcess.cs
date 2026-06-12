using System.Diagnostics;

namespace ScriptLab.Debug.Dap;

public sealed class DapAdapterProcess : IDisposable
{
    private readonly Process process;

    private DapAdapterProcess(Process process, DapProtocolClient client)
    {
        this.process = process;
        Client = client;
    }

    public DapProtocolClient Client { get; }

    public static DapAdapterProcess Start(
        string fileName,
        IEnumerable<string>? arguments = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        if (environment is not null)
        {
            foreach (var (name, value) in environment)
            {
                startInfo.Environment[name] = value;
            }
        }

        if (arguments is not null)
        {
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start DAP adapter process '{fileName}'.");
        process.ErrorDataReceived += (_, _) => { };
        process.BeginErrorReadLine();
        return new DapAdapterProcess(
            process,
            new DapProtocolClient(
                process.StandardOutput.BaseStream,
                process.StandardInput.BaseStream,
                leaveOpen: false));
    }

    public void Dispose()
    {
        Client.Dispose();
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        process.Dispose();
    }
}
