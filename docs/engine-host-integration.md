# ScriptLab Engine Host Integration

This guide describes the currently verified path for attaching ScriptLab to a native C++ engine host that owns CoreCLR through `hostfxr`.

The goal is not to define an editor UI. The goal is to give an engine app or package-first sample a repeatable backend sequence:

```text
loadGraph / emit debug files
-> write scriptlab.bridge.json
-> validateDebugHost
-> start native C++ CLR host
-> registerDebugHost
-> attachDebugHost
-> drainDebugEvents
-> readVariables
-> continue
-> waitDebugHostExit
-> disconnectDebugHost
```

The path above is covered by `DapNetcoredbgSmokeTests`, including the package-first JSON-RPC smoke that uses a native host process, `netcoredbg`, `validateDebugHost`, `registerDebugHost`, and `attachDebugHost`.

## Native Contract

The native side uses the minimal host in `Native/ScriptLab.EngineHost`:

```text
ScriptLabEngineHost.exe <bridge-manifest.json> <ready-file> <go-file>
```

The host:

- loads `hostfxr`
- initializes CoreCLR from the bridge runtimeconfig
- loads the managed bridge assembly
- calls the configured prepare method
- writes the ready file
- waits for the go file
- calls the configured entry method

Real engine packages should copy `Native/ScriptLab.EngineHost/scriptlab/ScriptLabBridgeContract.h` into their bridge/package include surface rather than duplicating manifest field names.

The managed bridge entry points must use this native-callable shape:

```cpp
int SCRIPTLAB_ENGINE_HOST_CALL Method(void* args, std::int32_t size);
```

In C#, the exported static methods used by the smoke tests have this shape:

```csharp
public static int Prepare(IntPtr args, int size);
public static int Entry(IntPtr args, int size);
```

## Bridge Manifest

`scriptlab.bridge.json` is a flat JSON object. Required runtime fields:

```json
{
  "hostfxrPath": "C:\\Users\\C66\\.dotnet\\host\\fxr\\10.0.8\\hostfxr.dll",
  "runtimeConfigPath": "D:\\Game\\packages\\scriptlab-dotnet-bridge\\bridge\\bin\\Debug\\net10.0\\bridge.runtimeconfig.json",
  "assemblyPath": "D:\\Game\\packages\\scriptlab-dotnet-bridge\\bridge\\bin\\Debug\\net10.0\\bridge.dll",
  "typeName": "ScriptLab.Tests.NativeHost.NativeHostBridge, bridge",
  "prepareMethod": "Prepare",
  "entryMethod": "Entry"
}
```

Recommended diagnostic fields:

```json
{
  "generatedAssemblyPath": "D:\\Game\\packages\\script-runtime\\generated\\PlayerMove.dll",
  "pdbPath": "D:\\Game\\packages\\script-runtime\\generated\\PlayerMove.pdb",
  "debugMapPath": "D:\\Game\\packages\\script-runtime\\generated\\PlayerMove.debugmap.json",
  "sourceDocumentPath": "D:\\Game\\Scripts\\PlayerMove.ash.cs"
}
```

The native host ignores the diagnostic fields. ScriptLab's JSON-RPC server validates them when present. That is intentional: the engine host only needs enough data to start CoreCLR, while ScriptLab needs enough data to catch stale or mismatched debug builds before attaching.

## Server Startup

Start the ScriptLab JSON-RPC server with a real DAP adapter:

```powershell
$env:SCRIPTLAB_DAP_ADAPTER="C:\Users\C66\AppData\Local\Microsoft\WinGet\Packages\Samsung.NetCoreDbg_Microsoft.Winget.Source_8wekyb3d8bbwe\netcoredbg\netcoredbg.exe"
$env:SCRIPTLAB_DOTNET="C:\Program Files\JetBrains\Rider\r2r\2026.1.1R\33CE8650EFF983231DE196E2331D5D3\windows-x64\dotnet\dotnet.exe"
$env:SCRIPTLAB_CPP_COMPILER="C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\Llvm\x64\bin\clang-cl.exe"

& $env:SCRIPTLAB_DOTNET run --project ScriptLab.csproj -- server --dap-adapter=$env:SCRIPTLAB_DAP_ADAPTER
```

The server reads line-delimited JSON-RPC from stdin and writes one JSON-RPC response per line to stdout.

For local VkEngine-style smoke runs, generate the managed bridge project and `scriptlab.bridge.json` first:

```powershell
scripts\New-ScriptLabEngineHostBridge.ps1 `
  -ScriptPath D:\Game\Scripts\PlayerMove.ash.cs `
  -OutputDirectory D:\Game\packages\script-runtime\generated `
  -BridgeDirectory D:\Game\packages\scriptlab-dotnet-bridge\bridge `
  -BridgeManifestPath D:\Game\apps\sample-viewer\scriptlab.bridge.json
```

The generated bridge is a small debug smoke adapter. It references the current ScriptLab debug assembly,
loads the generated behavior in `Prepare`, then creates entity `101`, presses `Key.W`, and ticks once in
`Entry`. The script prints the bridge project path, manifest path, and suggested ready/go file paths.

For build scripts or local preflight checks that do not need a long-running server, use the CLI wrapper over the same validation path:

```powershell
& $env:SCRIPTLAB_DOTNET run --project ScriptLab.csproj -- validate-host `
  D:\Game\Scripts\PlayerMove.ash.cs `
  D:\Game\packages\script-runtime\generated `
  D:\Game\apps\sample-viewer\scriptlab.bridge.json `
  --engine-root=D:\Game
```

Add `--json` to emit the raw validation result. A non-zero exit code means the bridge manifest failed the same checks used by `validateDebugHost`, `registerDebugHost`, and `attachDebugHost`.

For the full interactive attach flow, prefer the runner script. It builds ScriptLab, starts the JSON-RPC server from the built assembly, loads the script, sets the source breakpoint, validates the manifest, asks for the ready host process id, attaches, optionally writes the go file, drains the stopped event, reads variables, and disconnects:

```powershell
scripts\Invoke-ScriptLabEngineHostDebug.ps1 `
  -BridgeManifestPath D:\Game\apps\sample-viewer\scriptlab.bridge.json `
  -EnginePackageRoot D:\Game `
  -ScriptPath D:\Game\Scripts\PlayerMove.ash.cs `
  -OutputDirectory D:\Game\packages\script-runtime\generated `
  -BreakpointLine 14 `
  -BreakpointColumn 13 `
  -GoFilePath D:\Game\apps\sample-viewer\scriptlab.go
```

If the engine uses a different ready/go mechanism, omit `-GoFilePath`; the script pauses after attach and asks you to release the host manually.

For hosts that expose the `--scriptlab-host` command line, the runner can also rebuild a bridge project
after `loadGraph`, start the host, wait for its ready file, attach, write the go file, continue, and wait for
the host to exit:

```powershell
scripts\Invoke-ScriptLabEngineHostDebug.ps1 `
  -BridgeProjectPath D:\Game\packages\scriptlab-dotnet-bridge\bridge\bridge.csproj `
  -BridgeManifestPath D:\Game\apps\sample-viewer\scriptlab.bridge.json `
  -EngineHostPath D:\TechArt\VkEngine\build\cmake\msvc-debug\apps\sample-viewer\asharia-sample-viewer.exe `
  -ReadyFilePath D:\Game\apps\sample-viewer\scriptlab.ready `
  -GoFilePath D:\Game\apps\sample-viewer\scriptlab.go `
  -EnginePackageRoot D:\TechArt\VkEngine `
  -ScriptPath D:\Game\Scripts\PlayerMove.ash.cs `
  -OutputDirectory D:\Game\packages\script-runtime\generated `
  -ContinueAfterStop `
  -WaitForExit
```

## JSON-RPC Sequence

Use `loadGraph` to compile the current script into debug emit files:

```json
{"jsonrpc":"2.0","id":1,"method":"loadGraph","params":{"scriptPath":"D:\\Game\\Scripts\\PlayerMove.ash.cs","outputDirectory":"D:\\Game\\packages\\script-runtime\\generated"}}
```

Set source breakpoints before attaching:

```json
{"jsonrpc":"2.0","id":2,"method":"setSourceBreakpoints","params":{"sourcePath":"D:\\Game\\Scripts\\PlayerMove.ash.cs","breakpoints":[{"line":14,"column":13}]}}
```

Validate the bridge manifest before starting or attaching the native host:

```json
{"jsonrpc":"2.0","id":3,"method":"validateDebugHost","params":{"hostKind":"cppClr","bridgeManifestPath":"D:\\Game\\apps\\sample-viewer\\scriptlab.bridge.json","enginePackageRoot":"D:\\Game"}}
```

`validateDebugHost` does not register a process and does not attach the debugger. It validates:

- `hostKind` is `cppClr`
- the manifest exists and is valid JSON
- required string fields are present and non-empty
- `hostfxrPath`, `runtimeConfigPath`, and `assemblyPath` point to existing files
- optional diagnostic file fields point to existing files when present
- optional diagnostic paths match the current ScriptLab emit when present

After the native host is running and has written its ready file, register the process:

```json
{"jsonrpc":"2.0","id":4,"method":"registerDebugHost","params":{"processId":4242,"hostKind":"cppClr","bridgeManifestPath":"D:\\Game\\apps\\sample-viewer\\scriptlab.bridge.json","enginePackageRoot":"D:\\Game"}}
```

Attach through the configured DAP adapter:

```json
{"jsonrpc":"2.0","id":5,"method":"attachDebugHost","params":{}}
```

Poll debug events. Use `timeoutMilliseconds` for long-poll behavior and `afterEventSequence` to replay recent cached event batches:

```json
{"jsonrpc":"2.0","id":6,"method":"drainDebugEvents","params":{"timeoutMilliseconds":5000,"entityId":101}}
```

When stopped, read variables using the returned `debugStateId`:

```json
{"jsonrpc":"2.0","id":7,"method":"readVariables","params":{"debugStateId":1}}
```

Continue the stopped thread:

```json
{"jsonrpc":"2.0","id":8,"method":"continue","params":{"threadId":11}}
```

Wait for the host process to exit without taking ownership of it:

```json
{"jsonrpc":"2.0","id":9,"method":"waitDebugHostExit","params":{"timeoutMilliseconds":5000}}
```

Disconnect and clear server-side attach state:

```json
{"jsonrpc":"2.0","id":10,"method":"disconnectDebugHost","params":{"terminateDebuggee":false}}
```

## Expected Success Shape

A successful `validateDebugHost` response includes the current emit paths:

```json
{
  "status": "valid",
  "backend": "dap",
  "hostKind": "cppClr",
  "reason": "Bridge manifest is valid for the current ScriptLab debug emit.",
  "bridgeManifestPath": "D:\\Game\\apps\\sample-viewer\\scriptlab.bridge.json",
  "enginePackageRoot": "D:\\Game",
  "generatedAssemblyPath": "D:\\Game\\packages\\script-runtime\\generated\\PlayerMove.dll",
  "pdbPath": "D:\\Game\\packages\\script-runtime\\generated\\PlayerMove.pdb",
  "debugMapPath": "D:\\Game\\packages\\script-runtime\\generated\\PlayerMove.debugmap.json",
  "sourceDocumentPath": "D:\\Game\\Scripts\\PlayerMove.ash.cs"
}
```

A successful `attachDebugHost` response includes:

- `status: "attached"`
- `backend: "dap"`
- `lifecycle.phase: "attached"`
- `attachTarget.processId`
- `attachTarget.processStartTimeUtc` when readable
- `attachTarget.bridgeManifestPath`
- `attachTarget.enginePackageRoot`

## Common Failures

Manifest file does not exist:

```text
Bridge manifest file does not exist: ...
```

Required field is missing or not a string:

```text
Bridge manifest '...' is missing required string field 'entryMethod'.
```

Required runtime file is missing:

```text
Bridge manifest '...' field 'assemblyPath' points to a missing file: ...
```

Optional diagnostic path is stale:

```text
Bridge manifest '...' field 'generatedAssemblyPath' does not match the current generated assembly path: ...
```

DAP adapter is not configured:

```text
C++ CLR host attach requires the DAP backend path, which is not connected yet.
```

System `dotnet` does not match the smoke target framework:

```text
Set SCRIPTLAB_DOTNET to the dotnet executable that owns the target runtime.
```

For the current local setup, use Rider's bundled .NET 10 SDK at `C:\Program Files\JetBrains\Rider\r2r\2026.1.1R\33CE8650EFF983231DE196E2331D5D3\windows-x64\dotnet\dotnet.exe`.

## Verification Commands

Run the regular test suite:

```powershell
$env:SCRIPTLAB_DOTNET="C:\Program Files\JetBrains\Rider\r2r\2026.1.1R\33CE8650EFF983231DE196E2331D5D3\windows-x64\dotnet\dotnet.exe"
& $env:SCRIPTLAB_DOTNET test ScriptLab.sln
```

Run the real DAP/native host smoke suite:

```powershell
$env:SCRIPTLAB_DAP_ADAPTER="C:\Users\C66\AppData\Local\Microsoft\WinGet\Packages\Samsung.NetCoreDbg_Microsoft.Winget.Source_8wekyb3d8bbwe\netcoredbg\netcoredbg.exe"
$env:SCRIPTLAB_DOTNET="C:\Program Files\JetBrains\Rider\r2r\2026.1.1R\33CE8650EFF983231DE196E2331D5D3\windows-x64\dotnet\dotnet.exe"
$env:SCRIPTLAB_CPP_COMPILER="C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\Llvm\x64\bin\clang-cl.exe"

& $env:SCRIPTLAB_DOTNET test ScriptLab.Tests\ScriptLab.Tests.csproj --filter DapNetcoredbgSmokeTests
```

The package-first JSON-RPC smoke is the best current reference for the full flow:

```text
DapNetcoredbgSmokeTests.Attach_WhenJsonRpcServerUsesPackageFirstEngineSimulation_HitsGeneratedScriptSourceBreakpoint
```
