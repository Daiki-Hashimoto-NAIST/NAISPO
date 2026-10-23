using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Naispo.Interaction;

namespace Naispo.Tests
{
    public sealed class RallyBattleEffectsTests
    {
        GameObject root;
        RallyBattleEffects effects;
        Camera camera;

        [SetUp]
        public void CreateEffects()
        {
            root = new GameObject("Battle feedback test");
            var cameraObject = new GameObject("Test camera");
            cameraObject.transform.SetParent(root.transform);
            camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 5;
            camera.transform.position = new Vector3(2, 1, -10);
            effects = root.AddComponent<RallyBattleEffects>();
            effects.Initialize(camera, 8.5f, -3.4f, new Vector2(2, 1));
        }

        [TearDown]
        public void DestroyEffects()
        {
            if(root != null) Object.DestroyImmediate(root);
        }

        [Test]
        public void HitStaysLocalAndExpiresWithoutAddingPhysicsOrMovingCamera()
        {
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float size = camera.orthographicSize;
            Vector2 hit = new Vector2(-2, -1);
            effects.Hit(hit, -1, 1);
            Assert.That(effects.ActiveParticleCount, Is.GreaterThan(8));
            effects.Step(.12f);
            foreach(var renderer in root.GetComponentsInChildren<SpriteRenderer>())
                if(renderer.enabled) Assert.That(Vector2.Distance(renderer.transform.position, hit), Is.LessThan(1.2f));
            Assert.That(root.GetComponentsInChildren<Collider2D>(), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Rigidbody2D>(), Is.Empty);
            Assert.That(root.GetComponentsInChildren<Light>(), Is.Empty);
            Assert.That(root.GetComponentsInChildren<AudioListener>(), Is.Empty,
                "Feedback cannot create an audio listener when the host has none.");
            Assert.That(camera.transform.position, Is.EqualTo(cameraPosition));
            Assert.That(camera.transform.rotation, Is.EqualTo(cameraRotation));
            Assert.That(camera.orthographicSize, Is.EqualTo(size));
            effects.Step(.5f);
            Assert.That(effects.ActiveParticleCount, Is.Zero);
        }

        [Test]
        public void SuspensionFreezesGeometryAndRejectsEventsUntilResumed()
        {
            effects.Goal(new Vector2(-4, -3.4f), 1, true);
            effects.Step(.13f);
            float age = effects.EffectAge;
            int count = effects.ActiveParticleCount;
            var views = root.GetComponentsInChildren<Renderer>();
            var positions = new Vector3[views.Length];
            var scales = new Vector3[views.Length];
            for(int i = 0; i < views.Length; i++)
            {
                positions[i] = views[i].transform.position;
                scales[i] = views[i].transform.localScale;
            }
            effects.SetSuspended(true);
            effects.Step(5);
            effects.Hit(Vector2.zero, -1, 1);
            effects.Pickup(Vector2.one, 1);
            effects.Goal(Vector2.zero, -1, false);
            Assert.That(effects.EffectAge, Is.EqualTo(age));
            Assert.That(effects.ActiveParticleCount, Is.EqualTo(count));
            Assert.That(effects.IsVictory, Is.True);
            for(int i = 0; i < views.Length; i++)
            {
                Assert.That(views[i].transform.position, Is.EqualTo(positions[i]));
                Assert.That(views[i].transform.localScale, Is.EqualTo(scales[i]));
            }
            effects.SetSuspended(false);
            effects.Step(.1f);
            Assert.That(effects.EffectAge, Is.EqualTo(age + .1f).Within(.0001f));
        }

        [Test]
        public void VictoryHasDelayedBurstsAndFinishesEvenAcrossLongFrames()
        {
            effects.Goal(new Vector2(3, -3.4f), -1, true);
            Assert.That(effects.IsVictory, Is.True);
            effects.Step(1.4f);
            Assert.That(effects.IsVictory, Is.True);
            Assert.That(effects.ActiveParticleCount, Is.GreaterThan(0),
                "The win must continue beyond the initial point impact.");
            effects.Step(2);
            Assert.That(effects.IsVictory, Is.False);
            Assert.That(effects.ActiveParticleCount, Is.Zero,
                "A slow frame must age scheduled bursts instead of leaving a new burst alive indefinitely.");
        }

        [Test]
        public void HidingOrResettingClearsDelayedBurstsAndCannotReplayThem()
        {
            effects.Goal(Vector2.zero, -1, true);
            effects.Step(.2f);
            effects.SetVisible(false);
            AssertCleared();
            effects.Hit(Vector2.zero, 1, 1);
            effects.Goal(Vector2.zero, 1, true);
            effects.Step(2);
            AssertCleared();
            effects.SetVisible(true);
            effects.Step(2);
            Assert.That(effects.ActiveParticleCount, Is.Zero);
            Assert.That(effects.IsVictory, Is.False);
            effects.Pickup(Vector2.zero, -1);
            Assert.That(effects.ActiveParticleCount, Is.GreaterThan(0));
            effects.ResetEffects();
            AssertCleared();
            effects.Step(1);
            Assert.That(effects.ActiveParticleCount, Is.Zero);
        }

        [Test]
        public void RepeatedEventsReuseBoundedRenderersAndDoNotConsumeGameplayRandomness()
        {
            int objectCount = root.GetComponentsInChildren<Transform>(true).Length;
            int rendererCount = root.GetComponentsInChildren<Renderer>(true).Length;
            Random.State saved = Random.state;
            try
            {
                Random.InitState(27182);
                float expected = Random.value;
                Random.InitState(27182);
                for(int i = 0; i < 120; i++)
                {
                    effects.Hit(new Vector2(i % 8, 0), i % 2 == 0 ? -1 : 1, 1);
                    effects.Goal(Vector2.zero, 1, true);
                    effects.Pickup(Vector2.one, -1);
                }
                Assert.That(Random.value, Is.EqualTo(expected));
            }
            finally { Random.state = saved; }
            effects.Initialize(camera, 8.5f, -3.4f, Vector2.zero);
            Assert.That(root.GetComponentsInChildren<Transform>(true).Length, Is.EqualTo(objectCount));
            Assert.That(root.GetComponentsInChildren<Renderer>(true).Length, Is.EqualTo(rendererCount));
            Assert.That(effects.ActiveParticleCount, Is.LessThanOrEqualTo(rendererCount));
            effects.Step(20);
            Assert.That(effects.ActiveParticleCount, Is.Zero);
        }

        [Test]
        public void OrdinaryGoalUsesWinnerColorAndDoesNotStartVictory()
        {
            effects.Goal(new Vector2(-3, -3.4f), 1, false);
            Assert.That(effects.IsVictory, Is.False);
            foreach(var view in root.GetComponentsInChildren<SpriteRenderer>())
                if(view.enabled) Assert.That(view.color.r, Is.GreaterThan(view.color.b));
            effects.Step(1.5f);
            Assert.That(effects.ActiveParticleCount, Is.Zero);
            effects.ResetEffects();
            effects.Goal(new Vector2(3, -3.4f), -1, false);
            foreach(var view in root.GetComponentsInChildren<SpriteRenderer>())
                if(view.enabled) Assert.That(view.color.b, Is.GreaterThan(view.color.r));
        }

        [Test]
        public void DestroyingComponentReleasesItsArtworkAndGeneratedResources()
        {
            var field = typeof(RallyBattleEffects).GetField("resources", BindingFlags.Instance | BindingFlags.NonPublic);
            var owned = new List<Object>((List<Object>)field.GetValue(effects));
            Assert.That(owned.Count, Is.GreaterThanOrEqualTo(7));
            var descriptions = new string[owned.Count];
            for(int i = 0; i < owned.Count; i++) descriptions[i] = owned[i].name + " (" + owned[i].GetType().Name + ")";
            Object.DestroyImmediate(effects);
            for(int i = 0; i < owned.Count; i++) Assert.That(owned[i] == null, Is.True,
                "Generated resource survived real component destruction: " + descriptions[i]);
            Assert.That(root.GetComponentsInChildren<Renderer>(true), Is.Empty);
            Assert.That(root.GetComponentsInChildren<AudioSource>(true), Is.Empty);
        }

        void AssertCleared()
        {
            Assert.That(effects.ActiveParticleCount, Is.Zero);
            Assert.That(effects.EffectAge, Is.Zero);
            Assert.That(effects.IsVictory, Is.False);
        }
    }
}
