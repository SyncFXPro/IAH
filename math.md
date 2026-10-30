## Update: 3D Gaze Direction and Monitor Calibration

We have refined the mathematical model for our Windows eye/head tracking utility.

**Important: Modify the existing implementation. Do not rebuild the project, replace the UI, or unnecessarily refactor working code.**

### 1. Mathematical model

Our previous approach considered horizontal eye movement. We now need to support arbitrary monitor arrangements, including monitors positioned above, below, or diagonally relative to one another.

Represent estimated gaze direction in 3D:

\[
\vec g=(g_x,g_y,g_z)
\]

For a camera coordinate system where X points right, Y points down, and Z points forward:

\[
yaw=\operatorname{atan2}(g_x,g_z)
\]

\[
pitch=\operatorname{atan2}(-g_y,\sqrt{g_x^2+g_z^2})
\]

These formulas assume we already have a meaningful 3D gaze direction. Raw MediaPipe iris coordinates are not equivalent to a 3D gaze vector.

Initially, use calibrated features from head pose and iris offsets if actual gaze-vector estimation is not available.

### 2. Monitor calibration

Do not assume physical monitor positions from Windows virtual desktop coordinates.

Windows provides screen rectangles, not physical monitor orientation.

During calibration:

1. Detect connected monitors.
2. Display a calibration target on each monitor.
3. Capture multiple stable head and eye observations.
4. Calculate a reference feature distribution for each monitor.
5. Save calibration results.

Each monitor should have an independent reference in the learned gaze-feature space.

Support horizontal, vertical, diagonal, and asymmetrical monitor arrangements.

Do not assume all monitors share the same physical height or angle.

### 3. Directional movement

For two valid 2D directional vectors, use signed angular displacement:

\[
\theta=\operatorname{atan2}(a_xb_y-a_yb_x,\ a_xb_x+a_yb_y)
\]

This preserves clockwise and counterclockwise direction, unlike an unsigned `acos` angle.

For 3D movement, use appropriate yaw/pitch changes or 3D directional comparisons. Do not apply the 2D signed-angle formula directly to 3D vectors.

Use timestamps to derive angular changes over time.

Velocity helps identify transitions, but **the final gaze position determines the destination monitor**, not velocity alone.

### 4. Monitor classification

Start with a calibrated feature vector:

\[
z=(headYaw,headPitch,irisX,irisY)
\]

Optionally include head translation and tracking-confidence features.

Compare live observations with each monitor's calibration distribution.

Begin with a normalized-distance classifier. Consider regularized Mahalanobis distance after sufficient samples are collected.

Do not use raw Euclidean distance across features with incompatible scales.

Add temporal filtering and hysteresis to prevent accidental monitor switching.

### 5. Validation

Add diagnostic output showing:

- Head yaw and pitch.
- Relative iris coordinates.
- Predicted monitor.
- Classification distance or confidence.
- Processing latency.
- Time since the last confirmed monitor switch.

Test both horizontal and vertically arranged monitor layouts.

Make sure head movements don't immediately cause incorrect switching.

### 6. Implementation constraints

Keep the existing WPF UI, tracking pipeline, and solution structure.

Separate mathematical logic from model-specific inference.

Write unit tests for angle calculations, calibration, classification, and switching behavior.

Do not fabricate 3D gaze measurements if the model only provides image-space landmarks.

Do not report calibration or monitor switching as accurate until tested against actual webcam observations.

**Goal:** Extend our existing prototype to support calibrated monitor selection in two angular dimensions, without assuming monitors are physically aligned.

Inspect the current code, identify the smallest necessary changes, implement them, run the build and relevant tests, and report what works versus what still requires real-world validation.