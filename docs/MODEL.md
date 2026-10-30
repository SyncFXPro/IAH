# Landmark model notes

The two ONNX files used by the plan are downloaded locally and gitignored. Their tensor names were read from an ONNX Runtime session on 2026-10-30. Implementation order is in [INFERENCE-PLAN.md](INFERENCE-PLAN.md). A CPU session, detector decode, and landmark overlay now run from the dashboard Landmark preview. That preview does not publish gaze features.

Reference: https://github.com/yakhyo/mediapipe-face-mesh-onnx

License of that repository: Apache-2.0. The mesh topology and weights originate with Google's MediaPipe, also Apache-2.0. Glance does not vendor those weights.

## Why the 478-point model

`math.md` needs head yaw, head pitch, and relative iris position. The upstream README describes two landmark networks:

| Graph | Input (documented) | Points |
| --- | --- | --- |
| `face_mesh_Nx3x192x192.onnx` | NCHW 192 | 468, no iris |
| `face_landmarker_Nx3x256x256.onnx` | NCHW 256 | 478, iris appended |

The 468-point mesh is the wrong default because it has no iris points. The README says the 478-point file keeps the first 468 points in MediaPipe order and appends irises: 468-472 left (center, right, top, left, bottom) and 473-477 right, same order.

A third graph, `face_detection_short_range.onnx`, is the BlazeFace short-range detector. Documented input is NCHW 128. Documented output is 896 anchors. Anchor decoding, weighted NMS, and mapping the crop back to the image are not inside the ONNX file. The upstream implementation of that post-process is Python.

The README also says the raw face score is a logit. A sigmoid is applied in their Python wrapper. A confident face is described as landing around 20 to 40 before that sigmoid.

The crop they document is a square from the long side of the detector box, scaled 1.5x (25% margin), rotated so the eyes are level. They report the model tolerates about 10% shift or scale error and about 8 degrees of roll. Glance must not assume a fixed crop in the webcam frame.

Weights are release downloads, not files in the git tree. This repository does not include them. `assets/models/README.md` says where they would go.

## Checked session metadata

`onnxruntime` 1.31.1, CPU provider, on the development machine:

| File | Input | Outputs |
| --- | --- | --- |
| `face_detection_short_range.onnx` | `input` float32 `[batch, 3, 128, 128]` | `regressors` `[batch, 896, 16]`, `scores` `[batch, 896, 1]` |
| `face_landmarker_Nx3x256x256.onnx` | `input` float32 `[batch, 3, 256, 256]` | `landmarks` `[batch, 478, 3]`, `score` `[batch, 1]` |

`scores` and `score` are logits. A zero-tensor forward pass on that CPU session was about 1 ms (detector) and 5 ms (landmarker) at the median. That is not a webcam measurement. See [INFERENCE-PLAN.md](INFERENCE-PLAN.md).

## Implemented for the preview

`Glance.Tracking` loads both graphs on the CPU provider, checks the tensor names above, and runs a zero tensor in tests when the files are present. Detector post-process is letterbox, sigmoid, 896 anchors, weighted NMS, and unletterbox. The landmarker crop is one bilinear warp to 256. Landmark x and y are mapped back with the inverse affine, and z is scaled by the ROI side over 256. The dashboard Landmark preview draws boxes, the 478 points, and larger marks on iris indices 468-477.

`IFaceLandmarkModel.Load` still throws. `IGazeFeatureExtractor.TryExtract` still returns false. The tracking switch still runs the scripted preview and still does not open the webcam. The preview camera is a separate window.

## Still not implemented

Image landmarks are not a gaze vector. Yaw, pitch, and iris offsets are not filled. Calibration still stores no gaze samples. The cursor still does not move.

DirectML is a later benchmark, not an assumption that a GPU is faster.
