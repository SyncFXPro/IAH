# Model files

Do not commit ONNX weights. `*.onnx` in this folder is gitignored.

These two files are downloaded locally for the inference work in `docs/INFERENCE-PLAN.md`:

- `face_detection_short_range.onnx` (470549 bytes)
- `face_landmarker_Nx3x256x256.onnx` (4864717 bytes)

They came from the upstream release tagged `weights` on https://github.com/yakhyo/mediapipe-face-mesh-onnx. Session input and output names were checked with ONNX Runtime 1.31.1 and are recorded in `docs/MODEL.md`.

The 192-input face mesh without iris landmarks is not used. The application does not load this folder yet.
