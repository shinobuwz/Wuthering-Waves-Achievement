#include "ocr_engine.h"

#if defined(_MSC_VER)
#pragma warning(push)
#pragma warning(disable : 4702)
#endif
#include <clipper2/clipper.h>
#if defined(_MSC_VER)
#pragma warning(pop)
#endif
#include <opencv2/imgproc.hpp>

#include <algorithm>
#include <array>
#include <cmath>
#include <numeric>
#include <stdexcept>
#include <vector>

namespace wuwa::ocr {
namespace {

struct DetectionInput {
    std::vector<float> values;
    std::array<std::int64_t, 4> shape{};
    float scale_x = 1.0F;
    float scale_y = 1.0F;
};

DetectionInput PrepareDetectionInput(const cv::Mat& image, std::int32_t limit_side_length) {
    const auto ratio = std::min(image.rows, image.cols) < limit_side_length
        ? static_cast<double>(limit_side_length) / std::min(image.rows, image.cols)
        : 1.0;
    const auto target_width = std::max(static_cast<int>(std::round(image.cols * ratio / 32.0) * 32), 32);
    const auto target_height = std::max(static_cast<int>(std::round(image.rows * ratio / 32.0) * 32), 32);
    cv::Mat resized;
    cv::resize(image, resized, cv::Size(target_width, target_height), 0.0, 0.0, cv::INTER_LINEAR);

    DetectionInput input;
    input.shape = {1, 3, target_height, target_width};
    input.scale_x = static_cast<float>(target_width) / image.cols;
    input.scale_y = static_cast<float>(target_height) / image.rows;
    input.values.resize(static_cast<std::size_t>(3) * target_height * target_width);
    constexpr float mean[] = {0.485F, 0.456F, 0.406F};
    constexpr float deviation[] = {0.229F, 0.224F, 0.225F};
    const auto plane = static_cast<std::size_t>(target_height) * target_width;
    for (int y = 0; y < target_height; ++y) {
        const auto* row = resized.ptr<cv::Vec3b>(y);
        for (int x = 0; x < target_width; ++x) {
            for (int channel = 0; channel < 3; ++channel) {
                input.values[static_cast<std::size_t>(channel) * plane + static_cast<std::size_t>(y) * target_width + x] =
                    (static_cast<float>(row[x][channel]) / 255.0F - mean[channel]) / deviation[channel];
            }
        }
    }
    return input;
}

std::array<cv::Point2f, 4> OrderBox(const cv::RotatedRect& rectangle) {
    cv::Point2f points[4];
    rectangle.points(points);
    std::array<cv::Point2f, 4> ordered{};
    const auto by_sum_min = std::min_element(points, points + 4, [](auto left, auto right) { return left.x + left.y < right.x + right.y; });
    const auto by_sum_max = std::max_element(points, points + 4, [](auto left, auto right) { return left.x + left.y < right.x + right.y; });
    const auto by_diff_min = std::min_element(points, points + 4, [](auto left, auto right) { return left.y - left.x < right.y - right.x; });
    const auto by_diff_max = std::max_element(points, points + 4, [](auto left, auto right) { return left.y - left.x < right.y - right.x; });
    ordered[0] = *by_sum_min;
    ordered[1] = *by_diff_min;
    ordered[2] = *by_sum_max;
    ordered[3] = *by_diff_max;
    return ordered;
}

float BoxScore(const cv::Mat& probability, const std::array<cv::Point2f, 4>& box) {
    const auto bounds = cv::boundingRect(std::vector<cv::Point2f>(box.begin(), box.end())) & cv::Rect(0, 0, probability.cols, probability.rows);
    if (bounds.empty()) return 0.0F;
    cv::Mat mask(bounds.height, bounds.width, CV_8UC1, cv::Scalar(0));
    std::vector<cv::Point> local;
    local.reserve(4);
    for (const auto& point : box) local.emplace_back(cvRound(point.x) - bounds.x, cvRound(point.y) - bounds.y);
    cv::fillPoly(mask, std::vector<std::vector<cv::Point>>{local}, cv::Scalar(1));
    return static_cast<float>(cv::mean(probability(bounds), mask)[0]);
}

std::vector<cv::Point2f> Unclip(const std::array<cv::Point2f, 4>& box, float ratio) {
    Clipper2Lib::PathD path;
    path.reserve(box.size());
    for (const auto& point : box) path.emplace_back(point.x, point.y);
    const auto area = std::abs(Clipper2Lib::Area(path));
    const auto perimeter = Clipper2Lib::Length(path, true);
    if (perimeter <= 0.0) return {};
    const auto expanded = Clipper2Lib::InflatePaths(
        Clipper2Lib::PathsD{path},
        area * ratio / perimeter,
        Clipper2Lib::JoinType::Round,
        Clipper2Lib::EndType::Polygon);
    if (expanded.size() != 1 || expanded.front().size() < 4) return {};
    std::vector<cv::Point2f> result;
    result.reserve(expanded.front().size());
    for (const auto& point : expanded.front()) result.emplace_back(static_cast<float>(point.x), static_cast<float>(point.y));
    return result;
}

void SortReadingOrder(std::vector<std::array<cv::Point2f, 4>>& boxes) {
    std::sort(boxes.begin(), boxes.end(), [](const auto& left, const auto& right) {
        if (left[0].y == right[0].y) return left[0].x < right[0].x;
        return left[0].y < right[0].y;
    });
    for (std::size_t index = 0; index + 1 < boxes.size(); ++index) {
        for (auto current = index; current < boxes.size() - 1; ++current) {
            if (std::abs(boxes[current + 1][0].y - boxes[current][0].y) < 10.0F &&
                boxes[current + 1][0].x < boxes[current][0].x) {
                std::swap(boxes[current], boxes[current + 1]);
            } else {
                break;
            }
        }
    }
}

} // namespace

void OcrEngine::EnableDetection(
    const std::filesystem::path& model_path,
    float bitmap_threshold,
    float box_threshold,
    float unclip_ratio,
    std::int32_t limit_side_length) {
    if (!std::filesystem::is_regular_file(model_path)) throw IoError("Detection model file does not exist.");
    if (!(bitmap_threshold > 0.0F && bitmap_threshold < 1.0F) ||
        !(box_threshold > 0.0F && box_threshold < 1.0F) || unclip_ratio <= 0.0F || limit_side_length < 32) {
        throw std::invalid_argument("Detection thresholds, unclip ratio, and side limit are invalid.");
    }
    detection_session_ = std::make_unique<Ort::Session>(environment_, model_path.c_str(), session_options_);
    detection_input_names_storage_ = ReadNodeNames(*detection_session_, true);
    detection_output_names_storage_ = ReadNodeNames(*detection_session_, false);
    detection_input_names_ = NamePointers(detection_input_names_storage_);
    detection_output_names_ = NamePointers(detection_output_names_storage_);
    if (detection_input_names_.size() != 1 || detection_output_names_.empty()) throw std::runtime_error("Detection model must expose one image input and an output.");
    const auto input_shape = detection_session_->GetInputTypeInfo(0).GetTensorTypeAndShapeInfo().GetShape();
    const auto output_shape = detection_session_->GetOutputTypeInfo(0).GetTensorTypeAndShapeInfo().GetShape();
    if (input_shape.size() != 4 || (input_shape[1] > 0 && input_shape[1] != 3)) throw std::runtime_error("Detection input must be NCHW with three channels.");
    if (output_shape.size() != 4 || (output_shape[1] > 0 && output_shape[1] != 1)) throw std::runtime_error("Detection output must have shape [batch,1,height,width].");
    detection_bitmap_threshold_ = bitmap_threshold;
    detection_box_threshold_ = box_threshold;
    detection_unclip_ratio_ = unclip_ratio;
    detection_limit_side_length_ = limit_side_length;
}

std::vector<std::array<cv::Point2f, 4>> OcrEngine::Detect(const cv::Mat& image) {
    if (!detection_session_) throw std::logic_error("Detection is not enabled on this OCR handle.");
    auto input = PrepareDetectionInput(image, detection_limit_side_length_);
    auto memory = Ort::MemoryInfo::CreateCpu(OrtArenaAllocator, OrtMemTypeDefault);
    auto tensor = Ort::Value::CreateTensor<float>(memory, input.values.data(), input.values.size(), input.shape.data(), input.shape.size());
    auto outputs = detection_session_->Run(Ort::RunOptions{nullptr}, detection_input_names_.data(), &tensor, 1, detection_output_names_.data(), 1);
    if (outputs.empty() || !outputs[0].IsTensor()) throw std::runtime_error("Detection inference returned no tensor output.");
    const auto shape = outputs[0].GetTensorTypeAndShapeInfo().GetShape();
    if (shape.size() != 4 || shape[0] != 1 || shape[1] != 1 || shape[2] <= 0 || shape[3] <= 0) throw std::runtime_error("Detection output shape is invalid.");
    cv::Mat probability(static_cast<int>(shape[2]), static_cast<int>(shape[3]), CV_32FC1, outputs[0].GetTensorMutableData<float>());
    cv::Mat bitmap;
    cv::threshold(probability, bitmap, detection_bitmap_threshold_, 255.0, cv::THRESH_BINARY);
    bitmap.convertTo(bitmap, CV_8UC1);
    std::vector<std::vector<cv::Point>> contours;
    cv::findContours(bitmap, contours, cv::RETR_LIST, cv::CHAIN_APPROX_SIMPLE);

    std::vector<std::array<cv::Point2f, 4>> boxes;
    const auto candidate_count = std::min<std::size_t>(contours.size(), 1000);
    for (std::size_t index = 0; index < candidate_count; ++index) {
        auto rectangle = cv::minAreaRect(contours[index]);
        if (std::min(rectangle.size.width, rectangle.size.height) < 3.0F) continue;
        const auto box = OrderBox(rectangle);
        if (BoxScore(probability, box) < detection_box_threshold_) continue;
        const auto expanded = Unclip(box, detection_unclip_ratio_);
        if (expanded.empty()) continue;
        rectangle = cv::minAreaRect(expanded);
        if (std::min(rectangle.size.width, rectangle.size.height) < 5.0F) continue;
        auto expanded_box = OrderBox(rectangle);
        for (auto& point : expanded_box) {
            point.x = std::clamp(point.x / input.scale_x, 0.0F, static_cast<float>(image.cols - 1));
            point.y = std::clamp(point.y / input.scale_y, 0.0F, static_cast<float>(image.rows - 1));
        }
        boxes.push_back(expanded_box);
    }
    SortReadingOrder(boxes);
    return boxes;
}

cv::Mat OcrEngine::CropTextLine(const cv::Mat& image, const std::array<cv::Point2f, 4>& box) {
    const auto width = std::max(
        cv::norm(box[0] - box[1]),
        cv::norm(box[2] - box[3]));
    const auto height = std::max(
        cv::norm(box[0] - box[3]),
        cv::norm(box[1] - box[2]));
    const auto target_width = std::max(1, cvRound(width));
    const auto target_height = std::max(1, cvRound(height));
    std::array<cv::Point2f, 4> destination{
        cv::Point2f(0.0F, 0.0F), cv::Point2f(static_cast<float>(target_width), 0.0F),
        cv::Point2f(static_cast<float>(target_width), static_cast<float>(target_height)), cv::Point2f(0.0F, static_cast<float>(target_height))};
    const auto transform = cv::getPerspectiveTransform(box.data(), destination.data());
    cv::Mat crop;
    cv::warpPerspective(image, crop, transform, cv::Size(target_width, target_height), cv::INTER_CUBIC, cv::BORDER_REPLICATE);
    if (crop.rows > crop.cols * 1.5) cv::rotate(crop, crop, cv::ROTATE_90_COUNTERCLOCKWISE);
    return crop;
}

const std::vector<TextLineResult>& OcrEngine::DetectAndRecognizeBgr(
    const std::uint8_t* pixels,
    std::int32_t width,
    std::int32_t height,
    std::int32_t stride) {
    if (pixels == nullptr || width <= 0 || height <= 0 || stride < width * 3) throw std::invalid_argument("A valid packed BGR image is required.");
    const cv::Mat image(height, width, CV_8UC3, const_cast<std::uint8_t*>(pixels), stride);
    last_page_.clear();
    for (const auto& box : Detect(image)) {
        auto crop = CropTextLine(image, box);
        if (ShouldRotate180(crop)) cv::rotate(crop, crop, cv::ROTATE_180);
        auto recognition = RecognizeMat(crop);
        if (recognition.text.empty()) continue;
        TextLineResult result;
        for (std::size_t point = 0; point < box.size(); ++point) {
            result.points[point * 2] = box[point].x;
            result.points[point * 2 + 1] = box[point].y;
        }
        result.text = std::move(recognition.text);
        result.score = recognition.score;
        last_page_.push_back(std::move(result));
    }
    last_error_.clear();
    return last_page_;
}

RecognitionResult OcrEngine::RecognizeMat(const cv::Mat& image) {
    return RecognizeBgr(image.data, image.cols, image.rows, static_cast<std::int32_t>(image.step));
}

} // namespace wuwa::ocr
