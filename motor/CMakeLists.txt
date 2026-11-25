cmake_minimum_required(VERSION 3.24)
project(VideoNRRenderWorker LANGUAGES C CXX)

if(NOT WIN32 OR NOT MSVC OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "O motor requer Windows x64 e MSVC.")
endif()

set(VIDEONR_ENGINE_SOURCE "" CACHE PATH "Caminho para o código-fonte extraído")
if(NOT EXISTS "${VIDEONR_ENGINE_SOURCE}/src/engine/ExportJobManager.cpp")
  message(FATAL_ERROR "Aponte VIDEONR_ENGINE_SOURCE para o código-fonte do motor.")
endif()

set(VEYRA_ENABLE_EXPERIMENTAL_DLSSNR ON CACHE BOOL "" FORCE)
set(VEYRA_BUILD_QML_UI OFF CACHE BOOL "" FORCE)
set(VEYRA_BUILD_QT_PROBE OFF CACHE BOOL "" FORCE)
add_subdirectory("${VIDEONR_ENGINE_SOURCE}" upstream)

if(NOT TARGET veyra_engine)
  message(FATAL_ERROR "Confira os caminhos do FFmpeg, do SDK DLSS e do SDK Optical Flow.")
endif()

add_executable(VideoNR-RenderWorker WorkerMain.cpp)
target_compile_features(VideoNR-RenderWorker PRIVATE cxx_std_20)
target_compile_definitions(VideoNR-RenderWorker PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(VideoNR-RenderWorker PRIVATE /utf-8)
target_link_options(VideoNR-RenderWorker PRIVATE /STACK:8388608)
target_link_libraries(VideoNR-RenderWorker PRIVATE veyra_engine ole32)
