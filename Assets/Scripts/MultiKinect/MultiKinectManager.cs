using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Naispo.Kinect;
using Naispo.Silhouette;
using UnityEngine;

namespace Naispo.MultiKinect
{
    [DefaultExecutionOrder(-300)]
    public sealed class MultiKinectManager : MonoBehaviour
    {
        public MultiKinectSettings settings;
        public KinectPipeReceiver[] receivers = new KinectPipeReceiver[2];
        public SilhouetteColliderController[] colliders = new SilhouetteColliderController[2];
        public string LifecycleStatus { get; private set; } = "Stopped";
        public WorkerLaunchMode ActiveMode { get; private set; }
        public bool IsReconfiguring => startRequested || discovery != null || !stopping.IsCompleted;
        public readonly float[] ReceiveFps = new float[2], BytesPerSecond = new float[2];
        public float RenderFps { get; private set; }
        readonly KinectWorkerProcess[] workers = { new KinectWorkerProcess(), new KinectWorkerProcess() };
        readonly long[] previousFrames = new long[2], previousBytes = new long[2];
        readonly string[] lines = new string[2];
        readonly bool[] expectedWorkers = new bool[2];
        Task stopping = Task.CompletedTask;
        Task<string> discovery;
        CancellationTokenSource discoveryCancel;
        Config[] pendingConfigs;
        string executablePath, runtimeDirectory;
        WorkerLaunchMode requestedMode;
        bool startRequested;
        double nextReport;
        int renderFrames;

        [Serializable]
        sealed class Config
        {
            public int Slot, ParentPid, MinimumConfidence, DeviceIndex = -1;
            public string Serial, Pipe, NativeDirectory, ModelPath, DepthMode, Fps, ProcessingMode;
            public float MinDistance, MaxDistance, MaxAbsX, LostTimeout;
            public bool Synthetic;
        }

        public int WorkerPid(int slot) => (uint)slot < 2 ? workers[slot].Id : 0;
        public bool WorkerRunning(int slot) => (uint)slot < 2 && workers[slot].Running;
        public bool TryGetIdentity(int slot, out string serial, out uint nativeBodyId)
        {
            serial = ""; nativeBodyId = uint.MaxValue;
            if ((uint)slot >= 2 || receivers == null || receivers.Length != 2 || receivers[slot] == null ||
                !receivers[slot].WorkerAlive || !receivers[slot].LatestMetadata.Tracked) return false;
            serial = receivers[slot].LatestMetadata.Serial;
            nativeBodyId = receivers[slot].LatestMetadata.BodyId;
            return true;
        }

        void OnEnable() { if (settings != null) StartWorkers(settings.launchMode); }

        // Settings are read when launching; switching mode does not mutate the shared asset.
        public void StartWorkers(WorkerLaunchMode mode)
        {
            StopWorkers();
            requestedMode = mode;
            startRequested = true;
            LifecycleStatus = "Preparing " + mode;
        }
        public void RestartWorkers(WorkerLaunchMode mode) => StartWorkers(mode);

        public void StopWorkers()
        {
            startRequested = false;
            Array.Clear(expectedWorkers, 0, expectedWorkers.Length);
            discoveryCancel?.Cancel();
            var cancelledDiscovery = discovery;
            discovery = null;
            discoveryCancel?.Dispose(); discoveryCancel = null;
            var tasks = new System.Collections.Generic.List<Task> { stopping };
            if (cancelledDiscovery != null) tasks.Add(cancelledDiscovery.ContinueWith(t => { var ignored = t.Exception; }));
            foreach (var worker in workers) tasks.Add(worker.StopAsync());
            stopping = Task.WhenAll(tasks);
            if (receivers != null) foreach (var receiver in receivers) if (receiver != null) receiver.enabled = false;
            LifecycleStatus = "Stopped";
        }

        bool ValidateSetup()
        {
            foreach (var source in FindObjectsByType<AzureKinectBodySource>(FindObjectsSortMode.None))
                if (source.isActiveAndEnabled || source.WorkerAlive)
                { LifecycleStatus = "Stop the single Kinect source before starting two sensors"; return false; }
            if (settings == null || settings.serialNumbers == null || settings.pipeNames == null ||
                settings.serialNumbers.Length != 2 || settings.pipeNames.Length != 2 ||
                receivers == null || receivers.Length != 2 || receivers[0] == null || receivers[1] == null)
            { LifecycleStatus = "Two serial/pipe settings and receiver references required"; return false; }
            if (string.IsNullOrWhiteSpace(settings.pipeNames[0]) || string.IsNullOrWhiteSpace(settings.pipeNames[1]) ||
                settings.pipeNames[0] == settings.pipeNames[1] ||
                (requestedMode != WorkerLaunchMode.AutomaticSynthetic && !string.IsNullOrEmpty(settings.serialNumbers[0]) &&
                    settings.serialNumbers[0] == settings.serialNumbers[1]))
            { LifecycleStatus = "Separate serial numbers and pipe names are required"; return false; }
            if (requestedMode != WorkerLaunchMode.AutomaticSynthetic &&
                (string.IsNullOrWhiteSpace(settings.serialNumbers[0]) || string.IsNullOrWhiteSpace(settings.serialNumbers[1])))
            { LifecycleStatus = "Configure both Kinect serial numbers"; return false; }
            return true;
        }

        void BeginStart()
        {
            startRequested = false;
            if (!ValidateSetup()) return;
            ActiveMode = requestedMode;
            Array.Clear(previousFrames, 0, 2); Array.Clear(previousBytes, 0, 2);
            Array.Clear(ReceiveFps, 0, 2); Array.Clear(BytesPerSecond, 0, 2);
            if (ActiveMode != WorkerLaunchMode.Manual)
            {
                string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                executablePath = Path.IsPathRooted(settings.workerExecutable) ? settings.workerExecutable : Path.Combine(root, settings.workerExecutable);
                if (!File.Exists(executablePath))
                {
                    LifecycleStatus = Application.isEditor
                        ? "KinectWorker.exe is missing. Run Tools/KinectWorker/Build.ps1, then retry."
                        : "KinectWorker.exe is missing from this build's Tools/KinectWorker folder.";
                    Debug.LogError("[MultiKinect] " + LifecycleStatus + " Path: " + executablePath, this);
                    return;
                }
                runtimeDirectory = Path.Combine(Application.persistentDataPath, "CoinRallyWorkers");
                Directory.CreateDirectory(runtimeDirectory);
            }

            pendingConfigs = new Config[2];
            for (int i = 0; i < 2; i++)
            {
                var receiver = receivers[i];
                receiver.playerSlot = i; receiver.pipeName = settings.pipeNames[i];
                receiver.serialNumber = ActiveMode == WorkerLaunchMode.AutomaticSynthetic ? "SYNTHETIC" + i : settings.serialNumbers[i];
                receiver.enabled = true;
                pendingConfigs[i] = new Config
                {
                    Slot = i, Serial = receiver.serialNumber, Pipe = receiver.pipeName,
                    ParentPid = System.Diagnostics.Process.GetCurrentProcess().Id,
                    Synthetic = ActiveMode == WorkerLaunchMode.AutomaticSynthetic,
                    NativeDirectory = Path.Combine(Application.dataPath, Application.isEditor ? "Plugins/AzureKinect/x86_64" : "Plugins/x86_64"),
                    ModelPath = Path.Combine(Application.streamingAssetsPath, "AzureKinect/dnn_model_2_0_op11.onnx"),
                    DepthMode = settings.depthMode.ToString(), Fps = settings.fps.ToString(), ProcessingMode = settings.processingMode.ToString(),
                    MinDistance = settings.minimumDistance, MaxDistance = settings.maximumDistance, MaxAbsX = settings.maximumAbsX,
                    LostTimeout = settings.lostTimeout, MinimumConfidence = settings.minimumConfidence
                };
            }
            if (ActiveMode == WorkerLaunchMode.Manual) { LifecycleStatus = "Waiting for manual workers"; return; }
            if (ActiveMode == WorkerLaunchMode.AutomaticSynthetic) { Launch(null); return; }
            LifecycleStatus = "Checking Kinect serial numbers";
            discoveryCancel = new CancellationTokenSource();
            discovery = KinectDiscovery.Run(executablePath, pendingConfigs[0].NativeDirectory, discoveryCancel.Token);
        }

        void Launch(KinectDiscovery.Result devices)
        {
            // Validate both devices before opening either one, so an incomplete pair can be retried cleanly.
            for (int i = 0; i < 2; i++)
            {
                var config = pendingConfigs[i];
                if (config.Synthetic) continue;
                if (devices?.devices != null)
                    foreach (var device in devices.devices)
                        if (device.serial == config.Serial && string.IsNullOrEmpty(device.error)) { config.DeviceIndex = device.index; break; }
                if (config.DeviceIndex < 0)
                {
                    LifecycleStatus = "Kinect " + config.Serial + " unavailable. Check USB, power, and other Kinect apps. Found " + (devices?.count ?? 0) + " device(s).";
                    foreach (var receiver in receivers) receiver.enabled = false;
                    return;
                }
            }
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    string path = Path.Combine(runtimeDirectory, "player" + i + ".json");
                    File.WriteAllText(path, JsonUtility.ToJson(pendingConfigs[i], true));
                    workers[i].Start(executablePath, path, Path.Combine(runtimeDirectory, "player" + i + ".log"));
                    expectedWorkers[i] = true;
                }
                LifecycleStatus = "Workers started";
            }
            catch (Exception error)
            {
                StopWorkers();
                LifecycleStatus = "Worker startup: " + error.Message;
            }
        }

        void Update()
        {
            if (startRequested && stopping.IsCompleted) BeginStart();
            if (discovery != null && discovery.IsCompleted)
            {
                try { Launch(JsonUtility.FromJson<KinectDiscovery.Result>(discovery.GetAwaiter().GetResult())); }
                catch (Exception error)
                {
                    LifecycleStatus = error.Message;
                    foreach (var receiver in receivers) receiver.enabled = false;
                }
                finally { discovery = null; discoveryCancel?.Dispose(); discoveryCancel = null; }
            }
            renderFrames++;
            double now = Time.unscaledTimeAsDouble;
            if (now < nextReport) return;
            double elapsed = nextReport == 0 ? 1 : Math.Max(.001, now - (nextReport - 1));
            nextReport = now + 1; RenderFps = (float)(renderFrames / elapsed); renderFrames = 0;
            if (receivers == null) return;
            string workerFailure = null;
            for (int i = 0; i < Math.Min(2, receivers.Length); i++)
            {
                var receiver = receivers[i]; if (receiver == null) continue;
                ReceiveFps[i] = (float)(Math.Max(0, receiver.ReceivedFrames - previousFrames[i]) / elapsed);
                BytesPerSecond[i] = (float)(Math.Max(0, receiver.BytesReceived - previousBytes[i]) / elapsed);
                previousFrames[i] = receiver.ReceivedFrames; previousBytes[i] = receiver.BytesReceived;
                var frame = receiver.LatestMetadata;
                lines[i] = $"P{i + 1}: {receiver.serialNumber} | {receiver.Status}\nTracking {frame.TrackingFps:F1} / Receive {ReceiveFps[i]:F1} FPS | age {receiver.AgeSeconds:F2}s\n{workers[i].LastLine}";
                if (workerFailure == null && expectedWorkers[i] && !workers[i].Running && !workers[i].IsStopping)
                {
                    string reason = workers[i].LastLine;
                    if (reason.Length > 220) reason = reason.Substring(0, 220) + "…";
                    workerFailure = $"Player {i + 1} Kinect worker stopped (exit {workers[i].ExitCode?.ToString() ?? "unknown"}): {reason}. Check the camera and reconnect.";
                }
            }
            if (workerFailure != null) LifecycleStatus = workerFailure;
        }

        void OnGUI()
        {
            if (settings == null || !settings.showStatus) return;
            GUI.Label(new Rect(12, 8, Screen.width - 24, 28), ActiveMode + " | " + LifecycleStatus);
            for (int i = 0; i < 2; i++) GUI.Label(new Rect(12 + i * Screen.width * .5f, 38, Screen.width * .5f - 20, 85), lines[i]);
        }
        void OnDisable() => StopWorkers();
        void OnApplicationQuit() => StopWorkers();
    }
}
