using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Azure.Kinect.Sensor;
using Naispo.Kinect;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LocalSetup
{
    const string Scene = "Assets/Scenes/KinectSilhouetteDemo.unity";

    [MenuItem("Naispo/Open Kinect Demo")]
    public static void OpenDemo()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene(Scene);
    }

    [MenuItem("Naispo/Check Local Runtime")]
    public static void CheckRuntime()
    {
        KinectNativeRuntime.EnsureLoaded(Application.dataPath, true);
        string model = Path.Combine(Application.streamingAssetsPath, "AzureKinect/dnn_model_2_0_op11.onnx");
        using (var stream = File.OpenRead(model))
        using (var sha = SHA256.Create())
        {
            string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            if (hash != "40BF149C8E3DC4CBA34CFF4F5C3DB6F5D5D4E6559D525DF1AA02C2E6C80F7D2D")
                throw new InvalidDataException("Body tracking model is missing or corrupt (possibly a Git LFS pointer).");
        }
        Debug.Log("NAISPO_RUNTIME_OK; connected Azure Kinect devices=" + Device.GetInstalledCount());
    }

    [MenuItem("Naispo/Build Windows Demo")]
    public static void BuildWindows()
    {
        CheckRuntime();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { Scene }, locationPathName = "Builds/Windows/Naispo.exe",
            target = BuildTarget.StandaloneWindows64, options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("Windows build failed: " + report.summary.result);
        Debug.Log("NAISPO_BUILD_OK");
    }
}
