# Kinect implementation checkpoint

Last updated: 2026-10-03 04:58 JST
State: IMPLEMENTATION_AND_AUTOMATED_VALIDATION_COMPLETE; PHYSICAL_PERSON_ACCEPTANCE_PENDING.
Editor: stopped; KinectSilhouetteDemo saved; no worker remained at last shutdown.
Automatic continuation: heartbeat `kinect` is PAUSED after autonomous completion, confirmed by the app tool. All work and results are saved on disk. Continue this task when a person is ready for physical testing.

## Requirements and constraints
- Full user prompt: `Documentation/KINECT_REQUIREMENTS.txt` (read before resuming).
- Implement in this Unity project; no Unity/SDK/package version upgrades, SDK removal, system modifications, or large external dependencies.
- Stop and ask only for the major issues listed in section 3 of the prompt.
- Never claim physical interaction or sensor imagery verified unless actually observed.
- Keep changes and verification results on disk after each phase. This folder is not a Git repository.

## Confirmed environment
- Root: `C:\Users\arima\Desktop\NAISPO\Unity\Naispo_shadow`
- Unity 6000.6.4f1; URP 17.6.0; Pipeline 0.8.0-exp.1.
- Unity CLI 1.0.0-beta.12 and MCP connected, port 7800.
- Current SampleScene has two root objects and UNSAVED changes on entry. Preserve it; build a separate demo scene without discarding user changes.
- Initial Console: 0 errors, 0 warnings; not playing or compiling.
- Sensor SDK: `C:\Program Files\Azure Kinect SDK v1.4.2`.
- Body Tracking SDK: `C:\Program Files\Azure Kinect Body Tracking SDK`.
- Both SDKs contain netstandard2.0 managed assemblies, amd64 native binaries and headers. Body SDK contains ONNX models and DirectML.
- No Azure Kinect viewer process was running at investigation time.

## Implementation decisions
- Use installed official netstandard2.0 Sensor / BodyTracking assemblies. Actual wrapper API is `SetTemporalSmooting` (SDK spelling), `TrackerProcessingMode.DirectML`, `Frame.GetBodyId`, `Image.Memory`.
- Unity provides Memory/Buffers/Unsafe facades; do not copy redundant System DLLs.
- Windows x64 plugin importers configured; native dependency load order is explicit and process-local (no PATH/system change).
- SDK copies + SHA256: `Documentation/SDK_COPY_MANIFEST.json`; licenses retained under Documentation/Licenses.
- No CUDA/TensorRT dependencies copied; DirectML + CPU supported configuration.
- Created demo scene additively, then completed all objects. SampleScene edits were preserved and saved, with the earlier disk version backed up.

## Next steps
1. Read `KINECT_SETUP.md` and `Documentation/VALIDATION.md`. Do not redo completed implementation or SDK investigation.
2. Await the user's confirmation that a person can stand 2.5–4m in front of Kinect. No reply to that request has arrived.
3. Then enter Play Mode, verify actual nonzero body IDs, capture Game View at native dimensions, check real silhouette/mirror/ball interactions, and test 2–3 people if available.
4. Record physical results honestly. The full physical acceptance checklist remains open. Standalone/IL2CPP and hot-unplug are unverified.

## Verification ledger
- 2026-10-03: `unity recompile ... --json` completed with 0 errors/0 warnings. CLI briefly reported stale compilationFailed=true; subsequent MCP recompile_status and console_status both false. Runtime source compiled and was instantiated.
- Play Mode: DLL loading succeeded; `Device.GetInstalledCount()` was 0. Warning accurately reports disconnected Kinect. WorkerAlive=false, ReceivedFrames=0, HasFailed=true; Editor responsive. Play Mode stopped.
- Asked user asynchronously to check power / USB 3; no answer yet. Real sensor/tracker/BodyIndexMap acceptance criteria remain UNVERIFIED.
- User subsequently confirmed connection. Retry: Sensor connected, NFOV_2x2Binned / FPS30 / color OFF; Tracker ready DirectML; 261 received frames, 29.996 FPS, 320x288 map, BodyCount=0. Real person detection remains to be observed.
- Native Tracker.Create has no cancellation API. Current bounded stop joins 5 seconds and reports lingering native initialization rather than aborting native code; review/test this limitation before final acceptance.
- Real-source StopCapture: worker joined, 112 ms. Re-entered Play Mode successfully with DirectML, 29.99 FPS, texture created. Current Game view aspect ~2.06; uniform mapping 0.03472222 units/pixel.
- SampleScene disk version backed up to Documentation/Backups; previously unsaved user edits were saved, then SampleScene closed. Demo is now the only open scene.
- Latest CLI compile: 0 errors, 0 warnings, compilationFailed=false.
- Unity EditMode tests: 11/11 passed (component separation, holes filled, border contours, concavity and point budget, stable IDs, 4-person cap/timeout, three aspect/mirror cases, triple buffer ownership/latest replacement).
- Extended Unity EditMode tests: 15/15 passed, including continuous wall reflection, kinematic push and contact-motion estimation.
- Baseline polygon-path-only expansion physically separated the ball but left vx=0.000. Added isolated `SilhouetteSurfaceMotion` + `SilhouetteBallPush` (no Kinect dependency) based on previous world-space contour/contact motion.
- Actual Play Mode isolated physics test (`Tools/ValidatePlayModePhysics.cs`): baseline vx=0.000, assisted vx=1.500; ball x=1.005 vs 1.110. Real collision callbacks exercised.
- `Tools/ValidateSyntheticFrame.cs`: synthetic 3-body visual/contour validation, average 2.3994 ms per 3 masks, 0 allocated bytes over 90 warmed contour extractions. These are synthetic results, NOT physical people.
- Images in `Assets/Documentation/Validation/`: live-initial.png (0 bodies), synthetic-three-bodies.png (three colored mirrored masks). Both visually inspected.
- MCP performance (0 real bodies): CPU frame 1.72 ms / main thread 0.66 ms, GPU 0.14 ms; Kinect 30 FPS; collider scheduler 25 Hz.
- Asked user to stand at 2.5–4m; no reply at this checkpoint. Sensor continues detecting 0 bodies. Do not claim physical mirror/motion/person-ball interaction or 3 real people verified yet.
- Final lifecycle: native Tracker.Create flag was true when stopped; worker joined in 736 ms. Normal stops 99–115 ms. Multiple successful restarts.
- Final disable check: after 654 real frames, disabling KinectSystem joined worker in 115 ms, hid renderer, disabled colliders and cleared frame freshness.
- Final Scene audit: saved, 6 roots, 3 Kinematic person colliders, missing scripts=0, required references assigned, ball gravity=0. Editor stopped.
- Final CLI result saved to `Documentation/Validation/final-recompile.json`: 0 errors/0 warnings. Current Editor Console also 0 errors/0 warnings.
- NUnit 15/15 report copied to `Documentation/Validation/EditMode-TestResults.xml`; all validation evidence and remaining physical checks in `Documentation/VALIDATION.md`.
- Setup instructions, DLL placement, settings, known limits and SDF replacement points documented in `KINECT_SETUP.md`.
