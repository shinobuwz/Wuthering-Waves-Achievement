# Native OCR third-party components

## ONNX Runtime

- Project: Microsoft ONNX Runtime
- Version: 1.22.1
- Distribution: `Microsoft.ML.OnnxRuntime` NuGet package
- License: MIT
- Source: https://github.com/microsoft/onnxruntime

## OpenCV

- Project: OpenCV
- Version: 4.12.0 official Windows SDK
- Archive SHA-256: `b753b14d880b9bc8d89d6acd3b665c040baec0211078435432fcae117db707af`
- License: Apache-2.0
- Source: https://github.com/opencv/opencv

The SDK is downloaded to the user's local dependency cache by `scripts/build-native-ocr.ps1`; it is not committed to this repository.

## Clipper2

- Project: Clipper2
- Source commit: `f9c5eb6e14a59f6f5d65fbfb3564519a561cf4fd`
- License: Boost Software License 1.0
- Source: https://github.com/AngusJohnson/Clipper2

The required C++ source is vendored under `ocr/third_party/clipper2`; its upstream license is included there.
