using UnityEngine;
namespace Naispo.MultiKinect
{
    public enum WorkerLaunchMode { Manual, AutomaticSensors, AutomaticSynthetic }
    [CreateAssetMenu(menuName="Naispo/Multi Kinect Settings")]
    public sealed class MultiKinectSettings:ScriptableObject
    {
        public WorkerLaunchMode launchMode=WorkerLaunchMode.Manual;
        public string workerExecutable="Tools/KinectWorker/bin/Release/net10.0-windows/KinectWorker.exe";
        public string[] serialNumbers={"",""};
        public string[] pipeNames={"NaispoCoinP1","NaispoCoinP2"};
        public Microsoft.Azure.Kinect.Sensor.DepthMode depthMode=Microsoft.Azure.Kinect.Sensor.DepthMode.NFOV_2x2Binned;
        public Microsoft.Azure.Kinect.Sensor.FPS fps=Microsoft.Azure.Kinect.Sensor.FPS.FPS15;
        public Microsoft.Azure.Kinect.BodyTracking.TrackerProcessingMode processingMode=Microsoft.Azure.Kinect.BodyTracking.TrackerProcessingMode.DirectML;
        public float minimumDistance=.5f,maximumDistance=4.5f,maximumAbsX=1.2f,lostTimeout=1;
        [Range(1,3)]public int minimumConfidence=1;
        public bool showStatus=true;
    }
}
