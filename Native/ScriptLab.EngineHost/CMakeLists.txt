cmake_minimum_required(VERSION 3.20)

project(ScriptLabEngineHost LANGUAGES CXX)

add_executable(ScriptLabEngineHost ScriptLabEngineHost.cpp)
target_compile_features(ScriptLabEngineHost PRIVATE cxx_std_17)
target_include_directories(ScriptLabEngineHost PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
