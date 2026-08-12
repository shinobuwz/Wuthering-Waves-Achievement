#include "ocr_engine.h"

#include <opencv2/imgproc.hpp>

#include <algorithm>
#include <array>
#include <cmath>
#include <stdexcept>
#include <vector>

namespace wuwa::ocr {

void OcrEngine::EnableClassifier(const std::filesystem::path& model_path, float rotation_threshold) {
    if (!std::filesystem::is_regular_file(model_path)) throw IoError("Classifier model file does not exist.");
    if (!(rotation_threshold > 0.0F && rotation_threshold < 1.0F)) throw std::invalid_argument("Classifier rotation threshold must be between zero and one.");
    classifier_session_ = std::make_unique<Ort::Session>(environment_, model_path.c_str(), session_options_);
    classifier_input_names_storage_ = ReadNodeNames(*classifier_session_, true);
    classifier_output_names_storage_ = ReadNodeNames(*classifier_session_, false);
    classifier_input_names_ = NamePointers(classifier_input_names_storage_);
    classifier_output_names_ = NamePointers(classifier_output_names_storage_);
    if (classifier_input_names_.size() != 1 || classifier_output_names_.empty()) throw std::runtime_error("Classifier model must expose one image input and an output.");
    const auto input_shape = classifier_session_->GetInputTypeInfo(0).GetTensorTypeAndShapeInfo().GetShape();
    const auto output_shape = classifier_session_->GetOutputTypeInfo(0).GetTensorTypeAndShapeInfo().GetShape();
    if (input_shape.size() != 4 || (input_shape[1] > 0 && input_shape[1] != 3)) throw std::runtime_error("Classifier input must be NCHW with three channels.");
    if (output_shape.size() != 2 || (output_shape[1] > 0 && output_shape[1] != 2)) throw std::runtime_error("Classifier output must have shape [batch,2].");
    classifier_rotation_threshold_ = rotation_threshold;
}

bool OcrEngine::ShouldRotate180(const cv::Mat& image) {
    if (!classifier_session_) return false;
    constexpr int target_height = 48;
    constexpr int target_width = 192;
    const auto resized_width = std::clamp(static_cast<int>(std::ceil(target_height * image.cols / static_cast<double>(image.rows))), 1, target_width);
    cv::Mat resized;
    cv::resize(image, resized, cv::Size(resized_width, target_height), 0.0, 0.0, cv::INTER_LINEAR);
    std::vector<float> values(static_cast<std::size_t>(3) * target_height * target_width, 0.0F);
    const auto plane = static_cast<std::size_t>(target_height) * target_width;
    for (int y = 0; y < target_height; ++y) {
        const auto* row = resized.ptr<cv::Vec3b>(y);
        for (int x = 0; x < resized_width; ++x) {
            for (int channel = 0; channel < 3; ++channel) {
                values[static_cast<std::size_t>(channel) * plane + static_cast<std::size_t>(y) * target_width + x] =
                    static_cast<float>(row[x][channel]) / 127.5F - 1.0F;
            }
        }
    }
    std::array<std::int64_t, 4> shape{1, 3, target_height, target_width};
    auto memory = Ort::MemoryInfo::CreateCpu(OrtArenaAllocator, OrtMemTypeDefault);
    auto tensor = Ort::Value::CreateTensor<float>(memory, values.data(), values.size(), shape.data(), shape.size());
    auto outputs = classifier_session_->Run(Ort::RunOptions{nullptr}, classifier_input_names_.data(), &tensor, 1, classifier_output_names_.data(), 1);
    if (outputs.empty() || !outputs[0].IsTensor()) throw std::runtime_error("Classifier inference returned no tensor output.");
    const auto output_shape = outputs[0].GetTensorTypeAndShapeInfo().GetShape();
    if (output_shape.size() != 2 || output_shape[0] != 1 || output_shape[1] != 2) throw std::runtime_error("Classifier output shape is invalid.");
    const auto* probabilities = outputs[0].GetTensorData<float>();
    return probabilities[1] > probabilities[0] && probabilities[1] > classifier_rotation_threshold_;
}

} // namespace wuwa::ocr
