#pragma once

#include <stdint.h>

#if defined(_WIN32)
#  if defined(WUWA_OCR_NATIVE_EXPORTS)
#    define WUWA_OCR_API __declspec(dllexport)
#  else
#    define WUWA_OCR_API __declspec(dllimport)
#  endif
#  define WUWA_OCR_CALL __cdecl
#else
#  define WUWA_OCR_API
#  define WUWA_OCR_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef void* WuwaOcrHandle;

typedef enum WuwaOcrStatus {
    WUWA_OCR_OK = 0,
    WUWA_OCR_INVALID_ARGUMENT = 1,
    WUWA_OCR_IO_ERROR = 2,
    WUWA_OCR_MODEL_ERROR = 3,
    WUWA_OCR_INFERENCE_ERROR = 4,
    WUWA_OCR_BUFFER_TOO_SMALL = 5,
    WUWA_OCR_INTERNAL_ERROR = 6
} WuwaOcrStatus;

typedef struct WuwaOcrConfig {
    uint32_t abi_version;
    const wchar_t* recognition_model_path;
    const wchar_t* character_dictionary_path;
    int32_t intra_op_threads;
    int32_t inter_op_threads;
    int32_t recognition_height;
    int32_t recognition_min_width;
    int32_t recognition_max_width;
    float minimum_score;
    int32_t include_space_character;
} WuwaOcrConfig;

typedef struct WuwaOcrResult {
    const char* text_utf8;
    float score;
} WuwaOcrResult;

typedef struct WuwaOcrTextLine {
    float points[8];
    const char* text_utf8;
    float score;
} WuwaOcrTextLine;

typedef struct WuwaOcrTextPage {
    const WuwaOcrTextLine* lines;
    int32_t count;
} WuwaOcrTextPage;

typedef struct WuwaOcrIcon {
    int32_t x;
    int32_t y;
    int32_t label;
    float confidence;
} WuwaOcrIcon;

typedef struct WuwaOcrIconPage {
    const WuwaOcrIcon* icons;
    int32_t count;
} WuwaOcrIconPage;

WUWA_OCR_API uint32_t WUWA_OCR_CALL wuwa_ocr_abi_version(void);
WUWA_OCR_API const char* WUWA_OCR_CALL wuwa_ocr_version(void);

WUWA_OCR_API WuwaOcrStatus WUWA_OCR_CALL wuwa_ocr_create(
    const WuwaOcrConfig* config,
    WuwaOcrHandle* handle);

WUWA_OCR_API void WUWA_OCR_CALL wuwa_ocr_destroy(WuwaOcrHandle handle);

WUWA_OCR_API WuwaOcrStatus WUWA_OCR_CALL wuwa_ocr_recognize_bgr(
    WuwaOcrHandle handle,
    const uint8_t* pixels,
    int32_t width,
    int32_t height,
    int32_t stride,
    WuwaOcrResult* result);

WUWA_OCR_API WuwaOcrStatus WUWA_OCR_CALL wuwa_ocr_recognize_bgr_clahe(
    WuwaOcrHandle handle,
    const uint8_t* pixels,
    int32_t width,
    int32_t height,
    int32_t stride,
    WuwaOcrResult* result);

WUWA_OCR_API WuwaOcrStatus WUWA_OCR_CALL wuwa_ocr_enable_detection(
    WuwaOcrHandle handle,
    const wchar_t* detection_model_path,
    float bitmap_threshold,
    float box_threshold,
    float unclip_ratio,
    int32_t limit_side_length);

WUWA_OCR_API WuwaOcrStatus WUWA_OCR_CALL wuwa_ocr_enable_classifier(
    WuwaOcrHandle handle,
    const wchar_t* classifier_model_path,
    float rotation_threshold);

WUWA_OCR_API WuwaOcrStatus WUWA_OCR_CALL wuwa_ocr_detect_and_recognize_bgr(
    WuwaOcrHandle handle,
    const uint8_t* pixels,
    int32_t width,
    int32_t height,
    int32_t stride,
    WuwaOcrTextPage* result);

WUWA_OCR_API WuwaOcrStatus WUWA_OCR_CALL wuwa_ocr_find_achievement_icons(
    WuwaOcrHandle handle,
    const uint8_t* pixels,
    int32_t width,
    int32_t height,
    int32_t stride,
    const wchar_t* template_directory,
    float threshold,
    float nms_distance,
    WuwaOcrIconPage* result);

WUWA_OCR_API WuwaOcrStatus WUWA_OCR_CALL wuwa_ocr_last_error(
    WuwaOcrHandle handle,
    char* buffer_utf8,
    int32_t buffer_size,
    int32_t* required_size);

#ifdef __cplusplus
}
#endif
