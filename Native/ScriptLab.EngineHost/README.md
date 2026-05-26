# ScriptLab Engine Host

This is the minimal native C++ host used by ScriptLab smoke tests to exercise the real mixed native/.NET attach path.

The host owns the process, initializes CoreCLR through `hostfxr`, loads a managed bridge assembly, calls the configured prepare method, writes a ready file, waits for a go file, then calls the configured entry method. Tests attach `netcoredbg` to this native process and set breakpoints in the generated ScriptLab source document.

`scriptlab/ScriptLabBridgeContract.h` is the native-side contract header. It names the stable manifest fields, the `cppClr` host kind, the bridge component entry-point signature, and the host exit codes. Real engine packages should copy that header into their bridge/package include surface instead of duplicating field names in app code.

The preferred command line is:

```text
ScriptLabEngineHost.exe <bridge-manifest.json> <ready-file> <go-file>
```

The manifest is a flat JSON object:

```json
{
  "hostfxrPath": "C:\\Program Files\\dotnet\\host\\fxr\\...\\hostfxr.dll",
  "runtimeConfigPath": "D:\\...\\bridge.runtimeconfig.json",
  "assemblyPath": "D:\\...\\bridge.dll",
  "typeName": "ScriptLab.Tests.NativeHost.NativeHostBridge, bridge",
  "prepareMethod": "Prepare",
  "entryMethod": "Entry"
}
```

Required runtime fields:

- `hostfxrPath`: native hostfxr DLL used to initialize CoreCLR.
- `runtimeConfigPath`: runtimeconfig for the managed bridge assembly.
- `assemblyPath`: managed bridge assembly loaded by `hostfxr`.
- `typeName`: fully qualified managed type name, including assembly name.
- `prepareMethod`: static method called before the host signals ready.
- `entryMethod`: static method called after the go file appears.

Optional diagnostic fields may be present and are ignored by the native host, including `generatedAssemblyPath`, `pdbPath`, `debugMapPath`, and `sourceDocumentPath`.

The configured prepare and entry methods must match the native component entry-point signature from the contract header:

```cpp
int SCRIPTLAB_ENGINE_HOST_CALL Method(void* args, std::int32_t size);
```

The legacy command line remains available for tests and debugging:

```text
ScriptLabEngineHost.exe <hostfxr.dll> <bridge.runtimeconfig.json> <bridge.dll> <type-name> <prepare-method> <entry-method> <ready-file> <go-file>
```

Exit codes:

- `2`: invalid command line.
- `3`: `hostfxr.dll` could not be loaded.
- `4`: required hostfxr exports were missing.
- `5`: runtime config initialization failed.
- `6`: `load_assembly_and_get_function_pointer` delegate was unavailable.
- `7`: prepare method could not be resolved.
- `8`: prepare method returned failure.
- `9`: entry method could not be resolved.
- `10`: bridge manifest could not be read or was missing required fields.

Keep this host intentionally small. Engine package layout, scene/world behavior, and script system simulation should live in the managed bridge fixture until the real engine app is ready to replace it.
