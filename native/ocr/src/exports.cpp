#include "ocr_engine.h"

#include <algorithm>
#include <cstring>
#include <exception>
#include <limits>
#include <new>
#include <string>
#include <vector>

namespace {

thread_local std::string g_last_error;

struct ExportPageStorage {
    std::vector<WuwaOcrTextLine> lines;
};

thread_local ExportPageStorage g_page_storage;

WuwaOcrStatus StoreError(WuwaOcrHandle handle, WuwaOcrStatus status, std::string message) noexcept {
    g_last_error = std::move(message);
    if (handle != nullptr) {
        static_cast<wuwa::ocr::OcrEngine*>(handle)->SetLastError(g_last_error);
    }
    return status;
}

WuwaOcrStatus CopyError(std::string_view message, char* buffer, std::int32_t buffer_size, std::int32_t* required_size) noexcept {
    if (required_size == nullptr) {
        return WUWA_OCR_INVALID_ARGUMENT;
    }
    const auto required = message.size() + 1U;
    if (required > static_cast<std::size_t>(std::numeric_limits<std::int32_t>::max())) {
        return WUWA_OCR_INTERNAL_ERROR;
    }
    *required_size = static_cast<std::int32_t>(required);
    if (buffer == nullptr || buffer_size < static_cast<std::int32_t>(required)) {
        return WUWA_OCR_BUFFER_TOO_SMALL;
    }
    std::memcpy(buffer, message.data(), message.size());
    buffer[message.size()] = '\0';
    return WUWA_OCR_OK;
}

} // namespace

std::uint32_t WUWA_OCR_CALL wuwa_ocr_abi_version(void) {
    return wuwa::ocr::kAbiVersion;
}

const char* WUWA_OCR_CALL wuwa_ocr_version(void) {
    return wuwa::ocr::kVersion.data();
}

WuwaOcrStatus WUWA_OCR_CALL wuwa_ocr_create(const WuwaOcrConfig* config, WuwaOcrHandle* handle) {
    if (handle == nullptr) {
        return StoreError(nullptr, WUWA_OCR_INVALID_ARGUMENT, "Output handle is required.");
    }
    *handle = nullptr;
    if (config == nullptr) {
        return StoreError(nullptr, WUWA_OCR_INVALID_ARGUMENT, "OCR configuration is required.");
    }

    try {
        auto engine = std::make_unique<wuwa::ocr::OcrEngine>(*config);
        *handle = engine.release();
        g_last_error.clear();
        return WUWA_OCR_OK;
    } catch (const std::invalid_argument& exception) {
        return StoreError(nullptr, WUWA_OCR_INVALID_ARGUMENT, exception.what());
    } catch (const wuwa::ocr::IoError& exception) {
        return StoreError(nullptr, WUWA_OCR_IO_ERROR, exception.what());
    } catch (const Ort::Exception& exception) {
        return StoreError(nullptr, WUWA_OCR_MODEL_ERROR, exception.what());
    } catch (const std::filesystem::filesystem_error& exception) {
        return StoreError(nullptr, WUWA_OCR_IO_ERROR, exception.what());
    } catch (const std::exception& exception) {
        return StoreError(nullptr, WUWA_OCR_MODEL_ERROR, exception.what());
    } catch (...) {
        return StoreError(nullptr, WUWA_OCR_INTERNAL_ERROR, "Unknown native OCR initialization failure.");
    }
}

void WUWA_OCR_CALL wuwa_ocr_destroy(WuwaOcrHandle handle) {
    delete static_cast<wuwa::ocr::OcrEngine*>(handle);
}

WuwaOcrStatus WUWA_OCR_CALL wuwa_ocr_recognize_bgr(
    WuwaOcrHandle handle,
    const std::uint8_t* pixels,
    std::int32_t width,
    std::int32_t height,
    std::int32_t stride,
    WuwaOcrResult* result) {
    if (handle == nullptr || result == nullptr) {
        return StoreError(handle, WUWA_OCR_INVALID_ARGUMENT, "OCR handle and result are required.");
    }
    result->text_utf8 = nullptr;
    result->score = 0.0F;

    try {
        auto* engine = static_cast<wuwa::ocr::OcrEngine*>(handle);
        const auto recognition = engine->RecognizeBgr(pixels, width, height, stride);
        result->text_utf8 = engine->LastResultText().c_str();
        result->score = recognition.score;
        g_last_error.clear();
        return WUWA_OCR_OK;
    } catch (const std::invalid_argument& exception) {
        return StoreError(handle, WUWA_OCR_INVALID_ARGUMENT, exception.what());
    } catch (const Ort::Exception& exception) {
        return StoreError(handle, WUWA_OCR_INFERENCE_ERROR, exception.what());
    } catch (const std::exception& exception) {
        return StoreError(handle, WUWA_OCR_INFERENCE_ERROR, exception.what());
    } catch (...) {
        return StoreError(handle, WUWA_OCR_INTERNAL_ERROR, "Unknown native OCR inference failure.");
    }
}

WuwaOcrStatus WUWA_OCR_CALL wuwa_ocr_enable_detection(
    WuwaOcrHandle handle,
    const wchar_t* detection_model_path,
    float bitmap_threshold,
    float box_threshold,
    float unclip_ratio,
    std::int32_t limit_side_length) {
    if (handle == nullptr || detection_model_path == nullptr) {
        return StoreError(handle, WUWA_OCR_INVALID_ARGUMENT, "OCR handle and detection model path are required.");
    }
    try {
        static_cast<wuwa::ocr::OcrEngine*>(handle)->EnableDetection(
            std::filesystem::path(detection_model_path), bitmap_threshold, box_threshold, unclip_ratio, limit_side_length);
        g_last_error.clear();
        return WUWA_OCR_OK;
    } catch (const std::invalid_argument& exception) {
        return StoreError(handle, WUWA_OCR_INVALID_ARGUMENT, exception.what());
    } catch (const wuwa::ocr::IoError& exception) {
        return StoreError(handle, WUWA_OCR_IO_ERROR, exception.what());
    } catch (const Ort::Exception& exception) {
        return StoreError(handle, WUWA_OCR_MODEL_ERROR, exception.what());
    } catch (const std::exception& exception) {
        return StoreError(handle, WUWA_OCR_MODEL_ERROR, exception.what());
    } catch (...) {
        return StoreError(handle, WUWA_OCR_INTERNAL_ERROR, "Unknown native OCR detection initialization failure.");
    }
}

WuwaOcrStatus WUWA_OCR_CALL wuwa_ocr_enable_classifier(
    WuwaOcrHandle handle,
    const wchar_t* classifier_model_path,
    float rotation_threshold) {
    if (handle == nullptr || classifier_model_path == nullptr) {
        return StoreError(handle, WUWA_OCR_INVALID_ARGUMENT, "OCR handle and classifier model path are required.");
    }
    try {
        static_cast<wuwa::ocr::OcrEngine*>(handle)->EnableClassifier(std::filesystem::path(classifier_model_path), rotation_threshold);
        g_last_error.clear();
        return WUWA_OCR_OK;
    } catch (const std::invalid_argument& exception) {
        return StoreError(handle, WUWA_OCR_INVALID_ARGUMENT, exception.what());
    } catch (const wuwa::ocr::IoError& exception) {
        return StoreError(handle, WUWA_OCR_IO_ERROR, exception.what());
    } catch (const Ort::Exception& exception) {
        return StoreError(handle, WUWA_OCR_MODEL_ERROR, exception.what());
    } catch (const std::exception& exception) {
        return StoreError(handle, WUWA_OCR_MODEL_ERROR, exception.what());
    } catch (...) {
        return StoreError(handle, WUWA_OCR_INTERNAL_ERROR, "Unknown native OCR classifier initialization failure.");
    }
}

WuwaOcrStatus WUWA_OCR_CALL wuwa_ocr_detect_and_recognize_bgr(
    WuwaOcrHandle handle,
    const std::uint8_t* pixels,
    std::int32_t width,
    std::int32_t height,
    std::int32_t stride,
    WuwaOcrTextPage* result) {
    if (handle == nullptr || result == nullptr) {
        return StoreError(handle, WUWA_OCR_INVALID_ARGUMENT, "OCR handle and page result are required.");
    }
    result->lines = nullptr;
    result->count = 0;
    try {
        const auto& page = static_cast<wuwa::ocr::OcrEngine*>(handle)->DetectAndRecognizeBgr(pixels, width, height, stride);
        g_page_storage.lines.clear();
        g_page_storage.lines.reserve(page.size());
        for (const auto& item : page) {
            WuwaOcrTextLine exported{};
            std::copy(item.points.begin(), item.points.end(), exported.points);
            exported.text_utf8 = item.text.c_str();
            exported.score = item.score;
            g_page_storage.lines.push_back(exported);
        }
        result->lines = g_page_storage.lines.data();
        result->count = static_cast<std::int32_t>(g_page_storage.lines.size());
        g_last_error.clear();
        return WUWA_OCR_OK;
    } catch (const std::invalid_argument& exception) {
        return StoreError(handle, WUWA_OCR_INVALID_ARGUMENT, exception.what());
    } catch (const Ort::Exception& exception) {
        return StoreError(handle, WUWA_OCR_INFERENCE_ERROR, exception.what());
    } catch (const std::exception& exception) {
        return StoreError(handle, WUWA_OCR_INFERENCE_ERROR, exception.what());
    } catch (...) {
        return StoreError(handle, WUWA_OCR_INTERNAL_ERROR, "Unknown native OCR page inference failure.");
    }
}

WuwaOcrStatus WUWA_OCR_CALL wuwa_ocr_last_error(
    WuwaOcrHandle handle,
    char* buffer_utf8,
    std::int32_t buffer_size,
    std::int32_t* required_size) {
    const auto& message = handle == nullptr
        ? g_last_error
        : static_cast<wuwa::ocr::OcrEngine*>(handle)->LastError();
    return CopyError(message, buffer_utf8, buffer_size, required_size);
}
