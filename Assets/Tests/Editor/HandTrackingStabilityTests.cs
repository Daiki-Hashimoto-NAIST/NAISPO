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
    public sealed class HandTrackingStabilityTests
    {
        static readonly Vector2 Left = new Vector2(-2, -1);
        static readonly Vector2 Right = new Vector2(2, -1);
        CooperativeRallyApp app;
        bool sceneOpened;

        static void Observe(HandPoseStabilizer pose, long revision, double time, bool valid = true,
            uint? identity = 7) => pose.Observe(revision, identity, valid, Left, Right, 1.2f, time, time);

        static HandPoseStabilizer Acquired()
        {
            var pose = new HandPoseStabilizer();
            Observe(pose, 1, 0);
            Observe(pose, 2, .21);
            Assert.That(pose.Ready, Is.True);
            return pose;
        }

        [Test]
        public void AcquisitionRequiresDistinctSamplesSpanningRecoveryTime()
        {
            var pose = new HandPoseStabilizer();
            Observe(pose, 1, 0);
            Assert.That(pose.Ready, Is.False);
            Assert.That(pose.Acquiring, Is.True);
            pose.Advance(.21);
            Assert.That(pose.Ready, Is.False, "Elapsed time alone is not evidence of stable tracking.");
            Observe(pose, 1, .21);
            Assert.That(pose.Ready, Is.False, "A cached frame cannot finish acquisition.");
            Observe(pose, 2, .22);
            Assert.That(pose.Ready, Is.True);
            Assert.That(pose.Acquiring, Is.False);
        }

        [Test]
        public void BriefLossPreservesLastValidPoseAndExpiresFromItsReceipt()
        {
            var pose = Acquired();
            pose.Observe(3, 7, false, Vector2.one * 99, Vector2.one * 100, 9, .26, .26);
            Assert.That(pose.Ready, Is.True);
            Assert.That(pose.Holding, Is.True);
            Assert.That(pose.Left, Is.EqualTo(Left));
            Assert.That(pose.Right, Is.EqualTo(Right));
            Assert.That(pose.SeparationMeters, Is.EqualTo(1.2f));
            Observe(pose, 4, .44, false);
            Assert.That(pose.Ready, Is.True);
            pose.Advance(.47);
            Assert.That(pose.Ready, Is.False, "Repeated invalid samples must not postpone expiry.");
            Assert.That(pose.Holding, Is.False);
        }

        [Test]
        public void DuplicateValidFrameCannotExtendGraceOrChangeHeldPose()
        {
            var pose = Acquired();
            pose.Observe(2, 7, true, Vector2.one * 99, Vector2.one * 100, 9, .44, .44);
            Assert.That(pose.Left, Is.EqualTo(Left));
            Assert.That(pose.Right, Is.EqualTo(Right));
            pose.Advance(.47);
            Assert.That(pose.Ready, Is.False);
        }

        [Test]
        public void QuickRecoveryKeepsPlayingButLongLossRequiresStableRecovery()
        {
            var pose = Acquired();
            Observe(pose, 3, .25, false);
            Observe(pose, 4, .35);
            Assert.That(pose.Ready, Is.True, "A brief occlusion must not restart acquisition.");
            Assert.That(pose.Holding, Is.False);
            Observe(pose, 5, .65, false);
            Assert.That(pose.Ready, Is.False);
            Observe(pose, 6, .70);
            pose.Advance(.92);
            Assert.That(pose.Ready, Is.False, "One good frame after a long loss must not restart the game.");
            Observe(pose, 7, .93);
            Assert.That(pose.Ready, Is.True);
        }

        [Test]
        public void AlternatingGoodAndBadSamplesCannotRepeatedlyResumeTheGame()
        {
            var pose = Acquired();
            Observe(pose, 3, .50, false);
            Observe(pose, 4, .60);
            Observe(pose, 5, .69, false);
            Observe(pose, 6, .78);
            Observe(pose, 7, .87, false);
            Observe(pose, 8, .96);
            Assert.That(pose.Ready, Is.False);
            Observe(pose, 9, 1.10);
            Assert.That(pose.Ready, Is.False);
            Observe(pose, 10, 1.18);
            Assert.That(pose.Ready, Is.True);
        }

        [Test]
        public void MissingIdentityCanUseGraceButNewKnownIdentityCannotBorrowIt()
        {
            var pose = Acquired();
            Observe(pose, 3, .25, false, null);
            Assert.That(pose.Ready, Is.True);
            Assert.That(pose.Owner, Is.EqualTo(7));
            Observe(pose, 4, .28, false, 8);
            Assert.That(pose.Ready, Is.False);
            Assert.That(pose.Holding, Is.False);
            Observe(pose, 5, .32, true, 8);
            Assert.That(pose.Ready, Is.False);
            Observe(pose, 6, .54, true, 8);
            Assert.That(pose.Ready, Is.True);
            Assert.That(pose.Owner, Is.EqualTo(8));
        }

        [Test]
        public void OldReceiptAndLongGapCannotCountAsContinuousRecovery()
        {
            var pose = new HandPoseStabilizer();
            pose.Observe(1, 7, true, Left, Right, 1.2f, 0, 1);
            Assert.That(pose.Acquiring, Is.False);
            Observe(pose, 2, 1.1);
            Observe(pose, 3, 1.5);
            Assert.That(pose.Ready, Is.False);
            Observe(pose, 4, 1.72);
            Assert.That(pose.Ready, Is.True);
        }

        [Test]
        public void ResetDropsPoseAndRequiresNewStableInput()
        {
            var pose = Acquired();
            pose.Reset();
            Assert.That(pose.Ready || pose.Holding || pose.Acquiring, Is.False);
            Assert.That(pose.Left, Is.EqualTo(Vector2.zero));
            Observe(pose, 1, 1);
            Assert.That(pose.Ready, Is.False);
            Observe(pose, 2, 1.21);
            Assert.That(pose.Ready, Is.True);
        }

        [UnityTest]
        public IEnumerator SensorPipelineHoldsWallThenPausesAndOnlyResumesAfterStableFrames()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/CooperativeCoinRally.unity", OpenSceneMode.Single);
            sceneOpened = true;
            yield return new EnterPlayMode();
            app = UnityEngine.Object.FindFirstObjectByType<CooperativeRallyApp>();
            Assert.That(app, Is.Not.Null);
            app.UseSensors();
            // The launch is deferred until Update. Replace the pending physical
            // sources in this same turn, so this test never opens real cameras.
            app.Sensors.StopWorkers();
            app.Sensors.enabled = false;
            app.Game.SetPaused(true);
            var sources = new ControlledHandSource[2];
            for (int slot = 0; slot < 2; slot++)
            {
                var player = app.Players[slot];
                player.enabled = false;
                player.source.enabled = false;
                sources[slot] = player.gameObject.AddComponent<ControlledHandSource>();
                sources[slot].Identity = (uint)(900 + slot);
                player.source = sources[slot];
                player.enabled = true;
            }
            var controller = app.Players[0].GetComponent<HandWallController>();
            var otherController = app.Players[1].GetComponent<HandWallController>();
            yield return WaitFor(() => app.BothPlayersReady && app.Game.InputReady, 5,
                "Stable hand samples from both players must prepare the game.");
            var wall = controller.Wall(0);
            sources[0].ValidHands = false;
            sources[0].HorizontalOffset = 30;
            yield return WaitFor(() => controller.HoldingLastPose, 1, "A brief loss must hold the last barrier.");
            Vector2 heldPosition = wall.attachedRigidbody.position;
            float heldRotation = wall.attachedRigidbody.rotation;
            yield return new WaitForFixedUpdate();
            Assert.That(controller.HasUsableWall && wall.enabled, Is.True);
            Assert.That(wall.GetComponent<LineRenderer>().enabled, Is.True);
            Assert.That(Vector2.Distance(wall.attachedRigidbody.position, heldPosition), Is.LessThan(.001f));
            Assert.That(wall.attachedRigidbody.rotation, Is.EqualTo(heldRotation).Within(.001f));
            Assert.That(wall.attachedRigidbody.linearVelocity.sqrMagnitude, Is.LessThan(.000001f));
            Assert.That(Mathf.Abs(wall.attachedRigidbody.angularVelocity), Is.LessThan(.001f));
            Assert.That(app.BothPlayersReady && app.Game.InputReady, Is.True);

            yield return WaitFor(() => !controller.HasUsableWall && !app.Game.InputReady, 2,
                "A prolonged loss must remove the wall and pause in the same physics tick.");
            Assert.That(wall.enabled || wall.GetComponent<LineRenderer>().enabled, Is.False);
            Assert.That(otherController.HasUsableWall, Is.True, "The other player's wall must remain usable.");
            sources[0].ValidHands = true;
            sources[0].Streaming = false;
            sources[0].FramesRemaining = 1;
            long previousRevision = app.Players[0].FrameRevision;
            yield return WaitFor(() => app.Players[0].FrameRevision > previousRevision, 1,
                "The single recovery sample must reach the actual manager.");
            long singleRevision = app.Players[0].FrameRevision;
            yield return new WaitForSecondsRealtime(.22f);
            Assert.That(app.Players[0].FrameRevision, Is.EqualTo(singleRevision));
            Assert.That(controller.HasUsableWall || app.Game.InputReady, Is.False,
                "Repeated reads of one cached good frame must not unlock the ball.");
            sources[0].Streaming = true;
            yield return WaitFor(() => controller.HasUsableWall && app.Game.InputReady, 3,
                "Continuous valid samples must recover without reconnecting the camera.");

            sources[0].Streaming = false;
            sources[0].Generation++;
            yield return WaitFor(() => !controller.HasUsableWall && !app.Game.InputReady, 1,
                "A restarted sensor session must invalidate the old hand pose immediately.");
            Assert.That(controller.HoldingLastPose, Is.False);
        }

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

    // A live transport with controllable hand samples; it keeps transport health
    // independent of whether another camera frame has arrived.
    public sealed class ControlledHandSource : BodyTrackingSource
    {
        public bool ValidHands = true, Streaming = true;
        public int FramesRemaining, Generation;
        public float HorizontalOffset;
        public uint Identity = 900;
        long sequence;
        public override string Status => "Controlled test hands";
        public override float CaptureFps => 30;
        public override bool WorkerAlive => isActiveAndEnabled;
        public override bool HasFailed => false;
        public override int CaptureGeneration => Generation;
        public override bool TryGetLatest(ref BodyFrameData frame)
        {
            if (!Streaming && FramesRemaining <= 0) return false;
            if (!Streaming) FramesRemaining--;
            frame.EnsureSize(320, 288);
            frame.Sequence = ++sequence;
            frame.TimestampSeconds = Time.unscaledTimeAsDouble;
            frame.BodyCount = 1;
            frame.BodyIds[0] = Identity;
            frame.HasColor = false;
            frame.Hands[0] = new BodyFrameData.HandPair
            {
                Left = new System.Numerics.Vector2(100 + HorizontalOffset, 207),
                Right = new System.Numerics.Vector2(220 + HorizontalOffset, 207),
                SeparationMeters = 1.1f,
                Valid = ValidHands
            };
            return true;
        }
    }
}
