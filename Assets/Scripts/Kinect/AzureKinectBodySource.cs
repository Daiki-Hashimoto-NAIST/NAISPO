using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Microsoft.Azure.Kinect.Sensor;
using Microsoft.Azure.Kinect.BodyTracking;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace Naispo.Kinect
{
    [DisallowMultipleComponent]
    public sealed class AzureKinectBodySource : MonoBehaviour
    {
        public KinectSettings settings;
        [SerializeField] string status = "Stopped";
        [SerializeField] string activeProcessingMode;
        [SerializeField] long receivedFrames;
        [SerializeField] float captureFps;
        [SerializeField] int detectedBodyCount, mapWidth, mapHeight;
        public string Status => status;
        public float CaptureFps => captureFps;
        public long ReceivedFrames => Interlocked.Read(ref receivedFrames);
        public bool WorkerAlive => worker != null && worker.IsAlive;
        public bool HasFailed => failed;
        public bool IsInitializingTracker => initializingTracker;

        LatestBodyFrameBuffer frames = new LatestBodyFrameBuffer();
        readonly ConcurrentQueue<Notice> notices = new ConcurrentQueue<Notice>();
        Thread worker;
        volatile bool stopRequested, failed;
        volatile bool initializingTracker;
        readonly object trackerGate = new object();
        Tracker liveTracker;
        struct Notice { public string Text; public bool Error; public Notice(string t, bool e) { Text = t; Error = e; } }

        void OnEnable() { StartCapture(); }
        public void StartCapture()
        {
            if (WorkerAlive) return;
            if (settings == null) { status = "KinectSettings is missing"; Debug.LogError(status, this); return; }
            // Snapshot all Unity settings/paths on the main thread.
            var config = new CaptureOptions(settings, Application.dataPath, Application.streamingAssetsPath, Application.isEditor);
            stopRequested = failed = false;
            frames = new LatestBodyFrameBuffer();
            receivedFrames = 0;
            captureFps = 0;
            status = "Starting Kinect";
            worker = new Thread(() => CaptureLoop(config)) { IsBackground = true, Name = "Naispo Azure Kinect Capture" };
            worker.Start();
        }

        public bool TryGetLatest(ref BodyFrameData destination) => frames.TryTake(ref destination);

        void Update()
        {
            while (notices.TryDequeue(out var notice))
            {
                status = notice.Text;
                if (notice.Error) Debug.LogWarning("[Kinect] " + notice.Text, this);
                else if (settings.debugLogging) Debug.Log("[Kinect] " + notice.Text, this);
            }
        }

        void CaptureLoop(CaptureOptions options)
        {
            Device device = null;
            Tracker tracker = null;
            Transformation colorTransform = null;
            Microsoft.Azure.Kinect.Sensor.Image alignedColor = null;
            long colorFrames = 0;
            bool camerasStarted = false;
            try
            {
                KinectNativeRuntime.EnsureLoaded(options.DataPath, options.Editor);
                if (!File.Exists(options.ModelPath)) throw new FileNotFoundException("Body tracking model not found", options.ModelPath);
                if (options.DepthMode == DepthMode.Off || options.DepthMode == DepthMode.PassiveIR)
                    throw new ArgumentException("Body tracking requires an active depth mode.");
                if (options.DepthMode == DepthMode.WFOV_Unbinned && options.Fps == FPS.FPS30)
                    throw new ArgumentException("WFOV_Unbinned supports at most 15 FPS. Change Camera FPS.");
                if (Device.GetInstalledCount() <= options.DeviceIndex)
                    throw new InvalidOperationException("Kinect is not connected. Check power/USB 3 and close other Kinect viewers, then re-enable KinectSystem.");
                device = Device.Open(options.DeviceIndex);
                device.StartCameras(new DeviceConfiguration { DepthMode = options.DepthMode, CameraFPS = options.Fps,
                    ColorResolution = options.Color ? ColorResolution.R720p : ColorResolution.Off,
                    ColorFormat = ImageFormat.ColorBGRA32,
                    SynchronizedImagesOnly = options.Color, WiredSyncMode = WiredSyncMode.Standalone });
                camerasStarted = true;
                var calibration = device.GetCalibration();
                if(options.Color)
                {
                    colorTransform = new Transformation(calibration);
                    alignedColor = new Microsoft.Azure.Kinect.Sensor.Image(ImageFormat.ColorBGRA32,
                        calibration.DepthCameraCalibration.ResolutionWidth,calibration.DepthCameraCalibration.ResolutionHeight);
                }
                notices.Enqueue(new Notice("Sensor connected; depth=" + options.DepthMode + ", FPS=" + options.Fps + ", color=" + (options.Color ? "720p BGRA / depth-aligned" : "OFF"), false));
                if (stopRequested) return;
                var trackerConfig = TrackerConfiguration.Default;
                trackerConfig.SensorOrientation = SensorOrientation.Default;
                trackerConfig.ProcessingMode = options.Mode;
                trackerConfig.ModelPath = options.ModelPath;
                initializingTracker = true;
                try { tracker = Tracker.Create(device.GetCalibration(), trackerConfig); }
                catch (Exception ex) when (options.CpuFallback && options.Mode != TrackerProcessingMode.Cpu && !stopRequested)
                {
                    notices.Enqueue(new Notice("GPU tracker initialization failed: " + ex.Message + ". Retrying CPU explicitly.", true));
                    trackerConfig.ProcessingMode = TrackerProcessingMode.Cpu;
                    tracker = Tracker.Create(device.GetCalibration(), trackerConfig);
                }
                finally { initializingTracker = false; }
                lock (trackerGate) { liveTracker = tracker; }
                if (stopRequested) return;
                activeProcessingMode = trackerConfig.ProcessingMode.ToString();
                // Spelling is defined this way by the installed official 1.1.2 wrapper.
                tracker.SetTemporalSmooting(options.Smoothing);
                notices.Enqueue(new Notice("Tracker ready: " + activeProcessingMode, false));
                var producer = new BodyFrameData();
                var timer = Stopwatch.StartNew();
                double lastFpsTime = 0, lastCaptureTime = 0, lastResultTime = 0;
                int fpsFrames = 0, inFlight = 0;
                long sequence = 0;
                // Keep at most two captures inside the SDK, and only one pending frame for Unity.
                while (!stopRequested)
                {
                    if (inFlight < 2)
                    {
                        try
                        {
                            using (var capture = device.GetCapture(TimeSpan.FromMilliseconds(50)))
                            {
                                lastCaptureTime = timer.Elapsed.TotalSeconds;
                                tracker.EnqueueCapture(capture, TimeSpan.FromMilliseconds(50));
                                ++inFlight;
                            }
                        }
                        catch (TimeoutException) { }
                    }
                    using (var frame = tracker.PopResult(TimeSpan.FromMilliseconds(10), false))
                    {
                        if (frame != null)
                        {
                            inFlight = Math.Max(0, inFlight - 1);
                            lastResultTime = timer.Elapsed.TotalSeconds;
                            using (var map = frame.BodyIndexMap)
                            {
                                if (map == null) throw new InvalidOperationException("BodyIndexMap is missing.");
                                producer.EnsureSize(map.WidthPixels, map.HeightPixels);
                                var source = map.Memory.Span;
                                for (int y = 0; y < map.HeightPixels; ++y)
                                    source.Slice(y * map.StrideBytes, map.WidthPixels).CopyTo(producer.BodyIndexMap.AsSpan(y * map.WidthPixels, map.WidthPixels));
                                producer.BodyCount = Math.Min(255, (int)frame.NumberOfBodies);
                                for (uint i = 0; i < producer.BodyCount; ++i)
                                {
                                    producer.BodyIds[i] = frame.GetBodyId(i);
                                    producer.Hands[i] = default;
                                    var skeleton = frame.GetBodySkeleton(i);
                                    var left = skeleton.GetJoint(JointId.HandLeft);
                                    var right = skeleton.GetJoint(JointId.HandRight);
                                    if (left.ConfidenceLevel < JointConfidenceLevel.Medium ||
                                        right.ConfidenceLevel < JointConfidenceLevel.Medium ||
                                        left.Position.Z <= 0 || right.Position.Z <= 0) continue;
                                    var lp = calibration.TransformTo2D(left.Position, CalibrationDeviceType.Depth, CalibrationDeviceType.Depth);
                                    var rp = calibration.TransformTo2D(right.Position, CalibrationDeviceType.Depth, CalibrationDeviceType.Depth);
                                    if (lp.HasValue && rp.HasValue)
                                        producer.Hands[i] = new BodyFrameData.HandPair { Left = lp.Value, Right = rp.Value,
                                            SeparationMeters = System.Numerics.Vector3.Distance(left.Position, right.Position) * 0.001f, Valid = true };
                                }
                                producer.TimestampSeconds = frame.DeviceTimestamp.TotalSeconds;
                                producer.Sequence = ++sequence;
                                producer.HasColor = false;
                                if(options.Color)
                                {
                                    // Use the capture associated with THIS tracker result, not a newer image.
                                    using(var matchedCapture = frame.Capture)
                                    using(var color = matchedCapture.Color)
                                    using(var depth = matchedCapture.Depth)
                                    {
                                        if(color != null && depth != null)
                                        {
                                            colorTransform.ColorImageToDepthCamera(depth,color,alignedColor);
                                            var bgra = alignedColor.Memory.Span;
                                            for(int y=0;y<map.HeightPixels;y++)
                                                bgra.Slice(y*alignedColor.StrideBytes,map.WidthPixels*4).CopyTo(producer.AlignedColorBgra.AsSpan(y*map.WidthPixels*4,map.WidthPixels*4));
                                            producer.HasColor = true;
                                            if(++colorFrames == 1) notices.Enqueue(new Notice("Tracker ready: " + activeProcessingMode + "; aligned RGB ready " + map.WidthPixels + "x" + map.HeightPixels,false));
                                        }
                                    }
                                }
                                detectedBodyCount = producer.BodyCount;
                                mapWidth = producer.Width;
                                mapHeight = producer.Height;
                            }
                            frames.Publish(ref producer);
                            Interlocked.Increment(ref receivedFrames);
                            ++fpsFrames;
                        }
                    }
                    double now = timer.Elapsed.TotalSeconds;
                    if (now - lastFpsTime >= 1)
                    { captureFps = (float)(fpsFrames / (now - lastFpsTime)); fpsFrames = 0; lastFpsTime = now; }
                    if (inFlight == 0 && now - lastCaptureTime > 5)
                        throw new TimeoutException("No depth captures for 5 seconds. Check Kinect power/USB connection.");
                    if (now - lastResultTime > 15)
                        throw new TimeoutException("Body tracker produced no results for 15 seconds.");
                }
            }
            catch (Exception ex)
            {
                if (!stopRequested) { failed = true; notices.Enqueue(new Notice(ex.GetType().Name + ": " + ex.Message, true)); }
            }
            finally
            {
                lock (trackerGate) { liveTracker = null; }
                try { alignedColor?.Dispose(); colorTransform?.Dispose(); }
                catch(Exception ex) { notices.Enqueue(new Notice("Color cleanup: " + ex.Message,true)); }
                try { if (tracker != null) { tracker.Shutdown(); tracker.Dispose(); } }
                catch (Exception ex) { notices.Enqueue(new Notice("Tracker cleanup: " + ex.Message, true)); }
                try { if (device != null) { if (camerasStarted) device.StopCameras(); device.Dispose(); } }
                catch (Exception ex) { notices.Enqueue(new Notice("Sensor cleanup: " + ex.Message, true)); }
                captureFps = 0;
            }
        }

        public void StopCapture()
        {
            stopRequested = true;
            lock (trackerGate)
            {
                try { liveTracker?.Shutdown(); }
                catch (Exception ex) { Debug.LogWarning("[Kinect] Shutdown: " + ex.Message, this); }
            }
            if (worker != null && worker.IsAlive && !worker.Join(5000))
            {
                // Native Tracker.Create has no cancellation API. Never abort a thread
                // inside native code: it owns the handles and will clean up on return.
                Debug.LogWarning("[Kinect] Native initialization is still returning; cancellation requested. Do not restart capture until WorkerAlive is false.", this);
                return;
            }
            worker = null;
            status = failed ? status : "Stopped (worker joined)";
        }
        void OnDisable() => StopCapture();
        void OnDestroy() => StopCapture();
        void OnApplicationQuit() => StopCapture();

        sealed class CaptureOptions
        {
            public readonly string DataPath, ModelPath;
            public readonly bool Editor, CpuFallback, Color;
            public readonly int DeviceIndex;
            public readonly DepthMode DepthMode;
            public readonly FPS Fps;
            public readonly TrackerProcessingMode Mode;
            public readonly float Smoothing;
            public CaptureOptions(KinectSettings s, string data, string streaming, bool editor)
            {
                DataPath = data; ModelPath = Path.IsPathRooted(s.modelFile) ? s.modelFile : Path.Combine(streaming, s.modelFile);
                Editor = editor; CpuFallback = s.allowCpuFallback; DeviceIndex = s.deviceIndex;
                Color = s.captureColor;
                DepthMode = s.depthMode; Fps = s.cameraFPS; Mode = s.processingMode; Smoothing = s.skeletonSmoothing;
            }
        }
    }
}
