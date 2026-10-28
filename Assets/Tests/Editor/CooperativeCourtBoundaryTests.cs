using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Naispo.Interaction;

namespace Naispo.Tests
{
    public sealed class CooperativeCourtBoundaryTests
    {
        const float CenterX = 1.25f;
        const float BarrierY = .5f;
        const float Delta = .02f;
        Scene scene, controlScene;
        CooperativeCoinGame game;
        AirHockeyBall ball, control;
        ElasticBarrier barrier;
        Rigidbody2D hands;
        float originalTimeScale;

        [UnitySetUp]
        public IEnumerator CreateCourt()
        {
            originalTimeScale = Time.timeScale;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            Time.timeScale = 1;
            scene = SceneManager.CreateScene("Court boundary physics",
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            controlScene = SceneManager.CreateScene("Court boundary free-flight control",
                new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            var camera = Create("Camera", scene).AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.aspect = 16f / 9f;
            camera.transform.position = new Vector3(CenterX, 0, -10);
            ball = CreateBall("Game ball", scene);
            control = CreateBall("Free-flight ball", controlScene);
            game = Create("Game", scene).AddComponent<CooperativeCoinGame>();
            game.Configure(ball, camera);
            game.SetMode(RallyMode.Versus);

            var wall = Create("Hand barrier", scene);
            hands = wall.AddComponent<Rigidbody2D>();
            hands.bodyType = RigidbodyType2D.Kinematic;
            hands.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var edge = wall.AddComponent<EdgeCollider2D>();
            var line = wall.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            barrier = wall.AddComponent<ElasticBarrier>();
            barrier.Initialize(edge, line);
            ConfigureSide(-1);
            PlaceBarrier(new Vector2(CenterX - 2, BarrierY));

            double deadline = Time.realtimeSinceStartupAsDouble + 4;
            while (!game.CanAdvanceBall && Time.realtimeSinceStartupAsDouble < deadline)
                yield return null;
            Assert.That(game.CanAdvanceBall, Is.True, "The actual game must have finished serving.");
            ball.Body.gravityScale = control.Body.gravityScale = 0;
            ball.Body.linearDamping = control.Body.linearDamping = 0;
            MoveBalls(new Vector2(CenterX - 5, 3), Vector2.zero);
        }

        static GameObject Create(string name, Scene destination)
        {
            var result = new GameObject(name);
            SceneManager.MoveGameObjectToScene(result, destination);
            return result;
        }

        static AirHockeyBall CreateBall(string name, Scene destination)
        {
            var result = Create(name, destination).AddComponent<AirHockeyBall>();
            result.GetComponent<CircleCollider2D>().radius = .22f;
            result.Body.interpolation = RigidbodyInterpolation2D.None;
            result.Body.gravityScale = 0;
            result.Body.linearDamping = 0;
            return result;
        }

        void ConfigureSide(int side)
        {
            barrier.PlayerSide = side;
            barrier.ConfigureCourt(game, side, CenterX, game.HalfWidth);
            barrier.SetTracking(true);
        }

        void PlaceBarrier(Vector2 position, float rotation = 0, float span = 3.2f)
        {
            // This is the same immediate-pose path used for initial acquisition/teleports.
            hands.position = position;
            hands.rotation = rotation;
            hands.transform.SetPositionAndRotation(position, Quaternion.Euler(0, 0, rotation));
            barrier.SetPoseTarget(position, rotation);
            barrier.SetTracking(true);
            barrier.SetSpan(span, 1.5f, .16f, true);
            barrier.PreparePhysics(0);
            Physics2D.SyncTransforms();
        }

        void MoveBalls(Vector2 position, Vector2 velocity)
        {
            Assert.That(ball.IsInBarrierImpact, Is.False, "Finish the previous contact before moving the probe.");
            foreach (var probe in new[] { ball, control })
            {
                probe.Body.position = position;
                probe.transform.position = position;
                probe.Body.linearVelocity = velocity;
                probe.Body.angularVelocity = 0;
            }
            Physics2D.SyncTransforms();
        }

        void Tick(Vector2? destination = null, float? rotation = null, bool compareControl = false)
        {
            Vector2 position = destination ?? hands.position;
            float angle = rotation ?? hands.rotation;
            // Simulate does not invoke FixedUpdate. Explicitly run the production order.
            barrier.SetPoseTarget(position, angle);
            hands.MovePosition(position);
            hands.MoveRotation(angle);
            barrier.PreparePhysics(Delta);
            Assert.That(scene.GetPhysicsScene2D().Simulate(Delta), Is.True);
            barrier.SendMessage("LateUpdate");
            if (compareControl)
                Assert.That(controlScene.GetPhysicsScene2D().Simulate(Delta), Is.True);
        }

        void AssertFreeFlight(string context)
        {
            Assert.That(ball.IsInBarrierImpact, Is.False, context + ": no capture is permitted.");
            Assert.That(Vector2.Distance(ball.Body.position, control.Body.position), Is.LessThan(.0005f),
                context + ": rejecting a callback must not leave the solver's positional correction behind.");
            Assert.That(Vector2.Distance(ball.Body.linearVelocity, control.Body.linearVelocity), Is.LessThan(.0005f),
                context + ": the opponent barrier must not impart an impulse.");
        }

        void WaitForContact()
        {
            for (int step = 0; step < 20 && !ball.IsInBarrierImpact; step++) Tick();
            Assert.That(ball.IsInBarrierImpact, Is.True, "A real own-court collision must enter compression.");
            Assert.That(barrier.IsHoldingBall, Is.True);
            Assert.That(ball.Body.simulated, Is.False);
        }

        void FinishContact()
        {
            for (int step = 0; step < 20 && ball.IsInBarrierImpact; step++) Tick();
            Assert.That(ball.IsInBarrierImpact, Is.False, "An ordinary valid hit must release normally.");
            Assert.That(ball.Body.simulated, Is.True);
            Assert.That(float.IsNaN(ball.Body.position.x) || float.IsNaN(ball.Body.position.y), Is.False);
        }

        void OpponentProbe(int side)
        {
            ConfigureSide(side);
            PlaceBarrier(new Vector2(CenterX + side * .4f, BarrierY));
            game.VersusState.RegisterBarrierHit(-side);
            Assert.That(game.VersusState.TryActivateSpell(RallySpell.Fireball), Is.True);
            // This sphere overlaps the nominal full barrier and crosses its height.
            MoveBalls(new Vector2(CenterX - side * .12f, 1.2f), Vector2.down * 4);
            for (int step = 0; step < 10; step++)
            {
                Tick(compareControl: true);
                AssertFreeFlight("Opponent-centered sphere, side " + side + ", step " + step);
            }
            Assert.That(game.VersusState.LastHitter, Is.EqualTo(-side));
            Assert.That(game.VersusState.ActiveSpell, Is.EqualTo(RallySpell.Fireball));
            Assert.That(game.VersusState.LeftScore + game.VersusState.RightScore, Is.Zero);
        }

        [UnityTest]
        public IEnumerator LeftBarrierCannotPushOrClaimAnOpponentCenteredSphere()
        {
            OpponentProbe(-1);
            yield break;
        }

        [UnityTest]
        public IEnumerator RightBarrierCannotPushOrClaimAnOpponentCenteredSphere()
        {
            OpponentProbe(1);
            yield break;
        }

        void NearCenterProbe(int side)
        {
            ConfigureSide(side);
            PlaceBarrier(new Vector2(CenterX + side * .4f, BarrierY));
            MoveBalls(new Vector2(CenterX + side * .12f, 1.4f), Vector2.down * 6);
            WaitForContact();
            Assert.That(side * (ball.Body.position.x - CenterX), Is.GreaterThanOrEqualTo(-.0001f));
            Assert.That(game.VersusState.LastHitter, Is.EqualTo(side));
            FinishContact();
            Assert.That(ball.Body.linearVelocity.y, Is.GreaterThan(1),
                "A legitimate near-net pickup must rebound, not silently abort at the clipped endpoint.");
            Assert.That(Mathf.Abs(ball.Body.linearVelocity.x), Is.LessThan(.01f),
                "A clipping endpoint must not turn a horizontal barrier into a vertical rebound wall.");
        }

        [UnityTest]
        public IEnumerator LeftNearCenterOwnCourtSphereCanBeReturned()
        {
            NearCenterProbe(-1);
            yield break;
        }

        [UnityTest]
        public IEnumerator RightNearCenterOwnCourtSphereCanBeReturned()
        {
            NearCenterProbe(1);
            yield break;
        }

        [UnityTest]
        public IEnumerator OrdinaryTiltedOwnCourtContactPreservesCompressionAndHandNormalReflection()
        {
            PlaceBarrier(new Vector2(CenterX - 3, BarrierY), -26.56505f);
            // Stay above the game's intentional low-speed rebound floor.
            MoveBalls(new Vector2(CenterX - 3, 1.5f), Vector2.down * 8);
            WaitForContact();
            float firstY = ball.Body.position.y;
            Tick();
            Tick();
            Assert.That(ball.IsInBarrierImpact, Is.True);
            Assert.That(ball.Body.position.y, Is.LessThan(firstY), "The normal compression phase must remain visible.");
            FinishContact();
            Assert.That(Vector2.Angle(ball.Body.linearVelocity, new Vector2(.8f, .6f)), Is.LessThan(.15f));
            Assert.That(ball.Body.linearVelocity.magnitude, Is.EqualTo(4.64f).Within(.03f));
            yield break;
        }

        [UnityTest]
        public IEnumerator FinalReleaseWaitsForTheQueuedHandPoseInTheProductionPreparationPath()
        {
            PlaceBarrier(new Vector2(CenterX - 3, BarrierY));
            MoveBalls(new Vector2(CenterX - 3, 1.5f), Vector2.down * 8);
            WaitForContact();
            barrier.SetPoseTarget(hands.position, hands.rotation);
            barrier.PreparePhysics(.19f);
            Assert.That(scene.GetPhysicsScene2D().Simulate(Delta), Is.True);
            barrier.SendMessage("LateUpdate");
            Assert.That(ball.IsInBarrierImpact, Is.True, "Stop just before the wide barrier finishes its .20-second impact.");

            Vector2 target = hands.position + new Vector2(.3f, .4f);
            barrier.SetPoseTarget(target, -5);
            hands.MovePosition(target);
            hands.MoveRotation(-5);
            barrier.PreparePhysics(Delta);
            Assert.That(ball.IsInBarrierImpact, Is.True,
                "PreparePhysics must defer the final release until the queued hand movement has been simulated.");
            Assert.That(ball.Body.simulated, Is.False);
            Assert.That(scene.GetPhysicsScene2D().Simulate(Delta), Is.True);
            Assert.That(Vector2.Distance(hands.position, target), Is.LessThan(.001f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(hands.rotation, -5)), Is.LessThan(.01f));
            barrier.SendMessage("LateUpdate");
            Assert.That(ball.IsInBarrierImpact, Is.False);
            Assert.That(ball.Body.simulated, Is.True);
            Vector2 localBall = hands.transform.InverseTransformPoint(ball.Body.position);
            Assert.That(localBall.y, Is.GreaterThan(.3f), "The final hand translation must not overtake a ball released at the old pose.");
            Assert.That(Vector2.Angle(ball.Body.linearVelocity, new Vector2(.1736482f, .9848078f)), Is.LessThan(.15f),
                "The release must use the newly applied five-degree hand angle.");
            yield break;
        }

        [UnityTest]
        public IEnumerator ScaledBarrierAppliesItsHeldBallClearanceInWorldUnitsOnlyOnce()
        {
            hands.transform.localScale = new Vector3(1, 2, 1);
            PlaceBarrier(new Vector2(CenterX - 3, BarrierY));
            MoveBalls(new Vector2(CenterX - 3, 1.6f), Vector2.down * 8);
            WaitForContact();
            // World radii: ball .22, barrier .08 * 2, plus the .025 separation skin.
            // At initial contact the spring has not yet compressed, so its surface
            // is exactly the horizontal line through the hand body's position.
            float worldGap = Vector2.Dot(ball.Body.position - hands.position, Vector2.up);
            Assert.That(worldGap, Is.EqualTo(.405f).Within(.015f),
                "A world-space clearance must not be multiplied by the barrier's scale a second time.");
            FinishContact();
            Assert.That(ball.Body.linearVelocity.y, Is.GreaterThan(1));
            yield break;
        }

        [UnityTest]
        public IEnumerator MovingOpponentOverhangCannotCancelLightOrClaimAContact()
        {
            PlaceBarrier(new Vector2(CenterX - .55f, BarrierY), 0, 4);
            MoveBalls(new Vector2(CenterX + .08f, .78f), Vector2.zero);
            game.VersusState.RegisterBarrierHit(1);
            Assert.That(game.TryActivateSpell(RallySpell.Light), Is.True);
            Assert.That(game.LightPhase, Is.EqualTo(RallyLightPhase.Warning));
            float warning = game.LightPhaseSecondsRemaining;
            for (int step = 0; step < 8; step++)
            {
                Tick(new Vector2(CenterX - (.55f - .065f * (step + 1)), BarrierY), -(step + 1) * 7f, true);
                AssertFreeFlight("Light overhang, step " + step);
                Assert.That(game.ActiveSpell, Is.EqualTo(RallySpell.Light));
                Assert.That(game.LightPhase, Is.EqualTo(RallyLightPhase.Warning),
                    "An inadmissible collision must not notify Light before being rejected.");
                Assert.That(game.LightPhaseSecondsRemaining, Is.EqualTo(warning).Within(.001f));
                Assert.That(game.VersusState.LastHitter, Is.EqualTo(1));
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator QueuedTranslationsAndRotationsCannotMoveOpponentSpheresOnEitherSide()
        {
            foreach (int side in new[] { -1, 1 })
            {
                ConfigureSide(side);
                PlaceBarrier(new Vector2(CenterX + side * .55f, BarrierY), 0, 4);
                MoveBalls(new Vector2(CenterX - side * .08f, .78f), Vector2.zero);
                for (int step = 0; step < 8; step++)
                {
                    var target = new Vector2(CenterX + side * (.55f - .065f * (step + 1)), BarrierY);
                    Tick(target, side * (step + 1) * 7f, true);
                    AssertFreeFlight("Queued translation/rotation, side " + side + ", step " + step);
                }
            }
            yield break;
        }

        [UnityTest]
        public IEnumerator AppliedPoseTargetDoesNotLeaveAPermanentNearCenterDeadStrip()
        {
            PlaceBarrier(new Vector2(CenterX - 1.6f, BarrierY), -28, 4);
            Vector2 target = new Vector2(CenterX - .4f, BarrierY);
            Tick(target, 0);
            // Rotation may be speed limited. Continue commanding the final pose until settled.
            for (int step = 0; step < 20 && (Vector2.Distance(hands.position, target) > .001f ||
                Mathf.Abs(Mathf.DeltaAngle(hands.rotation, 0)) > .01f); step++) Tick(target, 0);
            Assert.That(Vector2.Distance(hands.position, target), Is.LessThan(.001f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(hands.rotation, 0)), Is.LessThan(.01f));
            Tick();
            MoveBalls(new Vector2(CenterX - .12f, 1.4f), Vector2.down * 6);
            WaitForContact();
            FinishContact();
            Assert.That(ball.Body.linearVelocity.y, Is.GreaterThan(1));
            yield break;
        }

        [UnityTest]
        public IEnumerator FullyClippedTrackedBarrierReentersWithoutAnotherTrackingAcquisition()
        {
            PlaceBarrier(new Vector2(CenterX + 1, BarrierY), 0, 1);
            foreach (var edge in barrier.GetComponentsInChildren<EdgeCollider2D>())
                Assert.That(edge.enabled, Is.False, "There must be no retained collision run in the opponent court.");
            Assert.That(game.InputReady, Is.True);
            // Keep tracking continuously true; only the virtual pose crosses the boundary.
            Tick(new Vector2(CenterX - .7f, BarrierY), 0);
            Tick();
            bool hasRun = false;
            foreach (var edge in barrier.GetComponentsInChildren<EdgeCollider2D>()) hasRun |= edge.enabled;
            Assert.That(hasRun, Is.True, "An empty clipped shape must continue preparing and restore itself on reentry.");
            MoveBalls(new Vector2(CenterX - .7f, 1.4f), Vector2.down * 6);
            WaitForContact();
            FinishContact();
            yield break;
        }

        [UnityTest]
        public IEnumerator CarryingACompressedBallAcrossCenterAbortsWithoutConsumingItsSpell()
        {
            PlaceBarrier(new Vector2(CenterX - .7f, BarrierY));
            game.VersusState.RegisterBarrierHit(1);
            Assert.That(game.VersusState.TryActivateSpell(RallySpell.Fireball), Is.True);
            Vector2 incoming = Vector2.down * 6;
            MoveBalls(new Vector2(CenterX - .7f, 1.4f), incoming);
            WaitForContact();
            Vector2 lastAccepted = ball.Body.position;
            Vector2 target = hands.position + Vector2.right * 1.6f;
            barrier.SetPoseTarget(target, 0);
            hands.MovePosition(target);
            hands.MoveRotation(0);
            Assert.DoesNotThrow(() => barrier.PreparePhysics(Delta));
            Assert.That(ball.IsInBarrierImpact, Is.False);
            Assert.That(barrier.IsHoldingBall, Is.False);
            Assert.That(Vector2.Distance(ball.Body.position, lastAccepted), Is.LessThan(.001f),
                "Abort must preserve the last accepted position instead of dragging or clamping the held ball.");
            Assert.That(Vector2.Distance(ball.Body.linearVelocity, incoming), Is.LessThan(.02f));
            Assert.That(game.VersusState.ActiveSpell, Is.EqualTo(RallySpell.Fireball));
            Assert.That(ball.Body.simulated, Is.True);
            Assert.That(scene.GetPhysicsScene2D().Simulate(Delta), Is.True);
            Assert.DoesNotThrow(() => barrier.SendMessage("LateUpdate"));
            Tick();
            Assert.That(game.VersusState.ActiveSpell, Is.EqualTo(RallySpell.Fireball),
                "Neither the abort nor subsequent cleanup may consume a rebound spell.");
            Assert.That(game.VersusState.LeftScore + game.VersusState.RightScore, Is.Zero);
            yield break;
        }

        void IncomingCrossingProbe(int side)
        {
            ConfigureSide(side);
            PlaceBarrier(new Vector2(CenterX + side * .6f, BarrierY));
            // The centre starts on the opponent side, then crosses and reaches the
            // retained endpoint during THIS simulation step. A current-side-only
            // IgnoreCollision decision would miss this otherwise valid pickup.
            MoveBalls(new Vector2(CenterX - side * .04f, .76f), new Vector2(side * 8, -6));
            Tick();
            Assert.That(ball.IsInBarrierImpact, Is.True,
                "A ball that reaches its own court during this step must not be ignored for the whole step.");
            Assert.That(barrier.IsHoldingBall, Is.True);
            Assert.That(side * (ball.Body.position.x - CenterX), Is.GreaterThanOrEqualTo(-.0001f));
            Assert.That(game.VersusState.LastHitter, Is.EqualTo(side));
            FinishContact();
            Assert.That(ball.Body.linearVelocity.y, Is.GreaterThan(1));
            Assert.That(side * ball.Body.linearVelocity.x, Is.GreaterThan(1));
        }

        [UnityTest]
        public IEnumerator FastBallCrossingFromRightCanReachTheLeftBarrierInTheSameStep()
        {
            IncomingCrossingProbe(-1);
            yield break;
        }

        [UnityTest]
        public IEnumerator FastBallCrossingFromLeftCanReachTheRightBarrierInTheSameStep()
        {
            IncomingCrossingProbe(1);
            yield break;
        }

        [UnityTest]
        public IEnumerator MovingAndRotatingInitialContactKeepsTheIncomingWorldVelocity()
        {
            PlaceBarrier(new Vector2(CenterX - 3, BarrierY));
            MoveBalls(new Vector2(CenterX - 2.2f, .93f), new Vector2(-2, -8));
            Vector2 target = hands.position + new Vector2(.08f, .04f);
            Tick(target, -5, true);
            Assert.That(ball.IsInBarrierImpact, Is.True,
                "Exercise an off-center collision during actual queued translation AND rotation.");
            Assert.That(Vector2.Distance(hands.position, target), Is.LessThan(.001f));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(hands.rotation, -5)), Is.LessThan(.01f));
            Vector2 incoming = control.Body.linearVelocity;
            Vector2 reflected = Vector2.Reflect(incoming, (Vector2)hands.transform.up);
            FinishContact();
            Assert.That(Vector2.Angle(ball.Body.linearVelocity, reflected), Is.LessThan(.2f),
                "Kinematic linear or angular velocity must not become an invented component of the incoming ball velocity.");
            Assert.That(ball.Body.linearVelocity.magnitude, Is.EqualTo(incoming.magnitude * .58f).Within(.04f),
                "Without a filtered hand-motion sample there must be no extra kinematic speed boost.");
            yield break;
        }

        [UnityTest]
        public IEnumerator GravityAndAForceAppliedDuringTheContactStepRemainInTheRebound()
        {
            PlaceBarrier(new Vector2(CenterX - 3, BarrierY));
            MoveBalls(new Vector2(CenterX - 3, .92f), new Vector2(3, -8));
            ball.Body.gravityScale = control.Body.gravityScale = 1.7f;
            // ForceMode2D.Force is integrated by Simulate, after preparation. An
            // unconditional velocity snapshot from PreparePhysics loses both this
            // horizontal force and this tick's gravity.
            ball.Body.AddForce(Vector2.right * (30 * ball.Body.mass), ForceMode2D.Force);
            control.Body.AddForce(Vector2.right * (30 * control.Body.mass), ForceMode2D.Force);
            Tick(compareControl: true);
            Assert.That(ball.IsInBarrierImpact, Is.True, "The first integration step must actually hit the barrier.");
            Vector2 incoming = control.Body.linearVelocity;
            Assert.That(incoming.x, Is.GreaterThan(3.4f), "The no-barrier control must demonstrate the force integration.");
            Assert.That(incoming.y, Is.LessThan(-8.2f), "The no-barrier control must demonstrate gravity integration.");
            Vector2 reflected = Vector2.Reflect(incoming, Vector2.up);
            FinishContact();
            Assert.That(Vector2.Angle(ball.Body.linearVelocity, reflected), Is.LessThan(.2f),
                "The rebound must reflect the actual velocity entering contact, including forces from this physics step.");
            Assert.That(ball.Body.linearVelocity.magnitude, Is.EqualTo(incoming.magnitude * .58f).Within(.04f));
            yield break;
        }

        [UnityTest]
        public IEnumerator AWallBounceEarlierInTheSameStepIsNotErasedByBarrierCapture()
        {
            PlaceBarrier(new Vector2(CenterX - 3, BarrierY));
            var contactMaterial = new PhysicsMaterial2D("Same-step wall probe") {
                friction = 0, bounciness = .9f, bounceCombine = PhysicsMaterialCombine2D.Minimum
            };
            try
            {
                foreach (Scene destination in new[] { scene, controlScene })
                {
                    var wall = Create("Earlier wall collision", destination);
                    wall.transform.position = new Vector2(CenterX - 2, 1.1f);
                    var collider = wall.AddComponent<BoxCollider2D>();
                    collider.size = new Vector2(.1f, 5);
                    collider.sharedMaterial = contactMaterial;
                }
                // The vertical wall is .02 units away; the horizontal barrier is
                // .14 units away. At the same speed, wall contact must occur first.
                MoveBalls(new Vector2(CenterX - 2.29f, .94f), new Vector2(8, -8));
                Tick(compareControl: true);
                Assert.That(control.Body.linearVelocity.x, Is.LessThan(-1),
                    "The independent control must have actually bounced off the added wall in this step.");
                Assert.That(ball.IsInBarrierImpact, Is.True,
                    "The game probe must reach the hand barrier during that same simulation step.");
                Vector2 incoming = control.Body.linearVelocity;
                Vector2 reflected = Vector2.Reflect(incoming, Vector2.up);
                FinishContact();
                Assert.That(ball.Body.linearVelocity.x, Is.LessThan(-1),
                    "A velocity cached before simulation must not erase the earlier wall bounce.");
                Assert.That(Vector2.Angle(ball.Body.linearVelocity, reflected), Is.LessThan(.5f));
                Assert.That(ball.Body.linearVelocity.magnitude, Is.EqualTo(incoming.magnitude * .58f).Within(.1f));
            }
            finally { UnityEngine.Object.Destroy(contactMaterial); }
            yield break;
        }

        [UnityTest]
        public IEnumerator AWallBounceLaterInTheSameStepIsNotErasedByBarrierCapture()
        {
            PlaceBarrier(new Vector2(CenterX - 3, BarrierY));
            var contactMaterial = new PhysicsMaterial2D("Later same-step wall probe") {
                friction = 0, bounciness = .9f, bounceCombine = PhysicsMaterialCombine2D.Minimum
            };
            try
            {
                foreach (Scene destination in new[] { scene, controlScene })
                {
                    var wall = Create("Later wall collision", destination);
                    wall.transform.position = new Vector2(CenterX - 2, 1.1f);
                    var collider = wall.AddComponent<BoxCollider2D>();
                    collider.size = new Vector2(.1f, 5);
                    collider.sharedMaterial = contactMaterial;
                }
                // Reverse the earlier-wall regression: the horizontal barrier is
                // .02 units away and the vertical wall is .14 units away. The
                // world velocity reconstructed at callback time must retain the
                // foreign wall impulse regardless of solver/contact ordering.
                MoveBalls(new Vector2(CenterX - 2.41f, .82f), new Vector2(8, -8));
                Tick(compareControl: true);
                Assert.That(control.Body.linearVelocity.x, Is.LessThan(-1),
                    "The control must reach the added wall during this simulation step.");
                Assert.That(ball.IsInBarrierImpact, Is.True,
                    "The game probe must enter compression during the same step.");
                Vector2 incoming = control.Body.linearVelocity;
                Vector2 reflected = Vector2.Reflect(incoming, Vector2.up);
                FinishContact();
                Assert.That(ball.Body.linearVelocity.x, Is.LessThan(-1),
                    "Reconstructing the hand-barrier contact must not erase a different collider's impulse.");
                Assert.That(Vector2.Angle(ball.Body.linearVelocity, reflected), Is.LessThan(.5f));
                Assert.That(ball.Body.linearVelocity.magnitude, Is.EqualTo(incoming.magnitude * .58f).Within(.1f));
            }
            finally { UnityEngine.Object.Destroy(contactMaterial); }
            yield break;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (Application.isPlaying)
            {
                Time.timeScale = originalTimeScale;
                yield return new ExitPlayMode();
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            game = null;
            ball = control = null;
            barrier = null;
            hands = null;
        }
    }
}
