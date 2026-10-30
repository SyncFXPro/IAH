# Glance

IAH (I Am Here) is an open-source Windows 11 desktop utility. The application is named Glance. It is meant to notice which monitor you are looking at and, later, move the cursor there.

The mathematical model lives in [math.md](math.md). Code follows that document. It does not replace it.

This build is a skeleton. It is not a working eye tracker.

## What is real

- A Windows 11-style WPF shell, with Dashboard, Calibration, and Settings.
- A system tray icon. Closing the window leaves Glance running. Exit is on the tray menu.
- Settings saved under `%LOCALAPPDATA%\Glance\settings.json`.
- Optional sign-in startup, written only after you turn that setting on.
- Live Windows display enumeration, including negative virtual-desktop coordinates and monitors that sit above or beside each other.
- A calibration target that can be placed on a chosen display. Confirming it stores a display-addressing check only. That file has no gaze samples and the classifier refuses it.
- The math from `math.md`: yaw and pitch from a 3D direction, signed 2D angles, wrapped angular rate, normalized distance, and regularized Mahalanobis distance once a monitor has enough samples.
- A switch state machine. Fast feature change can open a candidate. Only a settled position classification can confirm a monitor.
- The pointer is never moved. A policy blocks relocation unless a real webcam classifier is running, and this build has no such classifier.

## What is not real

- No webcam is opened.
- No ONNX model is loaded. The weights are downloaded locally and gitignored. The implementation order is [docs/INFERENCE-PLAN.md](docs/INFERENCE-PLAN.md). Tensor names are in [docs/MODEL.md](docs/MODEL.md).
- Iris pixels are not turned into a 3D gaze vector. Head yaw, head pitch, iris position, classification distance, and frame latency stay "Not measured".
- The highlighted monitor in the dashboard, when eye tracking is on, is a timer. It is labeled as a scripted preview.
- No latency numbers in the UI were measured. The histogram type exists and is unit-tested with caller-supplied samples only.
- Monitor rectangles are drawn so you can see the Windows layout. They are not physical gaze angles.

## Run

Requires the .NET 9 SDK and the Windows desktop runtime.

```text
dotnet test tests/Glance.Tests/Glance.Tests.csproj
dotnet run --project src/Glance.App/Glance.App.csproj
```

Startup registration launches the same executable with `--background`, which opens the tray without the main window.

Sign-in registration is off until you enable it in Settings. Cursor movement stays disarmed in this build even if that preference is saved.

## Layout

```text
Glance.sln
src/Glance.App          WPF shell, tray, settings, scripted preview
src/Glance.Tracking     Camera and model adapters (not connected)
src/Glance.Core         math.md, calibration, classification, switching
src/Glance.Windows      Displays, cursor policy, startup registration
tests/Glance.Tests
assets/models           Weights are not vendored
docs
math.md                 Mathematical model
```

## Privacy

Frames, landmarks, and calibration samples are not uploaded. This build does not capture frames. The only files written are local settings, an optional display-addressing check, and an optional log if you enable it. There is no telemetry.

## Next step

Port the face detector post-process and the 478-point landmarker described in [docs/MODEL.md](docs/MODEL.md). Until that exists, do not treat dashboard highlights as gaze.
