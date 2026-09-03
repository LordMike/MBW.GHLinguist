cmake_minimum_required(VERSION 3.20)

project(MBW.GHLinguist.Native VERSION 0.1.0 LANGUAGES CXX)

set(GHL_RUBY_ROOT "" CACHE PATH "RubyInstaller root used to embed CRuby (for example C:/Ruby40-x64)")
option(GHL_ENABLE_RUBY_EMBEDDING "Build the CRuby embedding runtime" OFF)
option(GHL_BUILD_SMOKE "Build the native runtime smoke executable" OFF)
set(GHL_SMOKE_ASSET_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../../extern/linguist" CACHE PATH "Full GitHub Linguist asset root used by the runtime smoke test")
set(GHL_BUNDLED_BRIDGE "${CMAKE_CURRENT_SOURCE_DIR}/ruby/ghlinguist/bridge.rb")

if(NOT EXISTS "${GHL_BUNDLED_BRIDGE}")
    message(FATAL_ERROR "The bundled GHLinguist Ruby bridge is missing: ${GHL_BUNDLED_BRIDGE}")
endif()

if(GHL_RUBY_ROOT)
    set(GHL_ENABLE_RUBY_EMBEDDING ON)
endif()

if(GHL_ENABLE_RUBY_EMBEDDING AND NOT WIN32)
    message(FATAL_ERROR "CRuby embedding is currently implemented only for Windows. Configure without GHL_ENABLE_RUBY_EMBEDDING on this platform.")
endif()

if(GHL_ENABLE_RUBY_EMBEDDING AND MSVC)
    message(FATAL_ERROR "RubyInstaller's x64-ucrt headers require its UCRT MinGW toolchain. Configure with a MinGW C++ compiler, not MSVC.")
endif()

add_library(ghlinguist SHARED
    src/ghlinguist.cpp)

target_compile_features(ghlinguist PRIVATE cxx_std_17)
target_include_directories(ghlinguist
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>)

target_compile_definitions(ghlinguist PRIVATE GHLINGUIST_BUILDING_LIBRARY=1)

if(GHL_ENABLE_RUBY_EMBEDDING)
    if(NOT GHL_RUBY_ROOT)
        message(FATAL_ERROR "GHL_ENABLE_RUBY_EMBEDDING requires -DGHL_RUBY_ROOT=<RubyInstaller root>.")
    endif()

    set(GHL_RUBY_INCLUDE_DIR "${GHL_RUBY_ROOT}/include/ruby-4.0.0")
    set(GHL_RUBY_ARCH_INCLUDE_DIR "${GHL_RUBY_INCLUDE_DIR}/x64-mingw-ucrt")
    find_file(GHL_RUBY_IMPORT_LIBRARY
        NAMES x64-ucrt-ruby400.lib libx64-ucrt-ruby400.dll.a
        PATHS "${GHL_RUBY_ROOT}/lib"
        NO_DEFAULT_PATH)

    if(NOT EXISTS "${GHL_RUBY_INCLUDE_DIR}/ruby.h" OR NOT EXISTS "${GHL_RUBY_ARCH_INCLUDE_DIR}/ruby/config.h" OR NOT GHL_RUBY_IMPORT_LIBRARY)
        message(FATAL_ERROR "GHL_RUBY_ROOT must contain RubyInstaller 4.0 x64-ucrt headers and the x64-ucrt-ruby400 import library: ${GHL_RUBY_ROOT}")
    endif()

    target_include_directories(ghlinguist SYSTEM PRIVATE
        "${GHL_RUBY_INCLUDE_DIR}"
        "${GHL_RUBY_ARCH_INCLUDE_DIR}")
    target_link_libraries(ghlinguist PRIVATE "${GHL_RUBY_IMPORT_LIBRARY}")
    target_compile_definitions(ghlinguist PRIVATE GHL_RUBY_EMBEDDING=1)
    message(STATUS "CRuby embedding enabled from ${GHL_RUBY_ROOT}")
else()
    message(STATUS "CRuby embedding disabled; building native ABI stubs only")
endif()

if(MSVC)
    target_compile_options(ghlinguist PRIVATE /W4 /permissive-)
else()
    target_compile_options(ghlinguist PRIVATE -Wall -Wextra -Wpedantic)
    set_target_properties(ghlinguist PROPERTIES
        CXX_VISIBILITY_PRESET hidden
        VISIBILITY_INLINES_HIDDEN YES)
endif()

set_target_properties(ghlinguist PROPERTIES
    OUTPUT_NAME ghlinguist
    WINDOWS_EXPORT_ALL_SYMBOLS OFF)

if(GHL_BUILD_SMOKE)
    add_executable(ghlinguist_smoke tests/runtime_smoke.cpp)
    target_compile_features(ghlinguist_smoke PRIVATE cxx_std_17)
    target_link_libraries(ghlinguist_smoke PRIVATE ghlinguist)

    if(GHL_ENABLE_RUBY_EMBEDDING)
        include(CTest)
        if(BUILD_TESTING)
            add_test(NAME ghlinguist.runtime-smoke
                COMMAND ghlinguist_smoke "${GHL_SMOKE_ASSET_ROOT}")
            set_tests_properties(ghlinguist.runtime-smoke PROPERTIES
                ENVIRONMENT "PATH=${GHL_RUBY_ROOT}/bin\\;${GHL_RUBY_ROOT}/msys64/ucrt64/bin\\;$ENV{PATH}")
        endif()
    endif()
endif()

include(GNUInstallDirs)
install(TARGETS ghlinguist
    EXPORT MBWGHLinguistNativeTargets
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR})
install(FILES include/ghlinguist.h DESTINATION ${CMAKE_INSTALL_INCLUDEDIR})
install(EXPORT MBWGHLinguistNativeTargets
    NAMESPACE MBW::
    DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/MBW.GHLinguist.Native)
