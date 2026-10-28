using System;
using System.Collections;
using System.Collections.Generic;
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
    /// Exercises spell presentation through the real owner Update/LateUpdate loop.
    /// Local physics lets the fixture move the ball without changing gameplay time.
    /// </summary>
    public sealed class SpellBallPresentationPlayTests
    {
        const float FrameStep = 1f / 60f;
        const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Scene scene;
        CooperativeCoinGame game;
        AirHockeyBall ball;
        ElasticBarrier barrier;
        Color ordinaryBallColor;
        float previousCaptureDeltaTime, previousTimeScale;
        bool clockChanged;

        [UnitySetUp]
        public IEnumerator CreateCourt()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            previousCaptureDeltaTime = Time.captureDeltaTime;
            previousTimeScale = Time.timeScale;
            Time.captureDeltaTime = FrameStep;
            Time.timeScale = 1;
            clockChanged = true;

            scene = SceneManager.CreateScene("Spell ball presentation court",
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            var camera = Create("Camera").AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.aspect = 16f / 9f;
            ball = Create("Ball").AddComponent<AirHockeyBall>();
            ball.GetComponent<CircleCollider2D>().radius = .22f;
            ball.Body.interpolation = RigidbodyInterpolation2D.None;
            game = Create("Game").AddComponent<CooperativeCoinGame>();
            game.Configure(ball, camera);
            game.SetMode(RallyMode.Versus);
            ball.Body.gravityScale = 0;
            ball.Body.linearDamping = 0;

            var hand = Create("Left barrier");
            hand.transform.position = new Vector2(-3, -1.8f);
            hand.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var edge = hand.AddComponent<EdgeCollider2D>();
            var line = hand.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            barrier = hand.AddComponent<ElasticBarrier>();
            barrier.Initialize(edge, line);
            barrier.SetSpan(3, .3f, .16f, true);
            barrier.PlayerSide = -1;

            yield return Until(() => game.CanAdvanceBall, 4, "The initial serve must start.");
            MoveBall(new Vector2(-6, 3));
            yield return Frames(2);
            Assert.That(game.SpellBallEffects, Is.Not.Null);
            Assert.That(game.SpellBallEffects.gameObject.scene, Is.EqualTo(scene));
            ordinaryBallColor = BallView.color;
            HitAndRelease();
            MoveBall(new Vector2(-6, 3));
            yield return Frames(2);
        }

        [UnityTest]
        public IEnumerator FireAndClockTintTheCoreAndUseAFixedTrailPoolUntilConsumed()
        {
            int rendererCount = EffectsRenderers().Length;
            Assert.That(game.TryActivateSpell(RallySpell.Fireball), Is.True);
            yield return Travel(12);
            Color fire = BallView.color;
            Assert.That(fire.r, Is.GreaterThan(fire.b + .1f), "Fire must read as a warm ball core.");
            Assert.That(game.SpellBallEffects.ActiveParticleCount, Is.InRange(1, 36));
            yield return Travel(48);
            Assert.That(EffectsRenderers().Length, Is.EqualTo(rendererCount),
                "A running tail must recycle its renderer pool instead of adding objects every frame.");
            Assert.That(game.SpellBallEffects.ActiveParticleCount, Is.InRange(1, 36));

            Assert.That(game.TryActivateSpell(RallySpell.Timer), Is.True);
            yield return Travel(12);
            Color clock = BallView.color;
            Assert.That(clock.b, Is.GreaterThan(clock.r + .1f), "The clock must read as a cool ball core.");
            Assert.That(ColorDistance(fire, clock), Is.GreaterThan(.25f));
            Assert.That(game.SpellBallEffects.ActiveParticleCount, Is.InRange(1, 3),
                "Replacing Fire must remove its wisps and leave only the short clock afterimages.");
            Assert.That(EffectsRenderers().Length, Is.EqualTo(rendererCount));

            Vector2 incomingReturn = new Vector2(3, 4);
            Vector2 modifiedReturn = game.ApplyPendingSpellToRebound(incomingReturn);
            Assert.That(modifiedReturn.magnitude,
                Is.EqualTo(incomingReturn.magnitude * RallyVersusState.TimerMultiplier).Within(.001f));
            yield return Frames(2);
            Assert.That(game.ActiveSpell, Is.EqualTo(RallySpell.None));
            Assert.That(game.SpellBallEffects.ActiveParticleCount, Is.Zero,
                "A consumed speed spell must not leave an obsolete tail behind.");
            AssertOrdinaryCore();
        }

        [UnityTest]
        public IEnumerator PauseTrackingLossAndSettingsFreezeBothTrailTimeAndItsPose()
        {
            Assert.That(game.TryActivateSpell(RallySpell.Fireball), Is.True);
            yield return Travel(12);
            yield return AssertSuspension(() => game.SetPaused(true), () => game.SetPaused(false), "pause");
            yield return AssertSuspension(() => game.SetInputReady(false), () => game.SetInputReady(true), "tracking loss");
            yield return AssertSuspension(() => SetSettings(true), () => SetSettings(false), "settings");
        }

        [UnityTest]
        public IEnumerator LightReplacesTheOldTrailAndBurstsOnlyAtTheDisappearancePosition()
        {
            Assert.That(game.TryActivateSpell(RallySpell.Fireball), Is.True);
            yield return Travel(12);
            Assert.That(game.SpellBallEffects.ActiveParticleCount, Is.GreaterThan(0));
            Vector2 vanishPosition = ball.Body.position;
            ball.Body.linearVelocity = Vector2.zero;
            Assert.That(game.TryActivateSpell(RallySpell.Light), Is.True);
            yield return Frames(2);
            Assert.That(game.LightPhase, Is.EqualTo(RallyLightPhase.Warning));
            Assert.That(BallView.enabled, Is.True);
            Assert.That(game.SpellBallEffects.ActiveParticleCount, Is.Zero,
                "Light must clear the old flame trail and wait until the actual disappearance to burst.");

            yield return Until(() => game.LightPhase == RallyLightPhase.Hidden, 2,
                "The visible warning must finish through the real game loop.");
            yield return Frames(1);
            Assert.That(BallView.enabled, Is.False);
            int burstCount = game.SpellBallEffects.ActiveParticleCount;
            Assert.That(burstCount, Is.InRange(1, 9));
            Vector2 hiddenPosition = vanishPosition + Vector2.right * 3;
            MoveBall(hiddenPosition);
            yield return Frames(5);
            Assert.That(game.LightPhase, Is.EqualTo(RallyLightPhase.Hidden));
            Assert.That(BallView.enabled, Is.False);
            Assert.That(game.SpellBallEffects.ActiveParticleCount, Is.LessThanOrEqualTo(burstCount),
                "An invisible ball must not keep emitting particles that disclose its path.");
            int visibleCount = 0;
            foreach (var renderer in EffectsRenderers())
            {
                if (!Visible(renderer)) continue;
                visibleCount++;
                Assert.That(Vector2.Distance(renderer.transform.position, vanishPosition), Is.LessThan(1.5f),
                    "The disappearance burst stays near the point where the ball vanished.");
                Assert.That(Vector2.Distance(renderer.transform.position, hiddenPosition), Is.GreaterThan(1.5f),
                    "No attached glow or ghost may follow the hidden ball.");
            }
            Assert.That(visibleCount, Is.GreaterThan(0), "The brief burst must actually be presented before it fades.");

            yield return Until(() => game.ActiveSpell == RallySpell.None, 2, "Light must end normally.");
            yield return Frames(30);
            Assert.That(game.SpellBallEffects.ActiveParticleCount, Is.Zero);
            Assert.That(BallView.enabled, Is.True);
            AssertOrdinaryCore();
        }

        [UnityTest]
        public IEnumerator RestartAndModeChangeClearSpellArtworkAndRestoreTheOrdinaryCore()
        {
            Assert.That(game.TryActivateSpell(RallySpell.Fireball), Is.True);
            yield return Travel(12);
            game.RestartGame();
            yield return Frames(2);
            AssertPresentationCleared();
            yield return Until(() => game.CanAdvanceBall, 4, "Restart must serve again.");
            HitAndRelease();
            MoveBall(new Vector2(-6, 3));
            Assert.That(game.TryActivateSpell(RallySpell.Timer), Is.True);
            yield return Travel(12);
            Assert.That(game.SpellBallEffects.ActiveParticleCount, Is.GreaterThan(0));

            game.SetMode(RallyMode.StarCollection);
            yield return Frames(2);
            AssertPresentationCleared();
            Assert.That(game.IsVersus, Is.False);
        }

        IEnumerator AssertSuspension(Action suspend, Action resume, string reason)
        {
            suspend();
            yield return Frames(2);
            float age = game.SpellBallEffects.EffectAge;
            int count = game.SpellBallEffects.ActiveParticleCount;
            var poses = new List<RendererPose>();
            foreach (var renderer in EffectsRenderers()) poses.Add(new RendererPose(renderer));
            yield return Frames(8);
            Assert.That(game.SpellBallEffects.EffectAge, Is.EqualTo(age).Within(.0001f), reason);
            Assert.That(game.SpellBallEffects.ActiveParticleCount, Is.EqualTo(count), reason);
            foreach (var pose in poses) pose.AssertUnchanged(reason);
            resume();
            yield return Travel(6);
            Assert.That(game.SpellBallEffects.EffectAge, Is.GreaterThan(age + .02f), reason + " must resume the effect clock.");
        }

        IEnumerator Travel(int frameCount)
        {
            for (int i = 0; i < frameCount; i++)
            {
                MoveBall(ball.Body.position + Vector2.right * .035f);
                ball.Body.linearVelocity = Vector2.right * 2.1f;
                yield return Frames(1);
            }
        }

        void AssertPresentationCleared()
        {
            Assert.That(game.ActiveSpell, Is.EqualTo(RallySpell.None));
            Assert.That(game.SpellBallEffects.ActiveParticleCount, Is.Zero);
            foreach (var renderer in EffectsRenderers()) Assert.That(Visible(renderer), Is.False, renderer.name);
            AssertOrdinaryCore();
        }

        void AssertOrdinaryCore()
        {
            Assert.That(ColorDistance(BallView.color, ordinaryBallColor), Is.LessThan(.001f),
                "A cleared spell must restore the ordinary ball tint (serve alpha may still pulse).");
        }

        void SetSettings(bool open)
        {
            var field = typeof(CooperativeCoinGame).GetField("settingsOpen", InstanceMembers);
            Assert.That(field, Is.Not.Null);
            field.SetValue(game, open);
            game.SetInputReady(game.InputReady);
        }

        SpriteRenderer BallView => ball.GetComponent<SpriteRenderer>();
        SpriteRenderer[] EffectsRenderers() => game.SpellBallEffects.GetComponentsInChildren<SpriteRenderer>(true);
        static bool Visible(SpriteRenderer renderer) => renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.color.a > .001f;
        static float ColorDistance(Color a, Color b) => Vector3.Distance(new Vector3(a.r, a.g, a.b), new Vector3(b.r, b.g, b.b));

        void HitAndRelease()
        {
            barrier.ResetSpring();
            MoveBall((Vector2)barrier.transform.position + Vector2.up);
            ball.Body.linearVelocity = Vector2.down * 8;
            for (int i = 0; i < 20 && !ball.IsInBarrierImpact; i++) scene.GetPhysicsScene2D().Simulate(.02f);
            Assert.That(ball.IsInBarrierImpact, Is.True, "A real barrier hit must establish spell ownership.");
            barrier.Step(.25f);
            Assert.That(ball.IsInBarrierImpact, Is.False);
            Assert.That(game.VersusState.LastHitter, Is.EqualTo(-1));
        }

        void MoveBall(Vector2 position)
        {
            ball.Body.position = position;
            ball.transform.position = position;
            ball.Body.linearVelocity = Vector2.zero;
            Physics2D.SyncTransforms();
        }

        GameObject Create(string name)
        {
            var result = new GameObject(name);
            SceneManager.MoveGameObjectToScene(result, scene);
            return result;
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

        readonly struct RendererPose
        {
            readonly SpriteRenderer renderer;
            readonly Vector3 position, scale;
            readonly Quaternion rotation;
            readonly Color color;
            readonly bool enabled, active;

            public RendererPose(SpriteRenderer renderer)
            {
                this.renderer = renderer;
                position = renderer.transform.position;
                scale = renderer.transform.localScale;
                rotation = renderer.transform.rotation;
                color = renderer.color;
                enabled = renderer.enabled;
                active = renderer.gameObject.activeInHierarchy;
            }

            public void AssertUnchanged(string reason)
            {
                Assert.That(renderer.transform.position, Is.EqualTo(position), reason + ": " + renderer.name + " position");
                Assert.That(renderer.transform.localScale, Is.EqualTo(scale), reason + ": " + renderer.name + " scale");
                Assert.That(renderer.transform.rotation, Is.EqualTo(rotation), reason + ": " + renderer.name + " rotation");
                Assert.That(renderer.color, Is.EqualTo(color), reason + ": " + renderer.name + " color");
                Assert.That(renderer.enabled, Is.EqualTo(enabled), reason + ": " + renderer.name + " visibility");
                Assert.That(renderer.gameObject.activeInHierarchy, Is.EqualTo(active), reason + ": " + renderer.name + " activation");
            }
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (clockChanged)
            {
                Time.captureDeltaTime = previousCaptureDeltaTime;
                Time.timeScale = previousTimeScale;
            }
            clockChanged = false;
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            game = null;
            ball = null;
            barrier = null;
        }
    }
}
