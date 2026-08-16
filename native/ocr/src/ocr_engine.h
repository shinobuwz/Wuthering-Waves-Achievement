#pragma once

#include "wuwa_ocr.h"

#include <onnxruntime_cxx_api.h>
#include <opencv2/core.hpp>

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

struct TextLineResult {
    std::array<float, 8> points{};
    std::string text;
    float score = 0.0F;
};

struct IconResult {
    std::int32_t x = 0;
    std::int32_t y = 0;
    std::int32_t label = 0;
    float confidence = 0.0F;
};

class OcrEngine final {
public:
    explicit OcrEngine(const WuwaOcrConfig& config);

    RecognitionResult RecognizeBgr(
        const std::uint8_t* pixels,
        std::int32_t width,
        std::int32_t height,
        std::int32_t stride);

    void EnableDetection(
        const std::filesystem::path& model_path,
        float bitmap_threshold,
        float box_threshold,
        float unclip_ratio,
        std::int32_t limit_side_length);

    void EnableClassifier(const std::filesystem::path& model_path, float rotation_threshold);

    const std::vector<TextLineResult>& DetectAndRecognizeBgr(
        const std::uint8_t* pixels,
        std::int32_t width,
        std::int32_t height,
        std::int32_t stride);

    const std::vector<IconResult>& FindAchievementIcons(
        const std::uint8_t* pixels,
        std::int32_t width,
        std::int32_t height,
        std::int32_t stride,
        const std::filesystem::path& template_directory,
        float threshold,
        float nms_distance);

    const std::string& LastResultText() const noexcept { return last_result_text_; }
    const std::vector<TextLineResult>& LastPage() const noexcept { return last_page_; }
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
    static std::vector<std::string> ReadNodeNames(Ort::Session& session, bool inputs);
    static std::vector<const char*> NamePointers(const std::vector<std::string>& storage);
    RecognitionResult RecognizeMat(const cv::Mat& image);
    std::vector<std::array<cv::Point2f, 4>> Detect(const cv::Mat& image);
    bool ShouldRotate180(const cv::Mat& image);
    static cv::Mat CropTextLine(const cv::Mat& image, const std::array<cv::Point2f, 4>& box);

    Ort::Env environment_;
    Ort::SessionOptions session_options_;
    std::unique_ptr<Ort::Session> recognition_session_;
    std::unique_ptr<Ort::Session> detection_session_;
    std::unique_ptr<Ort::Session> classifier_session_;
    std::vector<std::string> input_names_storage_;
    std::vector<const char*> input_names_;
    std::vector<std::string> output_names_storage_;
    std::vector<const char*> output_names_;
    std::vector<std::string> detection_input_names_storage_;
    std::vector<const char*> detection_input_names_;
    std::vector<std::string> detection_output_names_storage_;
    std::vector<const char*> detection_output_names_;
    std::vector<std::string> classifier_input_names_storage_;
    std::vector<const char*> classifier_input_names_;
    std::vector<std::string> classifier_output_names_storage_;
    std::vector<const char*> classifier_output_names_;
    std::vector<std::string> characters_;
    std::int32_t recognition_height_;
    std::int32_t recognition_min_width_;
    std::int32_t recognition_max_width_;
    float minimum_score_;
    float detection_bitmap_threshold_ = 0.3F;
    float detection_box_threshold_ = 0.6F;
    float detection_unclip_ratio_ = 1.5F;
    std::int32_t detection_limit_side_length_ = 64;
    float classifier_rotation_threshold_ = 0.9F;
    std::string last_result_text_;
    std::vector<TextLineResult> last_page_;
    std::vector<IconResult> last_icons_;
    std::string last_error_;
};

} // namespace wuwa::ocr
