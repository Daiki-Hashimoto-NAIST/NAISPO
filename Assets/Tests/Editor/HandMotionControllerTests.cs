using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Naispo.Interaction;
using Naispo.Kinect;
using Naispo.Silhouette;

namespace Naispo.Tests
{
    public sealed class HandMotionControllerTests
    {
        Scene scene;
        GameObject root;
        KinectSettings settings;
        KinectViewportMapper mapper;
        HandWallController controller;
        ElasticBarrier barrier;
        BodyColorRegistry registry;
        BodyFrameData frame;

        [SetUp]
        public void Setup()
        {
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Hand motion wiring test");
            SceneManager.MoveGameObjectToScene(root, scene);
            settings = ScriptableObject.CreateInstance<KinectSettings>();
            var camera = root.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 5; camera.aspect = 1;
            mapper = root.AddComponent<KinectViewportMapper>();
            mapper.targetCamera = camera; mapper.settings = settings; mapper.Refresh(100, 100);
            controller = root.AddComponent<HandWallController>();
            controller.Initialize(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Silhouette.mat"),
                settings.barrierColors, AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>("Assets/Materials/AirHockey.physicsMaterial2D"));
            barrier = controller.Wall(0).GetComponent<ElasticBarrier>();
            registry = new BodyColorRegistry();
            frame = new BodyFrameData { BodyCount = 1 };
            frame.BodyIds[0] = 42;
        }

        [TearDown]
        public void Cleanup()
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(settings);
            EditorSceneManager.ClosePreviewScene(scene);
        }

        void Sample(int revision, double time, float upward, bool valid = true)
        {
            frame.Hands[0] = new BodyFrameData.HandPair {
                Left = new System.Numerics.Vector2(35, 60 - upward / mapper.UnitsPerPixel),
                Right = new System.Numerics.Vector2(65, 60 - upward / mapper.UnitsPerPixel),
                Valid = valid, SeparationMeters = 1.1f
            };
            registry.Update(frame, time, 3, 0);
            controller.Apply(frame, registry, mapper, true, revision, time, time);
            scene.GetPhysicsScene2D().Simulate(.02f);
        }

        void Moving()
        {
            for (int i = 0; i < 4; i++) Sample(i + 1, i * .04, i * .04f);
            Assert.That(barrier.HandMotionBoost(.5f, Vector2.up, .12), Is.GreaterThan(0));
        }

        [Test]
        public void ControllerPassesSensorVelocityAndOriginalReceiptToBarrier()
        {
            Moving();
            Assert.That(barrier.HandMotionBoost(.5f, Vector2.down, .12), Is.Zero);
            controller.Apply(frame, registry, mapper, true, 4, .12, .24);
            Assert.That(barrier.HandMotionBoost(.5f, Vector2.up, .24), Is.GreaterThan(0));
            Assert.That(barrier.HandMotionBoost(.5f, Vector2.up, .271), Is.Zero,
                "Reading the same pose in another physics tick must not refresh its motion.");
        }

        [Test]
        public void TrackingLossClearsBoostAndRecoveryDoesNotReuseTheOldSwing()
        {
            Moving();
            Sample(5, .14, .14f, false);
            Assert.That(barrier.HandMotionBoost(.5f, Vector2.up, .14), Is.Zero);
            Assert.That(controller.HasUsableWall, Is.False);
            Sample(6, .16, .16f);
            Assert.That(controller.HasUsableWall, Is.True);
            Assert.That(barrier.HandMotionBoost(.5f, Vector2.up, .16), Is.Zero);
        }

        [Test]
        public void MappingChangeResetsVelocityEvenWhenThePoseMovesOnlySlightly()
        {
            Moving();
            mapper.viewport = new Rect(.01f, 0, .99f, 1);
            mapper.Refresh(100, 100);
            Sample(5, .16, .16f);
            Assert.That(barrier.HandMotionBoost(.5f, Vector2.up, .16), Is.Zero);
        }

        [Test]
        public void PoseTeleportOrOwnerChangeCannotProduceBoost()
        {
            Moving();
            Sample(5, .16, 3f);
            Assert.That(barrier.HandMotionBoost(.5f, Vector2.up, .16), Is.Zero);
            controller.ResetTracking();
            Moving();
            frame.BodyIds[0] = 43;
            Sample(5, .16, .16f);
            Assert.That(controller.Wall(0).GetComponent<ElasticBarrier>().HandMotionBoost(.5f, Vector2.up, .16), Is.Zero);
            Assert.That(controller.Wall(1).GetComponent<ElasticBarrier>().HandMotionBoost(.5f, Vector2.up, .16), Is.Zero);
        }

        [Test]
        public void LegacyPoseOnlyCallAndExplicitResetBothClearBoost()
        {
            Moving();
            controller.Apply(frame, registry, mapper, true);
            Assert.That(barrier.HandMotionBoost(.5f, Vector2.up, .12), Is.Zero);
            controller.ResetTracking();
            Moving();
            controller.ResetTracking();
            Assert.That(barrier.HandMotionBoost(.5f, Vector2.up, .12), Is.Zero);
        }
    }
}
