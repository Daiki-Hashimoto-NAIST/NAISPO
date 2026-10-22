using UnityEngine;

namespace Naispo.Kinect
{
    // Common CPU frame interface for a local sensor, worker pipe, or keyboard demo.
    public abstract class BodyTrackingSource : MonoBehaviour
    {
        public abstract string Status { get; }
        public abstract float CaptureFps { get; }
        public abstract bool WorkerAlive { get; }
        public abstract bool HasFailed { get; }
        public virtual bool IsReconfiguring => false;
        public virtual int CaptureGeneration => 0;
        public abstract bool TryGetLatest(ref BodyFrameData frame);
    }
}
