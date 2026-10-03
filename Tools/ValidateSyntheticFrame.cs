using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Naispo.Kinect;
using Naispo.Silhouette;

public static class ValidateSyntheticFrame
{
    public static object Run()
    {
        var scene=SceneManager.CreateScene("Synthetic Kinect Validation",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var root=new GameObject("Synthetic validation fixture");SceneManager.MoveGameObjectToScene(root,scene);root.SetActive(false);
        var settings=ScriptableObject.CreateInstance<KinectSettings>();settings.showDebugOverlay=false;
        var cameraGo=new GameObject("SyntheticValidationCamera");cameraGo.transform.SetParent(root.transform);
        var camera=cameraGo.AddComponent<Camera>();camera.transform.position=new Vector3(1000,1000,-10);camera.orthographic=true;camera.orthographicSize=5;camera.aspect=16f/9f;camera.backgroundColor=Color.black;camera.clearFlags=CameraClearFlags.SolidColor;camera.cullingMask=1<<31;camera.enabled=false;
        cameraGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
        var mapper=root.AddComponent<KinectViewportMapper>();mapper.settings=settings;mapper.targetCamera=camera;
        var go=new GameObject("Synthetic silhouettes");go.layer=31;go.transform.SetParent(root.transform);
        var renderer=go.AddComponent<SilhouetteRenderer>();renderer.settings=settings;renderer.mapper=mapper;
        var mat=new Material(Shader.Find("Naispo/SilhouetteUnlit"));renderer.silhouetteMaterial=mat;
        var frame=new BodyFrameData{BodyCount=3,Sequence=1};frame.EnsureSize(320,288);
        for(int i=0;i<frame.BodyIndexMap.Length;i++)frame.BodyIndexMap[i]=255;
        for(int b=0;b<3;b++)
        {
            frame.BodyIds[b]=(uint)(101+b*101);int cx=60+b*100;
            for(int y=25;y<260;y++)for(int x=cx-42;x<=cx+42;x++)
            {
                bool head=(x-cx)*(x-cx)+(y-52)*(y-52)<18*18;
                bool torso=Math.Abs(x-cx)<21&&y>=80&&y<175;
                bool arms=y>=90&&y<104;
                bool legs=y>=170&&((x>=cx-21&&x<cx-4)||(x>cx+4&&x<=cx+21));
                if(head||torso||arms||legs)frame.BodyIndexMap[y*320+x]=(byte)b;
            }
        }
        var registry=new BodyColorRegistry();registry.Update(frame,0,3,0.75);
        root.SetActive(true);mapper.Refresh(320,288);renderer.Apply(frame,registry);
        var extractor=new SilhouetteContourExtractor();int[] points=new int[3];
        for(int b=0;b<3;b++)
        {
            extractor.Extract(frame.BodyIndexMap,320,288,registry.BodyIndexToSlot,b,40,2,2.5f,192,4);points[b]=extractor.TotalPoints;
            var person=new GameObject("Synthetic collider "+b);SceneManager.MoveGameObjectToScene(person,scene);person.transform.SetParent(root.transform);
            var rb=person.AddComponent<Rigidbody2D>();rb.bodyType=RigidbodyType2D.Kinematic;
            var collider=person.AddComponent<PolygonCollider2D>();collider.pathCount=extractor.PathCount;
            for(int p=0;p<extractor.PathCount;p++)
            {var world=new List<Vector2>();foreach(var pixel in extractor.Paths[p].Points)world.Add(mapper.PixelToWorld(pixel));collider.SetPath(p,world);}
        }
        for(int i=0;i<6;i++)extractor.Extract(frame.BodyIndexMap,320,288,registry.BodyIndexToSlot,0,40,2,2.5f,192,4);
        var timer=System.Diagnostics.Stopwatch.StartNew();long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<30;i++)for(int b=0;b<3;b++)extractor.Extract(frame.BodyIndexMap,320,288,registry.BodyIndexToSlot,b,40,2,2.5f,192,4);
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;timer.Stop();
        return new{kind="SYNTHETIC (not sensor data)",persons=3,points,meanMillisecondsPerThreeBodies=timer.Elapsed.TotalMilliseconds/30,allocatedBytesAcross90Extractions=allocated,aspect=320f/288f,worldSize=new Vector2(mapper.UnitsPerPixel*320,mapper.UnitsPerPixel*288),camera="SyntheticValidationCamera"};
    }
}
