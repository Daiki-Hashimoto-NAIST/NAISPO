using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Naispo.Interaction;

namespace Naispo.Tests
{
    /// <summary>
    /// Exercises collection and suspension through real Update/LateUpdate frames.
    /// Local physics keeps the ball still without disabling the game clock.
    /// </summary>
    public sealed class MagicalGardenPlayTests
    {
        const float FrameStep = 1f / 60f;
        static readonly FieldInfo SettingsOpenField = typeof(CooperativeCoinGame)
            .GetField("settingsOpen", BindingFlags.Instance | BindingFlags.NonPublic);
        CooperativeCoinGame game;
        AirHockeyBall ball;
        float previousCaptureDeltaTime;
        bool captureTimeChanged;

        enum Suspension { Pause, InputWait, Settings, ExternalOverlay, ComponentDisabled }

        [UnitySetUp]
        public IEnumerator CreateCourt()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            previousCaptureDeltaTime = Time.captureDeltaTime;
            Time.captureDeltaTime = FrameStep;
            captureTimeChanged = true;

            var scene = SceneManager.CreateScene("Magical garden play test",
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            var cameraObject = new GameObject("Test camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.aspect = 16f / 9f;
            var ballObject = new GameObject("Shared ball");
            SceneManager.MoveGameObjectToScene(ballObject, scene);
            ball = ballObject.AddComponent<AirHockeyBall>();
            ball.GetComponent<CircleCollider2D>().radius = .22f;
            ball.Body.interpolation = RigidbodyInterpolation2D.None;
            var gameObject = new GameObject("Cooperative game");
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            game = gameObject.AddComponent<CooperativeCoinGame>();
            game.Configure(ball, camera);
            Assert.That(game.GardenPresentation, Is.Not.Null);
            Assert.That(game.GardenPresentation.gameObject.scene, Is.EqualTo(scene));
            yield return Frames(2);
        }

        [UnityTest]
        public IEnumerator OneRealPickupTransfersOnceThenLightsOnlyItsBud()
        {
            yield return WaitForServe();
            yield return Collect(0);
            var garden = game.GardenPresentation;
            Assert.That(garden.TransferCount, Is.EqualTo(1));
            Assert.That(garden.AwakenedBudCount, Is.Zero);
            Assert.That(garden.IsAwake(0), Is.False);

            // Twenty-four actual game frames are 0.4 seconds: still in flight.
            yield return Frames(24);
            Assert.That(garden.TransferCount, Is.EqualTo(1),
                "The light must travel for 0.45 seconds before waking the bud.");
            Assert.That(garden.AwakenedBudCount, Is.Zero);
            yield return Frames(8);
            Assert.That(garden.TransferCount, Is.Zero);
            Assert.That(garden.AwakenedBudCount, Is.EqualTo(1));
            Assert.That(garden.IsAwake(0), Is.True);

            // The ball remains on the same collected star through more real frames.
            yield return Frames(35);
            Assert.That(game.CollectedCount, Is.EqualTo(1));
            Assert.That(game.TeamScore, Is.EqualTo(1));
            Assert.That(garden.TransferCount, Is.Zero,
                "A collected star must not start another transfer each frame.");
            Assert.That(garden.AwakenedBudCount, Is.EqualTo(1));
            Assert.That(garden.BloomProgress, Is.Zero);
            AssertAllFlowers(0, "Individual pickups light buds; flowers open after all nine stars.");
        }

        [UnityTest]
        public IEnumerator ARealMissPreservesAwakeBudsAndFinishesAnInFlightTransferDuringServe()
        {
            yield return WaitForServe();
            yield return Collect(0);
            yield return Frames(32);
            Assert.That(game.GardenPresentation.AwakenedBudCount, Is.EqualTo(1));
            yield return Collect(2);
            Assert.That(game.GardenPresentation.TransferCount, Is.EqualTo(1));

            MoveBall(new Vector2(-game.HalfWidth * .5f, game.ArenaFloor - 1));
            yield return Until(() => game.Misses == 1, 2, "The ball below the floor must trigger a real miss.");
            Assert.That(game.WaitingToServe, Is.True);
            Assert.That(game.CollectedCount, Is.EqualTo(2));
            Assert.That(game.GardenPresentation.AwakenedBudCount, Is.EqualTo(1));
            Assert.That(game.GardenPresentation.TransferCount, Is.EqualTo(1),
                "BeginServe must preserve the transfer started by the last pickup.");

            yield return Frames(32);
            Assert.That(game.WaitingToServe, Is.True, "The transfer must finish during the serve countdown.");
            Assert.That(game.GardenPresentation.AwakenedBudCount, Is.EqualTo(2));
            Assert.That(game.GardenPresentation.TransferCount, Is.Zero);
            Assert.That(game.GardenPresentation.IsAwake(0), Is.True);
            Assert.That(game.GardenPresentation.IsAwake(2), Is.True);
            AssertAllFlowers(0, "Missing does not complete the round or open the flowers.");
        }

        [UnityTest]
        public IEnumerator NineRealPickupsBloomWithinClearThenNextRoundResetsTheGarden()
        {
            yield return WaitForServe();
            yield return CollectAll();
            Assert.That(game.RoundComplete, Is.True);
            Assert.That(game.TeamScore, Is.EqualTo(CooperativeCoinGame.CoinsPerRound));
            Assert.That(game.Round, Is.EqualTo(1));

            // 97 frames permit the 1.6-second bloom boundary without reaching
            // the existing 2-second clear timeout, even at either coroutine phase.
            yield return Frames(97);
            Assert.That(game.Round, Is.EqualTo(1), "Full bloom must be visible before the round changes.");
            Assert.That(game.GardenPresentation.AwakenedBudCount, Is.EqualTo(9));
            Assert.That(game.GardenPresentation.TransferCount, Is.Zero);
            Assert.That(game.GardenPresentation.BloomProgress, Is.EqualTo(1).Within(.001f));
            AssertAllFlowers(1, "Every flower must finish opening by 1.6 seconds of the clear display.");

            yield return Until(() => game.Round == 2, 3, "The clear display must advance to round two.");
            Assert.That(game.WaitingToServe, Is.True);
            Assert.That(game.CollectedCount, Is.Zero);
            Assert.That(game.TeamScore, Is.EqualTo(9));
            AssertGardenReset();
        }

        [UnityTest]
        public IEnumerator RestartClearsBothAwakeAndTravellingLightsImmediately()
        {
            yield return WaitForServe();
            yield return Collect(0);
            yield return Frames(32);
            yield return Collect(2);
            Assert.That(game.GardenPresentation.AwakenedBudCount, Is.EqualTo(1));
            Assert.That(game.GardenPresentation.TransferCount, Is.EqualTo(1));

            game.RestartGame();
            Assert.That(game.Round, Is.EqualTo(1));
            Assert.That(game.TeamScore, Is.Zero);
            Assert.That(game.CollectedCount, Is.Zero);
            AssertGardenReset();
            yield return Frames(32);
            AssertGardenReset();
        }

        [UnityTest]
        public IEnumerator EverySuspensionPathFreezesTransfersAndTheExistingPickupFade()
        {
            foreach (Suspension blocker in Enum.GetValues(typeof(Suspension)))
            {
                game.RestartGame();
                yield return WaitForServe();
                yield return Collect(0);
                SetSuspended(blocker, true);
                // Observe a complete blocked frame before taking the snapshot;
                // this avoids relying on whether the test resumed before Update.
                yield return Frames(2);
                var coin = Coin(0);
                var scale = coin.transform.localScale;
                float alpha = coin.color.a;
                Assert.That(alpha, Is.GreaterThan(0), blocker + ": pickup fade starts visible.");
                Assert.That(game.GardenPresentation.TransferCount, Is.EqualTo(1));
                Assert.That(game.GardenPresentation.AwakenedBudCount, Is.Zero);

                // Longer than both the 0.35-second fade and 0.45-second transfer.
                yield return Frames(35);
                Assert.That(game.GardenPresentation.TransferCount, Is.EqualTo(1), blocker + ": transfer clock froze.");
                Assert.That(game.GardenPresentation.AwakenedBudCount, Is.Zero, blocker + ": bud stayed dormant.");
                Assert.That(coin.color.a, Is.EqualTo(alpha).Within(.0001f), blocker + ": fade alpha froze.");
                Assert.That(Vector3.Distance(coin.transform.localScale, scale), Is.LessThan(.0001f),
                    blocker + ": pickup expansion froze.");
                Assert.That(coin.enabled, Is.True, blocker + ": collected star did not vanish while suspended.");

                SetSuspended(blocker, false);
                yield return Frames(35);
                Assert.That(game.GardenPresentation.TransferCount, Is.Zero, blocker + ": transfer resumed.");
                Assert.That(game.GardenPresentation.AwakenedBudCount, Is.EqualTo(1));
                Assert.That(coin.enabled, Is.False, blocker + ": pickup fade finished after resuming.");
                AssertAllFlowers(0, blocker + ": resume does not open a flower before round completion.");
            }
        }

        [UnityTest]
        public IEnumerator EverySuspensionPathFreezesFinalBloomAndPreservesTheClearCountdown()
        {
            yield return WaitForServe();
            yield return CollectAll();
            yield return Frames(6);
            Assert.That(game.GardenPresentation.BloomProgress, Is.GreaterThan(0).And.LessThan(1));

            foreach (Suspension blocker in Enum.GetValues(typeof(Suspension)))
            {
                SetSuspended(blocker, true);
                yield return Frames(2);
                float progress = game.GardenPresentation.BloomProgress;
                var flowers = new float[RallyGardenWorld.BudCount];
                for (int i = 0; i < flowers.Length; i++) flowers[i] = game.GardenPresentation.FlowerBloom(i);
                // More than a complete clear timeout passes while each blocker is active.
                yield return Frames(125);
                Assert.That(game.Round, Is.EqualTo(1), blocker + ": clear countdown must stop.");
                Assert.That(game.RoundComplete, Is.True);
                Assert.That(game.GardenPresentation.BloomProgress, Is.EqualTo(progress).Within(.0001f),
                    blocker + ": shared bloom progress froze.");
                for (int i = 0; i < flowers.Length; i++)
                    Assert.That(game.GardenPresentation.FlowerBloom(i), Is.EqualTo(flowers[i]).Within(.0001f),
                        blocker + ": flower " + i + " froze.");

                SetSuspended(blocker, false);
                yield return Frames(3);
                Assert.That(game.GardenPresentation.BloomProgress, Is.GreaterThan(progress),
                    blocker + ": bloom resumed from its previous progress.");
            }
            yield return Until(() => game.GardenPresentation.BloomProgress >= .999f, 3,
                "The same clear animation must finish after all blockers are removed.");
            Assert.That(game.Round, Is.EqualTo(1));
            AssertAllFlowers(1, "The resumed clear animation finishes every flower.");
        }

        IEnumerator CollectAll()
        {
            for (int i = 0; i < CooperativeCoinGame.CoinsPerRound; i++) yield return Collect(i);
        }

        IEnumerator Collect(int index)
        {
            int before = game.CollectedCount;
            // Reset the swept path using the public light switch. Only the target
            // star is touched, so the test cannot accidentally collect its neighbors.
            game.BallLightEnabled = false;
            MoveBall(Coin(index).transform.position);
            game.BallLightEnabled = true;
            yield return Until(() => game.CollectedCount > before, 2, "Real ball contact must collect star " + index + ".");
            Assert.That(game.CollectedCount, Is.EqualTo(before + 1), "Each contact collects exactly one star.");
        }

        IEnumerator WaitForServe() => Until(() => !game.WaitingToServe, 4, "The serve countdown must finish.");

        void SetSuspended(Suspension blocker, bool active)
        {
            switch (blocker)
            {
                case Suspension.Pause: game.SetPaused(active); break;
                case Suspension.InputWait: game.SetInputReady(!active); break;
                case Suspension.Settings:
                    Assert.That(SettingsOpenField, Is.Not.Null);
                    SettingsOpenField.SetValue(game, active);
                    // Match the UI's simulation refresh through an existing public setter.
                    game.SetInputReady(game.InputReady);
                    break;
                case Suspension.ExternalOverlay:
                    game.ExternalOverlayOpen = active;
                    game.SetInputReady(game.InputReady);
                    break;
                case Suspension.ComponentDisabled: game.enabled = !active; break;
            }
        }

        void AssertGardenReset()
        {
            Assert.That(game.GardenPresentation.AwakenedBudCount, Is.Zero);
            Assert.That(game.GardenPresentation.TransferCount, Is.Zero);
            Assert.That(game.GardenPresentation.BloomProgress, Is.Zero);
            AssertAllFlowers(0, "Reset returns every flower to a closed bud.");
        }

        void AssertAllFlowers(float expected, string message)
        {
            for (int i = 0; i < RallyGardenWorld.BudCount; i++)
                Assert.That(game.GardenPresentation.FlowerBloom(i), Is.EqualTo(expected).Within(.001f),
                    message + " Index: " + i);
        }

        SpriteRenderer Coin(int index) => game.transform.Find("Cooperative court/Shared coin " + (index + 1))
            .GetComponent<SpriteRenderer>();

        void MoveBall(Vector2 position)
        {
            ball.Body.position = position;
            ball.transform.position = position;
            ball.Body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                int frame = Time.frameCount;
                do { yield return null; } while (Time.frameCount == frame);
            }
        }

        static IEnumerator Until(Func<bool> condition, float timeout, string message)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + timeout;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline) yield return Frames(1);
            Assert.That(condition(), Is.True, message);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (captureTimeChanged) Time.captureDeltaTime = previousCaptureDeltaTime;
            captureTimeChanged = false;
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            game = null;
            ball = null;
        }
    }
}
