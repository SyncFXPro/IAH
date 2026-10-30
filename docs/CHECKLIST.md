# Checklist

## Done in this skeleton

- [x] Solution and projects: App, Tracking, Core, Windows, Tests
- [x] WPF UI shell: Dashboard, Calibration, Settings, navigation
- [x] Tray icon, close-to-tray, Exit
- [x] JSON settings
- [x] Windows display enumeration, including negative and vertical positions
- [x] Calibration target placement on a selected display
- [x] Display-addressing file that cannot be loaded as gaze calibration
- [x] math.md yaw, pitch, signed 2D angle, angular rate
- [x] Normalized-distance classifier, Mahalanobis after enough samples
- [x] Switch state machine with dwell, cooldown, and a fast-motion hold
- [x] Cursor relocation policy that refuses simulated tracking
- [x] Unit tests for the math, classifier, switching, layout, and cursor clamp
- [x] Scripted monitor preview, labeled as such
- [x] Detector and 478-point landmarker ONNX files downloaded locally (gitignored)
- [x] ONNX input and output names checked; implementation plan in `docs/INFERENCE-PLAN.md`
- [x] C# CPU session, detector decode, ROI warp, and a landmark preview that draws boxes and the 478 points
- [x] Camera list, saved webcam, and live capture while eye tracking is on
- [x] Image-space head and iris offsets on the dashboard. These are not angles
- [x] Calibration keeps repeated frames at five points per monitor and saves that region in `gaze-profile.json`
- [x] A live vector is accepted only when it lies inside one monitor's five-point region. Overlap and points outside the region are rejected. The cursor stays off
- [x] Page height is limited to the navigation content area so the dashboard, settings, and calibration pages can scroll

## Next

- [ ] Cursor movement from a confirmed real classification, still behind the existing relocation policy

## Later

- [ ] Separation of injected cursor motion from physical mouse motion
- [ ] Measured latency (p50, p95, p99) on real frames, including webcam buffering
- [ ] Installer

## Checks run

- `dotnet test tests/Glance.Tests/Glance.Tests.csproj` on this machine
- The shell was launched and the dashboard and calibration pages were read back from the window

No claim is made that monitor classification is accurate. After a gaze profile is saved, the dashboard shows the nearest monitor and the distance. The cursor still does not move.
