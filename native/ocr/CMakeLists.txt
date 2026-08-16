cmake_minimum_required(VERSION 3.24)

project(WuwaOcrNative VERSION 0.1.0 LANGUAGES CXX)

if(NOT WIN32)
    message(FATAL_ERROR "Wuwa.Ocr.Native currently supports Windows x64 only.")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL")

set(WUWA_ONNXRUNTIME_VERSION "1.22.1" CACHE STRING "ONNX Runtime NuGet package version")
set(ONNXRUNTIME_ROOT "" CACHE PATH "Root of the Microsoft.ML.OnnxRuntime NuGet package")
set(WUWA_OCR_REC_MODEL "" CACHE FILEPATH "Optional recognition model used by native integration tests")
set(WUWA_OCR_DET_MODEL "" CACHE FILEPATH "Optional detection model used by native integration tests")
set(WUWA_OCR_CLS_MODEL "" CACHE FILEPATH "Optional classifier model used by native integration tests")
set(WUWA_OCR_DICTIONARY "" CACHE FILEPATH "Optional PP-OCR recognition dictionary used by native integration tests")

if(NOT ONNXRUNTIME_ROOT AND DEFINED ENV{ONNXRUNTIME_ROOT})
    file(TO_CMAKE_PATH "$ENV{ONNXRUNTIME_ROOT}" ONNXRUNTIME_ROOT)
endif()

if(NOT ONNXRUNTIME_ROOT AND DEFINED ENV{USERPROFILE})
    file(TO_CMAKE_PATH "$ENV{USERPROFILE}/.nuget/packages/microsoft.ml.onnxruntime/${WUWA_ONNXRUNTIME_VERSION}" ONNXRUNTIME_ROOT)
endif()

find_path(ONNXRUNTIME_INCLUDE_DIR
    NAMES onnxruntime_cxx_api.h
    PATHS "${ONNXRUNTIME_ROOT}/build/native/include"
    NO_DEFAULT_PATH)
find_library(ONNXRUNTIME_LIBRARY
    NAMES onnxruntime
    PATHS "${ONNXRUNTIME_ROOT}/runtimes/win-x64/native"
    NO_DEFAULT_PATH)
find_file(ONNXRUNTIME_DLL
    NAMES onnxruntime.dll
    PATHS "${ONNXRUNTIME_ROOT}/runtimes/win-x64/native"
    NO_DEFAULT_PATH)
find_file(ONNXRUNTIME_PROVIDERS_SHARED_DLL
    NAMES onnxruntime_providers_shared.dll
    PATHS "${ONNXRUNTIME_ROOT}/runtimes/win-x64/native"
    NO_DEFAULT_PATH)

if(NOT ONNXRUNTIME_INCLUDE_DIR OR NOT ONNXRUNTIME_LIBRARY OR NOT ONNXRUNTIME_DLL)
    message(FATAL_ERROR
        "ONNX Runtime native files were not found. Restore Microsoft.ML.OnnxRuntime ${WUWA_ONNXRUNTIME_VERSION} "
        "or pass -DONNXRUNTIME_ROOT=<NuGet package root>.")
endif()

if(NOT OpenCV_DIR AND DEFINED ENV{OpenCV_DIR})
    file(TO_CMAKE_PATH "$ENV{OpenCV_DIR}" OpenCV_DIR)
endif()
find_package(OpenCV 4.8 REQUIRED COMPONENTS core imgproc imgcodecs)

add_library(clipper2 STATIC
    third_party/clipper2/src/clipper.engine.cpp
    third_party/clipper2/src/clipper.offset.cpp
    third_party/clipper2/src/clipper.rectclip.cpp
    third_party/clipper2/src/clipper.triangulation.cpp)
target_include_directories(clipper2 SYSTEM PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/third_party/clipper2/include")
target_compile_options(clipper2 PRIVATE /utf-8 /EHsc)

add_library(Wuwa.Ocr.Native SHARED
    include/wuwa_ocr.h
    src/ocr_engine.cpp
    src/ocr_engine.h
    src/detection.cpp
    src/classifier.cpp
    src/exports.cpp)

target_include_directories(Wuwa.Ocr.Native
    PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/include"
    PRIVATE "${ONNXRUNTIME_INCLUDE_DIR}" "${CMAKE_CURRENT_SOURCE_DIR}/src" ${OpenCV_INCLUDE_DIRS})
target_link_libraries(Wuwa.Ocr.Native PRIVATE "${ONNXRUNTIME_LIBRARY}" ${OpenCV_LIBS} clipper2)
target_compile_definitions(Wuwa.Ocr.Native PRIVATE WUWA_OCR_NATIVE_EXPORTS NOMINMAX WIN32_LEAN_AND_MEAN)
target_compile_options(Wuwa.Ocr.Native PRIVATE /utf-8 /W4 /wd4702 /permissive- /EHsc)

add_custom_command(TARGET Wuwa.Ocr.Native POST_BUILD
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different "${ONNXRUNTIME_DLL}" "$<TARGET_FILE_DIR:Wuwa.Ocr.Native>")
if(ONNXRUNTIME_PROVIDERS_SHARED_DLL)
    add_custom_command(TARGET Wuwa.Ocr.Native POST_BUILD
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different "${ONNXRUNTIME_PROVIDERS_SHARED_DLL}" "$<TARGET_FILE_DIR:Wuwa.Ocr.Native>")
endif()
add_custom_command(TARGET Wuwa.Ocr.Native POST_BUILD
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different "$<TARGET_FILE:opencv_core>" "$<TARGET_FILE_DIR:Wuwa.Ocr.Native>"
    COMMAND "${CMAKE_COMMAND}" -E copy_if_different "$<TARGET_FILE:opencv_imgproc>" "$<TARGET_FILE_DIR:Wuwa.Ocr.Native>")

include(CTest)
if(BUILD_TESTING)
    add_executable(Wuwa.Ocr.Native.Tests tests/ocr_tests.cpp src/ocr_engine.cpp src/ocr_engine.h src/detection.cpp src/classifier.cpp)
    target_include_directories(Wuwa.Ocr.Native.Tests PRIVATE
        "${CMAKE_CURRENT_SOURCE_DIR}/include" "${CMAKE_CURRENT_SOURCE_DIR}/src" "${ONNXRUNTIME_INCLUDE_DIR}" ${OpenCV_INCLUDE_DIRS})
    target_link_libraries(Wuwa.Ocr.Native.Tests PRIVATE "${ONNXRUNTIME_LIBRARY}" ${OpenCV_LIBS} clipper2)
    target_compile_definitions(Wuwa.Ocr.Native.Tests PRIVATE NOMINMAX WIN32_LEAN_AND_MEAN)
    target_compile_options(Wuwa.Ocr.Native.Tests PRIVATE /utf-8 /W4 /wd4702 /permissive- /EHsc)
    add_custom_command(TARGET Wuwa.Ocr.Native.Tests POST_BUILD
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different "${ONNXRUNTIME_DLL}" "$<TARGET_FILE_DIR:Wuwa.Ocr.Native.Tests>")
    if(ONNXRUNTIME_PROVIDERS_SHARED_DLL)
        add_custom_command(TARGET Wuwa.Ocr.Native.Tests POST_BUILD
            COMMAND "${CMAKE_COMMAND}" -E copy_if_different "${ONNXRUNTIME_PROVIDERS_SHARED_DLL}" "$<TARGET_FILE_DIR:Wuwa.Ocr.Native.Tests>")
    endif()
    add_custom_command(TARGET Wuwa.Ocr.Native.Tests POST_BUILD
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different "$<TARGET_FILE:opencv_core>" "$<TARGET_FILE_DIR:Wuwa.Ocr.Native.Tests>"
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different "$<TARGET_FILE:opencv_imgproc>" "$<TARGET_FILE_DIR:Wuwa.Ocr.Native.Tests>")

    add_test(NAME Wuwa.Ocr.Native.Unit COMMAND Wuwa.Ocr.Native.Tests)
    if(WUWA_OCR_REC_MODEL AND WUWA_OCR_DICTIONARY)
        add_test(NAME Wuwa.Ocr.Native.ModelSmoke
            COMMAND Wuwa.Ocr.Native.Tests "${WUWA_OCR_REC_MODEL}" "${WUWA_OCR_DICTIONARY}" "${WUWA_OCR_DET_MODEL}" "${WUWA_OCR_CLS_MODEL}")
    endif()
endif()

install(TARGETS Wuwa.Ocr.Native RUNTIME DESTINATION .)
install(FILES "${ONNXRUNTIME_DLL}" DESTINATION .)
install(FILES "$<TARGET_FILE:opencv_core>" "$<TARGET_FILE:opencv_imgproc>" DESTINATION .)
if(ONNXRUNTIME_PROVIDERS_SHARED_DLL)
    install(FILES "${ONNXRUNTIME_PROVIDERS_SHARED_DLL}" DESTINATION .)
endif()
