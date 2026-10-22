using UnityEngine;
using Naispo.Kinect;
using Naispo.Silhouette;

namespace Naispo.Core
{
    [DefaultExecutionOrder(-100)]
    public sealed class KinectSilhouetteManager : MonoBehaviour
    {
        public BodyTrackingSource source;
        public KinectSettings settings;
        public KinectViewportMapper mapper;
        public SilhouetteRenderer silhouetteRenderer;
        public SilhouetteColliderController colliderController;
        public bool useHandWalls = true;
        public bool autoAddCoinGame = true;
        public bool showControls = true;
        void Awake()
        {
            if (GetComponent<Naispo.Interaction.HandWallController>() == null)
                gameObject.AddComponent<Naispo.Interaction.HandWallController>();
            if (autoAddCoinGame && GetComponent<Naispo.Interaction.LightCoinGame>() == null)
                gameObject.AddComponent<Naispo.Interaction.LightCoinGame>();
        }
        public BodyColorRegistry Registry { get; } = new BodyColorRegistry();
        public BodyFrameData CurrentFrame => current;
        public long FrameRevision { get; private set; }
        public double LastFrameReceivedAt => lastFrameTime;
        public bool IsFresh => hasFrame && source != null && source.isActiveAndEnabled && !source.HasFailed &&
            settings != null && Time.unscaledTimeAsDouble - lastFrameTime <= settings.staleFrameTimeout;
        BodyFrameData current = new BodyFrameData();
        BodyTrackingSource previousSource;
        int previousGeneration = -1;
        bool hasFrame;
        double lastFrameTime, nextSummary;
        string summary = "Starting Kinect…";

        void Update()
        {
            double now = Time.unscaledTimeAsDouble;
            if (settings == null || mapper == null || silhouetteRenderer == null) return;
            if (previousSource != source || (source != null && previousGeneration != source.CaptureGeneration))
            {
                hasFrame = false;
                current.BodyCount = 0;
                Registry.Update(null, now, settings.maximumBodyCount, 0);
                previousSource = source;
                previousGeneration = source != null ? source.CaptureGeneration : -1;
            }
            if (source != null && source.isActiveAndEnabled && source.TryGetLatest(ref current))
            {
                if (previousGeneration != source.CaptureGeneration)
                    Registry.Update(null, now, settings.maximumBodyCount, 0);
                previousGeneration = source.CaptureGeneration;
                hasFrame = true; lastFrameTime = now; FrameRevision++;
                Registry.Update(current, now, settings.maximumBodyCount, settings.bodyLostTimeout);
                mapper.Refresh(current.Width, current.Height);
                silhouetteRenderer.Apply(current, Registry);
            }
            else
            {
                mapper.Refresh(mapper.Width, mapper.Height);
                silhouetteRenderer.RefreshGeometry();
                if (!IsFresh) { Registry.Update(null, now, settings.maximumBodyCount, settings.bodyLostTimeout); silhouetteRenderer.Clear(); }
            }
            if (now >= nextSummary)
            {
                nextSummary = now + settings.summaryInterval;
                summary = $"{(source != null ? source.Status : "No input source")}\nKinect {(source != null ? source.CaptureFps : 0):F1} FPS | map {current.Width}×{current.Height} | detected {current.BodyCount} | shown {Registry.PresentCount}";
                if(colliderController!=null) summary += $"\nCollider {colliderController.ColliderFps:F1} Hz | {colliderController.TotalPoints} points | {colliderController.LastRebuildMilliseconds:F2} ms";
                for (int i = 0; i < 3; i++) if (Registry.Slots[i].Assigned) summary += $"\nSlot {i}: ID {Registry.Slots[i].BodyId}";
                if (settings.debugLogging) Debug.Log("[Kinect] " + summary.Replace('\n',' '), this);
            }
        }
        void OnGUI()
        {
            if (!showControls || silhouetteRenderer == null) return;
            if (GUI.Button(new Rect(12,145,270,36), useHandWalls ? "Mode: Hand walls (click to switch)" : "Mode: Body silhouette (click to switch)"))
                useHandWalls = !useHandWalls;
            if(GUI.Button(new Rect(12,185,270,32),silhouetteRenderer.LightEnabled ? "Ball light: ON" : "Ball light: OFF"))
                silhouetteRenderer.LightEnabled = !silhouetteRenderer.LightEnabled;
            if (settings == null || !settings.showDebugOverlay) return;
            GUI.Box(new Rect(12,12,540,125), GUIContent.none);
            GUI.Label(new Rect(24,20,515,115), summary);
        }
        void OnDisable()
        {
            hasFrame=false;
            if(silhouetteRenderer!=null)silhouetteRenderer.Clear();
            if(colliderController!=null)colliderController.Clear();
            Registry.Update(null,Time.unscaledTimeAsDouble,3,0);
        }
    }
}
