using UnityEngine;
using Naispo.Kinect;
using Naispo.Silhouette;

namespace Naispo.Core
{
    [DefaultExecutionOrder(-100)]
    public sealed class KinectSilhouetteManager : MonoBehaviour
    {
        public AzureKinectBodySource source;
        public KinectSettings settings;
        public KinectViewportMapper mapper;
        public SilhouetteRenderer silhouetteRenderer;
        public SilhouetteColliderController colliderController;
        public BodyColorRegistry Registry { get; } = new BodyColorRegistry();
        public BodyFrameData CurrentFrame => current;
        public bool IsFresh => hasFrame && !source.HasFailed && Time.unscaledTimeAsDouble - lastFrameTime <= settings.staleFrameTimeout;
        BodyFrameData current = new BodyFrameData();
        bool hasFrame;
        double lastFrameTime, nextSummary;
        string summary = "Starting Kinect…";

        void Update()
        {
            double now = Time.unscaledTimeAsDouble;
            if (source.TryGetLatest(ref current))
            {
                hasFrame = true; lastFrameTime = now;
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
                summary = $"{source.Status}\nKinect {source.CaptureFps:F1} FPS | map {current.Width}×{current.Height} | detected {current.BodyCount} | shown {Registry.PresentCount}";
                if(colliderController!=null) summary += $"\nCollider {colliderController.ColliderFps:F1} Hz | {colliderController.TotalPoints} points | {colliderController.LastRebuildMilliseconds:F2} ms";
                for (int i = 0; i < 3; i++) if (Registry.Slots[i].Assigned) summary += $"\nSlot {i}: ID {Registry.Slots[i].BodyId}";
                if (settings.debugLogging) Debug.Log("[Kinect] " + summary.Replace('\n',' '), this);
            }
        }
        void OnGUI()
        {
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
