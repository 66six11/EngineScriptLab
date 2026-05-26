#pragma once

#include <cstdint>

#if defined(_WIN32)
#define SCRIPTLAB_ENGINE_HOST_CALL __stdcall
#else
#define SCRIPTLAB_ENGINE_HOST_CALL
#endif

namespace scriptlab::engine_host
{
inline constexpr char host_kind_cpp_clr[] = "cppClr";
inline constexpr char bridge_manifest_file_name[] = "scriptlab.bridge.json";

inline constexpr char manifest_hostfxr_path[] = "hostfxrPath";
inline constexpr char manifest_runtime_config_path[] = "runtimeConfigPath";
inline constexpr char manifest_assembly_path[] = "assemblyPath";
inline constexpr char manifest_type_name[] = "typeName";
inline constexpr char manifest_prepare_method[] = "prepareMethod";
inline constexpr char manifest_entry_method[] = "entryMethod";

inline constexpr char manifest_generated_assembly_path[] = "generatedAssemblyPath";
inline constexpr char manifest_pdb_path[] = "pdbPath";
inline constexpr char manifest_debug_map_path[] = "debugMapPath";
inline constexpr char manifest_source_document_path[] = "sourceDocumentPath";

enum class exit_code : int
{
    ok = 0,
    invalid_command_line = 2,
    hostfxr_load_failed = 3,
    hostfxr_exports_missing = 4,
    runtime_config_initialization_failed = 5,
    load_assembly_delegate_unavailable = 6,
    prepare_method_unresolved = 7,
    prepare_method_failed = 8,
    entry_method_unresolved = 9,
    bridge_manifest_invalid = 10
};

using bridge_component_entry_point_fn = int(SCRIPTLAB_ENGINE_HOST_CALL*)(void*, std::int32_t);
}
