#include <windows.h>

#include <cstdint>
#include <fstream>
#include <string>

#include "scriptlab/ScriptLabBridgeContract.h"

namespace contract = scriptlab::engine_host;

enum hostfxr_delegate_type
{
    hdt_com_activation,
    hdt_load_in_memory_assembly,
    hdt_winrt_activation,
    hdt_com_register,
    hdt_com_unregister,
    hdt_load_assembly_and_get_function_pointer
};

using hostfxr_handle = void*;
using hostfxr_initialize_for_runtime_config_fn = int32_t(__cdecl*)(
    const wchar_t*,
    const void*,
    hostfxr_handle*);
using hostfxr_get_runtime_delegate_fn = int32_t(__cdecl*)(
    const hostfxr_handle,
    hostfxr_delegate_type,
    void**);
using hostfxr_close_fn = int32_t(__cdecl*)(const hostfxr_handle);
using load_assembly_and_get_function_pointer_fn = int(__stdcall*)(
    const wchar_t*,
    const wchar_t*,
    const wchar_t*,
    const wchar_t*,
    void*,
    void**);
using component_entry_point_fn = contract::bridge_component_entry_point_fn;

struct host_configuration
{
    std::wstring hostfxr_path;
    std::wstring runtime_config_path;
    std::wstring bridge_assembly_path;
    std::wstring type_name;
    std::wstring prepare_method;
    std::wstring entry_method;
    std::wstring ready_path;
    std::wstring go_path;
};

static bool file_exists(const wchar_t* path)
{
    return GetFileAttributesW(path) != INVALID_FILE_ATTRIBUTES;
}

static bool is_json_space(char value)
{
    return value == ' ' || value == '\t' || value == '\r' || value == '\n';
}

static int hex_value(char value)
{
    if (value >= '0' && value <= '9')
    {
        return value - '0';
    }

    if (value >= 'a' && value <= 'f')
    {
        return value - 'a' + 10;
    }

    if (value >= 'A' && value <= 'F')
    {
        return value - 'A' + 10;
    }

    return -1;
}

static bool parse_hex_quad(
    const std::string& text,
    size_t index,
    uint32_t* code_point)
{
    uint32_t value = 0;
    for (size_t offset = 0; offset < 4; ++offset)
    {
        const int digit = index + offset < text.size()
            ? hex_value(text[index + offset])
            : -1;
        if (digit < 0)
        {
            return false;
        }

        value = (value << 4) | static_cast<uint32_t>(digit);
    }

    *code_point = value;
    return true;
}

static void append_utf8(uint32_t code_point, std::string* output)
{
    if (code_point <= 0x7f)
    {
        output->push_back(static_cast<char>(code_point));
        return;
    }

    if (code_point <= 0x7ff)
    {
        output->push_back(static_cast<char>(0xc0 | (code_point >> 6)));
        output->push_back(static_cast<char>(0x80 | (code_point & 0x3f)));
        return;
    }

    if (code_point <= 0xffff)
    {
        output->push_back(static_cast<char>(0xe0 | (code_point >> 12)));
        output->push_back(static_cast<char>(0x80 | ((code_point >> 6) & 0x3f)));
        output->push_back(static_cast<char>(0x80 | (code_point & 0x3f)));
        return;
    }

    output->push_back(static_cast<char>(0xf0 | (code_point >> 18)));
    output->push_back(static_cast<char>(0x80 | ((code_point >> 12) & 0x3f)));
    output->push_back(static_cast<char>(0x80 | ((code_point >> 6) & 0x3f)));
    output->push_back(static_cast<char>(0x80 | (code_point & 0x3f)));
}

static bool parse_json_string(
    const std::string& text,
    size_t* index,
    std::string* value)
{
    if (*index >= text.size() || text[*index] != '"')
    {
        return false;
    }

    ++(*index);
    while (*index < text.size())
    {
        const char next = text[(*index)++];
        if (next == '"')
        {
            return true;
        }

        if (next != '\\')
        {
            value->push_back(next);
            continue;
        }

        if (*index >= text.size())
        {
            return false;
        }

        const char escape = text[(*index)++];
        switch (escape)
        {
        case '"':
        case '\\':
        case '/':
            value->push_back(escape);
            break;
        case 'b':
            value->push_back('\b');
            break;
        case 'f':
            value->push_back('\f');
            break;
        case 'n':
            value->push_back('\n');
            break;
        case 'r':
            value->push_back('\r');
            break;
        case 't':
            value->push_back('\t');
            break;
        case 'u':
        {
            uint32_t code_point = 0;
            if (!parse_hex_quad(text, *index, &code_point))
            {
                return false;
            }

            *index += 4;
            if (code_point >= 0xd800 && code_point <= 0xdbff)
            {
                if (*index + 6 > text.size() ||
                    text[*index] != '\\' ||
                    text[*index + 1] != 'u')
                {
                    return false;
                }

                uint32_t low_surrogate = 0;
                if (!parse_hex_quad(text, *index + 2, &low_surrogate) ||
                    low_surrogate < 0xdc00 ||
                    low_surrogate > 0xdfff)
                {
                    return false;
                }

                *index += 6;
                code_point = 0x10000 +
                    (((code_point - 0xd800) << 10) | (low_surrogate - 0xdc00));
            }

            append_utf8(code_point, value);
            break;
        }
        default:
            return false;
        }
    }

    return false;
}

static bool find_json_string_value(
    const std::string& text,
    const char* property_name,
    std::string* value)
{
    const std::string quoted_name = std::string("\"") + property_name + "\"";
    const size_t name_index = text.find(quoted_name);
    if (name_index == std::string::npos)
    {
        return false;
    }

    size_t index = name_index + quoted_name.size();
    while (index < text.size() && is_json_space(text[index]))
    {
        ++index;
    }

    if (index >= text.size() || text[index] != ':')
    {
        return false;
    }

    ++index;
    while (index < text.size() && is_json_space(text[index]))
    {
        ++index;
    }

    return parse_json_string(text, &index, value);
}

static bool utf8_to_wide(const std::string& value, std::wstring* wide)
{
    if (value.empty())
    {
        wide->clear();
        return true;
    }

    const int required = MultiByteToWideChar(
        CP_UTF8,
        MB_ERR_INVALID_CHARS,
        value.data(),
        static_cast<int>(value.size()),
        nullptr,
        0);
    if (required <= 0)
    {
        return false;
    }

    wide->assign(static_cast<size_t>(required), L'\0');
    return MultiByteToWideChar(
        CP_UTF8,
        MB_ERR_INVALID_CHARS,
        value.data(),
        static_cast<int>(value.size()),
        &(*wide)[0],
        required) == required;
}

static bool read_utf8_file(const wchar_t* path, std::string* text)
{
    HANDLE file = CreateFileW(
        path,
        GENERIC_READ,
        FILE_SHARE_READ,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr);
    if (file == INVALID_HANDLE_VALUE)
    {
        return false;
    }

    LARGE_INTEGER file_size{};
    if (!GetFileSizeEx(file, &file_size) || file_size.QuadPart < 0 || file_size.QuadPart > 1024 * 1024)
    {
        CloseHandle(file);
        return false;
    }

    text->assign(static_cast<size_t>(file_size.QuadPart), '\0');
    DWORD bytes_read = 0;
    const BOOL read_result = ReadFile(
        file,
        text->empty() ? nullptr : &(*text)[0],
        static_cast<DWORD>(text->size()),
        &bytes_read,
        nullptr);
    CloseHandle(file);
    if (!read_result || bytes_read != text->size())
    {
        return false;
    }

    return true;
}

static bool read_manifest_field(
    const std::string& manifest,
    const char* property_name,
    std::wstring* value)
{
    std::string utf8_value;
    return find_json_string_value(manifest, property_name, &utf8_value) &&
        utf8_to_wide(utf8_value, value) &&
        !value->empty();
}

static bool load_manifest_configuration(
    const wchar_t* manifest_path,
    const wchar_t* ready_path,
    const wchar_t* go_path,
    host_configuration* configuration)
{
    std::string manifest;
    if (!read_utf8_file(manifest_path, &manifest))
    {
        return false;
    }

    configuration->ready_path = ready_path;
    configuration->go_path = go_path;
    return read_manifest_field(manifest, contract::manifest_hostfxr_path, &configuration->hostfxr_path) &&
        read_manifest_field(manifest, contract::manifest_runtime_config_path, &configuration->runtime_config_path) &&
        read_manifest_field(manifest, contract::manifest_assembly_path, &configuration->bridge_assembly_path) &&
        read_manifest_field(manifest, contract::manifest_type_name, &configuration->type_name) &&
        read_manifest_field(manifest, contract::manifest_prepare_method, &configuration->prepare_method) &&
        read_manifest_field(manifest, contract::manifest_entry_method, &configuration->entry_method);
}

static void load_legacy_configuration(
    wchar_t** argv,
    host_configuration* configuration)
{
    configuration->hostfxr_path = argv[1];
    configuration->runtime_config_path = argv[2];
    configuration->bridge_assembly_path = argv[3];
    configuration->type_name = argv[4];
    configuration->prepare_method = argv[5];
    configuration->entry_method = argv[6];
    configuration->ready_path = argv[7];
    configuration->go_path = argv[8];
}

static void write_ready_file(const wchar_t* path)
{
    std::ofstream ready(path, std::ios::binary);
    ready << "ready";
}

static int load_component_entry(
    load_assembly_and_get_function_pointer_fn load_assembly,
    const wchar_t* bridge_assembly_path,
    const wchar_t* type_name,
    const wchar_t* method_name,
    component_entry_point_fn* entry_point)
{
    void* entry_ptr = nullptr;
    const auto result = load_assembly(
        bridge_assembly_path,
        type_name,
        method_name,
        nullptr,
        nullptr,
        &entry_ptr);
    if (result < 0 || entry_ptr == nullptr)
    {
        return result < 0 ? result : -1;
    }

    *entry_point = reinterpret_cast<component_entry_point_fn>(entry_ptr);
    return 0;
}

int wmain(int argc, wchar_t** argv)
{
    host_configuration configuration{};
    if (argc == 4)
    {
        if (!load_manifest_configuration(argv[1], argv[2], argv[3], &configuration))
        {
            return static_cast<int>(contract::exit_code::bridge_manifest_invalid);
        }
    }
    else if (argc >= 9)
    {
        load_legacy_configuration(argv, &configuration);
    }
    else
    {
        return static_cast<int>(contract::exit_code::invalid_command_line);
    }

    HMODULE hostfxr = LoadLibraryW(configuration.hostfxr_path.c_str());
    if (hostfxr == nullptr)
    {
        return static_cast<int>(contract::exit_code::hostfxr_load_failed);
    }

    auto initialize = reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(
        GetProcAddress(hostfxr, "hostfxr_initialize_for_runtime_config"));
    auto get_delegate = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(
        GetProcAddress(hostfxr, "hostfxr_get_runtime_delegate"));
    auto close = reinterpret_cast<hostfxr_close_fn>(
        GetProcAddress(hostfxr, "hostfxr_close"));
    if (initialize == nullptr || get_delegate == nullptr || close == nullptr)
    {
        return static_cast<int>(contract::exit_code::hostfxr_exports_missing);
    }

    hostfxr_handle context = nullptr;
    int32_t result = initialize(configuration.runtime_config_path.c_str(), nullptr, &context);
    if (result < 0 || context == nullptr)
    {
        return static_cast<int>(contract::exit_code::runtime_config_initialization_failed);
    }

    void* load_assembly_ptr = nullptr;
    result = get_delegate(
        context,
        hdt_load_assembly_and_get_function_pointer,
        &load_assembly_ptr);
    close(context);
    if (result < 0 || load_assembly_ptr == nullptr)
    {
        return static_cast<int>(contract::exit_code::load_assembly_delegate_unavailable);
    }

    auto load_assembly = reinterpret_cast<load_assembly_and_get_function_pointer_fn>(
        load_assembly_ptr);
    component_entry_point_fn prepare = nullptr;
    result = load_component_entry(
        load_assembly,
        configuration.bridge_assembly_path.c_str(),
        configuration.type_name.c_str(),
        configuration.prepare_method.c_str(),
        &prepare);
    if (result != 0 || prepare == nullptr)
    {
        return static_cast<int>(contract::exit_code::prepare_method_unresolved);
    }

    result = prepare(nullptr, 0);
    if (result != 0)
    {
        return static_cast<int>(contract::exit_code::prepare_method_failed);
    }

    component_entry_point_fn entry = nullptr;
    result = load_component_entry(
        load_assembly,
        configuration.bridge_assembly_path.c_str(),
        configuration.type_name.c_str(),
        configuration.entry_method.c_str(),
        &entry);
    if (result != 0 || entry == nullptr)
    {
        return static_cast<int>(contract::exit_code::entry_method_unresolved);
    }

    write_ready_file(configuration.ready_path.c_str());
    while (!file_exists(configuration.go_path.c_str()))
    {
        Sleep(25);
    }

    return entry(nullptr, 0);
}
