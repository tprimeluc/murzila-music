cmake_minimum_required(VERSION 3.20)
project(MurzilaBrowserNative LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# CEF SDK скачивается скриптом fetch_cef.ps1 в папку cef/
set(CEF_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/cef")
if(NOT EXISTS "${CEF_ROOT}/CMakeLists.txt")
  message(FATAL_ERROR "CEF SDK not found. Run fetch_cef.ps1 first.")
endif()

add_subdirectory(${CEF_ROOT} cef_build)

add_executable(MurzilaBrowserNative WIN32
  main.cpp
  browser_app.cpp
)

target_include_directories(MurzilaBrowserNative PRIVATE ${CEF_ROOT})
target_link_libraries(MurzilaBrowserNative PRIVATE libcef libcef_dll_wrapper)

# Отключаем V-sync и лимит кадров в максимальном режиме.
target_compile_definitions(MurzilaBrowserNative PRIVATE UNICODE _UNICODE)
