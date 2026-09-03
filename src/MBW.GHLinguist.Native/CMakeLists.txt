cmake_minimum_required(VERSION 3.20)

project(MBW.GHLinguist.Native VERSION 0.1.0 LANGUAGES CXX)

set(GHL_RUBY_ROOT "" CACHE PATH "Ruby installation prefix used to embed CRuby")
set(GHL_RUBY_INCLUDE_DIR "" CACHE PATH "Directory containing ruby.h")
set(GHL_RUBY_ARCH_INCLUDE_DIR "" CACHE PATH "Directory containing ruby/config.h")
set(GHL_RUBY_LIBRARY "" CACHE FILEPATH "Shared libruby used by the embedding runtime")
set(GHL_LINGUIST_REVISION "" CACHE STRING "Pinned GitHub Linguist Git revision")
set(GHL_CLASSIFIER_SHA256 "" CACHE STRING "SHA-256 of the generated Linguist classifier data")
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
    if(NOT GHL_RUBY_INCLUDE_DIR OR NOT GHL_RUBY_ARCH_INCLUDE_DIR OR NOT GHL_RUBY_LIBRARY)
        if(WIN32)
            if(NOT GHL_RUBY_ROOT)
                message(FATAL_ERROR "GHL_ENABLE_RUBY_EMBEDDING on Windows requires -DGHL_RUBY_ROOT=<RubyInstaller root> or explicit GHL_RUBY_INCLUDE_DIR, GHL_RUBY_ARCH_INCLUDE_DIR, and GHL_RUBY_LIBRARY values.")
            endif()
            set(GHL_RUBY_INCLUDE_DIR "${GHL_RUBY_ROOT}/include/ruby-4.0.0" CACHE PATH "Directory containing ruby.h" FORCE)
            set(GHL_RUBY_ARCH_INCLUDE_DIR "${GHL_RUBY_INCLUDE_DIR}/x64-mingw-ucrt" CACHE PATH "Directory containing ruby/config.h" FORCE)
            unset(GHL_RUBY_LIBRARY CACHE)
            find_file(GHL_RUBY_LIBRARY
                NAMES x64-ucrt-ruby400.lib libx64-ucrt-ruby400.dll.a
                PATHS "${GHL_RUBY_ROOT}/lib"
                NO_DEFAULT_PATH)
        else()
            find_program(GHL_RUBY_EXECUTABLE NAMES ruby HINTS "${GHL_RUBY_ROOT}/bin")
            if(NOT GHL_RUBY_EXECUTABLE)
                message(FATAL_ERROR "GHL_ENABLE_RUBY_EMBEDDING requires Ruby on PATH or -DGHL_RUBY_ROOT=<Ruby prefix>.")
            endif()
            execute_process(COMMAND "${GHL_RUBY_EXECUTABLE}" -rrbconfig -e "print RbConfig::CONFIG.fetch('rubyhdrdir')"
                OUTPUT_VARIABLE GHL_RUBY_INCLUDE_DIR OUTPUT_STRIP_TRAILING_WHITESPACE RESULT_VARIABLE GHL_RUBY_DISCOVERY_RESULT)
            if(NOT GHL_RUBY_DISCOVERY_RESULT EQUAL 0)
                message(FATAL_ERROR "Unable to discover Ruby headers using ${GHL_RUBY_EXECUTABLE}.")
            endif()
            execute_process(COMMAND "${GHL_RUBY_EXECUTABLE}" -rrbconfig -e "print RbConfig::CONFIG.fetch('rubyarchhdrdir')"
                OUTPUT_VARIABLE GHL_RUBY_ARCH_INCLUDE_DIR OUTPUT_STRIP_TRAILING_WHITESPACE)
            execute_process(COMMAND "${GHL_RUBY_EXECUTABLE}" -rrbconfig -e "print File.join(RbConfig::CONFIG.fetch('libdir'), RbConfig::CONFIG.fetch('LIBRUBY_SO'))"
                OUTPUT_VARIABLE GHL_RUBY_LIBRARY OUTPUT_STRIP_TRAILING_WHITESPACE)
            set(GHL_RUBY_INCLUDE_DIR "${GHL_RUBY_INCLUDE_DIR}" CACHE PATH "Directory containing ruby.h" FORCE)
            set(GHL_RUBY_ARCH_INCLUDE_DIR "${GHL_RUBY_ARCH_INCLUDE_DIR}" CACHE PATH "Directory containing ruby/config.h" FORCE)
            set(GHL_RUBY_LIBRARY "${GHL_RUBY_LIBRARY}" CACHE FILEPATH "Shared libruby used by the embedding runtime" FORCE)
        endif()
    endif()

    if(NOT EXISTS "${GHL_RUBY_INCLUDE_DIR}/ruby.h" OR NOT EXISTS "${GHL_RUBY_ARCH_INCLUDE_DIR}/ruby/config.h" OR NOT EXISTS "${GHL_RUBY_LIBRARY}")
        message(FATAL_ERROR "Ruby embedding requires ruby.h, ruby/config.h, and a shared libruby. Set GHL_RUBY_INCLUDE_DIR, GHL_RUBY_ARCH_INCLUDE_DIR, and GHL_RUBY_LIBRARY explicitly if discovery is unsuitable.")
    endif()

    target_include_directories(ghlinguist SYSTEM PRIVATE
        "${GHL_RUBY_INCLUDE_DIR}"
        "${GHL_RUBY_ARCH_INCLUDE_DIR}")
    target_link_libraries(ghlinguist PRIVATE "${GHL_RUBY_LIBRARY}")
    target_compile_definitions(ghlinguist PRIVATE GHL_RUBY_EMBEDDING=1)
    if(GHL_LINGUIST_REVISION)
        target_compile_definitions(ghlinguist PRIVATE GHL_LINGUIST_REVISION="${GHL_LINGUIST_REVISION}")
    endif()
    if(GHL_CLASSIFIER_SHA256)
        target_compile_definitions(ghlinguist PRIVATE GHL_CLASSIFIER_SHA256="${GHL_CLASSIFIER_SHA256}")
    endif()
    message(STATUS "CRuby embedding enabled with ${GHL_RUBY_LIBRARY}")
else()
    message(STATUS "CRuby embedding disabled; building native ABI stubs only")
endif()

if(UNIX AND NOT APPLE)
    # The packaged bridge lives beside a closure-local lib directory.
    target_link_options(ghlinguist PRIVATE "-Wl,--disable-new-dtags")
    set_target_properties(ghlinguist PROPERTIES
        BUILD_WITH_INSTALL_RPATH YES
        INSTALL_RPATH "\$ORIGIN/lib")
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

    if(UNIX AND NOT APPLE)
        target_link_options(ghlinguist_smoke PRIVATE "-Wl,--disable-new-dtags")
        set_target_properties(ghlinguist_smoke PROPERTIES
            BUILD_WITH_INSTALL_RPATH YES
            INSTALL_RPATH "\$ORIGIN/..")
    endif()

    if(GHL_ENABLE_RUBY_EMBEDDING)
        include(CTest)
        if(BUILD_TESTING)
            add_test(NAME ghlinguist.runtime-smoke
                COMMAND ghlinguist_smoke "${GHL_SMOKE_ASSET_ROOT}")
            if(WIN32)
                set_tests_properties(ghlinguist.runtime-smoke PROPERTIES
                    ENVIRONMENT "PATH=${GHL_RUBY_ROOT}/bin\\;${GHL_RUBY_ROOT}/msys64/ucrt64/bin\\;$ENV{PATH}")
            endif()
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
