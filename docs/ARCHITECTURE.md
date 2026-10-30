# Architecture

Glance is one process. Inference, when it exists, must run off the WPF UI thread. Nothing here is ready to split into a second process, but camera, model, and feature extraction are behind interfaces so a worker can replace them later.

## Projects

`Glance.Core` has no Windows or WPF dependency. It implements [math.md](../math.md):

- `GazeDirection` is a camera-frame vector: X right, Y down, Z forward. A zero vector is rejected. There is no constructor from iris pixels.
- `GazeAngles` uses `yaw = atan2(gx, gz)` and `pitch = atan2(-gy, sqrt(gx^2 + gz^2))`.
- `SignedAngle` is the 2D formula `atan2(ax*by - ay*bx, ax*bx + ay*by)`. It is not applied to 3D vectors.
- Yaw and pitch rates use a wrapped difference over a real `TimeSpan`.
- `z = (headYaw, headPitch, irisX, irisY)`.
- `MonitorClassifier` does not accept display rectangles. With fewer than 30 samples it uses normalized distance (each feature divided by that monitor's own scale). At 30 or more it uses regularized Mahalanobis distance, and falls back if the covariance cannot be inverted. Thirty is a heuristic, not a proof. The distance is not a probability.
- `MonitorSwitchStateMachine` confirms the classifier's monitor id after a dwell. Feature speed above the policy can delay confirmation. It cannot choose a different monitor. Timestamps are `Stopwatch` ticks.

`Glance.Windows` enumerates monitors with `EnumDisplayMonitors`, reads DPI, and can clamp a cursor into a work area that may start at a negative coordinate. `CursorRelocationPolicy` refuses to move the pointer unless the caller says the pipeline is a real webcam classifier. The app never makes that claim.

`Glance.Tracking` exposes `UnavailableCameraCapture` and `UnavailableFaceLandmarkModel`. Both fail visibly. `ScriptedMonitorPreview` only cycles monitor ids.

`Glance.App` hosts the shell. `TrackingCoordinator` is a `BackgroundService`. It polls the cursor position about once a second so a later milestone can restore it, and it does not call `SetCursorPos`.

## UI updates

The coordinator publishes a `TrackingStatus` at most every 500 ms when the highlight has not changed. Yaw, pitch, iris, distance, and latency fields are null until a real extractor fills them. The dashboard prints "Not measured" for those.

## Settings

`%LOCALAPPDATA%\Glance\settings.json`

`%LOCALAPPDATA%\Glance\display-addressing.json` is a placement check. `kind` is `display-addressing-check` and `containsGazeSamples` is false. `CalibrationDocument.TryReadGazeProfile` rejects it.

A gaze profile, when one exists later, must use `kind` `gaze-profile` and `containsGazeSamples` true. Simulated profiles are rejected unless a test opts in.
