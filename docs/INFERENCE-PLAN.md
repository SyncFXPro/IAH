# Inference implementation plan

The weights are downloaded and the ONNX sessions have been opened. Inference is not wired into Glance yet. This is the order of work for doing that without pretending image landmarks are a 3D gaze ray.

Source repository: https://github.com/yakhyo/mediapipe-face-mesh-onnx (Apache-2.0). The mathematical model remains [math.md](../math.md).

## What was downloaded

Local only. `assets/models/*.onnx` is gitignored.

| File | Bytes | Role |
| --- | ---: | --- |
| `assets/models/face_detection_short_range.onnx` | 470549 | BlazeFace short-range detector |
| `assets/models/face_landmarker_Nx3x256x256.onnx` | 4864717 | 478-point face landmarker, including irises |

The 192-input 468-point mesh was not downloaded. It has no iris points, so it cannot fill `irisX` and `irisY`.

## Tensor contract, checked

Inspected on 2026-10-30 with Python `onnxruntime` 1.31.1, `CPUExecutionProvider`. Available providers in that install were `AzureExecutionProvider` and `CPUExecutionProvider`. DirectML was not present. A C# session must read these names from the graph and refuse to run if they differ.

Detector `face_detection_short_range.onnx`:

- Input `input`, float32, shape `[batch, 3, 128, 128]`
- Output `regressors`, float32, `[batch, 896, 16]`
- Output `scores`, float32, `[batch, 896, 1]` (logits, not probabilities)

Landmarker `face_landmarker_Nx3x256x256.onnx`:

- Input `input`, float32, shape `[batch, 3, 256, 256]`
- Output `landmarks`, float32, `[batch, 478, 3]` in crop pixels
- Output `score`, float32, `[batch, 1]` (a logit; confident faces are large positive numbers, not 0 to 1)

Dummy forward passes used an all-zero input, 5 warmup runs, then 30 timed runs, nearest-rank percentiles. This is the network only, on this machine, not a webcam frame and not preprocess.

| Graph | p50 | p95 |
| --- | ---: | ---: |
| Detector | 0.99 ms | 1.21 ms |
| Landmarker | 4.79 ms | 8.11 ms |

Those numbers are not the product latency. Letterboxing, anchor decode, the warp, and camera delay are still unmeasured.

## What the graphs do not do

Both files are the bare networks.

The detector does not decode boxes. Post-process, reimplemented in C# rather than shipped as Python, is:

1. Letterbox the frame to 128x128 (uniform scale, center pad, black borders).
2. Convert BGR to RGB and normalize with `(pixel - 127.5) / 127.5`, then NCHW.
3. Decode 896 anchors. MediaPipe's short-range config uses strides `[8, 16, 16, 16]`, one square anchor plus one extra scale per layer. Cells that share a stride stack: `16*16*2 + 8*8*6 = 896`.
4. For each anchor, `cx, cy = regressor[0:2] / 128 + anchor`, `w, h = regressor[2:4] / 128`. Six keypoints follow the same pattern in the remaining 12 values. Keypoint order is right eye, left eye, nose, mouth, right ear, left ear. Rows 0 and 1 are the two eyes and set the crop roll.
5. `sigmoid` the score logit. Drop scores under a named threshold (start at 0.5).
6. Weighted NMS at IoU 0.3: overlapping boxes are score-weighted averages, not hard deletes.
7. Undo the letterbox so boxes and keypoints are full-frame pixels.

The landmarker does not crop the face. For each kept box:

1. Build a square ROI. Side = `1.5 * max(box width, box height)` (25% margin on each side). Center is the box center. Roll is the angle of the eye line from detector keypoints 0 and 1. Default roll is 0 if those points are missing.
2. Warp that square to 256x256 in one bilinear sample. Out-of-frame pixels are 0. Keep the inverse affine.
3. BGR to RGB, divide by 255 so the range is `[0, 1]`, then NCHW. This is a different normalization from the detector.
4. Run the landmarker. Apply `sigmoid` to `score`.
5. Map landmark `x, y` from crop pixels back through the inverse affine. Scale `z` by `roi side / 256`. `z` is relative depth on that pixel scale, not a camera-space coordinate.

Iris points, per the upstream README and MediaPipe's 478-point layout: 468-472 left eye (center, right, top, left, bottom) and 473-477 right eye, same order. Confirm with a debug overlay before trusting them. The first 468 points stay in the standard face-mesh order.

If several faces survive NMS, keep the highest score for the status line and set the existing multiple-face flag. Calibration already rejects that flag.

## Features, without inventing a gaze ray

`math.md` says raw iris coordinates are not a 3D gaze vector. The first extractor must not construct a `GazeDirection`.

Fill `z = (headYaw, headPitch, irisX, irisY)` with image-space offsets, and tell `FeatureVelocity` that yaw and pitch are not radians (`YawAndPitchAreRadians = false`):

- `headYaw`: horizontal offset of a stable face point (nose tip) from the image center, divided by the distance between the two eye-corner midpoints.
- `headPitch`: the same vertically.
- `irisX`: iris center relative to that eye's corner segment, divided by the corner-to-corner width, averaged across both eyes.
- `irisY`: the same vertically, using the upper and lower eyelid landmarks.

The iris ring around the iris center is not enough. Those points move with the iris, so the center is always in the middle of the ring. The socket corners have to be the reference. Use MediaPipe's published corner indices as the hypothesis (33 and 133, 263 and 362) and prove them with an overlay. A wrong index should be obvious on that overlay before any calibration sample is stored.

`GazeAngles` stays unused until a later, explicitly labeled head-pose experiment. That experiment would be `solvePnP` with a documented subset of the canonical face, an assumed focal length (image width) and principal point (image center), and the resulting rotation turned into yaw and pitch by the formulas already in `GazeAngles`. That pose is still not an eye-gaze ray. It is out of the first implementation.

## Where it lands in this solution

`Glance.Tracking` owns the session, the decode, the warp, and the feature extractor. `Glance.Core` stays free of OpenCV and ONNX. `Glance.App` only swaps which pipeline `TrackingCoordinator` runs.

Packages, added to `Glance.Tracking` when implementation starts:

- `Microsoft.ML.OnnxRuntime` 1.31.1
- `OpenCvSharp4` 4.13.0.20260627
- `OpenCvSharp4.runtime.win` at the same version

DirectML is a later A/B test (`Microsoft.ML.OnnxRuntime.DirectML`), not the first provider. The dummy CPU times above are already in the same range as the 15 ms inference target, so the CPU path is the baseline.

Replace these current types rather than growing a second path beside them:

- `UnavailableFaceLandmarkModel` becomes a loader that reads `assets/models`, checks the tensor names above, and runs one face crop.
- `UnavailableCameraCapture.Start` stops throwing and opens a camera. `ListCameras` returns real devices.
- `UnavailableGazeFeatureExtractor` starts returning true only when a 478-point frame is present.
- `ScriptedMonitorPreview` remains the fallback when the files or the camera are missing. The dashboard text stays "scripted preview" in that case and switches to a webcam source only when a frame was actually processed.
- `TrackingCoordinator` keeps the latest frame instead of queuing. It already records stage timings. Feed `StageLatencyRecorder` for capture, preprocess, detection, landmarks, features, classification, and end to end. Also record the age of the frame at the moment inference starts.
- Cursor policy stays as it is. A webcam classifier may move the pointer only after a gaze profile exists and the user has enabled cursor movement. The first inference milestone does not flip that on.

`LandmarkObservation` is too small for this. Extend it with the 478 points in full-image pixels, the presence probability after sigmoid, the face count, and the capture timestamp. Do not add a field that looks like a metric 3D ray.

Copy the ONNX files to the build output with a content item, and look beside the executable as well as `assets/models`, so `dotnet run` and a launched exe both work. If a file is missing, stay on the scripted preview and say which file is missing.

## Order of work

Steps 1-3 are in the tree. `OnnxFacePipeline` loads both CPU sessions, decodes boxes, warps the crop, and draws the mesh in the dashboard Landmark preview. The preview does not publish yaw, pitch, or iris features, and it does not move the cursor. Steps 4-8 are not started. The tracking switch is still the scripted preview.

1. **Session smoke test.** Load both files in C#, run a zero tensor, assert the shapes above. Skip the test when the files are absent so a clean checkout still builds. No UI change.
2. **Detector decode.** Pure tests for anchor count (896), one hand-built regressor row, sigmoid, weighted NMS, and undoing a known letterbox. Then one real image or webcam frame in a manual harness, drawing boxes only.
3. **Crop and landmarks.** Warp tests with a known square and rotation. Run the landmarker on a detected face. Draw the 478 points and especially the 10 iris points. Fix indices before any feature math.
4. **Feature extractor.** Unit tests on a synthetic 478-point cloud where the iris center is shifted by a known fraction of the eye width. Assert `irisX` / `irisY` and that `GazeDirection` is never created. Set the radians flag to false.
5. **Camera.** Enumerate devices, capture on a background thread, latest-frame-wins, stop when tracking is off, survive unplug. Show the real webcam name on the dashboard. Still publish "not measured" until step 4 is connected.
6. **Live status.** Dashboard yaw, pitch, iris, and presence update from the extractor at the existing UI throttle. Classifier stays idle until a gaze profile exists. Scripted highlight stops once real features are flowing.
7. **Calibration samples.** While each target is showing, keep frames that pass `SampleQualityGate` until about 30 per monitor. Save a `gaze-profile` document. The display-addressing file remains a different kind and still cannot be classified.
8. **Classification on the live vector.** Predicted monitor and distance come from `MonitorClassifier`. The switch state machine already consumes that. Cursor movement stays off.

Steps 1-4 can land without a camera. Step 6 is the first time the dashboard is allowed to stop saying "Not measured".

## Tests that must exist before the dashboard trusts a number

- Tensor names and ranks match this document.
- Anchor generator length is 896.
- A known regressor maps to a known box in letterbox space and in image space.
- Weighted NMS blends two overlapping boxes instead of keeping only one.
- ROI side is 1.5 times the long side, and the roll matches the eye-keypoint angle.
- Inverse warp of the crop center lands on the ROI center.
- Synthetic iris shift produces the expected `irisX` and does not call `GazeAngles`.
- Missing ONNX files leave tracking on the scripted preview.
- Multiple faces set the multiple-face rejection.
- A gaze profile saved by the wizard loads, and a display-addressing file still does not.

## Explicitly later

- `solvePnP` head pose and `GazeAngles`.
- DirectML versus CPU on the same frames.
- Cursor relocation, and telling injected moves apart from the physical mouse.
- More than one calibration point per monitor.
- A second process. The tracker stays in-process until a profile says otherwise.
