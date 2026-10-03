using System;
using System.Threading.Tasks;
using UnityEngine;
using Naispo.Kinect;

public static class ValidateCaptureLifecycle
{
    public static async Task<string> Run()
    {
        var source=UnityEngine.Object.FindAnyObjectByType<AzureKinectBodySource>();
        if(source==null||!Application.isPlaying)throw new InvalidOperationException("Requires running demo.");
        var timer=System.Diagnostics.Stopwatch.StartNew();source.StopCapture();long normal=timer.ElapsedMilliseconds;
        if(source.WorkerAlive)throw new Exception("Normal stop left a worker running.");
        source.StartCapture();timer.Restart();
        while(!source.IsInitializingTracker&&!source.HasFailed&&timer.ElapsedMilliseconds<5000)await Task.Delay(10);
        bool duringNativeCreate=source.IsInitializingTracker;
        timer.Restart();source.StopCapture();long initializing=timer.ElapsedMilliseconds;
        if(source.WorkerAlive)throw new Exception("Stop during initialization left a worker running.");
        source.StartCapture();
        return $"Normal stop: {normal} ms; stop during native Tracker.Create={duringNativeCreate}: {initializing} ms; both workers joined. Real capture restarted.";
    }
}
