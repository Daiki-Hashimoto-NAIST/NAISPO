using Microsoft.Azure.Kinect.Sensor;
using Microsoft.Azure.Kinect.BodyTracking;
using UnityEngine;

namespace Naispo.Kinect
{
    [CreateAssetMenu(menuName = "Naispo/Kinect Settings")]
    public sealed class KinectSettings : ScriptableObject
    {
        [Header("Capture — restart Play Mode after changing")]
        public DepthMode depthMode = DepthMode.NFOV_2x2Binned;
        public FPS cameraFPS = FPS.FPS30;
        [Min(0)] public int deviceIndex;
        public TrackerProcessingMode processingMode = TrackerProcessingMode.DirectML;
        public bool allowCpuFallback = true;
        [Range(0, 1)] public float skeletonSmoothing = 0.2f;
        public string modelFile = "AzureKinect/dnn_model_2_0_op11.onnx";
        public bool captureColor = true;

        [Header("Ball light")]
        public Shader ballLightShader;
        [Min(0.2f)] public float ballLightRadius = 1.8f;
        [Range(0,1)] public float shadowBrightness = 0.14f;

        [Header("Display and identities")]
        public bool mirrorX = true;
        [Range(1, 3)] public int maximumBodyCount = 3;
        [Min(0.1f)] public float bodyLostTimeout = 0.75f;
        [Min(0.1f)] public float staleFrameTimeout = 0.5f;
        public Color[] personColors = { Color.cyan, Color.magenta, Color.yellow };

        [Header("Contours (depth-image pixels)")]
        [Min(1)] public int minimumComponentArea = 40;
        [Range(0, 4)] public int noiseNeighborThreshold = 2;
        [Range(0.25f, 10)] public float contourEpsilon = 2.5f;
        [Range(16, 256)] public int maximumContourPoints = 192;
        [Range(1, 8)] public int maximumComponentsPerBody = 4;
        [Range(5, 30)] public float colliderUpdateRate = 25;

        [Header("Elastic barrier (independent of body colors)")]
        public Color[] barrierColors = { new Color(1f,0.48f,0.12f), new Color(0.55f,1f,0.32f), new Color(0.65f,0.42f,1f) };
        [Min(0.05f)] public float barrierNarrowMeters = 0.3f;
        [Min(0.1f)] public float barrierWideMeters = 1.5f;
        [Range(0,1)] public float barrierNarrowBounce = 1f;
        [Range(0,1)] public float barrierWideBounce = 0.4f;
        [Min(0.01f)] public float barrierNarrowBend = 0.08f;
        [Min(0.01f)] public float barrierWideBend = 0.65f;

        [Header("Diagnostics")]
        public bool debugLogging = true;
        public bool showDebugOverlay = true;
        [Min(1)] public float summaryInterval = 5;
    }
}
