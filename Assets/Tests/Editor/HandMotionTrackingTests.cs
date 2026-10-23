using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Naispo.Interaction;
using Naispo.Kinect;

namespace Naispo.Tests
{
    public sealed class HandMotionTrackingTests
    {
        CooperativeRallyApp app;
        bool sceneOpened;

        [UnityTest]
        public IEnumerator BriefLossAndSensorRestartClearBoostWhilePoseRecoveryStaysIndependent()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/CooperativeCoinRally.unity", OpenSceneMode.Single);
            sceneOpened = true;
            yield return new EnterPlayMode();
            app = UnityEngine.Object.FindFirstObjectByType<CooperativeRallyApp>();
            app.UseSensors();
            app.Sensors.StopWorkers();
            app.Sensors.enabled = false;
            app.Game.SetPaused(true);
            var sources = new MotionTrackingTestSource[2];
            for (int slot = 0; slot < 2; slot++)
            {
                var player = app.Players[slot];
                player.enabled = false;
                player.source.enabled = false;
                sources[slot] = player.gameObject.AddComponent<MotionTrackingTestSource>();
                sources[slot].Identity = (uint)(920 + slot);
                player.source = sources[slot];
                player.enabled = true;
            }
            var controller = app.Players[0].GetComponent<HandWallController>();
            var barrier = controller.Wall(0).GetComponent<ElasticBarrier>();
            yield return WaitFor(() => controller.HasUsableWall && Boost(barrier) > .05f, 5,
                "Continuous, gently rising hands must produce the capped swing input.");

            sources[0].ValidHands = false;
            yield return WaitFor(() => controller.HoldingLastPose, 1, "Brief loss must preserve the barrier pose.");
            Assert.That(controller.HasUsableWall, Is.True);
            Assert.That(Boost(barrier), Is.Zero, "A held pose must never retain the preceding swing.");

            sources[0].ValidHands = true;
            yield return WaitFor(() => controller.HasUsableWall && !controller.HoldingLastPose, 1,
                "Pose continuity should recover immediately from a brief loss.");
            Assert.That(Boost(barrier), Is.Zero,
                "Pose recovery must not also grant motion confidence from before the lost frame.");
            yield return WaitFor(() => Boost(barrier) > .05f, 3,
                "Fresh stable samples should rebuild motion after recovery.");

            sources[0].Generation++;
            yield return WaitFor(() => !controller.HasUsableWall, 1,
                "A changed sensor generation must invalidate the old pose and motion.");
            Assert.That(Boost(barrier), Is.Zero);
            yield return WaitFor(() => controller.HasUsableWall && Boost(barrier) > .05f, 3,
                "Both pose and motion should acquire again from the restarted sensor.");
        }

        static float Boost(ElasticBarrier barrier) => barrier.HandMotionBoost(.5f, Vector2.up, Time.unscaledTimeAsDouble);

        static IEnumerator WaitFor(Func<bool> predicate, float seconds, string message)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (!predicate() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(predicate(), Is.True, message);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (!sceneOpened) yield break;
            if (Application.isPlaying)
            {
                if (app != null && app.Sensors != null)
                {
                    app.Sensors.StopWorkers();
                    app.Sensors.enabled = false;
                }
                yield return new ExitPlayMode();
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            app = null;
            sceneOpened = false;
        }
    }

    public sealed class MotionTrackingTestSource : BodyTrackingSource
    {
        public bool ValidHands = true;
        public int Generation;
        public uint Identity = 920;
        double lastAt;
        float y = 207;
        long sequence;
        public override string Status => "Controlled rising hands";
        public override float CaptureFps => 30;
        public override bool WorkerAlive => isActiveAndEnabled;
        public override bool HasFailed => false;
        public override int CaptureGeneration => Generation;

        public override bool TryGetLatest(ref BodyFrameData frame)
        {
            double now = Time.unscaledTimeAsDouble;
            if (sequence > 0) y -= (float)(now - lastAt) * 24;
            lastAt = now;
            frame.EnsureSize(320, 288);
            frame.Sequence = ++sequence;
            frame.TimestampSeconds = now;
            frame.BodyCount = 1;
            frame.BodyIds[0] = Identity;
            frame.HasColor = false;
            frame.Hands[0] = new BodyFrameData.HandPair {
                Left = new System.Numerics.Vector2(100, y),
                Right = new System.Numerics.Vector2(220, y),
                SeparationMeters = 1.1f, Valid = ValidHands
            };
            return true;
        }
    }
}
