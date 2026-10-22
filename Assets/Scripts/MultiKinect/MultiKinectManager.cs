using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Naispo.Kinect;
using Naispo.Silhouette;
using UnityEngine;

namespace Naispo.MultiKinect
{
    public enum KinectStartupState { Stopped, Preparing, Discovering, StartingWorkers, Running, Error }
    [DefaultExecutionOrder(-300)]
    public sealed class MultiKinectManager : MonoBehaviour
    {
        public MultiKinectSettings settings;
        public KinectPipeReceiver[] receivers = new KinectPipeReceiver[2];
        public SilhouetteColliderController[] colliders = new SilhouetteColliderController[2];
        public string LifecycleStatus { get; private set; } = "Kinectはまだ起動していません。";
        public KinectStartupState StartupState { get; private set; }
        public string DiagnosticDirectory => runtimeDirectory ?? "";
        public string DiscoveryReport { get; private set; } = "カメラはまだ確認していません。";
        public WorkerLaunchMode ActiveMode { get; private set; }
        public bool IsReconfiguring => startRequested || discovery != null || !stopping.IsCompleted;
        public readonly float[] ReceiveFps = new float[2], BytesPerSecond = new float[2];
        public float RenderFps { get; private set; }
        readonly KinectWorkerProcess[] workers = { new KinectWorkerProcess(), new KinectWorkerProcess() };
        readonly long[] previousFrames = new long[2], previousBytes = new long[2];
        readonly string[] lines = new string[2];
        readonly bool[] expectedWorkers = new bool[2];
        readonly string[] assignedSerials = { "", "" };
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
        public string WorkerDetail(int slot) => (uint)slot < 2 ? workers[slot].LastLine : "";
        public string AssignedSerial(int slot) => (uint)slot < 2 ? assignedSerials[slot] : "";
        void SetState(KinectStartupState state,string message)
        {
            if(StartupState==state&&LifecycleStatus==message)return;
            StartupState=state;LifecycleStatus=message;
            Debug.Log("[MultiKinect] "+state+": "+message,this);
        }
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
            Array.Clear(assignedSerials,0,2);
            DiscoveryReport="接続中のカメラを確認していません。";
            startRequested = true;
            SetState(KinectStartupState.Preparing,"カメラを起動する準備中です。");
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
            SetState(KinectStartupState.Stopped,"Kinectは停止しています。再接続してください。");
        }

        bool ValidateSetup()
        {
            foreach (var source in FindObjectsByType<AzureKinectBodySource>(FindObjectsSortMode.None))
                if (source.isActiveAndEnabled || source.WorkerAlive)
                { SetState(KinectStartupState.Error,"1台用のKinectを停止してから再接続してください。"); return false; }
            if (settings == null || settings.serialNumbers == null || settings.pipeNames == null ||
                settings.serialNumbers.Length != 2 || settings.pipeNames.Length != 2 ||
                receivers == null || receivers.Length != 2 || receivers[0] == null || receivers[1] == null)
            { SetState(KinectStartupState.Error,"左右2台の入力設定が不足しています。"); return false; }
            if (string.IsNullOrWhiteSpace(settings.pipeNames[0]) || string.IsNullOrWhiteSpace(settings.pipeNames[1]) ||
                settings.pipeNames[0] == settings.pipeNames[1] ||
                (requestedMode != WorkerLaunchMode.AutomaticSynthetic && !string.IsNullOrEmpty(settings.serialNumbers[0]) &&
                    settings.serialNumbers[0] == settings.serialNumbers[1]))
            { SetState(KinectStartupState.Error,"左右には別々のカメラ番号と通信名を指定してください。"); return false; }
            if (requestedMode == WorkerLaunchMode.Manual &&
                (string.IsNullOrWhiteSpace(settings.serialNumbers[0]) || string.IsNullOrWhiteSpace(settings.serialNumbers[1])))
            { SetState(KinectStartupState.Error,"手動起動には左右のカメラ番号が必要です。"); return false; }
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
                    SetState(KinectStartupState.Error,Application.isEditor
                        ? "KinectWorker.exeがありません。Tools/KinectWorker/Build.ps1を実行して再接続してください。"
                        : "KinectWorker.exeがありません。配布フォルダー全体をコピーしてください。");
                    Debug.Log("[MultiKinect] Missing worker path: " + executablePath, this);
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
                receiver.serialNumber = ActiveMode == WorkerLaunchMode.AutomaticSynthetic ? "SYNTHETIC" + i : (settings.serialNumbers[i]??"").Trim();
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
            if (ActiveMode == WorkerLaunchMode.Manual)
            {
                foreach(var receiver in receivers)receiver.enabled=true;
                SetState(KinectStartupState.StartingWorkers,"手動起動したKinectからのデータを待っています。");return;
            }
            if (ActiveMode == WorkerLaunchMode.AutomaticSynthetic) { Launch(null); return; }
            SetState(KinectStartupState.Discovering,"接続中のKinectを確認しています。");
            discoveryCancel = new CancellationTokenSource();
            discovery = KinectDiscovery.Run(executablePath, pendingConfigs[0].NativeDirectory, discoveryCancel.Token);
        }

        void Launch(KinectDiscovery.Result devices)
        {
            if(ActiveMode!=WorkerLaunchMode.AutomaticSynthetic)
            {
                var found=new System.Text.StringBuilder($"接続台数: {devices?.count??0}\n");
                if(devices?.devices!=null)foreach(var device in devices.devices)
                {
                    if(device==null)continue;
                    found.AppendLine(string.IsNullOrEmpty(device.error) ? $"番号 {device.serial}（使用可能）" : $"カメラ {device.index+1}: 開けません — {device.error}");
                }
                DiscoveryReport=found.ToString().TrimEnd();
                if(!KinectDiscovery.TrySelect(devices,new[]{pendingConfigs[0].Serial,pendingConfigs[1].Serial},out var selected,out var error))
                {
                    SetState(KinectStartupState.Error,error);
                    return;
                }
                for(int i=0;i<2;i++)
                {
                    pendingConfigs[i].DeviceIndex=selected[i].index;pendingConfigs[i].Serial=selected[i].serial;
                    receivers[i].serialNumber=selected[i].serial;
                }
            }
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    assignedSerials[i]=pendingConfigs[i].Serial;
                    // Start only after discovery resolves the serial captured by the receiver thread.
                    receivers[i].enabled=true;
                    string path = Path.Combine(runtimeDirectory, "player" + i + ".json");
                    File.WriteAllText(path, JsonUtility.ToJson(pendingConfigs[i], true));
                    workers[i].Start(executablePath, path, Path.Combine(runtimeDirectory, "player" + i + ".log"));
                    expectedWorkers[i] = true;
                }
                SetState(KinectStartupState.StartingWorkers,"カメラと人体認識を起動しています。初回は時間がかかることがあります。");
            }
            catch (Exception error)
            {
                StopWorkers();
                SetState(KinectStartupState.Error,"Kinectの起動に失敗しました。カメラ設定で詳細を確認してください。\n"+error.Message);
            }
        }

        void Update()
        {
            if (startRequested && stopping.IsCompleted)
            {
                try { BeginStart(); }
                catch(Exception error) { StopWorkers();SetState(KinectStartupState.Error,"Kinectの起動準備に失敗しました。\n"+error.Message); }
            }
            if (discovery != null && discovery.IsCompleted)
            {
                try { Launch(JsonUtility.FromJson<KinectDiscovery.Result>(discovery.GetAwaiter().GetResult())); }
                catch (Exception error)
                {
                    SetState(KinectStartupState.Error,"Kinectを確認できませんでした。カメラ設定の詳細を確認してください。\n"+error.Message);
                    foreach (var receiver in receivers) receiver.enabled = false;
                }
                finally { discovery = null; discoveryCancel?.Dispose(); discoveryCancel = null; }
            }
            if(StartupState!=KinectStartupState.Error&&StartupState!=KinectStartupState.Stopped&&receivers!=null&&receivers.Length==2&&
                receivers[0]!=null&&receivers[1]!=null&&receivers[0].WorkerAlive&&receivers[1].WorkerAlive)
                SetState(KinectStartupState.Running,"左右のKinectからデータを受信しています。");
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
                    workerFailure = $"{(i==0?"左":"右")}の人体認識が停止しました（終了コード {workers[i].ExitCode?.ToString() ?? "不明"}）。カメラ設定の詳細を確認して再接続してください。\n{reason}";
                }
                if(workerFailure==null&&receiver.isActiveAndEnabled&&receiver.HasFailed)workerFailure=$"{(i==0?"左":"右")}のKinectデータを受信できません。\n{receiver.Status}";
            }
            if (workerFailure != null) SetState(KinectStartupState.Error,workerFailure);
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
