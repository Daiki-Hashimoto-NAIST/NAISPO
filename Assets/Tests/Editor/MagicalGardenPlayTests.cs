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
    /// <summary>Actual game frames exercise hanging targets, bloom, and round replacement.</summary>
    public sealed class MagicalGardenPlayTests
    {
        const float FrameStep = 1f / 60f;
        static readonly FieldInfo SettingsOpenField = typeof(CooperativeCoinGame)
            .GetField("settingsOpen", BindingFlags.Instance | BindingFlags.NonPublic);
        CooperativeCoinGame game;
        AirHockeyBall ball;
        float previousCaptureDeltaTime;
        bool captureTimeChanged;
        RallyGardenWorld Garden => game.GardenPresentation;

        enum Suspension { Pause, InputWait, Settings, ExternalOverlay, ComponentDisabled }

        [UnitySetUp]
        public IEnumerator CreateCourt()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            previousCaptureDeltaTime = Time.captureDeltaTime;
            Time.captureDeltaTime = FrameStep;
            captureTimeChanged = true;
            var scene = SceneManager.CreateScene("Hanging flower play test",
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
            Assert.That(Garden, Is.Not.Null);
            Assert.That(Garden.gameObject.scene, Is.EqualTo(scene));
            yield return Frames(2);
        }

        [UnityTest]
        public IEnumerator ContactOpensOnlyItsFlowerAndItStaysLitAfterTheBallLeaves()
        {
            yield return WaitForServe();
            yield return Collect(0);
            Assert.That(Garden.AwakenedBudCount, Is.EqualTo(1));
            Assert.That(Garden.IsAwake(0), Is.True);
            Assert.That(Garden.FlowerBloom(0), Is.LessThan(1), "Opening must animate instead of switching artwork instantly.");
            Vector2 contact = game.CollectionTargetPosition(0);
            MoveBall(new Vector2(0, -2));
            yield return Frames(35);
            Assert.That(game.CollectedCount, Is.EqualTo(1));
            Assert.That(game.TeamScore, Is.EqualTo(1));
            Assert.That(Garden.FlowerBloom(0), Is.EqualTo(1).Within(.001f));
            Assert.That(IsVisible(Garden.FlowerRenderer(0)), Is.True, "A flower records success even outside the ball light.");
            Assert.That(Vector2.Distance(Garden.FlowerRenderer(0).transform.position, contact), Is.LessThan(.001f),
                "The flower must open on the touched bud, without transferring to a separate decoration.");
            Assert.That(game.CoinVisibility(4), Is.Zero);
            Assert.That(IsVisible(game.CollectionTargetRenderer(4)), Is.False,
                "A completed flower must not reveal other buds.");
            Assert.That(IsVisible(game.CollectionHaloRenderer(4)), Is.False);
            Assert.That(TargetRenderer(4, "Flower radiance").enabled, Is.False,
                "Another flower's radiance must not reveal an unopened target.");
            Assert.That(TargetRenderer(4, "Flower heart").enabled, Is.False,
                "An unopened target must not show an emissive center outside the ball light.");
            for (int mote = 0; mote < 3; mote++)
                Assert.That(TargetMote(4, mote).enabled, Is.False,
                    "An unopened target must not emit motes outside the ball light.");
            Assert.That(BackgroundMaterial.GetInteger("_FlowerLightCount"), Is.EqualTo(RallyGardenWorld.BudCount));
            var lights = BackgroundMaterial.GetVectorArray("_FlowerLights");
            Assert.That(lights.Length, Is.EqualTo(RallyGardenWorld.BudCount));
            Assert.That(lights[0].w, Is.GreaterThan(0), "The opened flower must illuminate the background.");
            Assert.That(Vector2.Distance(new Vector2(lights[0].x, lights[0].y), contact), Is.LessThan(.001f),
                "Flower background light must stay centered on the actual bloom.");
            for (int i = 1; i < RallyGardenWorld.BudCount; i++)
            {
                Assert.That(Garden.FlowerBloom(i), Is.Zero);
                Assert.That(lights[i].w, Is.Zero, "An unopened bud must not illuminate the background.");
            }
        }

        [UnityTest]
        public IEnumerator MissKeepsFlowersAndTargetLocationsWithoutStartingAnotherDescent()
        {
            yield return WaitForServe();
            yield return Collect(0);
            yield return Frames(32);
            yield return Collect(2);
            var positions = TargetPositions();
            MoveBall(new Vector2(-game.HalfWidth * .5f, game.ArenaFloor - 1));
            yield return Until(() => game.Misses == 1, 2, "Dropping below the floor must trigger a real miss.");
            Assert.That(game.WaitingToServe, Is.True);
            Assert.That(game.CollectedCount, Is.EqualTo(2));
            Assert.That(Garden.ArrivalProgress, Is.EqualTo(1));
            Assert.That(Garden.TargetsReady, Is.True);
            Assert.That(Garden.AwakenedBudCount, Is.EqualTo(2));
            yield return Frames(32);
            Assert.That(game.WaitingToServe, Is.True);
            Assert.That(Garden.FlowerBloom(0), Is.EqualTo(1));
            Assert.That(Garden.FlowerBloom(2), Is.EqualTo(1), "An opening flower may finish during the next serve.");
            Assert.That(IsVisible(Garden.FlowerRenderer(0)), Is.True);
            Assert.That(IsVisible(Garden.FlowerRenderer(2)), Is.True);
            AssertTargetPositions(positions, "A miss must retain the same hanging targets.");
            Assert.That(Garden.RetractionProgress, Is.Zero);
        }

        [UnityTest]
        public IEnumerator AllFlowersRiseBeforeTheNextRoundBringsDownFreshBuds()
        {
            yield return WaitForServe();
            yield return CollectAll();
            Assert.That(game.RoundComplete, Is.True);
            Assert.That(game.TeamScore, Is.EqualTo(CooperativeCoinGame.CoinsPerRound));
            Assert.That(game.CanAdvanceBall, Is.False);
            var positions = TargetPositions();
            yield return Frames(35);
            Assert.That(game.Round, Is.EqualTo(1));
            AssertAllFlowers(1, "The final contacted flower opens before retraction.");
            Assert.That(Garden.RetractionProgress, Is.Zero, "Give the complete arrangement a short visible hold.");
            yield return Until(() => Garden.RetractionProgress > .6f, 3, "The completed vines must visibly retract.");
            Assert.That(game.Round, Is.EqualTo(1));
            for (int i = 0; i < positions.Length; i++)
                Assert.That(Garden.TargetWorldPosition(i).y, Is.GreaterThan(positions[i].y + .2f));
            Assert.That(game.CanAdvanceBall, Is.False);
            yield return Until(() => game.Round == 2, 3, "Retraction must advance to the next round.");
            Assert.That(game.WaitingToServe, Is.True);
            Assert.That(game.CollectedCount, Is.Zero);
            Assert.That(game.TeamScore, Is.EqualTo(9));
            AssertGardenReset();
            Assert.That(Garden.TargetsReady, Is.False);
            Assert.That(Garden.ArrivalProgress, Is.LessThan(.1f));
            yield return WaitForServe();
            Assert.That(Garden.TargetsReady, Is.True);
            Assert.That(Garden.ArrivalProgress, Is.EqualTo(1));
            Assert.That(game.TeamScore, Is.EqualTo(9), "Descent cannot collect new buds automatically.");
        }

        [UnityTest]
        public IEnumerator RestartAndModeSwitchClearOpenFlowersAndBeginFreshDescent()
        {
            yield return WaitForServe();
            yield return Collect(0);
            yield return Frames(32);
            yield return Collect(2);
            game.RestartGame();
            Assert.That(game.Round, Is.EqualTo(1));
            Assert.That(game.TeamScore, Is.Zero);
            Assert.That(game.CollectedCount, Is.Zero);
            AssertGardenReset();
            Assert.That(Garden.ArrivalProgress, Is.Zero);
            Assert.That(Garden.TargetsReady, Is.False);
            yield return WaitForServe();
            yield return Collect(4);
            game.SetMode(RallyMode.Versus);
            yield return Frames(2);
            foreach (var renderer in Garden.GetComponentsInChildren<Renderer>(true))
                Assert.That(renderer.enabled && renderer.gameObject.activeInHierarchy, Is.False,
                    "Vines and blooms cannot remain in the duel.");
            game.SetMode(RallyMode.StarCollection);
            AssertGardenReset();
            Assert.That(Garden.TargetsReady, Is.False);
            yield return WaitForServe();
            Assert.That(game.CollectedCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator DescendingTargetsCannotBeCollectedAndSettledArtworkMatchesCollisionCenters()
        {
            game.RestartGame();
            Assert.That(Garden.GetComponentsInChildren<Collider2D>(true), Is.Empty,
                "Vines are decoration and must never bounce or obstruct the ball.");
            for (int frame = 0; frame < 20; frame++)
            {
                MoveBall(Garden.TargetWorldPosition(0));
                yield return Frames(1);
                Assert.That(game.CanAdvanceBall, Is.False);
                Assert.That(game.CollectedCount, Is.Zero, "Arriving targets must not be hittable.");
            }
            yield return WaitForServe();
            for (int i = 0; i < RallyGardenWorld.BudCount; i++)
            {
                Vector2 contact = game.CollectionTargetPosition(i);
                Assert.That(Vector2.Distance(contact, Garden.TargetWorldPosition(i)), Is.LessThan(.001f));
                Assert.That(Vector2.Distance(contact, game.CollectionTargetRenderer(i).bounds.center), Is.LessThan(.001f),
                    "The displayed bud and the gameplay contact point must coincide.");
            }
            yield return Collect(0);
        }

        [UnityTest]
        public IEnumerator EverySuspensionPathFreezesOpeningFlowersAndResumesThem()
        {
            foreach (Suspension blocker in Enum.GetValues(typeof(Suspension)))
            {
                game.RestartGame();
                yield return WaitForServe();
                yield return Collect(0);
                yield return Frames(16);
                SetSuspended(blocker, true);
                yield return Frames(2);
                float bloom = Garden.FlowerBloom(0);
                var flower = Garden.FlowerRenderer(0);
                var layers = new[] { flower, TargetRenderer(0, "Flower radiance"), TargetRenderer(0, "Flower heart"),
                    TargetMote(0, 0), TargetMote(0, 1), TargetMote(0, 2) };
                var localPositions = new Vector3[layers.Length];
                var scales = new Vector3[layers.Length];
                var colors = new Color[layers.Length];
                var rotations = new Quaternion[layers.Length];
                for (int i = 0; i < layers.Length; i++)
                {
                    localPositions[i] = layers[i].transform.localPosition;
                    scales[i] = layers[i].transform.localScale;
                    colors[i] = layers[i].color;
                    rotations[i] = layers[i].transform.localRotation;
                }
                int lightCount = BackgroundMaterial.GetInteger("_FlowerLightCount");
                var lights = BackgroundMaterial.GetVectorArray("_FlowerLights");
                var lightColors = BackgroundMaterial.GetVectorArray("_FlowerLightColors");
                Assert.That(bloom, Is.LessThan(1));
                Assert.That(TargetMote(0, 0).enabled, Is.True, "Pause while a flower mote is already drifting.");
                yield return Frames(35);
                Assert.That(Garden.FlowerBloom(0), Is.EqualTo(bloom).Within(.0001f), blocker + ": opening froze.");
                for (int i = 0; i < layers.Length; i++)
                {
                    string layer = blocker + ": " + layers[i].name;
                    Assert.That(layers[i].transform.localPosition, Is.EqualTo(localPositions[i]), layer + " position froze.");
                    Assert.That(layers[i].transform.localScale, Is.EqualTo(scales[i]), layer + " size froze.");
                    Assert.That(layers[i].color, Is.EqualTo(colors[i]), layer + " color froze.");
                    Assert.That(layers[i].transform.localRotation, Is.EqualTo(rotations[i]), layer + " rotation froze.");
                }
                Assert.That(BackgroundMaterial.GetInteger("_FlowerLightCount"), Is.EqualTo(lightCount),
                    blocker + ": background light count froze.");
                CollectionAssert.AreEqual(lights, BackgroundMaterial.GetVectorArray("_FlowerLights"),
                    blocker + ": background light position and strength froze.");
                CollectionAssert.AreEqual(lightColors, BackgroundMaterial.GetVectorArray("_FlowerLightColors"),
                    blocker + ": background light color froze.");
                SetSuspended(blocker, false);
                yield return Frames(35);
                Assert.That(Garden.FlowerBloom(0), Is.EqualTo(1).Within(.001f));
                Assert.That(IsVisible(flower), Is.True, blocker + ": the resumed flower stays visible.");
            }
        }

        [UnityTest]
        public IEnumerator EverySuspensionPathFreezesArrivalAndTheServeCountdown()
        {
            foreach (Suspension blocker in Enum.GetValues(typeof(Suspension)))
            {
                game.RestartGame();
                yield return Frames(12);
                SetSuspended(blocker, true);
                yield return Frames(2);
                float arrival = Garden.ArrivalProgress;
                var positions = TargetPositions();
                Assert.That(arrival, Is.GreaterThan(0).And.LessThan(1));
                yield return Frames(80);
                Assert.That(game.WaitingToServe, Is.True, blocker + ": serve froze.");
                Assert.That(Garden.ArrivalProgress, Is.EqualTo(arrival).Within(.0001f), blocker + ": descent froze.");
                AssertTargetPositions(positions, blocker + ": targets stayed still.");
                SetSuspended(blocker, false);
                yield return Frames(3);
                Assert.That(Garden.ArrivalProgress, Is.GreaterThan(arrival));
            }
            yield return WaitForServe();
            Assert.That(Garden.TargetsReady, Is.True);
        }

        [UnityTest]
        public IEnumerator EverySuspensionPathFreezesRetractionAndRoundReplacement()
        {
            yield return WaitForServe();
            yield return CollectAll();
            yield return Until(() => Garden.RetractionProgress > .15f, 3, "Begin the real retraction before pausing it.");
            foreach (Suspension blocker in Enum.GetValues(typeof(Suspension)))
            {
                SetSuspended(blocker, true);
                yield return Frames(2);
                float retraction = Garden.RetractionProgress;
                var positions = TargetPositions();
                yield return Frames(140);
                Assert.That(game.Round, Is.EqualTo(1), blocker + ": the completed round must not be replaced while suspended.");
                Assert.That(game.RoundComplete, Is.True);
                Assert.That(Garden.RetractionProgress, Is.EqualTo(retraction).Within(.0001f));
                AssertTargetPositions(positions, blocker + ": retracting vines stayed still.");
                SetSuspended(blocker, false);
                yield return Frames(3);
                Assert.That(Garden.RetractionProgress, Is.GreaterThan(retraction));
            }
            yield return Until(() => game.Round == 2, 3, "The same retraction must finish after all blockers are removed.");
            AssertGardenReset();
        }

        IEnumerator CollectAll()
        {
            for (int i = 0; i < CooperativeCoinGame.CoinsPerRound; i++) yield return Collect(i);
        }

        IEnumerator Collect(int index)
        {
            int before = game.CollectedCount;
            game.BallLightEnabled = false;
            MoveBall(game.CollectionTargetPosition(index));
            game.BallLightEnabled = true;
            yield return Until(() => game.CollectedCount > before, 2, "Ball contact must bloom target " + index + ".");
            Assert.That(game.CollectedCount, Is.EqualTo(before + 1), "Each contact blooms exactly one target.");
        }

        IEnumerator WaitForServe() => Until(() => game.CanAdvanceBall, 4, "Descent and the serve countdown must finish.");

        void SetSuspended(Suspension blocker, bool active)
        {
            switch (blocker)
            {
                case Suspension.Pause: game.SetPaused(active); break;
                case Suspension.InputWait: game.SetInputReady(!active); break;
                case Suspension.Settings:
                    Assert.That(SettingsOpenField, Is.Not.Null);
                    SettingsOpenField.SetValue(game, active);
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
            Assert.That(Garden.AwakenedBudCount, Is.Zero);
            Assert.That(Garden.RetractionProgress, Is.Zero);
            Assert.That(Garden.BloomProgress, Is.Zero);
            Assert.That(BackgroundMaterial.GetInteger("_FlowerLightCount"), Is.Zero,
                "Previous blooms must not leave light on the background after reset.");
            foreach (var light in BackgroundMaterial.GetVectorArray("_FlowerLights"))
                Assert.That(light.w, Is.Zero, "Reset must clear every old flower's background illumination.");
            AssertAllFlowers(0, "Reset returns every flower to a closed bud.");
            for (int i = 0; i < RallyGardenWorld.BudCount; i++)
            {
                Assert.That(IsVisible(Garden.FlowerRenderer(i)), Is.False, "No previous bloom may leak into a new round.");
                for (int mote = 0; mote < 3; mote++)
                    Assert.That(TargetMote(i, mote).enabled, Is.False,
                        "A previous flower's motes must not leak into the next round.");
            }
        }

        void AssertAllFlowers(float expected, string message)
        {
            for (int i = 0; i < RallyGardenWorld.BudCount; i++)
                Assert.That(Garden.FlowerBloom(i), Is.EqualTo(expected).Within(.001f), message + " Index: " + i);
        }

        Vector2[] TargetPositions()
        {
            var positions = new Vector2[RallyGardenWorld.BudCount];
            for (int i = 0; i < positions.Length; i++) positions[i] = Garden.TargetWorldPosition(i);
            return positions;
        }

        void AssertTargetPositions(Vector2[] positions, string message)
        {
            for (int i = 0; i < positions.Length; i++)
                Assert.That(Vector2.Distance(Garden.TargetWorldPosition(i), positions[i]), Is.LessThan(.0001f), message);
        }

        static bool IsVisible(SpriteRenderer renderer) =>
            renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.color.a > .001f;

        Material BackgroundMaterial => Garden.Background.GetComponent<SpriteRenderer>().sharedMaterial;

        SpriteRenderer TargetMote(int index, int mote) => TargetRenderer(index, "Flower mote", "-" + (mote + 1));

        SpriteRenderer TargetRenderer(int index, string prefix, string suffix = "")
        {
            string name = prefix + " " + (index + 1) + suffix;
            Transform target = Garden.FlowerRenderer(index).transform.parent.Find(name);
            Assert.That(target, Is.Not.Null, "Missing flower layer: " + name);
            var renderer = target.GetComponent<SpriteRenderer>();
            Assert.That(renderer, Is.Not.Null, "Missing flower renderer: " + name);
            return renderer;
        }

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
