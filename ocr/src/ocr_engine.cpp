#include "ocr_engine.h"

#include <algorithm>
#include <cmath>
#include <fstream>
#include <filesystem>
#include <limits>

#include <opencv2/imgcodecs.hpp>
#include <opencv2/imgproc.hpp>
#include <numeric>
#include <stdexcept>

namespace wuwa::ocr {
namespace {

std::int32_t RoundUp(std::int32_t value, std::int32_t multiple) {
    return ((value + multiple - 1) / multiple) * multiple;
}

std::string StripLineEnding(std::string value) {
    while (!value.empty() && (value.back() == '\r' || value.back() == '\n')) {
        value.pop_back();
    }
    return value;
}

cv::Mat ReadColorImage(const std::filesystem::path& path) {
    std::ifstream input(path, std::ios::binary);
    if (!input) throw IoError("Unable to open icon template: " + path.string());
    const auto begin = std::istreambuf_iterator<char>(input);
    const auto end = std::istreambuf_iterator<char>();
    const std::vector<std::uint8_t> bytes(begin, end);
    auto image = cv::imdecode(bytes, cv::IMREAD_COLOR);
    if (image.empty()) throw IoError("Unable to decode icon template: " + path.string());
    return image;
}

} // namespace

std::vector<std::string> OcrEngine::ReadNodeNames(Ort::Session& session, bool inputs) {
    Ort::AllocatorWithDefaultOptions allocator;
    const auto count = inputs ? session.GetInputCount() : session.GetOutputCount();
    std::vector<std::string> names;
    names.reserve(count);
    for (std::size_t index = 0; index < count; ++index) {
        auto allocated = inputs
            ? session.GetInputNameAllocated(index, allocator)
            : session.GetOutputNameAllocated(index, allocator);
        names.emplace_back(allocated.get());
    }
    return names;
}

std::vector<const char*> OcrEngine::NamePointers(const std::vector<std::string>& storage) {
    std::vector<const char*> pointers;
    pointers.reserve(storage.size());
    for (const auto& name : storage) {
        pointers.push_back(name.c_str());
    }
    return pointers;
}

OcrEngine::OcrEngine(const WuwaOcrConfig& config)
    : environment_(ORT_LOGGING_LEVEL_WARNING, "Wuwa.Ocr.Native"),
      recognition_height_(config.recognition_height > 0 ? config.recognition_height : 48),
      recognition_min_width_(config.recognition_min_width > 0 ? config.recognition_min_width : 320),
      recognition_max_width_(config.recognition_max_width > 0 ? config.recognition_max_width : 1920),
      minimum_score_(config.minimum_score > 0.0F ? config.minimum_score : 0.0F) {
    if (config.abi_version != kAbiVersion) {
        throw std::invalid_argument("Unsupported Wuwa OCR ABI version.");
    }
    if (config.recognition_model_path == nullptr || config.character_dictionary_path == nullptr) {
        throw std::invalid_argument("Recognition model and character dictionary paths are required.");
    }
    if (recognition_min_width_ > recognition_max_width_) {
        throw std::invalid_argument("Recognition minimum width cannot exceed maximum width.");
    }

    const auto model_path = std::filesystem::path(config.recognition_model_path);
    const auto dictionary_path = std::filesystem::path(config.character_dictionary_path);
    if (!std::filesystem::is_regular_file(model_path)) {
        throw IoError("Recognition model file does not exist.");
    }
    if (!std::filesystem::is_regular_file(dictionary_path)) {
        throw IoError("Recognition dictionary file does not exist.");
    }

    session_options_.SetIntraOpNumThreads(config.intra_op_threads > 0 ? config.intra_op_threads : 4);
    session_options_.SetInterOpNumThreads(config.inter_op_threads > 0 ? config.inter_op_threads : 1);
    session_options_.SetGraphOptimizationLevel(GraphOptimizationLevel::ORT_ENABLE_ALL);
    session_options_.DisableMemPattern();

    characters_ = LoadDictionary(dictionary_path, config.include_space_character != 0);
    recognition_session_ = std::make_unique<Ort::Session>(environment_, model_path.c_str(), session_options_);
    input_names_storage_ = ReadNodeNames(*recognition_session_, true);
    output_names_storage_ = ReadNodeNames(*recognition_session_, false);
    input_names_ = NamePointers(input_names_storage_);
    output_names_ = NamePointers(output_names_storage_);

    if (input_names_.size() != 1 || output_names_.empty()) {
        throw std::runtime_error("Recognition model must expose one image input and at least one output.");
    }

    const auto input_info = recognition_session_->GetInputTypeInfo(0).GetTensorTypeAndShapeInfo();
    const auto input_shape = input_info.GetShape();
    if (input_shape.size() != 4 || (input_shape[1] > 0 && input_shape[1] != 3) ||
        (input_shape[2] > 0 && input_shape[2] != recognition_height_)) {
        throw std::runtime_error("Recognition model input must be NCHW with three channels and the configured height.");
    }

    const auto output_info = recognition_session_->GetOutputTypeInfo(0).GetTensorTypeAndShapeInfo();
    const auto output_shape = output_info.GetShape();
    if (output_shape.size() != 3) {
        throw std::runtime_error("Recognition model output must have shape [batch, time, classes].");
    }
    if (output_shape[2] > 0 && static_cast<std::size_t>(output_shape[2]) != characters_.size() + 1U) {
        throw std::runtime_error("Recognition dictionary size does not match the model class count.");
    }
}

RecognitionResult OcrEngine::RecognizeBgr(
    const std::uint8_t* pixels,
    std::int32_t width,
    std::int32_t height,
    std::int32_t stride) {
    auto input = PrepareRecognitionInput(pixels, width, height, stride);
    auto memory = Ort::MemoryInfo::CreateCpu(OrtArenaAllocator, OrtMemTypeDefault);
    auto input_tensor = Ort::Value::CreateTensor<float>(
        memory,
        input.values.data(),
        input.values.size(),
        input.shape.data(),
        input.shape.size());

    auto outputs = recognition_session_->Run(
        Ort::RunOptions{nullptr},
        input_names_.data(),
        &input_tensor,
        1,
        output_names_.data(),
        1);
    if (outputs.empty() || !outputs[0].IsTensor()) {
        throw std::runtime_error("Recognition inference returned no tensor output.");
    }

    const auto info = outputs[0].GetTensorTypeAndShapeInfo();
    const auto shape = info.GetShape();
    if (shape.size() != 3 || shape[0] != 1 || shape[1] <= 0 || shape[2] <= 0) {
        throw std::runtime_error("Recognition output shape is invalid.");
    }
    if (static_cast<std::size_t>(shape[2]) != characters_.size() + 1U) {
        throw std::runtime_error("Recognition output class count does not match the dictionary.");
    }

    auto result = DecodeCtc(outputs[0].GetTensorData<float>(), shape[1], shape[2], characters_);
    if (result.score < minimum_score_) {
        result.text.clear();
    }
    last_result_text_ = result.text;
    last_error_.clear();
    return result;
}

RecognitionResult OcrEngine::RecognizeBgrClahe(
    const std::uint8_t* pixels,
    std::int32_t width,
    std::int32_t height,
    std::int32_t stride) {
    if (pixels == nullptr || width <= 0 || height <= 0 || stride < width * 3) {
        throw std::invalid_argument("A valid packed BGR image is required.");
    }

    const cv::Mat image(height, width, CV_8UC3, const_cast<std::uint8_t*>(pixels), stride);
    cv::Mat gray;
    cv::Mat enhanced;
    cv::Mat result;
    cv::cvtColor(image, gray, cv::COLOR_BGR2GRAY);
    cv::createCLAHE(2.0, cv::Size(8, 8))->apply(gray, enhanced);
    cv::cvtColor(enhanced, result, cv::COLOR_GRAY2BGR);
    return RecognizeBgr(result.data, result.cols, result.rows, static_cast<std::int32_t>(result.step));
}

const std::vector<IconResult>& OcrEngine::FindAchievementIcons(
    const std::uint8_t* pixels,
    std::int32_t width,
    std::int32_t height,
    std::int32_t stride,
    const std::filesystem::path& template_directory,
    float threshold,
    float nms_distance) {
    if (pixels == nullptr || width <= 0 || height <= 0 || stride < width * 3) {
        throw std::invalid_argument("A valid BGR image is required for template matching.");
    }
    if (threshold < 0.0F || threshold > 1.0F || nms_distance < 0.0F) {
        throw std::invalid_argument("Template matching threshold or NMS distance is invalid.");
    }

    const cv::Mat image(height, width, CV_8UC3, const_cast<std::uint8_t*>(pixels), stride);
    struct TemplateDefinition {
        const char* filename;
        std::int32_t label;
    };
    constexpr TemplateDefinition definitions[] = {
        {"icon_1star.png", 1},
        {"icon_2star.png", 2},
        {"icon_3star.png", 3},
    };

    struct Candidate {
        std::int32_t x;
        std::int32_t y;
        std::int32_t label;
        float confidence;
    };
    std::vector<Candidate> candidates;
    for (const auto& definition : definitions) {
        const auto template_image = ReadColorImage(template_directory / definition.filename);
        if (template_image.cols > image.cols || template_image.rows > image.rows) continue;
        cv::Mat matches;
        cv::matchTemplate(image, template_image, matches, cv::TM_CCOEFF_NORMED);
        for (int y = 0; y < matches.rows; ++y) {
            for (int x = 0; x < matches.cols; ++x) {
                const auto confidence = matches.at<float>(y, x);
                if (confidence >= threshold) candidates.push_back({x, y, definition.label, confidence});
            }
        }
    }

    std::sort(candidates.begin(), candidates.end(), [](const Candidate& left, const Candidate& right) {
        return left.confidence > right.confidence;
    });
    last_icons_.clear();
    for (const auto& candidate : candidates) {
        const auto too_close = std::any_of(last_icons_.begin(), last_icons_.end(), [&](const IconResult& existing) {
            return std::abs(candidate.x - existing.x) < nms_distance &&
                std::abs(candidate.y - existing.y) < nms_distance;
        });
        if (!too_close) last_icons_.push_back({candidate.x, candidate.y, candidate.label, candidate.confidence});
    }
    std::sort(last_icons_.begin(), last_icons_.end(), [](const IconResult& left, const IconResult& right) {
        return left.y < right.y;
    });
    last_error_.clear();
    return last_icons_;
}

std::vector<std::string> OcrEngine::LoadDictionary(
    const std::filesystem::path& path,
    bool include_space_character) {
    std::ifstream stream(path, std::ios::binary);
    if (!stream) {
        throw IoError("Unable to open recognition dictionary.");
    }

    std::vector<std::string> characters;
    std::string line;
    while (std::getline(stream, line)) {
        line = StripLineEnding(std::move(line));
        if (!line.empty()) {
            characters.push_back(line);
        }
    }
    if (characters.empty()) {
        throw std::runtime_error("Recognition dictionary is empty.");
    }
    if (include_space_character) {
        characters.emplace_back(" ");
    }
    return characters;
}

RecognitionResult OcrEngine::DecodeCtc(
    const float* logits,
    std::int64_t time_steps,
    std::int64_t class_count,
    const std::vector<std::string>& characters) {
    if (logits == nullptr || time_steps <= 0 || class_count <= 1) {
        throw std::invalid_argument("CTC logits and dimensions are required.");
    }
    if (static_cast<std::size_t>(class_count) != characters.size() + 1U) {
        throw std::invalid_argument("CTC class count must equal dictionary size plus the blank class.");
    }

    RecognitionResult result;
    std::int64_t previous = -1;
    double score_sum = 0.0;
    std::int64_t selected = 0;
    for (std::int64_t step = 0; step < time_steps; ++step) {
        const float* row = logits + step * class_count;
        const auto best = static_cast<std::int64_t>(std::distance(row, std::max_element(row, row + class_count)));
        const float probability = row[best];
        if (best != 0 && best != previous) {
            result.text += characters[static_cast<std::size_t>(best - 1)];
            score_sum += probability;
            ++selected;
        }
        previous = best;
    }
    result.score = selected > 0 ? static_cast<float>(score_sum / static_cast<double>(selected)) : 0.0F;
    return result;
}

OcrEngine::TensorInput OcrEngine::PrepareRecognitionInput(
    const std::uint8_t* pixels,
    std::int32_t width,
    std::int32_t height,
    std::int32_t stride) const {
    if (pixels == nullptr || width <= 0 || height <= 0 || stride < width * 3) {
        throw std::invalid_argument("A valid packed BGR image is required.");
    }

    const auto aspect_width = static_cast<std::int32_t>(std::ceil(
        static_cast<double>(recognition_height_) * static_cast<double>(width) / static_cast<double>(height)));
    const auto tensor_width = std::clamp(RoundUp(std::max(aspect_width, 1), 8), recognition_min_width_, recognition_max_width_);
    const auto resized_width = std::clamp(aspect_width, 1, tensor_width);

    TensorInput input;
    input.shape = {1, 3, recognition_height_, tensor_width};
    input.values.assign(static_cast<std::size_t>(3) * recognition_height_ * tensor_width, 0.0F);
    const auto plane = static_cast<std::size_t>(recognition_height_) * tensor_width;

    for (std::int32_t destination_y = 0; destination_y < recognition_height_; ++destination_y) {
        const double source_y = (static_cast<double>(destination_y) + 0.5) * height / recognition_height_ - 0.5;
        const auto y0 = std::clamp(static_cast<std::int32_t>(std::floor(source_y)), 0, height - 1);
        const auto y1 = std::min(y0 + 1, height - 1);
        const double fy = std::clamp(source_y - std::floor(source_y), 0.0, 1.0);
        for (std::int32_t destination_x = 0; destination_x < resized_width; ++destination_x) {
            const double source_x = (static_cast<double>(destination_x) + 0.5) * width / resized_width - 0.5;
            const auto x0 = std::clamp(static_cast<std::int32_t>(std::floor(source_x)), 0, width - 1);
            const auto x1 = std::min(x0 + 1, width - 1);
            const double fx = std::clamp(source_x - std::floor(source_x), 0.0, 1.0);
            for (std::int32_t channel = 0; channel < 3; ++channel) {
                const auto p00 = pixels[static_cast<std::size_t>(y0) * stride + x0 * 3 + channel];
                const auto p01 = pixels[static_cast<std::size_t>(y0) * stride + x1 * 3 + channel];
                const auto p10 = pixels[static_cast<std::size_t>(y1) * stride + x0 * 3 + channel];
                const auto p11 = pixels[static_cast<std::size_t>(y1) * stride + x1 * 3 + channel];
                const double top = p00 + (p01 - p00) * fx;
                const double bottom = p10 + (p11 - p10) * fx;
                const auto value = static_cast<float>((top + (bottom - top) * fy) / 127.5 - 1.0);
                const auto index = static_cast<std::size_t>(channel) * plane +
                    static_cast<std::size_t>(destination_y) * tensor_width + destination_x;
                input.values[index] = value;
            }
        }
    }
    return input;
}

} // namespace wuwa::ocr
