using ScriptLab;

namespace ScriptLab.Debug.Dap;

public sealed class DapDebugBackendSession : IDisposable
{
    public DapDebugBackendSession(
        DapDebugSessionRuntime runtime,
        DapAdapterProcess? adapterProcess = null)
    {
        Runtime = runtime;
        AdapterProcess = adapterProcess;
    }

    public DapDebugSessionRuntime Runtime { get; }

    public DapAdapterProcess? AdapterProcess { get; }

    public DapDebugSessionLifecycle Lifecycle => Runtime.Lifecycle;

    public void Dispose()
    {
        AdapterProcess?.Dispose();
    }
}
