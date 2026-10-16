using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Naispo.Kinect;
using Naispo.Silhouette;
using Naispo.Interaction;

namespace Naispo.Tests
{
    public class HandWallTests
    {
        Scene scene;
        GameObject root;
        KinectSettings settings;
        KinectViewportMapper mapper;
        HandWallController controller;
        BodyColorRegistry registry;
        BodyFrameData frame;
        readonly System.Collections.Generic.List<PhysicsMaterial2D> ballMaterials = new System.Collections.Generic.List<PhysicsMaterial2D>();
        [SetUp] public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Hand wall test");
            SceneManager.MoveGameObjectToScene(root, scene);
            settings = ScriptableObject.CreateInstance<KinectSettings>();
            var camera = root.AddComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 5; camera.aspect = 1;
            mapper = root.AddComponent<KinectViewportMapper>(); mapper.targetCamera = camera; mapper.settings = settings; mapper.Refresh(100,100);
            controller = root.AddComponent<HandWallController>();
            controller.Initialize(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Silhouette.mat"), settings.barrierColors,
                AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>("Assets/Materials/AirHockey.physicsMaterial2D"));
            frame = new BodyFrameData { BodyCount = 2 };
            frame.BodyIds[0] = 10; frame.BodyIds[1] = 20;
            frame.Hands[0] = Pair(20,30,70,30); frame.Hands[1] = Pair(25,70,80,70);
            registry = new BodyColorRegistry(); registry.Update(frame,0,3,0.75);
        }
        static BodyFrameData.HandPair Pair(float x,float y,float u,float v) => new BodyFrameData.HandPair {
            Left = new System.Numerics.Vector2(x,y), Right = new System.Numerics.Vector2(u,v), Valid = true };
        [TearDown] public void Cleanup() { Object.DestroyImmediate(root); Object.DestroyImmediate(settings); EditorSceneManager.ClosePreviewScene(scene); foreach(var m in ballMaterials)Object.DestroyImmediate(m);ballMaterials.Clear(); }
        [Test] public void TwoPeopleKeepSeparateWallsWhenBodyIndicesSwap()
        {
            controller.Apply(frame,registry,mapper,true);
            var p0 = controller.Wall(0).attachedRigidbody.position;
            var p1 = controller.Wall(1).attachedRigidbody.position;
            Assert.That(p0.y,Is.Not.EqualTo(p1.y));
            (frame.BodyIds[0],frame.BodyIds[1])=(frame.BodyIds[1],frame.BodyIds[0]);
            (frame.Hands[0],frame.Hands[1])=(frame.Hands[1],frame.Hands[0]);
            registry.Update(frame,0.1,3,0.75); controller.Apply(frame,registry,mapper,true);
            scene.GetPhysicsScene2D().Simulate(0.02f);
            Assert.That(Vector2.Distance(controller.Wall(0).attachedRigidbody.position,p0),Is.LessThan(0.001f));
            Assert.That(Vector2.Distance(controller.Wall(1).attachedRigidbody.position,p1),Is.LessThan(0.001f));
            Assert.That(controller.Wall(2).enabled,Is.False);
        }
        [Test] public void TrackingLossAndModeSwitchRemoveCollidersImmediately()
        {
            controller.Apply(frame,registry,mapper,true);
            frame.Hands[0] = default;
            controller.Apply(frame,registry,mapper,true);
            Assert.That(controller.Wall(0).enabled,Is.False);
            Assert.That(controller.Wall(1).enabled,Is.True);
            controller.Apply(frame,registry,mapper,false);
            Assert.That(controller.Wall(1).enabled,Is.False);
            Assert.That(HandWallController.TryEndpoints(Pair(float.NaN,30,70,30),mapper,out _,out _),Is.False);
            Assert.That(HandWallController.TryEndpoints(Pair(-10,30,70,30),mapper,out _,out _),Is.False);
            Assert.That(HandWallController.TryEndpoints(Pair(30,30,30,30),mapper,out _,out _),Is.False);
        }
        [Test] public void BallBouncesOffHandWall()
        {
            controller.Apply(frame,registry,mapper,true);
            var ball = new GameObject("Ball"); SceneManager.MoveGameObjectToScene(ball,scene);
            ball.transform.position = controller.Wall(0).attachedRigidbody.position + Vector2.down;
            var ballCollider = ball.AddComponent<CircleCollider2D>(); ballCollider.radius = 0.15f;
            ballMaterials.Add(AirHockeyBall.ConfigureContact(ballCollider, AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>("Assets/Materials/AirHockey.physicsMaterial2D")));
            var body = ball.AddComponent<Rigidbody2D>(); body.gravityScale = 0; body.linearDamping = 0;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous; body.linearVelocity = Vector2.up*8;
            for(int i=0;i<10;i++) scene.GetPhysicsScene2D().Simulate(0.02f);
            Assert.That(body.linearVelocity.y,Is.LessThan(-1));
        }
        [Test] public void NarrowBarrierReflectsFasterThanWideBarrier()
        {
            frame.Hands[0].SeparationMeters = 0.3f;
            frame.Hands[1].SeparationMeters = 1.5f;
            controller.Apply(frame,registry,mapper,true);
            var balls = new Rigidbody2D[2];
            for(int i=0;i<2;i++)
            {
                var go = new GameObject("Bounce comparison"); SceneManager.MoveGameObjectToScene(go,scene);
                go.transform.position = controller.Wall(i).attachedRigidbody.position + Vector2.down;
                var ballCollider=go.AddComponent<CircleCollider2D>(); ballCollider.radius=0.15f;
                ballMaterials.Add(AirHockeyBall.ConfigureContact(ballCollider,AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>("Assets/Materials/AirHockey.physicsMaterial2D")));
                balls[i]=go.AddComponent<Rigidbody2D>(); balls[i].gravityScale=0; balls[i].linearDamping=0;
                balls[i].collisionDetectionMode=CollisionDetectionMode2D.Continuous;
                balls[i].linearVelocity=Vector2.up*8;
            }
            for(int i=0;i<10;i++) scene.GetPhysicsScene2D().Simulate(0.02f);
            Assert.That(balls[0].linearVelocity.y,Is.LessThan(-1));
            Assert.That(balls[1].linearVelocity.y,Is.LessThan(-1));
            Assert.That(-balls[0].linearVelocity.y,Is.GreaterThan(-balls[1].linearVelocity.y*1.5f));
        }
        [Test] public void WideBarrierBendsMoreAndSettlesWithPinnedEnds()
        {
            controller.Apply(frame,registry,mapper,true);
            var tight=controller.Wall(0).GetComponent<ElasticBarrier>();
            var soft=controller.Wall(1).GetComponent<ElasticBarrier>();
            tight.SetSpan(3,0.3f,0.16f,true); soft.SetSpan(3,1.5f,0.16f,true);
            tight.Kick(8,1); soft.Kick(8,1);
            float tightPeak=0, softPeak=0;
            bool reversed=false;
            for(int i=0;i<300;i++)
            {
                tight.Step(0.02f); soft.Step(0.02f);
                tightPeak=Mathf.Max(tightPeak,Mathf.Abs(tight.Deflection)); softPeak=Mathf.Max(softPeak,Mathf.Abs(soft.Deflection));
                if(soft.Deflection < -0.01f) reversed=true;
                Assert.That(soft.Shape[0].y,Is.Zero); Assert.That(soft.Shape[24].y,Is.Zero);
                Assert.That(controller.Wall(1).points,Is.EqualTo(soft.Shape));
            }
            Assert.That(softPeak,Is.GreaterThan(tightPeak*2));
            Assert.That(reversed,Is.True);
            Assert.That(Mathf.Abs(soft.Deflection),Is.LessThan(0.001f));
            soft.Kick(8,1); soft.Step(0.02f);
            controller.Apply(frame,registry,mapper,false);
            Assert.That(soft.Deflection,Is.Zero);
        }
        [Test] public void BarrierPaletteIsIndependentFromBodyPalette()
        {
            var texture=(Texture2D)controller.Wall(0).GetComponent<LineRenderer>().sharedMaterial.mainTexture;
            Assert.That(texture.GetPixel(0,0).r,Is.EqualTo(settings.barrierColors[0].r).Within(0.01f));
            Assert.That(texture.GetPixel(0,0),Is.Not.EqualTo(settings.personColors[0]));
        }
    }
}
