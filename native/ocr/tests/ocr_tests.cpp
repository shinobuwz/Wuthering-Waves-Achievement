#include "ocr_engine.h"

#include <cmath>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

namespace {

void Require(bool condition, const char* message) {
    if (!condition) {
        throw std::runtime_error(message);
    }
}

void TestDictionary() {
    const auto path = std::filesystem::temp_directory_path() / "wuwa-ocr-dictionary-test.txt";
    {
        std::ofstream writer(path, std::ios::binary | std::ios::trunc);
        writer << "A\r\n";
        writer << "鸣\n";
    }
    const auto without_space = wuwa::ocr::OcrEngine::LoadDictionary(path, false);
    Require(without_space.size() == 2, "Dictionary must preserve two non-empty entries.");
    Require(without_space[0] == "A", "Dictionary must remove CRLF endings.");
    Require(without_space[1] == "鸣", "Dictionary must preserve UTF-8 entries.");
    const auto with_space = wuwa::ocr::OcrEngine::LoadDictionary(path, true);
    Require(with_space.size() == 3 && with_space.back() == " ", "Dictionary must append the optional space class.");
    std::filesystem::remove(path);
}

void TestCtcDecode() {
    const std::vector<std::string> characters{"A", "鸣"};
    const std::vector<float> logits{
        0.90F, 0.05F, 0.05F,
        0.05F, 0.80F, 0.15F,
        0.05F, 0.70F, 0.25F,
        0.90F, 0.05F, 0.05F,
        0.05F, 0.10F, 0.85F};
    const auto result = wuwa::ocr::OcrEngine::DecodeCtc(logits.data(), 5, 3, characters);
    Require(result.text == "A鸣", "CTC decode must remove blanks and collapse adjacent duplicates.");
    Require(std::fabs(result.score - 0.825F) < 0.0001F, "CTC confidence must average selected token probabilities.");
}

void TestModelSmoke(const wchar_t* model, const wchar_t* dictionary, const wchar_t* detection_model, const wchar_t* classifier_model) {
    WuwaOcrConfig config{};
    config.abi_version = wuwa::ocr::kAbiVersion;
    config.recognition_model_path = model;
    config.character_dictionary_path = dictionary;
    config.intra_op_threads = 2;
    config.inter_op_threads = 1;
    config.recognition_height = 48;
    config.recognition_min_width = 320;
    config.recognition_max_width = 320;
    config.include_space_character = 1;

    wuwa::ocr::OcrEngine engine(config);
    std::vector<std::uint8_t> pixels(48U * 160U * 3U, 255U);
    const auto result = engine.RecognizeBgr(pixels.data(), 160, 48, 160 * 3);
    Require(std::isfinite(result.score), "Model smoke must return a finite confidence score.");
    if (detection_model != nullptr && *detection_model != L'\0') {
        engine.EnableDetection(detection_model, 0.3F, 0.6F, 1.5F, 64);
        if (classifier_model != nullptr && *classifier_model != L'\0') engine.EnableClassifier(classifier_model, 0.9F);
        const auto& page = engine.DetectAndRecognizeBgr(pixels.data(), 160, 48, 160 * 3);
        Require(page.empty(), "A blank image should not produce accepted text lines.");
    }
}

} // namespace

int wmain(int argc, wchar_t** argv) {
    try {
        TestDictionary();
        TestCtcDecode();
        if (argc >= 3) {
            TestModelSmoke(argv[1], argv[2], argc >= 4 ? argv[3] : L"", argc >= 5 ? argv[4] : L"");
        }
        std::cout << "Wuwa.Ocr.Native tests passed.\n";
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << "Wuwa.Ocr.Native tests failed: " << exception.what() << '\n';
        return 1;
    }
}
