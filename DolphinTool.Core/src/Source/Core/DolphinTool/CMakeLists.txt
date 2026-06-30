add_executable(dolphin-tool
  ToolHeadlessPlatform.cpp
  ExtractCommand.cpp
  ExtractCommand.h
  ConvertCommand.cpp
  ConvertCommand.h
  VerifyCommand.cpp
  VerifyCommand.h
  HeaderCommand.cpp
  HeaderCommand.h
  ToolMain.cpp
)

if (WIN32)
  target_sources(dolphin-tool PRIVATE DolphinTool.exe.manifest DolphinTool.rc)

  set_target_properties(dolphin-tool PROPERTIES
    DEBUG_POSTFIX    D
    OUTPUT_NAME      DolphinTool
  )
endif()

target_link_libraries(dolphin-tool
PRIVATE
  discio
  uicommon
  cpp-optparse
  fmt::fmt
)

if(MSVC)
  # Add precompiled header
  target_link_libraries(dolphin-tool PRIVATE use_pch)
endif()

set(CPACK_PACKAGE_EXECUTABLES ${CPACK_PACKAGE_EXECUTABLES} dolphin-tool)
install(TARGETS dolphin-tool RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR})

add_library(dolphin_rvz_wrapper SHARED
    dolphin_rvz_wrapper.cpp
    dolphin_rvz_wrapper.h
    ToolHeadlessPlatform.cpp
    ConvertCommand.cpp
    ExtractCommand.cpp
    HeaderCommand.cpp
    VerifyCommand.cpp
)
target_link_libraries(dolphin_rvz_wrapper PRIVATE discio uicommon cpp-optparse fmt::fmt)
target_include_directories(dolphin_rvz_wrapper PRIVATE "${CMAKE_SOURCE_DIR}/Source/Core")
if(MSVC)
    target_link_libraries(dolphin_rvz_wrapper PRIVATE use_pch)
endif()
