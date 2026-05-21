using System.Text.Json.Nodes;

namespace ScriptLab;

public sealed record DapDebugSessionLaunchResult(
    DapDebugSessionRuntime Runtime,
    DapBreakpointBackendCapabilities Capabilities,
    bool InitializedEventReceived,
    IReadOnlyList<ScriptBreakpointBackendResult> BreakpointResults);

public sealed class DapDebugSessionLauncher
{
    private readonly DapDebugSessionClient client;

    public DapDebugSessionLauncher(DapDebugSessionClient client)
    {
        this.client = client;
    }

    public DapDebugSessionLaunchResult Launch(
        ScriptDebugSession session,
        ScriptDebugMap debugMap,
        string sourcePath,
        JsonObject? initializeArguments,
        JsonObject launchArguments)
    {
        var handshake = client.Initialize(initializeArguments);
        var runtime = new DapDebugSessionRuntime(
            client,
            session,
            debugMap,
            handshake.Capabilities);
        var breakpointResults = runtime.ApplySourceBreakpoints(sourcePath);
        client.ConfigurationDone();
        client.Launch(launchArguments);

        return new DapDebugSessionLaunchResult(
            runtime,
            handshake.Capabilities,
            handshake.InitializedEventReceived,
            breakpointResults);
    }
}
