cmake_minimum_required(VERSION 3.20)
project(MW3ModRuntime CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_library(mw3mod SHARED
    src/dllmain.cpp
    src/dvar.cpp
    src/log.cpp
    src/settings.cpp
    src/proxy.cpp
)

target_compile_definitions(mw3mod PRIVATE
    WIN32_LEAN_AND_MEAN
    NOMINMAX
    UNICODE
    _UNICODE
)

set_target_properties(mw3mod PROPERTIES OUTPUT_NAME "MW3Mod")

target_link_libraries(mw3mod PRIVATE kernel32 user32)

if(MSVC)
    target_compile_options(mw3mod PRIVATE /W4 /permissive- /EHsc)
endif()
