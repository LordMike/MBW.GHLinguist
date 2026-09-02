cmake_minimum_required(VERSION 3.20)

project(MBW.GHLinguist.Native VERSION 0.1.0 LANGUAGES CXX)

add_library(ghlinguist SHARED
    src/ghlinguist.cpp)

target_compile_features(ghlinguist PRIVATE cxx_std_17)
target_include_directories(ghlinguist
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>)

target_compile_definitions(ghlinguist PRIVATE GHLINGUIST_BUILDING_LIBRARY=1)

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
