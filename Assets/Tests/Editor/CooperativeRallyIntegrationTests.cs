using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Naispo.Core;
using Naispo.Interaction;
using Naispo.Kinect;
using Naispo.MultiKinect;

namespace Naispo.Tests
{
    public sealed class CooperativeRallyIntegrationTests
    {
        const string ScenePath = "Assets/Scenes/CooperativeCoinRally.unity";
        CooperativeRallyApp app;

        [UnitySetUp]
        public IEnumerator OpenCooperativeScene()
        {
            Assert.That(File.Exists(ScenePath), Is.True, "The playable cooperative scene must be included.");
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            yield return new EnterPlayMode();
            app = UnityEngine.Object.FindFirstObjectByType<CooperativeRallyApp>();
            Assert.That(app, Is.Not.Null);
            yield return WaitFor(() => app.BothPlayersReady && VisibleBarrierCount(app.Players[0]) == 1 &&
                VisibleBarrierCount(app.Players[1]) == 1, 5, "Both demo players must create a barrier.");
        }

        [UnityTest]
        public IEnumerator SceneCreatesTwoIndependentPlayersAndOneSharedCourt()
        {
            Assert.That(UnityEngine.Object.FindObjectsByType<KinectSilhouetteManager>(FindObjectsSortMode.None).Length, Is.EqualTo(2));
            Assert.That(UnityEngine.Object.FindObjectsByType<CooperativeCoinGame>(FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(UnityEngine.Object.FindObjectsByType<LightCoinGame>(FindObjectsSortMode.None), Is.Empty,
                "Per-player single-player games would create duplicate balls, coins, and scoring.");
            var balls = UnityEngine.Object.FindObjectsByType<AirHockeyBall>(FindObjectsSortMode.None);
            Assert.That(balls.Length, Is.EqualTo(1));
            Assert.That(balls[0].CooperativeGame, Is.SameAs(app.Game));
            Assert.That(app.Game.NetCollider, Is.Not.Null);
            Assert.That(app.Game.NetCollider.isTrigger, Is.False);
            Assert.That(app.Game.NetCollider.transform.IsChildOf(app.Game.transform), Is.True);
            int centerNets = 0;
            foreach (var collider in UnityEngine.Object.FindObjectsByType<BoxCollider2D>(FindObjectsSortMode.None))
                if (collider.gameObject.name == "Low center net") centerNets++;
            Assert.That(centerNets, Is.EqualTo(1));
            Assert.That(app.Players[0], Is.Not.SameAs(app.Players[1]));
            Assert.That(app.Players[0].source, Is.Not.SameAs(app.Players[1].source));
            Assert.That(app.Players[0].CurrentFrame.BodyIds[0], Is.Not.EqualTo(app.Players[1].CurrentFrame.BodyIds[0]));
            for (int slot = 0; slot < 2; slot++) AssertPlayerBarrier(slot);
            Assert.That(app.Sensors.WorkerRunning(0) || app.Sensors.WorkerRunning(1), Is.False,
                "Opening the scene in demo mode must not start physical camera workers.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator DemoHandsMoveThroughTheSameFrameAndBarrierMappingAsSensors()
        {
            var player = app.Players[0];
            var source = player.source as CooperativeDemoSource;
            Assert.That(source, Is.Not.Null);
            long previousSequence = player.CurrentFrame.Sequence;
            Vector2 previousPosition = VisibleBarrier(player).attachedRigidbody.position;
            source.horizontal = 194;
            source.vertical = 187;
            source.angle = 27;
            source.span = 96;
            yield return WaitFor(() => player.CurrentFrame.Sequence > previousSequence + 2 &&
                Vector2.Distance(VisibleBarrier(player).attachedRigidbody.position, previousPosition) > .5f,
                3, "Demo changes must reach the actual physics barrier through BodyFrameData.");
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.That(player.CurrentFrame.Hands[0].Valid, Is.True);
            Assert.That(player.CurrentFrame.Hands[0].SeparationMeters, Is.GreaterThan(0));
            AssertPlayerBarrier(0);
            AssertPlayerBarrier(1);
        }

        [UnityTest]
        public IEnumerator MissingSensorsHideOldBarriersPauseBallAndReturnCleanlyToDemo()
        {
            app.UseSensors();
            // StartWorkers is deferred until Update. Stop it in this same turn so this
            // test exercises the real mode switch without opening or enumerating hardware.
            app.Sensors.StopWorkers();
            app.Sensors.enabled = false;
            yield return WaitFor(() => !app.Game.InputReady && VisibleBarrierCount(app.Players[0]) == 0 &&
                VisibleBarrierCount(app.Players[1]) == 0, 3, "Unavailable sensors must remove both previous demo barriers.");
            Assert.That(app.SensorMode, Is.True);
            Assert.That(app.BothPlayersReady, Is.False);
            var ball = UnityEngine.Object.FindFirstObjectByType<AirHockeyBall>();
            Assert.That(ball.Body.simulated, Is.False);
            Assert.That(app.Sensors.WorkerPid(0), Is.Zero);
            Assert.That(app.Sensors.WorkerPid(1), Is.Zero);

            app.UseDemo();
            yield return WaitFor(() => app.BothPlayersReady && app.Game.InputReady && ball.Body.simulated,
                5, "Returning to demo must restore input and release the next shared serve.");
            Assert.That(app.SensorMode, Is.False);
            for (int slot = 0; slot < 2; slot++)
            {
                Assert.That(app.Players[slot].source, Is.TypeOf<CooperativeDemoSource>());
                Assert.That(app.Players[slot].CurrentFrame.BodyIds[0], Is.EqualTo((uint)(100 + slot)));
                AssertPlayerBarrier(slot);
            }
        }

        [UnityTest]
        public IEnumerator TwoPackagedSyntheticWorkersFeedHandsAndStopWithoutStaleInput()
        {
            string workerPath = app.Sensors.settings.workerExecutable;
            if (!Path.IsPathRooted(workerPath))
                workerPath = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), workerPath);
            if (!File.Exists(workerPath)) Assert.Ignore("Build Tools/KinectWorker/Build.ps1 to run the packaged process integration test.");

            app.UseSensors();
            // Replace the pending physical launch before any Update can run.
            app.Sensors.settings.pipeNames = new[] { "CoinIntegrationLeft_" + Guid.NewGuid().ToString("N"),
                "CoinIntegrationRight_" + Guid.NewGuid().ToString("N") };
            app.Sensors.RestartWorkers(WorkerLaunchMode.AutomaticSynthetic);
            yield return WaitFor(() => app.BothPlayersReady && app.Game.InputReady &&
                app.Sensors.receivers[0].ReceivedFrames >= 3 && app.Sensors.receivers[1].ReceivedFrames >= 3 &&
                VisibleBarrierCount(app.Players[0]) == 1 && VisibleBarrierCount(app.Players[1]) == 1,
                15, "Two packaged workers must deliver independent fresh hand frames. " + app.Sensors.LifecycleStatus);
            Assert.That(app.Sensors.ActiveMode, Is.EqualTo(WorkerLaunchMode.AutomaticSynthetic));
            Assert.That(app.Sensors.WorkerPid(0), Is.GreaterThan(0));
            Assert.That(app.Sensors.WorkerPid(1), Is.GreaterThan(0));
            Assert.That(app.Sensors.WorkerPid(0), Is.Not.EqualTo(app.Sensors.WorkerPid(1)));
            for (int slot = 0; slot < 2; slot++)
            {
                var receiver = app.Sensors.receivers[slot];
                Assert.That(app.Players[slot].source, Is.SameAs(receiver));
                Assert.That(app.Players[slot].GetComponent<CooperativeDemoSource>().enabled, Is.False);
                Assert.That(receiver.WorkerAlive, Is.True);
                Assert.That(receiver.AgeSeconds, Is.LessThan(receiver.staleTimeout));
                Assert.That(app.Sensors.TryGetIdentity(slot, out var serial, out var nativeId), Is.True);
                Assert.That(serial, Is.EqualTo("SYNTHETIC" + slot));
                Assert.That(nativeId, Is.EqualTo(42));
                var frame = app.Players[slot].CurrentFrame;
                var left = receiver.LatestMetadata.Joints[KinectHandFrameAdapter.LeftHandJoint];
                var right = receiver.LatestMetadata.Joints[KinectHandFrameAdapter.RightHandJoint];
                Assert.That(frame.Hands[0].Left.X, Is.EqualTo(left.U).Within(.001));
                Assert.That(frame.Hands[0].Right.X, Is.EqualTo(right.U).Within(.001));
                float metricSpan = Vector3.Distance(new Vector3(left.X, left.Y, left.Z), new Vector3(right.X, right.Y, right.Z)) * .001f;
                Assert.That(frame.Hands[0].SeparationMeters, Is.EqualTo(metricSpan).Within(.001));
                // Worker motion can advance between Update and FixedUpdate. Check
                // the shared viewport and drawn collision shape independently of
                // that expected one-physics-tick delay.
                AssertPlayerBarrier(slot, false);
            }
            Assert.That(app.Players[0].CurrentFrame.BodyIds[0], Is.Not.EqualTo(app.Players[1].CurrentFrame.BodyIds[0]),
                "Identical native IDs from different sensors must have separate game identities.");

            // Leave the receiver enabled but stop its pipe thread: cached hands must
            // expire naturally and pause the common game when only one side is lost.
            app.Sensors.receivers[0].StopReceiving();
            yield return WaitFor(() => !app.Players[0].IsFresh && !app.Game.InputReady &&
                VisibleBarrierCount(app.Players[0]) == 0, 3, "A stopped stream must not leave a playable stale hand wall.");
            Assert.That(app.Players[1].IsFresh, Is.True);
            Assert.That(UnityEngine.Object.FindFirstObjectByType<AirHockeyBall>().Body.simulated, Is.False);
            app.UseDemo();
            yield return WaitFor(() => app.BothPlayersReady && app.Game.InputReady &&
                !app.Sensors.WorkerRunning(0) && !app.Sensors.WorkerRunning(1) && !app.Sensors.IsReconfiguring,
                10, "Switching to demo must stop both owned child processes and restore live demo input.");
            // A fresh demo frame is consumed in Update, while HandWallController
            // recreates its collision wall in FixedUpdate. Worker shutdown can
            // finish before the next physics tick, so wait for that real output.
            yield return WaitFor(() => VisibleBarrierCount(app.Players[0]) == 1 &&
                VisibleBarrierCount(app.Players[1]) == 1, 1,
                "The next physics update must restore exactly one demo barrier per player.");
            AssertPlayerBarrier(0);
            AssertPlayerBarrier(1);
        }

        void AssertPlayerBarrier(int slot, bool matchLatestFrame = true)
        {
            var player = app.Players[slot];
            Assert.That(player.IsFresh, Is.True);
            Assert.That(VisibleBarrierCount(player), Is.EqualTo(1), "Each player has exactly one active barrier.");
            var wall = VisibleBarrier(player);
            var line = wall.GetComponent<LineRenderer>();
            Assert.That(line.enabled, Is.True);
            var points = wall.points;
            Vector2 left = wall.transform.TransformPoint(points[0]);
            Vector2 right = wall.transform.TransformPoint(points[points.Length - 1]);
            Assert.That(HandWallController.TryEndpoints(player.CurrentFrame.Hands[0], player.mapper,
                out var expectedLeft, out var expectedRight), Is.True);
            if (matchLatestFrame)
            {
                Assert.That(Vector2.Distance(left, expectedLeft), Is.LessThan(.08f));
                Assert.That(Vector2.Distance(right, expectedRight), Is.LessThan(.08f));
            }
            foreach (var endpoint in new[] { left, right })
            {
                Vector3 viewport = app.gameCamera.WorldToViewportPoint(endpoint);
                Assert.That(viewport.x, Is.InRange(player.mapper.viewport.xMin, player.mapper.viewport.xMax));
                Assert.That(viewport.y, Is.InRange(player.mapper.viewport.yMin, player.mapper.viewport.yMax));
            }
            Assert.That(Vector3.Distance(line.transform.TransformPoint(line.GetPosition(0)), wall.transform.TransformPoint(points[0])), Is.LessThan(.02f));
            Assert.That(Vector3.Distance(line.transform.TransformPoint(line.GetPosition(line.positionCount - 1)), wall.transform.TransformPoint(points[points.Length - 1])), Is.LessThan(.02f));
        }

        static int VisibleBarrierCount(KinectSilhouetteManager player)
        {
            int count = 0;
            var controller = player.GetComponent<HandWallController>();
            for (int slot = 0; slot < 3; slot++) if (controller.Wall(slot) != null && controller.Wall(slot).enabled) count++;
            return count;
        }

        static EdgeCollider2D VisibleBarrier(KinectSilhouetteManager player)
        {
            var controller = player.GetComponent<HandWallController>();
            for (int slot = 0; slot < 3; slot++) if (controller.Wall(slot) != null && controller.Wall(slot).enabled) return controller.Wall(slot);
            Assert.Fail("Player has no active hand barrier.");
            return null;
        }

        static IEnumerator WaitFor(Func<bool> condition, float timeout, string message)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + timeout;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (Application.isPlaying)
            {
                if (app != null && app.Sensors != null)
                {
                    app.Sensors.StopWorkers();
                    app.Sensors.enabled = false;
                    double deadline = Time.realtimeSinceStartupAsDouble + 10;
                    while ((app.Sensors.WorkerRunning(0) || app.Sensors.WorkerRunning(1) || app.Sensors.IsReconfiguring) &&
                        Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                }
                yield return new ExitPlayMode();
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            app = null;
        }
    }
}
