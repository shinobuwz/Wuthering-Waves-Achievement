#pragma once

#include "wuwa_ocr.h"

#include <onnxruntime_cxx_api.h>

#include <array>
#include <cstdint>
#include <filesystem>
#include <memory>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>

namespace wuwa::ocr {

constexpr std::uint32_t kAbiVersion = 1;
constexpr std::string_view kVersion = "0.1.0";

class IoError final : public std::runtime_error {
public:
    using std::runtime_error::runtime_error;
};

struct RecognitionResult {
    std::string text;
    float score = 0.0F;
};

class OcrEngine final {
public:
    explicit OcrEngine(const WuwaOcrConfig& config);

    RecognitionResult RecognizeBgr(
        const std::uint8_t* pixels,
        std::int32_t width,
        std::int32_t height,
        std::int32_t stride);

    const std::string& LastResultText() const noexcept { return last_result_text_; }
    const std::string& LastError() const noexcept { return last_error_; }
    void SetLastError(std::string message) { last_error_ = std::move(message); }

    static std::vector<std::string> LoadDictionary(const std::filesystem::path& path, bool include_space_character);
    static RecognitionResult DecodeCtc(
        const float* logits,
        std::int64_t time_steps,
        std::int64_t class_count,
        const std::vector<std::string>& characters);

private:
    struct TensorInput {
        std::vector<float> values;
        std::array<std::int64_t, 4> shape{};
    };

    TensorInput PrepareRecognitionInput(
        const std::uint8_t* pixels,
        std::int32_t width,
        std::int32_t height,
        std::int32_t stride) const;

    Ort::Env environment_;
    Ort::SessionOptions session_options_;
    std::unique_ptr<Ort::Session> recognition_session_;
    std::vector<std::string> input_names_storage_;
    std::vector<const char*> input_names_;
    std::vector<std::string> output_names_storage_;
    std::vector<const char*> output_names_;
    std::vector<std::string> characters_;
    std::int32_t recognition_height_;
    std::int32_t recognition_min_width_;
    std::int32_t recognition_max_width_;
    float minimum_score_;
    std::string last_result_text_;
    std::string last_error_;
};

} // namespace wuwa::ocr
