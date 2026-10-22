using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Naispo.Kinect;
using Naispo.MultiKinect;
using Naispo.Interaction;

public static class CooperativeRallySetup
{
    public const string ScenePath="Assets/Scenes/CooperativeCoinRally.unity";
    [MenuItem("Naispo/Cooperative Rally/Open")]
    public static void Open()
    {
        if(!File.Exists(ScenePath))CreateScene();
        else if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(ScenePath);
    }

    [MenuItem("Naispo/Cooperative Rally/Create Scene")]
    public static void CreateScene()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play before scene setup.");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var cameraObject=new GameObject("Main Camera");cameraObject.tag="MainCamera";
        var camera=cameraObject.AddComponent<Camera>();cameraObject.AddComponent<AudioListener>();
        camera.orthographic=true;camera.orthographicSize=5;camera.aspect=16f/9;camera.nearClipPlane=.1f;camera.farClipPlane=100;
        camera.transform.position=new Vector3(0,0,-10);camera.clearFlags=CameraClearFlags.SolidColor;
        camera.backgroundColor=new Color(.018f,.028f,.065f);
        var sensors=AssetDatabase.LoadAssetAtPath<MultiKinectSettings>("Assets/Settings/CooperativeSensors.asset");
        if(sensors==null) {
            sensors=ScriptableObject.CreateInstance<MultiKinectSettings>();
            sensors.serialNumbers=new[]{"000415413212","000882315112"};
            sensors.minimumDistance=.5f;sensors.maximumDistance=4.5f;sensors.showStatus=false;
            sensors.launchMode=WorkerLaunchMode.Manual;
            AssetDatabase.CreateAsset(sensors,"Assets/Settings/CooperativeSensors.asset");
        }
        var root=new GameObject("Cooperative coin rally");
        var app=root.AddComponent<CooperativeRallyApp>();
        app.gameCamera=camera;
        app.template=AssetDatabase.LoadAssetAtPath<KinectSettings>("Assets/Settings/KinectSettings.asset");
        app.sensorTemplate=sensors;
        app.silhouetteMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Silhouette.mat");
        app.ballSprite=AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Ball.asset").OfType<Sprite>().First();
        app.japaneseFont=AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/Fonts/RoundedJapanese.ttf");
        if(app.template==null||app.japaneseFont==null||app.silhouetteMaterial==null)throw new InvalidOperationException("Missing rally assets");
        EditorSceneManager.SaveScene(scene,ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("COOP_SCENE_OK");
    }

    [MenuItem("Naispo/Cooperative Rally/Build Windows")]
    public static void BuildWindows()
    {
        if(!File.Exists(ScenePath))CreateScene();
        LocalSetup.CheckRuntime();
        string output="Builds/CooperativeRally/CoinRally.exe";
        PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=720;
        PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=true;
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes=new[]{ScenePath},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None
        });
        if(report.summary.result!=BuildResult.Succeeded)throw new Exception("Cooperative build failed: "+report.summary.result);
        string worker="Tools/KinectWorker/bin/Release/net10.0-windows";
        if(!File.Exists(Path.Combine(worker,"KinectWorker.exe")))throw new FileNotFoundException("Build the Kinect worker first.");
        CopyDirectory(worker,Path.Combine(Path.GetDirectoryName(output),worker));
        CopyDirectory("Documentation/Licenses",Path.Combine(Path.GetDirectoryName(output),"Licenses"));
        Debug.Log("COOP_BUILD_OK "+Path.GetFullPath(output));
    }

    static void CopyDirectory(string source,string target)
    {
        Directory.CreateDirectory(target);
        foreach(var file in Directory.GetFiles(source))File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true);
        foreach(var dir in Directory.GetDirectories(source))CopyDirectory(dir,Path.Combine(target,Path.GetFileName(dir)));
    }
}
