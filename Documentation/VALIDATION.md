# Kinect validation — 2026-10-03 JST

Environment: Unity 6000.6.4f1 / Windows x64 / GTX 1080 Ti.

## Actual sensor / Editor

| Check | Observed result |
|---|---|
| SDK | Official installed Sensor 1.4.2 / Body Tracking 1.1.2 files loaded |
| Capture | NFOV_2x2Binned, FPS30, color OFF |
| Tracker | DirectML startup successful; CPU fallback not exercised |
| BodyIndexMap | 320×288, approximately 29.99–30 FPS; **0 physical bodies observed** |
| Texture | Created and updated from real BodyIndexMap |
| No device | Initial actual enumeration was 0; clear warning, worker exited, Editor responsive. After user confirmed connection, retry succeeded |
| Normal stop | Repeated worker joins in 99–115 ms |
| Stop during native Tracker.Create | Creation flag confirmed; worker joined in 736 ms |
| Restart | Multiple successful Stop/Start and Play cycles |
| Disable KinectSystem | After 654 received frames: joined in 115 ms, renderer hidden, all colliders disabled, IsFresh=false |
| Scene | Saved, 6 roots, 3 Kinematic colliders, all references assigned, missing scripts=0, ball gravity=0 |
| Final compile | CLI: errors=0, warnings=0, compilationFailed=false; `Validation/final-recompile.json` |
| Final Console | Current Editor: 0 errors / 0 warnings. Historical MCP buffer retains the earlier expected disconnected warning |

## Synthetic validation

Unity EditMode **15/15 passed**, 0 failed/skipped. NUnit report: `Validation/EditMode-TestResults.xml`.

Covered border contours, holes, noise, disconnected/diagonal pieces, concavity/point budget, stable IDs/index reorder, 3-person cap and timeout, 16:9/square/portrait mapping, mirror/inverse mapping, buffer ownership/latest replacement, continuous wall reflection, Kinematic push and surface-motion estimation.

Shape-only polygon expansion separated the ball but left vx=0.000. This reproduced the requested contact-velocity concern before adding the independent helper.

`Tools/ValidatePlayModePhysics.cs` exercised real Play Mode collision callbacks in an isolated PhysicsScene2D:

| Expanding synthetic shape | Final ball x | Final vx |
|---|---:|---:|
| Physics alone | 1.005 | 0.000 |
| With SilhouetteBallPush | 1.110 | 1.500 |

`Tools/ValidateSyntheticFrame.cs` used the actual renderer/mapper and three artificial masks. The captured image was visually inspected: three colors, mirrored placement and uniform aspect fit.

- Synthetic image: `Assets/Documentation/Validation/synthetic-three-bodies.png` (**not physical people**).
- Live 0-person scene: `Assets/Documentation/Validation/live-initial.png`.
- Warm contour extraction: average **2.3994 ms per three bodies**, **0 managed bytes across 90 extractions**. This measures the contour routine, not total Editor/SDK allocation.
- Live 0-person sample: CPU frame 1.72 ms, main thread 0.66 ms, GPU 0.14 ms; collider scheduler 25Hz. This is not a sustained three-person benchmark.
- Profiler markers: `Naispo.Silhouette.TextureUpload`, `Naispo.Silhouette.ColliderRebuild`.

## Remaining physical acceptance

These are **unverified**; the full physical acceptance checklist remains open:

1. Real person detection, mirror direction, silhouette tracking latency and moving limb collision.
2. Two and three real people, distinct stable colors during entry/exit.
3. Natural ball pushing and resistance to tunnelling at expected exhibition movement speeds.
4. Physical USB disconnection during tracking and subsequent recovery.
5. Sustained three-person performance in the exhibition environment.

The user was asked to stand 2.5–4m in front of Kinect. No reply or nonzero body detection was available. The demo Scene is saved and the Editor is **stopped**.

## Known boundaries

- Native Tracker.Create has no cancellation API. The observed initialization-stop case joined successfully. A native call that fails to return within five seconds causes a warning and deferred cleanup; native code is never aborted. An absolute no-lingering-thread guarantee for a hung native SDK is not claimed.
- Standalone/other-PC/IL2CPP builds were not tested. Current backend: Mono2x.
- Mapper assumes an orthographic camera facing the XY plane, as configured.
- No Unity/SDK upgrades, external packages, OpenCV, or system SDK/PATH changes.
