using ScriptLab;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Xunit.Abstractions;

namespace ScriptLab.Tests;

public sealed class DapNetcoredbgSmokeTests
{
    private const string AdapterEnvironmentVariable = "SCRIPTLAB_DAP_ADAPTER";
    private const string DotnetEnvironmentVariable = "SCRIPTLAB_DOTNET";
    private const string TargetFrameworkEnvironmentVariable = "SCRIPTLAB_DAP_SMOKE_TFM";
    private const string CppCompilerEnvironmentVariable = "SCRIPTLAB_CPP_COMPILER";
    private const string NativeBridgeTypeName = "ScriptLab.Tests.NativeHost.NativeHostBridge, bridge";
    private const string NativeBridgePrepareMethod = "Prepare";
    private const string NativeBridgeEntryMethod = "Entry";
    private static readonly TimeSpan AdapterEventTimeout = TimeSpan.FromSeconds(20);
    private readonly ITestOutputHelper output;

    public DapNetcoredbgSmokeTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void Launch_WhenNetcoredbgAdapterIsConfigured_HitsBreakpointAndReadsLocals()
    {
        var adapterPath = Environment.GetEnvironmentVariable(AdapterEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adapterPath))
        {
            output.WriteLine(
                $"Set {AdapterEnvironmentVariable} to a netcoredbg.exe path to run the real DAP adapter smoke test.");
            return;
        }

        Assert.True(File.Exists(adapterPath), $"DAP adapter does not exist: {adapterPath}");
        var dotnetPath = ResolveDotnetPath();
        var targetFramework = Environment.GetEnvironmentVariable(TargetFrameworkEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            targetFramework = $"net{Environment.Version.Major}.0";
        }

        var workspace = Path.Combine(
            Path.GetTempPath(),
            "ScriptLab.Tests",
            "DapNetcoredbgSmoke",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        try
        {
            var smokeProgram = CreateSmokeProgram(dotnetPath, targetFramework, workspace);
            using var adapter = DapAdapterProcess.Start(
                adapterPath,
                new[] { "--interpreter=vscode" },
                smokeProgram.OutputDirectory,
                CreateDotnetAdapterEnvironment(dotnetPath));
            var client = new DapDebugSessionClient(adapter.Client, adapter.Client);

            var handshake = client.Initialize(new JsonObject
            {
                ["adapterID"] = "netcoredbg",
                ["clientID"] = "scriptlab",
                ["clientName"] = "ScriptLab",
                ["pathFormat"] = "path",
                ["linesStartAt1"] = true,
                ["columnsStartAt1"] = true,
                ["supportsVariableType"] = true,
                ["supportsRunInTerminalRequest"] = false
            });
            Assert.True(handshake.Capabilities.SupportsConditionalBreakpoints);

            var launch = client.BeginLaunch(new JsonObject
            {
                ["program"] = smokeProgram.ProgramPath,
                ["cwd"] = smokeProgram.OutputDirectory,
                ["stopAtEntry"] = false
            });
            output.WriteLine(
                client.WaitForInitializedEvent(AdapterEventTimeout)
                    ? "netcoredbg initialized event was observed."
                    : "netcoredbg initialized event was not observed before configuration.");

            var breakpoint = Assert.Single(client.SetBreakpoints(
                smokeProgram.SourcePath,
                new[] { new DapSourceBreakpointRequest(Line: 4, Column: 1, Condition: null, HitCondition: null) }));
            Assert.Equal(4, breakpoint.Line);

            client.ConfigurationDone();
            launch.Wait();

            var stopped = WaitForStoppedEvent(client, AdapterEventTimeout);
            Assert.Equal(ScriptStoppedReason.Breakpoint, stopped.Reason);
            Assert.True(stopped.AllThreadsStopped);
            Assert.NotNull(stopped.ThreadId);

            var frame = client.StackTrace(stopped.ThreadId!.Value)
                .First(candidate => PathsEqual(candidate.SourcePath, smokeProgram.SourcePath));
            Assert.Equal(4, frame.Line);

            var locals = client.Scopes(frame.Id)
                .First(scope => string.Equals(scope.Name, "Locals", StringComparison.OrdinalIgnoreCase));
            var value = client.Variables(locals.VariablesReference)
                .First(variable => string.Equals(variable.Name, "value", StringComparison.Ordinal));
            Assert.Equal("int", value.Type);
            Assert.Equal("41", value.Value);

            client.Disconnect(terminateDebuggee: true);
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Fact]
    public void Launch_WhenGeneratedScriptAssemblyIsLoaded_HitsDebugMapSourceBreakpoint()
    {
        var adapterPath = Environment.GetEnvironmentVariable(AdapterEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adapterPath))
        {
            output.WriteLine(
                $"Set {AdapterEnvironmentVariable} to a netcoredbg.exe path to run the generated script DAP smoke test.");
            return;
        }

        Assert.True(File.Exists(adapterPath), $"DAP adapter does not exist: {adapterPath}");
        var currentRuntimeDotnetPath = ResolveCurrentRuntimeDotnetPath();
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "ScriptLab.Tests",
            "DapGeneratedScriptSmoke",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        try
        {
            var smoke = CreateGeneratedScriptSmoke(currentRuntimeDotnetPath, workspace);
            using var adapter = DapAdapterProcess.Start(
                adapterPath,
                new[]
                {
                    "--interpreter=vscode",
                    "--",
                    currentRuntimeDotnetPath,
                    smoke.HostProgramPath
                },
                smoke.HostOutputDirectory);
            var client = new DapDebugSessionClient(adapter.Client, adapter.Client);

            var handshake = client.Initialize(CreateInitializeArguments());
            var launch = client.BeginLaunch(new JsonObject
            {
                ["program"] = smoke.HostProgramPath,
                ["cwd"] = smoke.HostOutputDirectory,
                ["stopAtEntry"] = false
            });
            launch.Wait();
            client.WaitForInitializedEvent(TimeSpan.FromMilliseconds(250));

            var hostBreakpoint = Assert.Single(client.SetBreakpoints(
                smoke.HostSourcePath,
                new[]
                {
                    new DapSourceBreakpointRequest(
                        smoke.HostReadyLine,
                        Column: 1,
                        Condition: null,
                        HitCondition: null)
                }));
            Assert.Equal(smoke.HostReadyLine, hostBreakpoint.Line);

            client.ConfigurationDone();
            var hostStopped = WaitForStoppedEvent(client, AdapterEventTimeout);
            Assert.Equal(ScriptStoppedReason.Breakpoint, hostStopped.Reason);
            Assert.True(hostStopped.ThreadId.HasValue);

            var session = new ScriptDebugSession(
                smoke.Emit.DebugMap,
                File.ReadAllText(smoke.Emit.DebugMap.SourceDocumentPath),
                smoke.Emit.DebugMap.SourceDocumentPath);
            session.SetSourceBreakpoints(
                smoke.Emit.DebugMap.SourceDocumentPath,
                new[] { new ScriptSourceBreakpointRequest(Line: 14, Column: 13) });
            var runtime = new DapDebugSessionRuntime(
                client,
                session,
                smoke.Emit.DebugMap,
                handshake.Capabilities);

            var backendResult = Assert.Single(runtime.ApplySourceBreakpoints(smoke.Emit.DebugMap.SourceDocumentPath));
            Assert.Equal(ScriptBreakpointBackendStatus.Applied, backendResult.Status);
            Assert.True(backendResult.Verified);
            var binding = session.ResolveSourceBreakpoint(
                smoke.Emit.DebugMap.SourceDocumentPath,
                14,
                13);
            Assert.Equal("Call", Assert.Single(binding.Candidates).Kind);

            runtime.Continue(hostStopped.ThreadId.GetValueOrDefault());
            var generatedStopped = WaitForRuntimeStoppedEvent(runtime, AdapterEventTimeout);
            AssertGeneratedCallStop(generatedStopped);

            client.Disconnect(terminateDebuggee: true);
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Fact]
    public void Attach_WhenManagedHostIsAlreadyRunning_HitsGeneratedScriptSourceBreakpoint()
    {
        var adapterPath = Environment.GetEnvironmentVariable(AdapterEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adapterPath))
        {
            output.WriteLine(
                $"Set {AdapterEnvironmentVariable} to a netcoredbg.exe path to run the attach DAP smoke test.");
            return;
        }

        Assert.True(File.Exists(adapterPath), $"DAP adapter does not exist: {adapterPath}");
        var currentRuntimeDotnetPath = ResolveCurrentRuntimeDotnetPath();
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "ScriptLab.Tests",
            "DapAttachGeneratedScriptSmoke",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        Process? hostProcess = null;

        try
        {
            var smoke = CreateGeneratedScriptAttachSmoke(currentRuntimeDotnetPath, workspace);
            hostProcess = StartProcess(
                currentRuntimeDotnetPath,
                smoke.HostOutputDirectory,
                smoke.HostProgramPath,
                smoke.ReadyPath,
                smoke.GoPath);
            WaitForFile(smoke.ReadyPath, AdapterEventTimeout);
            Assert.False(hostProcess.HasExited);

            using var adapter = DapAdapterProcess.Start(
                adapterPath,
                new[] { "--interpreter=vscode" },
                smoke.HostOutputDirectory);
            var client = new DapDebugSessionClient(adapter.Client, adapter.Client);

            var handshake = client.Initialize(CreateInitializeArguments());
            var attach = client.BeginAttach(new JsonObject
            {
                ["processId"] = hostProcess.Id
            });
            attach.Wait();
            client.WaitForInitializedEvent(TimeSpan.FromMilliseconds(250));

            var session = new ScriptDebugSession(
                smoke.Emit.DebugMap,
                File.ReadAllText(smoke.Emit.DebugMap.SourceDocumentPath),
                smoke.Emit.DebugMap.SourceDocumentPath);
            session.SetSourceBreakpoints(
                smoke.Emit.DebugMap.SourceDocumentPath,
                new[] { new ScriptSourceBreakpointRequest(Line: 14, Column: 13) });
            var runtime = new DapDebugSessionRuntime(
                client,
                session,
                smoke.Emit.DebugMap,
                handshake.Capabilities);
            var backendResult = Assert.Single(runtime.ApplySourceBreakpoints(smoke.Emit.DebugMap.SourceDocumentPath));
            client.ConfigurationDone();

            if (!backendResult.Verified)
            {
                var verifiedBreakpoint = WaitForBreakpointEvent(
                    client,
                    smoke.Emit.DebugMap.SourceDocumentPath,
                    AdapterEventTimeout);
                Assert.True(verifiedBreakpoint.Verified);
                Assert.True(PathsEqual(verifiedBreakpoint.SourcePath, smoke.Emit.DebugMap.SourceDocumentPath));
            }

            File.WriteAllText(smoke.GoPath, "go", Encoding.UTF8);
            var generatedStopped = WaitForRuntimeStoppedEvent(runtime, AdapterEventTimeout);
            AssertGeneratedCallStop(generatedStopped);

            runtime.Continue(generatedStopped.ThreadId.GetValueOrDefault());
            client.Disconnect(terminateDebuggee: false);
            Assert.True(hostProcess.WaitForExit(milliseconds: 5000));
            Assert.Equal(0, hostProcess.ExitCode);
        }
        finally
        {
            if (hostProcess is not null)
            {
                if (!hostProcess.HasExited)
                {
                    hostProcess.Kill(entireProcessTree: true);
                }

                hostProcess.Dispose();
            }

            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Fact]
    public void Attach_WhenJsonRpcServerUsesConfiguredNetcoredbgAdapter_HitsGeneratedScriptSourceBreakpoint()
    {
        var adapterPath = Environment.GetEnvironmentVariable(AdapterEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adapterPath))
        {
            output.WriteLine(
                $"Set {AdapterEnvironmentVariable} to a netcoredbg.exe path to run the JSON-RPC attach smoke test.");
            return;
        }

        Assert.True(File.Exists(adapterPath), $"DAP adapter does not exist: {adapterPath}");
        var currentRuntimeDotnetPath = ResolveCurrentRuntimeDotnetPath();
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "ScriptLab.Tests",
            "DapJsonRpcAttachGeneratedScriptSmoke",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        Process? hostProcess = null;

        try
        {
            var smoke = CreateGeneratedScriptAttachSmoke(currentRuntimeDotnetPath, workspace);
            var emitDirectory = Path.GetDirectoryName(smoke.Emit.AssemblyPath)!;
            using var server = new ScriptLabJsonRpcServer(
                new ScriptLabServerOptions(emitDirectory, adapterPath));

            SendServerRequest(
                server,
                1,
                "loadGraph",
                new
                {
                    scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                    outputDirectory = emitDirectory
                });
            SendServerRequest(
                server,
                2,
                "setSourceBreakpoints",
                new
                {
                    sourcePath = smoke.Emit.DebugMap.SourceDocumentPath,
                    breakpoints = new[] { new { line = 14, column = 13 } }
                });

            hostProcess = StartProcess(
                currentRuntimeDotnetPath,
                smoke.HostOutputDirectory,
                smoke.HostProgramPath,
                smoke.ReadyPath,
                smoke.GoPath);
            WaitForFile(smoke.ReadyPath, AdapterEventTimeout);
            Assert.False(hostProcess.HasExited);

            var attach = SendServerRequest(
                server,
                3,
                "attachDebugHost",
                new
                {
                    processId = hostProcess.Id,
                    hostKind = "cppClr"
                });
            var attachResult = attach["result"]!.AsObject();

            Assert.Equal("attached", attachResult["status"]!.GetValue<string>());
            Assert.Equal(DapDebugSessionPhase.Attached, attachResult["lifecycle"]!["phase"]!.GetValue<string>());
            Assert.False(string.IsNullOrWhiteSpace(attachResult["attachTarget"]!["processStartTimeUtc"]!.GetValue<string>()));

            var breakpointEvent = WaitForServerBreakpointEvent(
                server,
                smoke.Emit.DebugMap.SourceDocumentPath,
                AdapterEventTimeout);
            Assert.True(breakpointEvent["verified"]!.GetValue<bool>());

            File.WriteAllText(smoke.GoPath, "go", Encoding.UTF8);
            var stoppedResult = WaitForServerStoppedEvent(server, AdapterEventTimeout);
            var stoppedEvent = stoppedResult["stoppedEvent"]!.AsObject();
            Assert.Equal(ScriptStoppedEventStatus.Resolved, stoppedEvent["status"]!.GetValue<string>());
            Assert.Equal("n10", stoppedEvent["graphNodeId"]!.GetValue<string>());
            Assert.Equal(14, stoppedEvent["line"]!.GetValue<int>());
            Assert.Equal(13, stoppedEvent["column"]!.GetValue<int>());
            Assert.Equal(
                ScriptPausedSnapshotStatus.Partial,
                stoppedResult["pausedSnapshot"]!["status"]!.GetValue<string>());
            var debugStateId = stoppedResult["debugStateId"]!.GetValue<int>();

            var variables = SendServerRequest(
                server,
                4,
                "readVariables",
                new { debugStateId });
            var variablesResult = variables["result"]!.AsObject();
            Assert.Equal("ok", variablesResult["status"]!.GetValue<string>());
            Assert.True(variablesResult["totalCount"]!.GetValue<int>() > 0);

            SendServerRequest(
                server,
                5,
                "continue",
                new { threadId = stoppedEvent["threadId"]!.GetValue<int>() });
            var exitWait = SendServerRequest(
                server,
                6,
                "waitDebugHostExit",
                new { timeoutMilliseconds = 5000 });
            var exitWaitResult = exitWait["result"]!.AsObject();
            Assert.Equal("exited", exitWaitResult["status"]!.GetValue<string>());
            if (exitWaitResult["exitCode"] is null)
            {
                Assert.True(hostProcess.WaitForExit(milliseconds: 5000));
                Assert.Equal(0, hostProcess.ExitCode);
            }
            else
            {
                Assert.Equal(0, exitWaitResult["exitCode"]!.GetValue<int>());
            }

            SendServerRequest(
                server,
                7,
                "disconnectDebugHost",
                new { terminateDebuggee = false });
            Assert.Equal(0, hostProcess.ExitCode);
        }
        finally
        {
            if (hostProcess is not null)
            {
                if (!hostProcess.HasExited)
                {
                    hostProcess.Kill(entireProcessTree: true);
                }

                hostProcess.Dispose();
            }

            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Fact]
    public void Attach_WhenNativeHostOwnsClr_HitsGeneratedScriptSourceBreakpoint()
    {
        var adapterPath = Environment.GetEnvironmentVariable(AdapterEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adapterPath))
        {
            output.WriteLine(
                $"Set {AdapterEnvironmentVariable} to a netcoredbg.exe path to run the native host attach smoke test.");
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            output.WriteLine("Native host attach smoke currently builds a Windows hostfxr executable.");
            return;
        }

        var compilerPath = ResolveCppCompilerPath();
        if (compilerPath is null)
        {
            output.WriteLine(
                $"Set {CppCompilerEnvironmentVariable} to clang-cl.exe to run the native host attach smoke test.");
            return;
        }

        var hostFxrPath = ResolveHostFxrPath();
        if (hostFxrPath is null)
        {
            output.WriteLine("Could not locate hostfxr.dll for the current .NET runtime.");
            return;
        }

        Assert.True(File.Exists(adapterPath), $"DAP adapter does not exist: {adapterPath}");
        var currentRuntimeDotnetPath = ResolveCurrentRuntimeDotnetPath();
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "ScriptLab.Tests",
            "DapNativeHostGeneratedScriptSmoke",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        Process? hostProcess = null;

        try
        {
            var smoke = CreateNativeGeneratedScriptAttachSmoke(
                currentRuntimeDotnetPath,
                compilerPath,
                workspace);
            var bridgeManifestPath = WriteBridgeManifest(
                smoke.WorkingDirectory,
                hostFxrPath,
                smoke.Emit,
                smoke.BridgeRuntimeConfigPath,
                smoke.BridgeAssemblyPath);
            hostProcess = StartProcess(
                smoke.NativeHostPath,
                smoke.WorkingDirectory,
                bridgeManifestPath,
                smoke.ReadyPath,
                smoke.GoPath);
            WaitForFile(smoke.ReadyPath, AdapterEventTimeout);
            Assert.False(hostProcess.HasExited);

            using var adapter = DapAdapterProcess.Start(
                adapterPath,
                new[] { "--interpreter=vscode" },
                smoke.WorkingDirectory);
            var client = new DapDebugSessionClient(adapter.Client, adapter.Client);
            var handshake = client.Initialize(CreateInitializeArguments());
            var attach = client.BeginAttach(new JsonObject
            {
                ["processId"] = hostProcess.Id
            });
            attach.Wait();
            client.WaitForInitializedEvent(TimeSpan.FromMilliseconds(250));

            var session = new ScriptDebugSession(
                smoke.Emit.DebugMap,
                File.ReadAllText(smoke.Emit.DebugMap.SourceDocumentPath),
                smoke.Emit.DebugMap.SourceDocumentPath);
            session.SetSourceBreakpoints(
                smoke.Emit.DebugMap.SourceDocumentPath,
                new[] { new ScriptSourceBreakpointRequest(Line: 14, Column: 13) });
            var runtime = new DapDebugSessionRuntime(
                client,
                session,
                smoke.Emit.DebugMap,
                handshake.Capabilities);
            var backendResult = Assert.Single(runtime.ApplySourceBreakpoints(smoke.Emit.DebugMap.SourceDocumentPath));
            client.ConfigurationDone();

            if (!backendResult.Verified)
            {
                var verifiedBreakpoint = WaitForBreakpointEvent(
                    client,
                    smoke.Emit.DebugMap.SourceDocumentPath,
                    AdapterEventTimeout);
                Assert.True(verifiedBreakpoint.Verified);
                Assert.True(PathsEqual(verifiedBreakpoint.SourcePath, smoke.Emit.DebugMap.SourceDocumentPath));
            }

            File.WriteAllText(smoke.GoPath, "go", Encoding.UTF8);
            var generatedStopped = WaitForRuntimeStoppedEvent(runtime, AdapterEventTimeout);
            AssertGeneratedCallStop(generatedStopped);

            runtime.Continue(generatedStopped.ThreadId.GetValueOrDefault());
            client.Disconnect(terminateDebuggee: false);
            Assert.True(hostProcess.WaitForExit(milliseconds: 5000));
            Assert.Equal(0, hostProcess.ExitCode);
        }
        finally
        {
            if (hostProcess is not null)
            {
                if (!hostProcess.HasExited)
                {
                    hostProcess.Kill(entireProcessTree: true);
                }

                hostProcess.Dispose();
            }

            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Fact]
    public void Attach_WhenPackageFirstEngineSimulationOwnsClr_HitsGeneratedScriptSourceBreakpoint()
    {
        var adapterPath = Environment.GetEnvironmentVariable(AdapterEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adapterPath))
        {
            output.WriteLine(
                $"Set {AdapterEnvironmentVariable} to a netcoredbg.exe path to run the package-first engine simulation smoke test.");
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            output.WriteLine("Package-first engine simulation smoke currently builds a Windows hostfxr executable.");
            return;
        }

        var compilerPath = ResolveCppCompilerPath();
        if (compilerPath is null)
        {
            output.WriteLine(
                $"Set {CppCompilerEnvironmentVariable} to clang-cl.exe to run the package-first engine simulation smoke test.");
            return;
        }

        var hostFxrPath = ResolveHostFxrPath();
        if (hostFxrPath is null)
        {
            output.WriteLine("Could not locate hostfxr.dll for the current .NET runtime.");
            return;
        }

        Assert.True(File.Exists(adapterPath), $"DAP adapter does not exist: {adapterPath}");
        var currentRuntimeDotnetPath = ResolveCurrentRuntimeDotnetPath();
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "ScriptLab.Tests",
            "PackageFirstEngineSimulationSmoke",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        Process? hostProcess = null;

        try
        {
            var smoke = CreatePackageFirstEngineSimulationSmoke(
                currentRuntimeDotnetPath,
                compilerPath,
                workspace);
            AssertPackageFirstSimulationManifests(smoke);
            var bridgeManifestPath = WriteBridgeManifest(
                smoke.WorkingDirectory,
                hostFxrPath,
                smoke.Emit,
                smoke.BridgeRuntimeConfigPath,
                smoke.BridgeAssemblyPath);

            hostProcess = StartProcess(
                smoke.NativeHostPath,
                smoke.WorkingDirectory,
                bridgeManifestPath,
                smoke.ReadyPath,
                smoke.GoPath);
            WaitForFile(smoke.ReadyPath, AdapterEventTimeout);
            Assert.False(hostProcess.HasExited);

            using var adapter = DapAdapterProcess.Start(
                adapterPath,
                new[] { "--interpreter=vscode" },
                smoke.WorkingDirectory);
            var client = new DapDebugSessionClient(adapter.Client, adapter.Client);
            var handshake = client.Initialize(CreateInitializeArguments());
            var attach = client.BeginAttach(new JsonObject
            {
                ["processId"] = hostProcess.Id
            });
            attach.Wait();
            client.WaitForInitializedEvent(TimeSpan.FromMilliseconds(250));

            var session = new ScriptDebugSession(
                smoke.Emit.DebugMap,
                File.ReadAllText(smoke.Emit.DebugMap.SourceDocumentPath),
                smoke.Emit.DebugMap.SourceDocumentPath);
            session.SetSourceBreakpoints(
                smoke.Emit.DebugMap.SourceDocumentPath,
                new[] { new ScriptSourceBreakpointRequest(Line: 14, Column: 13) });
            var runtime = new DapDebugSessionRuntime(
                client,
                session,
                smoke.Emit.DebugMap,
                handshake.Capabilities);
            var backendResult = Assert.Single(runtime.ApplySourceBreakpoints(smoke.Emit.DebugMap.SourceDocumentPath));
            client.ConfigurationDone();

            if (!backendResult.Verified)
            {
                var verifiedBreakpoint = WaitForBreakpointEvent(
                    client,
                    smoke.Emit.DebugMap.SourceDocumentPath,
                    AdapterEventTimeout);
                Assert.True(verifiedBreakpoint.Verified);
                Assert.True(PathsEqual(verifiedBreakpoint.SourcePath, smoke.Emit.DebugMap.SourceDocumentPath));
            }

            File.WriteAllText(smoke.GoPath, "go", Encoding.UTF8);
            var generatedStopped = WaitForRuntimeStoppedEvent(runtime, AdapterEventTimeout);
            AssertGeneratedCallStop(generatedStopped);

            runtime.Continue(generatedStopped.ThreadId.GetValueOrDefault());
            client.Disconnect(terminateDebuggee: false);
            Assert.True(hostProcess.WaitForExit(milliseconds: 5000));
            Assert.Equal(0, hostProcess.ExitCode);
        }
        finally
        {
            if (hostProcess is not null)
            {
                if (!hostProcess.HasExited)
                {
                    hostProcess.Kill(entireProcessTree: true);
                }

                hostProcess.Dispose();
            }

            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Fact]
    public void Attach_WhenJsonRpcServerUsesPackageFirstEngineSimulation_HitsGeneratedScriptSourceBreakpoint()
    {
        var adapterPath = Environment.GetEnvironmentVariable(AdapterEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(adapterPath))
        {
            output.WriteLine(
                $"Set {AdapterEnvironmentVariable} to a netcoredbg.exe path to run the JSON-RPC package-first engine simulation smoke test.");
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            output.WriteLine("JSON-RPC package-first engine simulation smoke currently builds a Windows hostfxr executable.");
            return;
        }

        var compilerPath = ResolveCppCompilerPath();
        if (compilerPath is null)
        {
            output.WriteLine(
                $"Set {CppCompilerEnvironmentVariable} to clang-cl.exe to run the JSON-RPC package-first engine simulation smoke test.");
            return;
        }

        var hostFxrPath = ResolveHostFxrPath();
        if (hostFxrPath is null)
        {
            output.WriteLine("Could not locate hostfxr.dll for the current .NET runtime.");
            return;
        }

        Assert.True(File.Exists(adapterPath), $"DAP adapter does not exist: {adapterPath}");
        var currentRuntimeDotnetPath = ResolveCurrentRuntimeDotnetPath();
        var workspace = Path.Combine(
            Path.GetTempPath(),
            "ScriptLab.Tests",
            "JsonRpcPackageFirstEngineSimulationSmoke",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        Process? hostProcess = null;

        try
        {
            var smoke = CreatePackageFirstEngineSimulationSmoke(
                currentRuntimeDotnetPath,
                compilerPath,
                workspace);
            AssertPackageFirstSimulationManifests(smoke);
            var emitDirectory = Path.GetDirectoryName(smoke.Emit.AssemblyPath)!;
            using var server = new ScriptLabJsonRpcServer(
                new ScriptLabServerOptions(emitDirectory, adapterPath));

            SendServerRequest(
                server,
                1,
                "loadGraph",
                new
                {
                    scriptPath = GetSamplePath("PlayerMove.ash.cs"),
                    outputDirectory = emitDirectory
                });
            SendServerRequest(
                server,
                2,
                "setSourceBreakpoints",
                new
                {
                    sourcePath = smoke.Emit.DebugMap.SourceDocumentPath,
                    breakpoints = new[] { new { line = 14, column = 13 } }
                });

            var bridgeManifestPath = WriteBridgeManifest(
                smoke.WorkingDirectory,
                hostFxrPath,
                smoke.Emit,
                smoke.BridgeRuntimeConfigPath,
                smoke.BridgeAssemblyPath);
            var validation = SendServerRequest(
                server,
                3,
                "validateDebugHost",
                new
                {
                    hostKind = "cppClr",
                    bridgeManifestPath,
                    enginePackageRoot = smoke.EngineRoot
                });
            var validationResult = validation["result"]!.AsObject();
            Assert.Equal("valid", validationResult["status"]!.GetValue<string>());
            Assert.Equal("dap", validationResult["backend"]!.GetValue<string>());
            Assert.Equal("cppClr", validationResult["hostKind"]!.GetValue<string>());
            Assert.Equal(bridgeManifestPath, validationResult["bridgeManifestPath"]!.GetValue<string>());
            Assert.Equal(smoke.EngineRoot, validationResult["enginePackageRoot"]!.GetValue<string>());
            Assert.Equal(smoke.Emit.AssemblyPath, validationResult["generatedAssemblyPath"]!.GetValue<string>());
            Assert.Equal(smoke.Emit.PdbPath, validationResult["pdbPath"]!.GetValue<string>());
            Assert.Equal(smoke.Emit.DebugMapPath, validationResult["debugMapPath"]!.GetValue<string>());
            Assert.Equal(
                smoke.Emit.DebugMap.SourceDocumentPath,
                validationResult["sourceDocumentPath"]!.GetValue<string>());

            hostProcess = StartProcess(
                smoke.NativeHostPath,
                smoke.WorkingDirectory,
                bridgeManifestPath,
                smoke.ReadyPath,
                smoke.GoPath);
            WaitForFile(smoke.ReadyPath, AdapterEventTimeout);
            Assert.False(hostProcess.HasExited);

            var registered = SendServerRequest(
                server,
                4,
                "registerDebugHost",
                new
                {
                    processId = hostProcess.Id,
                    hostKind = "cppClr",
                    bridgeManifestPath,
                    enginePackageRoot = smoke.EngineRoot
                });
            var registeredResult = registered["result"]!.AsObject();
            Assert.Equal("registered", registeredResult["status"]!.GetValue<string>());
            Assert.Equal(hostProcess.Id, registeredResult["host"]!["processId"]!.GetValue<int>());
            Assert.False(string.IsNullOrWhiteSpace(registeredResult["host"]!["processStartTimeUtc"]!.GetValue<string>()));

            var attach = SendServerRequest(
                server,
                5,
                "attachDebugHost",
                new { });
            var attachResult = attach["result"]!.AsObject();
            Assert.Equal("attached", attachResult["status"]!.GetValue<string>());
            Assert.Equal(DapDebugSessionPhase.Attached, attachResult["lifecycle"]!["phase"]!.GetValue<string>());
            var attachTarget = attachResult["attachTarget"]!.AsObject();
            Assert.Equal(bridgeManifestPath, attachTarget["bridgeManifestPath"]!.GetValue<string>());
            Assert.Equal(smoke.EngineRoot, attachTarget["enginePackageRoot"]!.GetValue<string>());
            Assert.False(string.IsNullOrWhiteSpace(attachTarget["processStartTimeUtc"]!.GetValue<string>()));

            var breakpointEvent = WaitForServerBreakpointEvent(
                server,
                smoke.Emit.DebugMap.SourceDocumentPath,
                AdapterEventTimeout);
            Assert.True(breakpointEvent["verified"]!.GetValue<bool>());

            File.WriteAllText(smoke.GoPath, "go", Encoding.UTF8);
            var stoppedResult = WaitForServerStoppedEvent(server, AdapterEventTimeout);
            var stoppedEvent = stoppedResult["stoppedEvent"]!.AsObject();
            Assert.Equal(ScriptStoppedEventStatus.Resolved, stoppedEvent["status"]!.GetValue<string>());
            Assert.Equal("n10", stoppedEvent["graphNodeId"]!.GetValue<string>());
            Assert.Equal(14, stoppedEvent["line"]!.GetValue<int>());
            Assert.Equal(13, stoppedEvent["column"]!.GetValue<int>());
            Assert.Equal(
                ScriptPausedSnapshotStatus.Partial,
                stoppedResult["pausedSnapshot"]!["status"]!.GetValue<string>());
            Assert.True(stoppedResult["eventSequence"]!.GetValue<long>() > 0);
            Assert.True(
                stoppedResult["nextEventSequence"]!.GetValue<long>() >
                stoppedResult["eventSequence"]!.GetValue<long>());

            var debugStateId = stoppedResult["debugStateId"]!.GetValue<int>();
            var variables = SendServerRequest(
                server,
                6,
                "readVariables",
                new { debugStateId });
            var variablesResult = variables["result"]!.AsObject();
            Assert.Equal("ok", variablesResult["status"]!.GetValue<string>());
            Assert.True(variablesResult["totalCount"]!.GetValue<int>() > 0);

            SendServerRequest(
                server,
                7,
                "continue",
                new { threadId = stoppedEvent["threadId"]!.GetValue<int>() });
            var exitWait = SendServerRequest(
                server,
                8,
                "waitDebugHostExit",
                new { timeoutMilliseconds = 5000 });
            var exitWaitResult = exitWait["result"]!.AsObject();
            Assert.Equal("exited", exitWaitResult["status"]!.GetValue<string>());
            if (exitWaitResult["exitCode"] is null)
            {
                Assert.True(hostProcess.WaitForExit(milliseconds: 5000));
                Assert.Equal(0, hostProcess.ExitCode);
            }
            else
            {
                Assert.Equal(0, exitWaitResult["exitCode"]!.GetValue<int>());
            }

            SendServerRequest(
                server,
                9,
                "disconnectDebugHost",
                new { terminateDebuggee = false });
            Assert.Equal(0, hostProcess.ExitCode);
        }
        finally
        {
            if (hostProcess is not null)
            {
                if (!hostProcess.HasExited)
                {
                    hostProcess.Kill(entireProcessTree: true);
                }

                hostProcess.Dispose();
            }

            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    private static SmokeProgram CreateSmokeProgram(
        string dotnetPath,
        string targetFramework,
        string workspace)
    {
        RunProcess(dotnetPath, "new", "console", "--framework", targetFramework, "--output", workspace);

        var sourcePath = Path.Combine(workspace, "Program.cs");
        File.WriteAllText(
            sourcePath,
            """
            using System;

            var value = 41;
            value += 1;
            Console.WriteLine($"smoke:{value}");
            """,
            Encoding.UTF8);

        RunProcess(dotnetPath, "build", workspace, "--configuration", "Debug");

        var projectName = new DirectoryInfo(workspace).Name;
        var outputDirectory = Path.Combine(workspace, "bin", "Debug", targetFramework);
        return new SmokeProgram(
            sourcePath,
            outputDirectory,
            Path.Combine(outputDirectory, $"{projectName}.dll"));
    }

    private static GeneratedScriptSmoke CreateGeneratedScriptSmoke(
        string dotnetPath,
        string workspace)
    {
        var emit = DebugScriptCompiler.EmitFile(
            GetSamplePath("PlayerMove.ash.cs"),
            Path.Combine(workspace, "emit"));
        var hostDirectory = Path.Combine(workspace, "host");
        RunProcess(dotnetPath, "new", "console", "--framework", "net10.0", "--output", hostDirectory);
        AddGeneratedAssemblyReference(hostDirectory, emit.AssemblyPath);

        var hostSourcePath = Path.Combine(hostDirectory, "Program.cs");
        var hostSource = """
            using Asharia.Behavior;
            using com.game;

            Input.SetKeyDown(Key.W, true);
            var instance = new PlayerMoveSmokeHost();
            Console.WriteLine("ready");
            instance.Tick(0.016f);
            Console.WriteLine("generated-smoke-done");

            public sealed class PlayerMoveSmokeHost : PlayerMove
            {
                public void Tick(float delta)
                {
                    base.Update(delta);
                }
            }
            """;
        File.WriteAllText(hostSourcePath, hostSource, Encoding.UTF8);
        RunProcess(dotnetPath, "build", hostDirectory, "--configuration", "Debug");

        var outputDirectory = Path.Combine(hostDirectory, "bin", "Debug", "net10.0");
        return new GeneratedScriptSmoke(
            emit,
            hostSourcePath,
            FindLine(hostSource, "ready"),
            outputDirectory,
            Path.Combine(outputDirectory, $"{new DirectoryInfo(hostDirectory).Name}.dll"));
    }

    private static GeneratedScriptAttachSmoke CreateGeneratedScriptAttachSmoke(
        string dotnetPath,
        string workspace)
    {
        var emit = DebugScriptCompiler.EmitFile(
            GetSamplePath("PlayerMove.ash.cs"),
            Path.Combine(workspace, "emit"));
        var hostDirectory = Path.Combine(workspace, "host");
        RunProcess(dotnetPath, "new", "console", "--framework", "net10.0", "--output", hostDirectory);
        AddGeneratedAssemblyReference(hostDirectory, emit.AssemblyPath);

        var hostSourcePath = Path.Combine(hostDirectory, "Program.cs");
        var hostSource = """
            using Asharia.Behavior;
            using com.game;

            File.WriteAllText(args[0], "ready");
            while (!File.Exists(args[1]))
            {
                Thread.Sleep(25);
            }

            Input.SetKeyDown(Key.W, true);
            var instance = new PlayerMoveSmokeHost();
            instance.Tick(0.016f);
            Console.WriteLine("attach-host-done");

            public sealed class PlayerMoveSmokeHost : PlayerMove
            {
                public void Tick(float delta)
                {
                    base.Update(delta);
                }
            }
            """;
        File.WriteAllText(hostSourcePath, hostSource, Encoding.UTF8);
        RunProcess(dotnetPath, "build", hostDirectory, "--configuration", "Debug");

        var outputDirectory = Path.Combine(hostDirectory, "bin", "Debug", "net10.0");
        return new GeneratedScriptAttachSmoke(
            emit,
            outputDirectory,
            Path.Combine(outputDirectory, $"{new DirectoryInfo(hostDirectory).Name}.dll"),
            Path.Combine(workspace, "ready.txt"),
            Path.Combine(workspace, "go.txt"));
    }

    private static NativeGeneratedScriptAttachSmoke CreateNativeGeneratedScriptAttachSmoke(
        string dotnetPath,
        string compilerPath,
        string workspace)
    {
        var emit = DebugScriptCompiler.EmitFile(
            GetSamplePath("PlayerMove.ash.cs"),
            Path.Combine(workspace, "emit"));
        var bridgeDirectory = Path.Combine(workspace, "bridge");
        RunProcess(dotnetPath, "new", "console", "--framework", "net10.0", "--output", bridgeDirectory);
        AddGeneratedAssemblyReference(bridgeDirectory, emit.AssemblyPath);
        File.WriteAllText(
            Path.Combine(bridgeDirectory, "Program.cs"),
            """
            using Asharia.Behavior;
            using com.game;

            namespace ScriptLab.Tests.NativeHost;

            public static class Program
            {
                public static void Main()
                {
                }
            }

            public static class NativeHostBridge
            {
                public static int Prepare(IntPtr args, int size)
                {
                    _ = typeof(PlayerMove).Assembly.FullName;
                    return 0;
                }

                public static int Entry(IntPtr args, int size)
                {
                    Input.SetKeyDown(Key.W, true);
                    var instance = new PlayerMoveSmokeHost();
                    instance.Tick(0.016f);
                    return 0;
                }
            }

            public sealed class PlayerMoveSmokeHost : PlayerMove
            {
                public void Tick(float delta)
                {
                    base.Update(delta);
                }
            }
            """,
            Encoding.UTF8);
        RunProcess(dotnetPath, "build", bridgeDirectory, "--configuration", "Debug");

        var bridgeOutputDirectory = Path.Combine(bridgeDirectory, "bin", "Debug", "net10.0");
        var bridgeAssemblyPath = Path.Combine(bridgeOutputDirectory, $"{new DirectoryInfo(bridgeDirectory).Name}.dll");
        var nativeDirectory = Path.Combine(workspace, "native");
        Directory.CreateDirectory(nativeDirectory);
        var nativeHostPath = Path.Combine(nativeDirectory, "native_host.exe");
        RunProcess(
            compilerPath,
            "/nologo",
            "/EHsc",
            "/std:c++17",
            $"/I{ResolveEngineHostIncludeDirectory()}",
            ResolveEngineHostSourcePath(),
            $"/Fe:{nativeHostPath}");

        return new NativeGeneratedScriptAttachSmoke(
            emit,
            nativeDirectory,
            nativeHostPath,
            bridgeAssemblyPath,
            Path.ChangeExtension(bridgeAssemblyPath, ".runtimeconfig.json"),
            Path.Combine(workspace, "ready.txt"),
            Path.Combine(workspace, "go.txt"));
    }

    private static PackageFirstEngineSimulationSmoke CreatePackageFirstEngineSimulationSmoke(
        string dotnetPath,
        string compilerPath,
        string workspace)
    {
        var engineRoot = Path.Combine(workspace, "AshariaEngine");
        WritePackageManifest(
            Path.Combine(engineRoot, "engine", "core"),
            "com.asharia.core",
            "Asharia Engine Core",
            "Shared low-level utilities for Asharia Engine packages.",
            Array.Empty<string>(),
            new[] { "asharia-core" },
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["asharia-core"] = Array.Empty<string>()
            });
        WritePackageManifest(
            Path.Combine(engineRoot, "engine", "platform"),
            "com.asharia.platform",
            "Asharia Engine Platform",
            "Platform abstractions shared by host applications and packages.",
            new[] { "com.asharia.core" },
            new[] { "asharia-platform" },
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["asharia-platform"] = new[] { "asharia-core" }
            });
        WritePackageManifest(
            Path.Combine(engineRoot, "packages", "scene-core"),
            "com.asharia.scene-core",
            "Asharia Engine Scene Core",
            "Headless scene world, entity identity, and transform baseline.",
            new[] { "com.asharia.core" },
            new[] { "asharia-scene-core" },
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["asharia-scene-core"] = new[] { "asharia-core" }
            },
            new[] { "asharia-scene-core-smoke-tests" });

        var scriptRuntimeDirectory = Path.Combine(engineRoot, "packages", "script-runtime");
        WritePackageManifest(
            scriptRuntimeDirectory,
            "com.asharia.script-runtime",
            "Asharia Script Runtime",
            "Generated ScriptLab assemblies and runtime-safe script metadata.",
            new[] { "com.asharia.core", "com.asharia.scene-core" },
            new[] { "asharia-script-runtime" },
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["asharia-script-runtime"] = new[] { "asharia-core", "asharia-scene-core" }
            });
        var emit = DebugScriptCompiler.EmitFile(
            GetSamplePath("PlayerMove.ash.cs"),
            Path.Combine(scriptRuntimeDirectory, "generated"));

        var bridgePackageDirectory = Path.Combine(engineRoot, "packages", "scriptlab-dotnet-bridge");
        var bridgeIncludeDirectory = Path.Combine(bridgePackageDirectory, "include");
        CopyEngineHostContractHeaders(bridgeIncludeDirectory);
        WritePackageManifest(
            bridgePackageDirectory,
            "com.asharia.scriptlab-dotnet-bridge",
            "Asharia ScriptLab .NET Bridge",
            "HostFXR bridge used by native engine hosts to enter ScriptLab generated behaviors.",
            new[] { "com.asharia.core", "com.asharia.scene-core", "com.asharia.script-runtime" },
            new[] { "asharia-scriptlab-dotnet-bridge" },
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["asharia-scriptlab-dotnet-bridge"] = new[]
                {
                    "asharia-core",
                    "asharia-scene-core",
                    "asharia-script-runtime"
                }
            });

        var bridgeDirectory = Path.Combine(bridgePackageDirectory, "bridge");
        RunProcess(dotnetPath, "new", "console", "--framework", "net10.0", "--output", bridgeDirectory);
        AddGeneratedAssemblyReference(bridgeDirectory, emit.AssemblyPath);
        File.WriteAllText(
            Path.Combine(bridgeDirectory, "Program.cs"),
            """
            using Asharia.Behavior;
            using com.game;

            namespace ScriptLab.Tests.NativeHost;

            public static class Program
            {
                public static void Main()
                {
                }
            }

            public static class NativeHostBridge
            {
                private static readonly SimulatedWorld World = new();

                public static int Prepare(IntPtr args, int size)
                {
                    World.RegisterPackage("com.asharia.core");
                    World.RegisterPackage("com.asharia.scene-core");
                    World.RegisterPackage("com.asharia.script-runtime");
                    _ = typeof(PlayerMove).Assembly.FullName;
                    return World.PackageCount == 3 ? 0 : 21;
                }

                public static int Entry(IntPtr args, int size)
                {
                    var entity = World.CreateEntity(101);
                    Input.SetKeyDown(Key.W, true);
                    var system = new SimulatedScriptSystem(World);
                    system.Tick(entity, 0.016f);
                    return World.FrameCount == 1 ? 0 : 22;
                }
            }

            public sealed class SimulatedWorld
            {
                private readonly HashSet<string> packages = new(StringComparer.Ordinal);
                private readonly HashSet<int> entities = new();

                public int PackageCount => packages.Count;

                public int FrameCount { get; private set; }

                public void RegisterPackage(string packageName)
                {
                    packages.Add(packageName);
                }

                public int CreateEntity(int entityId)
                {
                    entities.Add(entityId);
                    return entityId;
                }

                public void AdvanceFrame(int entityId)
                {
                    if (!entities.Contains(entityId))
                    {
                        throw new InvalidOperationException("Script tick targeted an entity outside the simulated scene world.");
                    }

                    FrameCount++;
                }
            }

            public sealed class SimulatedScriptSystem
            {
                private readonly SimulatedWorld world;

                public SimulatedScriptSystem(SimulatedWorld world)
                {
                    this.world = world;
                }

                public void Tick(int entityId, float delta)
                {
                    var instance = new PlayerMoveSmokeHost();
                    instance.Tick(delta);
                    world.AdvanceFrame(entityId);
                }
            }

            public sealed class PlayerMoveSmokeHost : PlayerMove
            {
                public void Tick(float delta)
                {
                    base.Update(delta);
                }
            }
            """,
            Encoding.UTF8);
        RunProcess(dotnetPath, "build", bridgeDirectory, "--configuration", "Debug");

        var appDirectory = Path.Combine(engineRoot, "apps", "sample-viewer");
        WritePackageManifest(
            appDirectory,
            "com.asharia.apps.sample-viewer",
            "Asharia Engine Sample Viewer",
            "Native host application that composes runtime packages for smoke tests.",
            new[]
            {
                "com.asharia.core",
                "com.asharia.platform",
                "com.asharia.scene-core",
                "com.asharia.script-runtime",
                "com.asharia.scriptlab-dotnet-bridge"
            },
            new[] { "asharia-sample-viewer" },
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["asharia-sample-viewer"] = new[]
                {
                    "asharia-core",
                    "asharia-platform",
                    "asharia-scene-core",
                    "asharia-script-runtime",
                    "asharia-scriptlab-dotnet-bridge"
                }
            });
        var nativeSourceDirectory = Path.Combine(appDirectory, "src");
        Directory.CreateDirectory(nativeSourceDirectory);
        var nativeBinaryDirectory = Path.Combine(appDirectory, "bin");
        Directory.CreateDirectory(nativeBinaryDirectory);
        var nativeSourcePath = Path.Combine(nativeSourceDirectory, "sample_viewer_host.cpp");
        var nativeHostPath = Path.Combine(nativeBinaryDirectory, "asharia-sample-viewer.exe");
        File.Copy(ResolveEngineHostSourcePath(), nativeSourcePath, overwrite: true);
        RunProcess(
            compilerPath,
            "/nologo",
            "/EHsc",
            "/std:c++17",
            $"/I{bridgeIncludeDirectory}",
            nativeSourcePath,
            $"/Fe:{nativeHostPath}");

        var bridgeOutputDirectory = Path.Combine(bridgeDirectory, "bin", "Debug", "net10.0");
        var bridgeAssemblyPath = Path.Combine(bridgeOutputDirectory, $"{new DirectoryInfo(bridgeDirectory).Name}.dll");
        return new PackageFirstEngineSimulationSmoke(
            emit,
            engineRoot,
            appDirectory,
            nativeHostPath,
            bridgeAssemblyPath,
            Path.ChangeExtension(bridgeAssemblyPath, ".runtimeconfig.json"),
            Path.Combine(workspace, "ready.txt"),
            Path.Combine(workspace, "go.txt"));
    }

    private static string WriteBridgeManifest(
        string directory,
        string hostFxrPath,
        DebugScriptEmitResult emit,
        string runtimeConfigPath,
        string assemblyPath)
    {
        var manifestPath = Path.Combine(directory, "scriptlab.bridge.json");
        var manifest = new JsonObject
        {
            ["hostfxrPath"] = hostFxrPath,
            ["runtimeConfigPath"] = runtimeConfigPath,
            ["assemblyPath"] = assemblyPath,
            ["typeName"] = NativeBridgeTypeName,
            ["prepareMethod"] = NativeBridgePrepareMethod,
            ["entryMethod"] = NativeBridgeEntryMethod,
            ["generatedAssemblyPath"] = emit.AssemblyPath,
            ["pdbPath"] = emit.PdbPath,
            ["debugMapPath"] = emit.DebugMapPath,
            ["sourceDocumentPath"] = emit.DebugMap.SourceDocumentPath
        };

        File.WriteAllText(
            manifestPath,
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
        AssertBridgeManifest(manifestPath);
        return manifestPath;
    }

    private static void AssertBridgeManifest(string manifestPath)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        Assert.Equal(NativeBridgeTypeName, manifest["typeName"]!.GetValue<string>());
        Assert.Equal(NativeBridgePrepareMethod, manifest["prepareMethod"]!.GetValue<string>());
        Assert.Equal(NativeBridgeEntryMethod, manifest["entryMethod"]!.GetValue<string>());
        Assert.True(File.Exists(manifest["hostfxrPath"]!.GetValue<string>()));
        Assert.True(File.Exists(manifest["runtimeConfigPath"]!.GetValue<string>()));
        Assert.True(File.Exists(manifest["assemblyPath"]!.GetValue<string>()));
        Assert.True(File.Exists(manifest["generatedAssemblyPath"]!.GetValue<string>()));
        Assert.True(File.Exists(manifest["pdbPath"]!.GetValue<string>()));
        Assert.True(File.Exists(manifest["debugMapPath"]!.GetValue<string>()));
        Assert.True(File.Exists(manifest["sourceDocumentPath"]!.GetValue<string>()));
    }

    private static void WritePackageManifest(
        string directory,
        string name,
        string displayName,
        string description,
        IReadOnlyList<string> dependencies,
        IReadOnlyList<string> targets,
        IReadOnlyDictionary<string, IReadOnlyList<string>> targetDependencies,
        IReadOnlyList<string>? testTargets = null)
    {
        Directory.CreateDirectory(directory);
        var json = new JsonObject
        {
            ["name"] = name,
            ["version"] = "0.1.0",
            ["displayName"] = displayName,
            ["description"] = description,
            ["dependencies"] = CreateJsonArray(dependencies),
            ["targets"] = CreateJsonArray(targets),
            ["targetDependencies"] = CreateTargetDependencies(targetDependencies)
        };
        if (testTargets is not null)
        {
            json["testTargets"] = CreateJsonArray(testTargets);
        }

        File.WriteAllText(
            Path.Combine(directory, "asharia.package.json"),
            json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            Encoding.UTF8);
    }

    private static JsonArray CreateJsonArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static JsonObject CreateTargetDependencies(
        IReadOnlyDictionary<string, IReadOnlyList<string>> targetDependencies)
    {
        var json = new JsonObject();
        foreach (var (target, dependencies) in targetDependencies)
        {
            json[target] = CreateJsonArray(dependencies);
        }

        return json;
    }

    private static void AssertPackageFirstSimulationManifests(
        PackageFirstEngineSimulationSmoke smoke)
    {
        AssertManifest(
            Path.Combine(smoke.EngineRoot, "engine", "core", "asharia.package.json"),
            "com.asharia.core",
            Array.Empty<string>());
        AssertManifest(
            Path.Combine(smoke.EngineRoot, "engine", "platform", "asharia.package.json"),
            "com.asharia.platform",
            new[] { "com.asharia.core" });
        AssertManifest(
            Path.Combine(smoke.EngineRoot, "packages", "scene-core", "asharia.package.json"),
            "com.asharia.scene-core",
            new[] { "com.asharia.core" });
        AssertManifest(
            Path.Combine(smoke.EngineRoot, "packages", "script-runtime", "asharia.package.json"),
            "com.asharia.script-runtime",
            new[] { "com.asharia.core", "com.asharia.scene-core" });
        AssertManifest(
            Path.Combine(smoke.EngineRoot, "packages", "scriptlab-dotnet-bridge", "asharia.package.json"),
            "com.asharia.scriptlab-dotnet-bridge",
            new[] { "com.asharia.core", "com.asharia.scene-core", "com.asharia.script-runtime" });
        Assert.True(File.Exists(Path.Combine(
            smoke.EngineRoot,
            "packages",
            "scriptlab-dotnet-bridge",
            "include",
            "scriptlab",
            "ScriptLabBridgeContract.h")));
        AssertManifest(
            Path.Combine(smoke.EngineRoot, "apps", "sample-viewer", "asharia.package.json"),
            "com.asharia.apps.sample-viewer",
            new[]
            {
                "com.asharia.core",
                "com.asharia.platform",
                "com.asharia.scene-core",
                "com.asharia.script-runtime",
                "com.asharia.scriptlab-dotnet-bridge"
            });
    }

    private static void AssertManifest(
        string manifestPath,
        string expectedName,
        IReadOnlyList<string> expectedDependencies)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        Assert.Equal(expectedName, manifest["name"]!.GetValue<string>());
        var dependencies = manifest["dependencies"]!
            .AsArray()
            .Select(node => node!.GetValue<string>())
            .ToArray();
        Assert.Equal(expectedDependencies, dependencies);
        Assert.NotEmpty(manifest["targets"]!.AsArray());
        Assert.NotNull(manifest["targetDependencies"]);
    }

    private static DapStoppedEvent WaitForStoppedEvent(
        DapDebugSessionClient client,
        TimeSpan timeout)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            var stopped = client.DrainStoppedEvents().FirstOrDefault();
            if (stopped is not null)
            {
                return stopped;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(25));
        }

        throw new TimeoutException("Timed out waiting for a DAP stopped event.");
    }

    private static ScriptStoppedEvent WaitForRuntimeStoppedEvent(
        DapDebugSessionRuntime runtime,
        TimeSpan timeout)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            var stopped = runtime.DrainDebugEvents().CurrentStoppedEvent;
            if (stopped is not null)
            {
                return stopped;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(25));
        }

        throw new TimeoutException("Timed out waiting for a mapped DAP stopped event.");
    }

    private static void AssertGeneratedCallStop(ScriptStoppedEvent stopped)
    {
        Assert.Equal(ScriptStoppedEventStatus.Resolved, stopped.Status);
        Assert.Equal("n10", stopped.GraphNodeId);
        Assert.Equal(14, stopped.Line);
        Assert.Equal(13, stopped.Column);
        Assert.True(stopped.ThreadId.HasValue);
        var binding = stopped.Binding;
        Assert.NotNull(binding);
        Assert.Equal("Call", Assert.Single(binding!.Candidates).Kind);
        var sourceSpan = binding.SourceSpan;
        Assert.NotNull(sourceSpan);
        Assert.Equal(14, sourceSpan!.Line);
        Assert.Equal(13, sourceSpan.Column);
    }

    private static DapBreakpointEvent WaitForBreakpointEvent(
        DapDebugSessionClient client,
        string sourcePath,
        TimeSpan timeout)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            var breakpointEvent = client.DrainDebugEvents()
                .BreakpointEvents
                .FirstOrDefault(candidate => PathsEqual(candidate.SourcePath, sourcePath));
            if (breakpointEvent is not null)
            {
                return breakpointEvent;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(25));
        }

        throw new TimeoutException("Timed out waiting for a DAP breakpoint event.");
    }

    private static JsonObject WaitForServerBreakpointEvent(
        ScriptLabJsonRpcServer server,
        string sourcePath,
        TimeSpan timeout)
    {
        var requestId = 1000;
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            var response = SendServerRequest(
                server,
                requestId++,
                "drainDebugEvents",
                new { });
            var breakpointEvents = response["result"]!["breakpointEvents"]!.AsArray();
            foreach (var breakpointEventNode in breakpointEvents)
            {
                var breakpointEvent = breakpointEventNode!.AsObject();
                if (PathsEqual(breakpointEvent["sourcePath"]?.GetValue<string>(), sourcePath))
                {
                    return breakpointEvent;
                }
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(25));
        }

        throw new TimeoutException("Timed out waiting for a JSON-RPC DAP breakpoint event.");
    }

    private static JsonObject WaitForServerStoppedEvent(
        ScriptLabJsonRpcServer server,
        TimeSpan timeout)
    {
        var requestId = 2000;
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            var response = SendServerRequest(
                server,
                requestId++,
                "drainDebugEvents",
                new { entityId = 101 });
            var result = response["result"]!.AsObject();
            if (result["stoppedEvent"] is not null)
            {
                return result;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(25));
        }

        throw new TimeoutException("Timed out waiting for a JSON-RPC DAP stopped event.");
    }

    private static JsonObject SendServerRequest(
        ScriptLabJsonRpcServer server,
        int id,
        string method,
        object parameters)
    {
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters
        });
        var response = JsonNode.Parse(server.HandleRequest(request))!.AsObject();
        if (response["error"] is JsonObject error)
        {
            throw new InvalidOperationException(
                error["message"]?.GetValue<string>() ?? "JSON-RPC request failed.");
        }

        return response;
    }

    private static void AddGeneratedAssemblyReference(
        string projectDirectory,
        string assemblyPath)
    {
        var projectPath = Directory
            .EnumerateFiles(projectDirectory, "*.csproj")
            .Single();
        var document = XDocument.Load(projectPath);
        document.Root!.Add(new XElement(
            "ItemGroup",
            new XElement(
                "Reference",
                new XAttribute("Include", Path.GetFileNameWithoutExtension(assemblyPath)),
                new XElement("HintPath", assemblyPath),
                new XElement("Private", "true"))));
        document.Save(projectPath);
    }

    private static string? ResolveCppCompilerPath()
    {
        var configured = Environment.GetEnvironmentVariable(CppCompilerEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return File.Exists(configured) ? configured : null;
        }

        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft Visual Studio",
                "2022",
                "Community",
                "VC",
                "Tools",
                "Llvm",
                "x64",
                "bin",
                "clang-cl.exe"),
            "clang-cl.exe"
        };

        return candidates.FirstOrDefault(path =>
            Path.IsPathFullyQualified(path) ? File.Exists(path) : CanStart(path));
    }

    private static string? ResolveHostFxrPath()
    {
        var runtimeDirectory = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        var dotnetRoot = runtimeDirectory.Parent?.Parent?.Parent;
        if (dotnetRoot is null)
        {
            return null;
        }

        var hostFxrRoot = Path.Combine(dotnetRoot.FullName, "host", "fxr");
        if (!Directory.Exists(hostFxrRoot))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(hostFxrRoot, "hostfxr.dll", SearchOption.AllDirectories)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static bool CanStart(string fileName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                ArgumentList = { "--version" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return false;
            }

            process.WaitForExit(milliseconds: 5000);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveEngineHostSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "Native",
                "ScriptLab.EngineHost",
                "ScriptLabEngineHost.cpp");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not locate Native/ScriptLab.EngineHost/ScriptLabEngineHost.cpp.");
    }

    private static string ResolveEngineHostIncludeDirectory()
    {
        return Path.GetDirectoryName(ResolveEngineHostSourcePath())
               ?? throw new InvalidOperationException("Could not resolve Native/ScriptLab.EngineHost include directory.");
    }

    private static string ResolveEngineHostContractHeaderPath()
    {
        var candidate = Path.Combine(
            ResolveEngineHostIncludeDirectory(),
            "scriptlab",
            "ScriptLabBridgeContract.h");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException("Could not locate Native/ScriptLab.EngineHost/scriptlab/ScriptLabBridgeContract.h.");
    }

    private static void CopyEngineHostContractHeaders(string includeDirectory)
    {
        var scriptlabIncludeDirectory = Path.Combine(includeDirectory, "scriptlab");
        Directory.CreateDirectory(scriptlabIncludeDirectory);
        File.Copy(
            ResolveEngineHostContractHeaderPath(),
            Path.Combine(scriptlabIncludeDirectory, "ScriptLabBridgeContract.h"),
            overwrite: true);
    }

    private static void WaitForFile(string path, TimeSpan timeout)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (File.Exists(path))
            {
                return;
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(25));
        }

        throw new TimeoutException($"Timed out waiting for file '{path}'.");
    }

    private static JsonObject CreateInitializeArguments()
    {
        return new JsonObject
        {
            ["adapterID"] = "netcoredbg",
            ["clientID"] = "scriptlab",
            ["clientName"] = "ScriptLab",
            ["pathFormat"] = "path",
            ["linesStartAt1"] = true,
            ["columnsStartAt1"] = true,
            ["supportsVariableType"] = true,
            ["supportsRunInTerminalRequest"] = false
        };
    }

    private static string ResolveDotnetPath()
    {
        var configured = Environment.GetEnvironmentVariable(DotnetEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            Assert.True(File.Exists(configured), $"dotnet executable does not exist: {configured}");
            return configured;
        }

        if (OperatingSystem.IsWindows())
        {
            var systemDotnet = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "dotnet",
                "dotnet.exe");
            if (File.Exists(systemDotnet))
            {
                return systemDotnet;
            }
        }

        return "dotnet";
    }

    private static string ResolveCurrentRuntimeDotnetPath()
    {
        var configured = Environment.GetEnvironmentVariable(DotnetEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            Assert.True(File.Exists(configured), $"dotnet executable does not exist: {configured}");
            return configured;
        }

        var runtimeDirectory = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        var dotnetRoot = runtimeDirectory.Parent?.Parent?.Parent;
        if (dotnetRoot is not null)
        {
            var dotnetPath = Path.Combine(
                dotnetRoot.FullName,
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(dotnetPath))
            {
                return dotnetPath;
            }
        }

        return ResolveDotnetPath();
    }

    private static void RunProcess(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        ResetDotnetSdkEnvironment(startInfo, fileName);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");
        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var standardOutput = standardOutputTask.GetAwaiter().GetResult();
        var standardError = standardErrorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process '{fileName}' failed with exit code {process.ExitCode}." +
                Environment.NewLine +
                standardOutput +
                standardError);
        }
    }

    private static Process StartProcess(
        string fileName,
        string workingDirectory,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        ResetDotnetSdkEnvironment(startInfo, fileName);

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start process '{fileName}'.");
    }

    private static void ResetDotnetSdkEnvironment(
        ProcessStartInfo startInfo,
        string fileName)
    {
        startInfo.Environment.Remove("DOTNET_HOST_PATH");
        startInfo.Environment.Remove("DOTNET_ROOT");
        startInfo.Environment.Remove("DOTNET_ROOT(x86)");
        startInfo.Environment.Remove("DOTNET_ROOT_X64");
        startInfo.Environment.Remove("DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR");
        startInfo.Environment.Remove("DOTNET_MSBUILD_SDK_RESOLVER_SDKS_VER");
        startInfo.Environment.Remove("MSBUILD_EXE_PATH");
        startInfo.Environment.Remove("MSBuildSDKsPath");

        if (Path.IsPathFullyQualified(fileName) &&
            string.Equals(Path.GetFileName(fileName), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(fileName)!;
        }
    }

    private static IReadOnlyDictionary<string, string> CreateDotnetAdapterEnvironment(string dotnetPath)
    {
        var dotnetDirectory = Path.GetDirectoryName(dotnetPath)
                              ?? throw new InvalidOperationException(
                                  $"Could not resolve dotnet directory from '{dotnetPath}'.");
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DOTNET_ROOT"] = dotnetDirectory,
            ["DOTNET_ROOT_X64"] = dotnetDirectory,
            ["PATH"] = dotnetDirectory + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
        };
    }

    private static bool PathsEqual(string? left, string right)
    {
        return !string.IsNullOrWhiteSpace(left) &&
               string.Equals(
                   Path.GetFullPath(left),
                   Path.GetFullPath(right),
                   OperatingSystem.IsWindows()
                       ? StringComparison.OrdinalIgnoreCase
                       : StringComparison.Ordinal);
    }

    private sealed record SmokeProgram(
        string SourcePath,
        string OutputDirectory,
        string ProgramPath);

    private static int FindLine(string text, string marker)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].Contains(marker, StringComparison.Ordinal))
            {
                return index + 1;
            }
        }

        throw new InvalidOperationException($"Could not find marker '{marker}' in generated source.");
    }

    private static string GetSamplePath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Samples", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate sample script '{fileName}'.");
    }

    private sealed record GeneratedScriptSmoke(
        DebugScriptEmitResult Emit,
        string HostSourcePath,
        int HostReadyLine,
        string HostOutputDirectory,
        string HostProgramPath);

    private sealed record GeneratedScriptAttachSmoke(
        DebugScriptEmitResult Emit,
        string HostOutputDirectory,
        string HostProgramPath,
        string ReadyPath,
        string GoPath);

    private sealed record NativeGeneratedScriptAttachSmoke(
        DebugScriptEmitResult Emit,
        string WorkingDirectory,
        string NativeHostPath,
        string BridgeAssemblyPath,
        string BridgeRuntimeConfigPath,
        string ReadyPath,
        string GoPath);

    private sealed record PackageFirstEngineSimulationSmoke(
        DebugScriptEmitResult Emit,
        string EngineRoot,
        string WorkingDirectory,
        string NativeHostPath,
        string BridgeAssemblyPath,
        string BridgeRuntimeConfigPath,
        string ReadyPath,
        string GoPath);
}
