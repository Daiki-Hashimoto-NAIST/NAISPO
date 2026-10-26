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
    public sealed class MagicalSpellEdgeTests
    {
        const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Scene scene;
        CooperativeCoinGame game;
        AirHockeyBall ball;
        ElasticBarrier barrier;
        float originalTimeScale;

        [UnitySetUp]
        public IEnumerator CreateCourt()
        {
            originalTimeScale = Time.timeScale;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            Time.timeScale = 1;
            scene = SceneManager.CreateScene("Magical spell edge court",
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

            var wall = Create("Left barrier");
            wall.transform.position = new Vector2(-3, -1.8f);
            wall.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var edge = wall.AddComponent<EdgeCollider2D>();
            var line = wall.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            barrier = wall.AddComponent<ElasticBarrier>();
            barrier.Initialize(edge, line);
            barrier.SetSpan(3, .3f, .16f, true);
            barrier.PlayerSide = -1;
            yield return WaitFor(() => game.CanAdvanceBall, 3, "The initial serve must start.");
            MoveBall(new Vector2(-6, 3));
            yield return TwoFrames();
        }

        [UnityTest]
        public IEnumerator APointRequiresANewFirstReturnBeforeAnyPickupCanSpawn()
        {
            HitAndRelease();
            MoveBall(new Vector2(-6, 3));
            // Put the existing production timer just before a spawn attempt, then
            // lose the point before another Update can spend the remaining delay.
            float remaining = game.PickupSecondsUntilSpawn;
            Assert.That(remaining, Is.GreaterThan(.05f));
            StepPickups(remaining - .05f, ball.Body.position, ball.Body.position);
            Assert.That(game.PickupCount, Is.Zero);
            Assert.That(game.PickupSecondsUntilSpawn, Is.EqualTo(.05f).Within(.001f));
            MoveBall(new Vector2(-3, game.MatchFloor - .4f));
            yield return TwoFrames();

            Assert.That(game.VersusState.RightScore, Is.EqualTo(1));
            Assert.That(game.VersusState.LastHitter, Is.Zero);
            Assert.That(game.PickupCount, Is.Zero);
            Assert.That(game.PickupSecondsUntilSpawn, Is.Zero,
                "The old rally's nearly-expired spawn timer must be disarmed by the point.");
            yield return WaitFor(() => game.CanAdvanceBall, 3, "The next rally must serve.");
            MoveBall(new Vector2(-6, 3));
            yield return new WaitForSecondsRealtime(.2f);
            yield return TwoFrames();
            Assert.That(game.VersusState.LastHitter, Is.Zero);
            Assert.That(game.PickupCount, Is.Zero,
                "A new serve cannot inherit the previous rally's pickup cadence.");
            Assert.That(game.PickupSecondsUntilSpawn, Is.Zero);

            HitAndRelease();
            Assert.That(game.PickupSecondsUntilSpawn,
                Is.EqualTo(CooperativeCoinGame.PickupFirstDelay).Within(.001f));
            MoveBall(new Vector2(-6, 3));
            yield return WaitFor(() => game.PickupCount > 0, 2,
                "A real return must arm the fresh first-pickup delay again.");
        }

        [UnityTest]
        public IEnumerator ExpiringFirstPickupStillPrecedesTheLaterPickupOnTheFullSweep()
        {
            HitAndRelease();
            // The ball travels from -2.5 to +2.5. Its first pickup is at t=.1;
            // that pickup expires at t=.105. Its clipped local position (.952)
            // must not be sorted after the second pickup at full-frame t=.9.
            SetPickup(0, RallySpell.Fireball, new Vector2(-2, 0), 6.4475f);
            SetPickup(1, RallySpell.Timer, new Vector2(2, 0), 4.0475f);
            StepPickups(.5f, new Vector2(-2.5f, 0), new Vector2(2.5f, 0));
            AssertBothPickupsUsedWithTimerLast();
            yield break;
        }

        [UnityTest]
        public IEnumerator NewlyCollectibleLaterPickupDoesNotJumpBeforeTheEarlierPickup()
        {
            HitAndRelease();
            // The second slot becomes collectible at t=.9, exactly where the ball
            // reaches it. Its clipped local position (0) is not its travel order.
            SetPickup(0, RallySpell.Fireball, new Vector2(-2, 0), 2.45f);
            SetPickup(1, RallySpell.Timer, new Vector2(2, 0), .05f);
            StepPickups(.5f, new Vector2(-2.5f, 0), new Vector2(2.5f, 0));
            AssertBothPickupsUsedWithTimerLast();
            yield break;
        }

        [UnityTest]
        public IEnumerator RisingNearTheApexCancelsLightBeforeItsProjectedDescentEntersDanger()
        {
            HitAndRelease();
            MoveBall(new Vector2(-5, 3));
            // Keep normal Update/LateUpdate rendering active while controlling the
            // exact warning boundary independently of the test runner's frame rate.
            Time.timeScale = 0;
            Assert.That(game.TryActivateSpell(RallySpell.Light), Is.True);
            yield return TwoFrames();
            Assert.That(game.LightPhase, Is.EqualTo(RallyLightPhase.Warning));
            AssertBallVisible(true);
            Assert.That(FindRenderer("Ball spell/Light warning 0").enabled, Is.True);

            float radius = ball.GetComponent<CircleCollider2D>().radius;
            float danger = game.MatchFloor + 1.2f;
            MoveBall(new Vector2(-5, danger + radius + .1f));
            ball.Body.gravityScale = .55f;
            ball.Body.linearVelocity = Vector2.up * .1f;
            float horizon = RallyVersusState.LightHiddenSeconds;
            float projectedBottom = ball.Body.position.y - radius + .1f * horizon
                + .5f * Physics2D.gravity.y * ball.Body.gravityScale * horizon * horizon;
            Assert.That(ball.Body.position.y - radius, Is.GreaterThan(danger));
            Assert.That(ball.Body.linearVelocity.y, Is.GreaterThan(0));
            Assert.That(projectedBottom, Is.LessThan(danger),
                "The regression must rise now but enter the danger band during the hidden interval.");

            InvokeGame("StepLight", RallyVersusState.LightWarningSeconds);
            RefreshBallPresentation();
            Assert.That(game.ActiveSpell, Is.EqualTo(RallySpell.None));
            Assert.That(game.LightPhase, Is.EqualTo(RallyLightPhase.None),
                "Light must cancel before hiding even when its initial vertical velocity is positive.");
            AssertBallVisible(true);
            Assert.That(ball.Body.linearVelocity, Is.EqualTo(Vector2.up * .1f),
                "Cancelling a visual spell must leave the physical trajectory unchanged.");
        }

        [UnityTest]
        public IEnumerator DisablingTheGameRestoresHiddenBallAndReplaysWarningOnEnable()
        {
            yield return AssertHiddenSuspension(true);
        }

        [UnityTest]
        public IEnumerator OpeningSettingsRestoresHiddenBallAndReplaysWarningOnClose()
        {
            yield return AssertHiddenSuspension(false);
        }

        [UnityTest]
        public IEnumerator CooldownRecontactCancelsLightWarningWithoutRegisteringAnotherReturn()
        {
            AssertLightCancelledOnCooldownEnter(false);
            yield break;
        }

        [UnityTest]
        public IEnumerator CooldownRecontactRestoresHiddenBallWithoutRegisteringAnotherReturn()
        {
            AssertLightCancelledOnCooldownEnter(true);
            yield break;
        }

        [UnityTest]
        public IEnumerator ContinuingContactCancelsBothLightPhasesWithoutRegisteringAnotherReturn()
        {
            foreach (bool hidden in new[] { false, true })
            {
                // Each phase needs its own real release and cooldown recontact.
                // One Stay resolves the overlap, so the next step would no longer
                // be continuing contact; a phase cannot inherit the other's pair.
                var probe = PrepareCooldownRecontact();
                SimulateUntilRecontact(probe);
                float spawnDelay = game.PickupSecondsUntilSpawn;
                ArmLightForContact(hidden);
                int enters = probe.EnterCount;
                int stays = probe.StayCount;
                scene.GetPhysicsScene2D().Simulate(.01f);
                Assert.That(probe.EnterCount, Is.EqualTo(enters),
                    "This regression must use continuing contact, not another collision enter.");
                Assert.That(probe.StayCount, Is.GreaterThan(stays));
                AssertLightContactOutcome(spawnDelay);
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator CooldownRecontactAndStayDoNotConsumePendingFireballOrTimer()
        {
            foreach (RallySpell spell in new[] { RallySpell.Fireball, RallySpell.Timer })
            {
                var probe = PrepareCooldownRecontact();
                Assert.That(game.TryActivateSpell(spell), Is.True);
                float remaining = game.SpellSecondsRemaining;
                float spawnDelay = game.PickupSecondsUntilSpawn;
                float releasedSpeed = ball.Body.linearVelocity.magnitude;
                SimulateUntilRecontact(probe);
                AssertPendingSpellUnchanged(spell, remaining, spawnDelay, releasedSpeed);
                int enters = probe.EnterCount;
                int stays = probe.StayCount;
                scene.GetPhysicsScene2D().Simulate(.01f);
                Assert.That(probe.EnterCount, Is.EqualTo(enters));
                Assert.That(probe.StayCount, Is.GreaterThan(stays));
                AssertPendingSpellUnchanged(spell, remaining, spawnDelay, releasedSpeed);
            }
            yield break;
        }

        void AssertLightCancelledOnCooldownEnter(bool hidden)
        {
            var probe = PrepareCooldownRecontact();
            ArmLightForContact(hidden);
            float spawnDelay = game.PickupSecondsUntilSpawn;
            SimulateUntilRecontact(probe);
            AssertLightContactOutcome(spawnDelay);
        }

        MagicalSpellContactProbe PrepareCooldownRecontact()
        {
            // Each scenario first earns attribution and the cooldown through a real
            // hit. No frame is yielded between release and the subsequent contacts.
            ball.CancelBarrierImpact();
            var handBody = barrier.GetComponent<Rigidbody2D>();
            handBody.linearVelocity = Vector2.zero;
            handBody.position = new Vector2(-3, -1.8f);
            barrier.transform.position = handBody.position;
            barrier.PlayerSide = -1;
            barrier.SetSpan(4, 1.5f, .16f, true);
            handBody.linearVelocity = Vector2.up * 6;
            HitAndRelease();
            Vector2 releasedVelocity = ball.Body.linearVelocity;
            Assert.That(releasedVelocity.y, Is.GreaterThan(0).And.LessThan(6),
                "The moving hands must be able to catch the wide barrier's slower return.");

            // Establish separation, then approach again. This distinguishes Enter
            // from Stay even if the held ball's simulated toggle retained a contact.
            MoveBall(handBody.position + Vector2.up * 2);
            scene.GetPhysicsScene2D().Simulate(.01f);
            Assert.That(ball.Body.IsTouching(barrier.GetComponent<EdgeCollider2D>()), Is.False);
            float gap = ball.GetComponent<CircleCollider2D>().radius
                + barrier.GetComponent<EdgeCollider2D>().edgeRadius + .04f;
            MoveBall(handBody.position + Vector2.up * gap);
            ball.Body.linearVelocity = releasedVelocity;
            var probe = barrier.GetComponent<MagicalSpellContactProbe>();
            if (probe == null) probe = barrier.gameObject.AddComponent<MagicalSpellContactProbe>();
            probe.Observe(ball.Body);

            // Keep the same barrier identity for cooldown, but use the opposite
            // side as a sentinel for an erroneous second attribution.
            barrier.PlayerSide = 1;
            StepPickups(.2f, ball.Body.position, ball.Body.position);
            Assert.That(game.PickupSecondsUntilSpawn, Is.GreaterThan(0)
                .And.LessThan(CooperativeCoinGame.PickupFirstDelay));
            return probe;
        }

        void SimulateUntilRecontact(MagicalSpellContactProbe probe)
        {
            for (int i = 0; i < 12 && probe.EnterCount == 0; i++)
                scene.GetPhysicsScene2D().Simulate(.01f);
            Assert.That(probe.EnterCount, Is.EqualTo(1), "Exercise an actual collision enter.");
            Assert.That(ball.Body.IsTouching(barrier.GetComponent<EdgeCollider2D>()), Is.True);
            Assert.That(ball.IsInBarrierImpact, Is.False,
                "The same-barrier cooldown must reject capture during this contact.");
        }

        void ArmLightForContact(bool hidden)
        {
            Assert.That(game.TryActivateSpell(RallySpell.Light), Is.True);
            RefreshBallPresentation();
            if (hidden)
            {
                InvokeGame("StepLight", RallyVersusState.LightWarningSeconds);
                RefreshBallPresentation();
            }
            Assert.That(game.LightPhase,
                Is.EqualTo(hidden ? RallyLightPhase.Hidden : RallyLightPhase.Warning));
            AssertBallVisible(!hidden);
        }

        void AssertLightContactOutcome(float spawnDelay)
        {
            Assert.That(game.ActiveSpell, Is.EqualTo(RallySpell.None));
            Assert.That(game.LightPhase, Is.EqualTo(RallyLightPhase.None));
            RefreshBallPresentation();
            AssertBallVisible(true);
            AssertCooldownContactPreservesRally(spawnDelay);
        }

        void AssertPendingSpellUnchanged(RallySpell spell, float remaining, float spawnDelay, float speed)
        {
            Assert.That(game.ActiveSpell, Is.EqualTo(spell));
            Assert.That(game.SpellOwner, Is.EqualTo(-1));
            Assert.That(game.SpellSecondsRemaining, Is.EqualTo(remaining).Within(.001f));
            Assert.That(ball.Body.linearVelocity.magnitude, Is.EqualTo(speed).Within(.03f),
                "A rejected contact must neither consume the next-return spell nor apply its multiplier.");
            AssertCooldownContactPreservesRally(spawnDelay);
        }

        void AssertCooldownContactPreservesRally(float spawnDelay)
        {
            Assert.That(ball.IsInBarrierImpact, Is.False);
            Assert.That(game.VersusState.LastHitter, Is.EqualTo(-1),
                "Physical contact alone must not attribute a new successful return.");
            Assert.That(game.PickupSecondsUntilSpawn, Is.EqualTo(spawnDelay).Within(.001f),
                "Contact cancellation must preserve the existing pickup cadence.");
        }

        IEnumerator AssertHiddenSuspension(bool disableComponent)
        {
            HitAndRelease();
            MoveBall(new Vector2(-5, 3));
            Assert.That(game.TryActivateSpell(RallySpell.Light), Is.True);
            yield return TwoFrames();
            Assert.That(game.LightPhase, Is.EqualTo(RallyLightPhase.Warning));
            InvokeGame("StepLight", RallyVersusState.LightWarningSeconds);
            RefreshBallPresentation();
            Assert.That(game.LightPhase, Is.EqualTo(RallyLightPhase.Hidden));
            AssertBallVisible(false);
            float remainingHidden = game.LightPhaseSecondsRemaining;

            if (disableComponent)
            {
                game.enabled = false;
                AssertBallVisible(true);
            }
            else SetSettings(true);
            yield return TwoFrames();
            AssertBallVisible(true);
            Assert.That(ball.Body.simulated, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1),
                "The suspension check must use advancing game time, not a globally frozen clock.");
            yield return new WaitForSecondsRealtime(.2f);
            yield return TwoFrames();
            Assert.That(game.ActiveSpell, Is.EqualTo(RallySpell.Light));
            Assert.That(game.LightPhase, Is.EqualTo(RallyLightPhase.Hidden));
            Assert.That(game.LightPhaseSecondsRemaining, Is.EqualTo(remainingHidden).Within(.001f),
                "Suspension must preserve the unspent hidden interval.");
            AssertBallVisible(true);

            // Freeze only the resumed countdown, so actual Update/LateUpdate can
            // prove that re-enabling/closing settings installs a full new warning.
            Time.timeScale = 0;
            if (disableComponent) game.enabled = true;
            else SetSettings(false);
            yield return TwoFrames();
            Assert.That(game.LightPhase, Is.EqualTo(RallyLightPhase.Warning));
            Assert.That(game.LightPhaseSecondsRemaining,
                Is.EqualTo(RallyVersusState.LightWarningSeconds).Within(.001f));
            AssertBallVisible(true);
            Assert.That(FindRenderer("Ball spell/Light warning 0").enabled, Is.True);

            InvokeGame("StepLight", RallyVersusState.LightWarningSeconds - .01f);
            RefreshBallPresentation();
            Assert.That(game.LightPhase, Is.EqualTo(RallyLightPhase.Warning),
                "The preserved hidden time cannot resume before the full warning completes.");
            AssertBallVisible(true);
            InvokeGame("StepLight", .02f);
            RefreshBallPresentation();
            Assert.That(game.LightPhase, Is.EqualTo(RallyLightPhase.Hidden));
            Assert.That(game.LightPhaseSecondsRemaining, Is.EqualTo(remainingHidden).Within(.001f));
            AssertBallVisible(false);
        }

        void AssertBothPickupsUsedWithTimerLast()
        {
            Assert.That(game.GetPickupSnapshot(0).Visible, Is.False);
            Assert.That(game.GetPickupSnapshot(1).Visible, Is.False);
            Assert.That(game.PickupCount, Is.Zero);
            Assert.That(game.ActiveSpell, Is.EqualTo(RallySpell.Timer),
                "The last pickup on the complete ball path must replace the earlier spell.");
            Assert.That(game.SpellOwner, Is.EqualTo(-1));
        }

        void SetPickup(int index, RallySpell spell, Vector2 position, float age)
        {
            // Set up precise boundary conditions without adding mutation APIs to
            // production code. Collection and replacement use UpdateVersus itself.
            var slots = (Array)Field(typeof(CooperativeCoinGame), "pickupSlots").GetValue(game);
            object slot = slots.GetValue(index);
            Assert.That(slot, Is.Not.Null);
            Type type = slot.GetType();
            Field(type, "Active").SetValue(slot, true);
            Field(type, "Spell").SetValue(slot, spell);
            Field(type, "Position").SetValue(slot, position);
            Field(type, "Age").SetValue(slot, age);
        }

        void StepPickups(float dt, Vector2 from, Vector2 to)
        {
            InvokeGame("UpdateVersus", dt, from, to, .52f);
        }

        void SetSettings(bool open)
        {
            // Use the same flag and simulation refresh as the settings buttons.
            Field(typeof(CooperativeCoinGame), "settingsOpen").SetValue(game, open);
            InvokeGame("ApplySimulationState");
        }

        static FieldInfo Field(Type type, string name)
        {
            var field = type.GetField(name, InstanceFields);
            Assert.That(field, Is.Not.Null, "Missing fixture field: " + name);
            return field;
        }

        void InvokeGame(string name, params object[] arguments)
        {
            var method = typeof(CooperativeCoinGame).GetMethod(name, InstanceFields);
            Assert.That(method, Is.Not.Null, "Missing production entrypoint: " + name);
            method.Invoke(game, arguments);
        }

        void RefreshBallPresentation()
        {
            InvokeGame("RefreshVersusArtwork");
            InvokeGame("RefreshLighting");
        }

        void AssertBallVisible(bool visible)
        {
            Assert.That(game.BallPresentationVisible, Is.EqualTo(visible));
            Assert.That(ball.GetComponent<SpriteRenderer>().enabled, Is.EqualTo(visible));
            Assert.That(FindRenderer("Ball corona").enabled, Is.EqualTo(visible));
            if (game.ActiveSpell == RallySpell.Light)
            {
                var spell = game.transform.Find("Cooperative court/Ball spell");
                Assert.That(spell, Is.Not.Null);
                Assert.That(spell.gameObject.activeInHierarchy, Is.EqualTo(visible),
                    "The attached spell artwork must follow the ball's visibility.");
            }
        }

        SpriteRenderer FindRenderer(string path)
        {
            var child = game.transform.Find("Cooperative court/" + path);
            Assert.That(child, Is.Not.Null, path);
            return child.GetComponent<SpriteRenderer>();
        }

        void HitAndRelease()
        {
            barrier.ResetSpring();
            MoveBall((Vector2)barrier.transform.position + Vector2.up);
            ball.Body.linearVelocity = Vector2.down * 8;
            for (int i = 0; i < 20 && !ball.IsInBarrierImpact; i++)
                scene.GetPhysicsScene2D().Simulate(.02f);
            Assert.That(ball.IsInBarrierImpact, Is.True,
                "Ownership and pickup arming must come from a real barrier collision.");
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

        static IEnumerator TwoFrames()
        {
            int first = Time.frameCount;
            double deadline = Time.realtimeSinceStartupAsDouble + 3;
            while (Time.frameCount < first + 2 && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.That(Time.frameCount, Is.GreaterThanOrEqualTo(first + 2));
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
            Time.timeScale = originalTimeScale;
            if (Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            game = null;
            ball = null;
            barrier = null;
        }
    }

    public sealed class MagicalSpellContactProbe : MonoBehaviour
    {
        Rigidbody2D observedBody;
        public int EnterCount { get; private set; }
        public int StayCount { get; private set; }

        public void Observe(Rigidbody2D body)
        {
            observedBody = body;
            EnterCount = StayCount = 0;
        }

        void OnCollisionEnter2D(Collision2D collision)
        {
            if (collision.rigidbody == observedBody) EnterCount++;
        }

        void OnCollisionStay2D(Collision2D collision)
        {
            if (collision.rigidbody == observedBody) StayCount++;
        }
    }
}
