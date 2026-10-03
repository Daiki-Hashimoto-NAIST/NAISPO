using System;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Naispo.Kinect;
using Naispo.Core;
using Naispo.Silhouette;
using Naispo.Interaction;

public static class CompleteKinectScene
{
    public static string Run()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode first.");
        var scene=SceneManager.GetActiveScene();
        if(scene.path!="Assets/Scenes/KinectSilhouetteDemo.unity")throw new InvalidOperationException("Open KinectSilhouetteDemo first.");
        Directory.CreateDirectory("Assets/Materials");Directory.CreateDirectory("Assets/Art");
        var settings=AssetDatabase.LoadAssetAtPath<KinectSettings>("Assets/Settings/KinectSettings.asset");
        var camera=GameObject.Find("Main Camera").GetComponent<Camera>();
        camera.orthographic=true;camera.orthographicSize=5;camera.backgroundColor=Color.black;
        var system=GameObject.Find("KinectSystem");
        var manager=system.GetComponent<KinectSilhouetteManager>();
        var source=system.GetComponent<AzureKinectBodySource>();
        var mapper=system.GetComponent<KinectViewportMapper>();
        var bounce=AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>("Assets/Materials/AirHockey.physicsMaterial2D");
        if(bounce==null){bounce=new PhysicsMaterial2D("AirHockey"){friction=0,bounciness=0.9f};AssetDatabase.CreateAsset(bounce,"Assets/Materials/AirHockey.physicsMaterial2D");}
        var controller=Get<SilhouetteColliderController>(Root("PersonColliders"));
        controller.settings=settings;controller.manager=manager;controller.mapper=mapper;controller.contactMaterial=bounce;
        controller.personColliders=new PolygonCollider2D[3];
        for(int i=0;i<3;i++)
        {
            var person=Root("PersonSilhouetteCollider_"+i);person.transform.SetParent(controller.transform,false);
            var rb=Get<Rigidbody2D>(person);rb.bodyType=RigidbodyType2D.Kinematic;rb.gravityScale=0;
            rb.collisionDetectionMode=CollisionDetectionMode2D.Continuous;rb.interpolation=RigidbodyInterpolation2D.Interpolate;
            var polygon=Get<PolygonCollider2D>(person);polygon.enabled=false;polygon.sharedMaterial=bounce;
            Get<SilhouetteSurfaceMotion>(person);
            controller.personColliders[i]=polygon;
        }
        manager.colliderController=controller;
        var ball=Root("Ball");ball.transform.position=new Vector3(0,2.4f,0);
        ball.transform.localScale=Vector3.one;
        var renderer=Get<SpriteRenderer>(ball);renderer.sprite=CreateSprite("Ball",64,true);renderer.sortingOrder=10;
        var circle=Get<CircleCollider2D>(ball);circle.radius=0.25f;circle.sharedMaterial=bounce;
        var ballRb=Get<Rigidbody2D>(ball);ballRb.bodyType=RigidbodyType2D.Dynamic;ballRb.gravityScale=0;
        ballRb.mass=1;ballRb.linearDamping=0.025f;ballRb.collisionDetectionMode=CollisionDetectionMode2D.Continuous;
        ballRb.interpolation=RigidbodyInterpolation2D.Interpolate;ballRb.constraints=RigidbodyConstraints2D.FreezeRotation;
        var hockey=Get<AirHockeyBall>(ball);hockey.physicsMaterial=bounce;
        Get<SilhouetteBallPush>(ball);
        var boundary=Get<ScreenBoundaryController>(Root("ScreenBoundaries"));boundary.targetCamera=camera;boundary.physicsMaterial=bounce;
        string[] names={"TopWall","BottomWall","LeftWall","RightWall"};var walls=new BoxCollider2D[4];
        var wallSprite=CreateSprite("Wall",4,false);
        for(int i=0;i<4;i++)
        {
            var wall=Root(names[i]);wall.transform.SetParent(boundary.transform,false);
            walls[i]=Get<BoxCollider2D>(wall);walls[i].sharedMaterial=bounce;
            var view=Get<SpriteRenderer>(wall);view.sprite=wallSprite;view.color=new Color(0.3f,0.45f,0.55f);view.sortingOrder=5;
        }
        boundary.topWall=walls[0];boundary.bottomWall=walls[1];boundary.leftWall=walls[2];boundary.rightWall=walls[3];boundary.Refresh();
        // Keep Play Mode responsive while the Editor is not focused.
        PlayerSettings.runInBackground=true;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return "Saved KinectSilhouetteDemo: source, shared mapper, renderer, 3 kinematic person colliders, dynamic ball, 4 walls.";
    }
    static T Get<T>(GameObject go) where T:Component {var c=go.GetComponent<T>();return c!=null?c:go.AddComponent<T>();}
    static GameObject Root(string name){var existing=GameObject.Find(name);return existing!=null?existing:new GameObject(name);}
    static Sprite CreateSprite(string name,int size,bool circle)
    {
        string path="Assets/Art/"+name+".asset";
        var existing=AssetDatabase.LoadAssetAtPath<Sprite>(path);if(existing!=null)return existing;
        var tex=new Texture2D(size,size,TextureFormat.RGBA32,false){name=name+"Texture",filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp};
        var colors=new Color32[size*size];
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            float distance=Vector2.Distance(new Vector2(x+0.5f,y+0.5f),new Vector2(size/2f,size/2f));
            byte a=(byte)(circle?Mathf.Clamp01(size/2f-0.5f-distance)*255:255);
            colors[y*size+x]=new Color32(255,255,255,a);
        }
        tex.SetPixels32(colors);tex.Apply();
        var sprite=Sprite.Create(tex,new Rect(0,0,size,size),new Vector2(0.5f,0.5f),circle?size*2:size,0,SpriteMeshType.FullRect);
        sprite.name=name;AssetDatabase.CreateAsset(sprite,path);AssetDatabase.AddObjectToAsset(tex,sprite);return sprite;
    }
}
