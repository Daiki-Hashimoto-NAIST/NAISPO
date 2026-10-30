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
    public sealed class VersusRallyPlayTests
    {
        Scene scene;
        CooperativeCoinGame game;
        AirHockeyBall ball;
        ElasticBarrier leftBarrier, rightBarrier;

        [UnitySetUp]
        public IEnumerator CreateCourt()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            yield return new EnterPlayMode();
            scene=SceneManager.CreateScene("Versus integration court",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            var view=Create("Camera").AddComponent<Camera>();
            view.orthographic=true; view.orthographicSize=5; view.aspect=16f/9f;
            ball=Create("Ball").AddComponent<AirHockeyBall>();
            ball.GetComponent<CircleCollider2D>().radius=.22f;
            ball.Body.interpolation=RigidbodyInterpolation2D.None;
            game=Create("Game").AddComponent<CooperativeCoinGame>();
            game.Configure(ball,view);
            game.SetMode(RallyMode.Versus);
            ball.Body.gravityScale=0; ball.Body.linearDamping=0;
            leftBarrier=CreateBarrier(-1);
            rightBarrier=CreateBarrier(1);
            yield return WaitFor(() => game.CanAdvanceBall,3,"The initial versus serve must start.");
            MoveBall(new Vector2(-6,3));
            yield return TwoFrames();
        }

        GameObject Create(string name)
        {
            var result=new GameObject(name);
            SceneManager.MoveGameObjectToScene(result,scene);
            return result;
        }

        ElasticBarrier CreateBarrier(int side)
        {
            var wall=Create(side<0 ? "Left hand barrier" : "Right hand barrier");
            wall.transform.position=new Vector2(side*3,-1.8f);
            wall.AddComponent<Rigidbody2D>().bodyType=RigidbodyType2D.Kinematic;
            var edge=wall.AddComponent<EdgeCollider2D>();
            var line=wall.AddComponent<LineRenderer>(); line.useWorldSpace=false;
            var result=wall.AddComponent<ElasticBarrier>();
            result.Initialize(edge,line); result.SetSpan(3,.3f,.16f,true);
            result.PlayerSide=side;
            return result;
        }

        [UnityTest]
        public IEnumerator FloorCrossingScoresOnceAtContactSideAndThirdPointEndsTheMatch()
        {
            float radius=ball.GetComponent<CircleCollider2D>().radius;
            for(int point=1;point<=RallyVersusState.PointsToWin;point++)
            {
                yield return WaitFor(() => game.CanAdvanceBall,3,"The next rally must serve.");
                // The ball crosses the floor on the left, then finishes this frame on the right.
                // Scoring must use the crossing position rather than the final position.
                MoveBall(new Vector2(-2,game.MatchFloor+radius+.1f));
                yield return TwoFrames();
                MoveBall(new Vector2(2,game.MatchFloor+radius-1));
                yield return TwoFrames();
                Assert.That(game.VersusState.RightScore,Is.EqualTo(point),$"Floor {game.MatchFloor}, body {ball.Body.position}, transform {ball.transform.position}, scores {game.VersusState.LeftScore}:{game.VersusState.RightScore}, advance {game.CanAdvanceBall}, hold {ball.IsInBarrierImpact}, serve {game.WaitingToServe}");
                Assert.That(game.VersusState.LeftScore,Is.Zero);
                Assert.That(game.VersusState.NextServeSide,Is.EqualTo(-1));
                Assert.That(ball.Body.simulated,Is.False);
                if(point<RallyVersusState.PointsToWin)
                {
                    Assert.That(game.WaitingToServe,Is.True);
                    Assert.That(Vector2.Distance(ball.Body.position,game.ServePosition(-1)),Is.LessThan(.001f));
                    yield return TwoFrames();
                    Assert.That(game.VersusState.RightScore,Is.EqualTo(point),"A floor crossing can award only one point.");
                }
            }
            Assert.That(game.VersusState.Winner,Is.EqualTo(1));
            Assert.That(game.CanAdvanceBall,Is.False);
            Vector2 finalPosition=ball.Body.position;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(game.VersusState.RightScore,Is.EqualTo(3));
            Assert.That(ball.Body.position,Is.EqualTo(finalPosition));
            Assert.That(ball.Body.simulated,Is.False,"A finished match must remain stopped.");

            game.RestartGame();
            Assert.That(game.IsVersus,Is.True);
            Assert.That(game.VersusState.Winner,Is.Zero);
            Assert.That(game.VersusState.LeftScore+game.VersusState.RightScore,Is.Zero);
            Assert.That(game.PickupCount,Is.Zero);
            Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.None));
            Assert.That(game.PendingSpell(-1),Is.EqualTo(RallySpell.None));
            Assert.That(game.PendingSpell(1),Is.EqualTo(RallySpell.None));
            Assert.That(game.WaitingToServe,Is.True);
        }

        [UnityTest]
        public IEnumerator FloorCrossingBeforeALoweredBarrierScoresBeforeTheNextUpdate()
        {
            leftBarrier.transform.position=new Vector2(-3,game.MatchFloor-.5f);
            MoveBall(new Vector2(-3,game.MatchFloor+.9f));
            yield return TwoFrames();
            ball.Body.linearVelocity=Vector2.down*8;
            // All physics steps happen before another Update can notice the floor crossing.
            for(int i=0;i<30 && !game.WaitingToServe && !ball.IsInBarrierImpact;i++)
                scene.GetPhysicsScene2D().Simulate(.02f);

            Assert.That(game.VersusState.RightScore,Is.EqualTo(1),
                "Touching a barrier after crossing the floor cannot save an already-lost rally.");
            Assert.That(game.VersusState.LeftScore,Is.Zero);
            Assert.That(ball.IsInBarrierImpact,Is.False);
            Assert.That(leftBarrier.IsHoldingBall,Is.False);
            Assert.That(game.WaitingToServe,Is.True);
            Assert.That(ball.Body.simulated,Is.False);
            Assert.That(ball.Body.linearVelocity,Is.EqualTo(Vector2.zero));
            Assert.That(Vector2.Distance(ball.Body.position,game.ServePosition(-1)),Is.LessThan(.001f));
            yield return TwoFrames();
            Assert.That(game.VersusState.RightScore,Is.EqualTo(1),"The subsequent Update must not award the same point again.");
        }

        [UnityTest]
        public IEnumerator ValidCatchAboveTheFloorMayCompressBelowItWithoutLosingAPoint()
        {
            leftBarrier.transform.position=new Vector2(-3,game.MatchFloor+.05f);
            MoveBall((Vector2)leftBarrier.transform.position+Vector2.up);
            yield return TwoFrames();
            ball.Body.linearVelocity=Vector2.down*8;
            for(int i=0;i<20 && !ball.IsInBarrierImpact;i++) scene.GetPhysicsScene2D().Simulate(.02f);
            Assert.That(ball.IsInBarrierImpact,Is.True,"This contact occurs above the floor and must be playable.");
            float radius=ball.GetComponent<CircleCollider2D>().radius;
            Assert.That(ball.Body.position.y-radius,Is.GreaterThan(game.MatchFloor));
            leftBarrier.Step(.09f);
            Assert.That(ball.Body.position.y-radius,Is.LessThan(game.MatchFloor),
                "The regression must actually carry the ball below the scoring line during compression.");

            float originalTimeScale=Time.timeScale;
            try
            {
                // Run actual Update/LateUpdate while preventing FixedUpdate from releasing the
                // spring before the scoring check, even on a slow test machine.
                Time.timeScale=0;
                yield return TwoFrames();
                Assert.That(ball.IsInBarrierImpact,Is.True);
                Assert.That(game.VersusState.LeftScore+game.VersusState.RightScore,Is.Zero);
                Assert.That(game.WaitingToServe,Is.False);
            }
            finally { Time.timeScale=originalTimeScale; }

            leftBarrier.Step(.3f);
            Assert.That(ball.IsInBarrierImpact,Is.False);
            Assert.That(ball.Body.position.y-radius,Is.GreaterThan(game.MatchFloor));
            Assert.That(ball.Body.linearVelocity.y,Is.GreaterThan(0));
            yield return TwoFrames();
            Assert.That(game.VersusState.LeftScore+game.VersusState.RightScore,Is.Zero);
        }

        [UnityTest]
        public IEnumerator ModeSwitchClearsSpellsAndPickupsAndRestoresLitStars()
        {
            HitAndRelease(leftBarrier);
            MoveBall(new Vector2(-6,3));
            yield return WaitFor(() => game.PickupCount>0,3,"A returned rally should introduce a pickup.");
            game.SetPaused(true);
            MoveBall(Coin(4).transform.position);
            yield return TwoFrames();
            for(int i=0;i<CooperativeCoinGame.CoinsPerRound;i++)
            {
                Assert.That(IsVisible(Coin(i)),Is.False,"Versus must hide collection-mode stars.");
                Assert.That(IsVisible(game.CollectionHaloRenderer(i)),Is.False);
                Assert.That(game.CoinVisibility(i),Is.Zero);
            }
            // Set a pending effect independently of its artwork so both reset paths are exercised.
            Assert.That(game.VersusState.TryActivateSpell(RallySpell.Fireball),Is.True);
            Assert.That(game.PendingSpell(-1),Is.EqualTo(RallySpell.Fireball));
            Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.None));
            game.SetMode(RallyMode.StarCollection);
            Assert.That(game.PickupCount,Is.Zero);
            Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.None));
            Assert.That(game.PendingSpell(-1),Is.EqualTo(RallySpell.None));
            Assert.That(game.PendingSpell(1),Is.EqualTo(RallySpell.None));
            Assert.That(game.VersusState.LastHitter,Is.Zero);
            Assert.That(game.VersusState.Winner,Is.Zero);
            Assert.That(game.TeamScore,Is.Zero);
            yield return WaitFor(() => game.CanAdvanceBall,3,"Returning to collection must finish the fresh buds' descent.");
            game.SetPaused(true);
            MoveBall(Coin(4).transform.position);
            yield return TwoFrames();
            Assert.That(IsVisible(Coin(4)),Is.True,$"Switching back must restore stars inside the ball's light. Body {ball.Body.position}, transform {ball.transform.position}, coin {Coin(4).transform.position}, visibility {game.CoinVisibility(4)}, mode {game.Mode}, light {game.BallLightEnabled}");
            Assert.That(game.CoinVisibility(4),Is.EqualTo(1).Within(.001f));

            game.SetMode(RallyMode.Versus);
            Assert.That(game.PickupCount,Is.Zero);
            Assert.That(game.WaitingToServe,Is.True);
            Assert.That(game.VersusState.LeftScore+game.VersusState.RightScore,Is.Zero);
            yield return WaitFor(() => game.CanAdvanceBall,3,"Switching modes must start a fresh match.");
            HitAndRelease(leftBarrier);
            Assert.That(game.TryActivateSpell(RallySpell.Fireball),Is.True);
            game.RestartGame();
            Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.None));
            Assert.That(game.PendingSpell(-1),Is.EqualTo(RallySpell.None));
            Assert.That(game.PendingSpell(1),Is.EqualTo(RallySpell.None));
            Assert.That(game.VersusState.LastHitter,Is.Zero);
            Assert.That(game.PickupCount,Is.Zero);
        }

        [UnityTest]
        public IEnumerator PickupClockFreezesAndOneSweepCollectsBothSlotsInTravelOrder()
        {
            Assert.That(game.PickupCount,Is.Zero);
            Assert.That(game.PickupSecondsUntilSpawn,Is.Zero,
                "The serve cannot start pickup spawns before anyone returns the ball.");
            HitAndRelease(leftBarrier);
            Assert.That(game.VersusState.LastHitter,Is.EqualTo(-1));
            Assert.That(game.PickupSecondsUntilSpawn,Is.InRange(.5f,.9f));
            MoveBall(new Vector2(-6,3));
            float remaining=game.PickupSecondsUntilSpawn;
            game.SetPaused(true);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(game.PickupSecondsUntilSpawn,Is.EqualTo(remaining).Within(.001f));
            game.SetInputReady(false); game.SetPaused(false);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(game.PickupSecondsUntilSpawn,Is.EqualTo(remaining).Within(.001f));
            game.SetInputReady(true);
            yield return WaitFor(() => game.PickupCount>0,3,"The first pickup must appear after a real return.");

            int firstSlot=-1;
            var first=FindPickup(out firstSlot);
            Assert.That(first.Visible,Is.True);
            Assert.That(first.Collectible,Is.False,"New pickups must telegraph before pickup.");
            game.SetPaused(true);
            Vector2 warningPosition=first.Position;
            float warningAge=first.Age;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(game.GetPickupSnapshot(firstSlot).Collectible,Is.False,"Pause must freeze the warning age.");
            Assert.That(game.GetPickupSnapshot(firstSlot).Age,Is.EqualTo(warningAge).Within(.001f));
            Assert.That(game.GetPickupSnapshot(firstSlot).Position,Is.EqualTo(warningPosition));
            game.SetPaused(false);
            yield return WaitFor(() => game.GetPickupSnapshot(firstSlot).Collectible,1.5f,"An unpaused pickup must become collectible.");

            yield return WaitFor(() => game.PickupCount>=2,4,"The second slot must appear one retry interval later.");
            int secondSlot=-1;
            var second=FindPickup(out secondSlot,firstSlot);
            Assert.That(second.Visible,Is.True);
            Assert.That(secondSlot,Is.Not.EqualTo(firstSlot));
            Assert.That(Vector2.Distance(first.Position,second.Position),Is.GreaterThanOrEqualTo(CooperativeCoinGame.PickupSeparation-.001f),
                "Two pickups must remain separated.");
            float centerX=(game.ServePosition(-1).x+game.ServePosition(1).x)*.5f;
            Assert.That(first.Position.x,Is.LessThan(centerX));
            Assert.That(second.Position.x,Is.GreaterThan(centerX),"Spawn halves must alternate.");
            Assert.That(Mathf.Abs(first.Position.x-centerX),Is.InRange(.22f*game.HalfWidth-.01f,.60f*game.HalfWidth+.01f));
            Assert.That(first.Position.y,Is.GreaterThanOrEqualTo(Mathf.Max(game.ArenaFloor+game.NetHeight+.7f,game.MatchFloor+1.5f)-.001f));

            // Keep both slots occupied long enough to cover a full-capacity attempt.
            yield return new WaitForSecondsRealtime(1.6f);
            Assert.That(game.PickupCount,Is.LessThanOrEqualTo(CooperativeCoinGame.PickupSlotCount),
                "The game must never hold more than two pickup slots.");

            Assert.That(game.GetPickupSnapshot(firstSlot).Collectible,Is.True);
            Assert.That(game.GetPickupSnapshot(secondSlot).Collectible,Is.True);
            Vector2 direction=(second.Position-first.Position).normalized;
            MoveBall(first.Position-direction*1.3f);
            yield return TwoFrames();
            Assert.That(game.PickupCount,Is.EqualTo(2),"The preparation point must stay outside pickup range.");
            MoveBall(second.Position+direction*1.3f);
            yield return TwoFrames();
            Assert.That(game.PickupCount,Is.Zero,
                $"One swept frame must collect both intersections. First {first.Position}, second {second.Position}, body {ball.Body.position}");
            Assert.That(game.PendingSpell(-1),Is.EqualTo(second.Spell),
                "The last intersection in travel order replaces the first spell.");
            Assert.That(game.PendingSpell(1),Is.EqualTo(RallySpell.None));
            Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.None),"Collecting an item must not alter the ball in flight.");
        }

        [UnityTest]
        public IEnumerator AllThreeItemsWaitForTheirCollectorsNextRealReleaseAndKeepTheGlobalCap()
        {
            float ordinary=HitAndRelease(rightBarrier);
            foreach(var spell in new[] { RallySpell.Fireball,RallySpell.Timer,RallySpell.Light })
            {
                if(game.VersusState.LastHitter!=1) HitAndRelease(rightBarrier);
                Assert.That(game.TryCollectSpell(spell),Is.True);
                Assert.That(game.PendingSpell(1),Is.EqualTo(spell));
                Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.None));
                Assert.That(game.LightPhase,Is.EqualTo(RallyLightPhase.None),"Holding Light must not start its warning.");

                float opponentReturn=HitAndRelease(leftBarrier);
                Assert.That(opponentReturn,Is.EqualTo(ordinary).Within(.03f),"The opponent's return must be ordinary.");
                Assert.That(game.PendingSpell(1),Is.EqualTo(spell),"An opponent must not spend the collector's item.");
                Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.None));

                BeginHit(rightBarrier);
                Assert.That(game.PendingSpell(1),Is.EqualTo(spell),"Compression must retain the item until actual release.");
                Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.None));
                Assert.That(game.LightPhase,Is.EqualTo(RallyLightPhase.None));
                rightBarrier.Step(.25f);
                Assert.That(ball.IsInBarrierImpact,Is.False);
                float multiplier=spell==RallySpell.Fireball ? RallyVersusState.FireballMultiplier
                    : spell==RallySpell.Timer ? RallyVersusState.TimerMultiplier : 1f;
                Assert.That(ball.Body.linearVelocity.magnitude,Is.EqualTo(ordinary*multiplier).Within(.03f));
                Assert.That(game.PendingSpell(1),Is.EqualTo(RallySpell.None));
                Assert.That(game.ActiveSpell,Is.EqualTo(spell),"The released shot must retain its spell presentation.");
                Assert.That(game.SpellOwner,Is.EqualTo(1));
                if(spell==RallySpell.Light)
                    Assert.That(game.LightPhase,Is.EqualTo(RallyLightPhase.Warning));

                Assert.That(HitAndRelease(leftBarrier),Is.EqualTo(ordinary).Within(.03f));
                Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.None),"The following accepted return clears the previous shot's effect.");
            }
            Assert.That(game.TryCollectSpell(RallySpell.Fireball),Is.True);
            HitAndRelease(rightBarrier);
            float capped=HitAndRelease(leftBarrier,2f);
            Assert.That(capped,Is.EqualTo(ball.maximumSpeed).Within(.03f),
                "The existing maximumSpeed must cap a boosted hand return.");
            yield break;
        }

        [UnityTest]
        public IEnumerator PickupDuringCompressionAffectsThatReleaseAndCancellationDoesNotConsume()
        {
            float ordinary=HitAndRelease(leftBarrier);
            BeginHit(rightBarrier);
            Assert.That(game.VersusState.LastHitter,Is.EqualTo(1));
            Assert.That(game.TryActivateSpell(RallySpell.Fireball),Is.True,
                "A pickup collected during compression must be accepted.");
            rightBarrier.Step(.25f);
            Assert.That(ball.IsInBarrierImpact,Is.False);
            Assert.That(ball.Body.linearVelocity.magnitude,Is.EqualTo(ordinary*RallyVersusState.FireballMultiplier).Within(.03f));
            Assert.That(game.PendingSpell(1),Is.EqualTo(RallySpell.None));
            Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.Fireball));

            BeginHit(leftBarrier);
            Assert.That(game.TryActivateSpell(RallySpell.Timer),Is.True);
            leftBarrier.CancelImpact(false);
            Assert.That(ball.IsInBarrierImpact,Is.False);
            Assert.That(ball.Body.linearVelocity,Is.EqualTo(Vector2.zero));
            Assert.That(game.PendingSpell(-1),Is.EqualTo(RallySpell.Timer),
                "A cancelled zero-speed release must not spend the pending spell.");
            Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.None));
            Assert.That(HitAndRelease(rightBarrier),Is.EqualTo(ordinary).Within(.03f));
            Assert.That(game.PendingSpell(-1),Is.EqualTo(RallySpell.Timer));
            float slowed=HitAndRelease(leftBarrier);
            Assert.That(slowed,Is.EqualTo(ordinary*RallyVersusState.TimerMultiplier).Within(.03f));
            Assert.That(game.PendingSpell(-1),Is.EqualTo(RallySpell.None));
            Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.Timer));
            yield break;
        }

        [UnityTest]
        public IEnumerator BothPlayersKeepTheirOwnItemsThroughSuspensionAndLoseThemAtThePoint()
        {
            HitAndRelease(rightBarrier);
            Assert.That(game.TryCollectSpell(RallySpell.Light),Is.True);
            HitAndRelease(leftBarrier);
            Assert.That(game.TryCollectSpell(RallySpell.Fireball),Is.True);
            Assert.That(game.TryCollectSpell(RallySpell.Timer),Is.True);
            MoveBall(new Vector2(-6,3));
            foreach(var blocker in new[] { 0,1,2 })
            {
                if(blocker==0) game.SetPaused(true);
                else if(blocker==1) game.SetInputReady(false);
                else game.ExternalOverlayOpen=true;
                yield return new WaitForSecondsRealtime(.2f);
                Assert.That(game.PendingSpell(-1),Is.EqualTo(RallySpell.Timer),"A replacement belongs only to its collector.");
                Assert.That(game.PendingSpell(1),Is.EqualTo(RallySpell.Light));
                Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.None));
                Assert.That(game.LightPhase,Is.EqualTo(RallyLightPhase.None),"A held Light has no running clock.");
                Assert.That(game.BallPresentationVisible,Is.True);
                if(blocker==0) game.SetPaused(false);
                else if(blocker==1) game.SetInputReady(true);
                else game.ExternalOverlayOpen=false;
            }
            Assert.That(game.ApplyPendingSpellToRebound(Vector2.zero,-1),Is.EqualTo(Vector2.zero));
            Assert.That(game.PendingSpell(-1),Is.EqualTo(RallySpell.Timer),"A zero-speed cancellation must not spend a held item.");
            MoveBall(new Vector2(-3,game.MatchFloor+1));
            yield return TwoFrames();
            MoveBall(new Vector2(-3,game.MatchFloor-.4f));
            yield return TwoFrames();
            Assert.That(game.VersusState.RightScore,Is.EqualTo(1));
            Assert.That(game.PendingSpell(-1),Is.EqualTo(RallySpell.None));
            Assert.That(game.PendingSpell(1),Is.EqualTo(RallySpell.None));
            Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.None));
        }

        [UnityTest]
        public IEnumerator LightRendersAWarningHidesEveryVisualThenCancelsAtTheFloor()
        {
            HitAndRelease(leftBarrier);
            MoveBall(new Vector2(-5,3));
            ArmLightShot();
            yield return TwoFrames();
            Assert.That(game.LightPhase,Is.EqualTo(RallyLightPhase.Warning));
            Assert.That(game.BallPresentationVisible,Is.True);
            var ballView=ball.GetComponent<SpriteRenderer>();
            Assert.That(ballView.enabled,Is.True);
            var corona=FindRenderer("Cooperative court/Ball corona");
            Assert.That(corona.enabled,Is.True);
            var spellRoot=game.transform.Find("Cooperative court/Ball spell");
            var ring=spellRoot.Find("Light warning 0").GetComponent<SpriteRenderer>();
            Assert.That(ring.enabled,Is.True,"The contracting warning ring must be rendered before hiding.");

            yield return WaitFor(() => game.LightPhase==RallyLightPhase.Hidden,2,"The warning must hide the ball.");
            Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.Light));
            Assert.That(game.LightPhaseSecondsRemaining,Is.LessThanOrEqualTo(RallyVersusState.LightHiddenSeconds));
            Assert.That(game.LightPhaseSecondsRemaining,Is.GreaterThan(0));
            Assert.That(game.BallPresentationVisible,Is.False);
            Assert.That(ballView.enabled,Is.False,"The ivory core must hide with the spell.");
            Assert.That(corona.enabled,Is.False,"The corona must hide with the core.");
            Assert.That(FindRenderer("Cooperative court/Ball light").enabled,Is.False);
            Assert.That(spellRoot.gameObject.activeInHierarchy,Is.False,"All attached spell artwork must hide together.");
            foreach(var renderer in spellRoot.GetComponentsInChildren<SpriteRenderer>(true))
                Assert.That(renderer.gameObject.activeInHierarchy,Is.False);

            MoveBall(new Vector2(-6.5f,game.MatchFloor+1.35f));
            yield return TwoFrames();
            Assert.That(game.LightPhase,Is.EqualTo(RallyLightPhase.None),"Entering the floor safety zone must cancel Light.");
            Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.None));
            Assert.That(game.BallPresentationVisible,Is.True);
            Assert.That(ballView.enabled,Is.True);

            MoveBall(new Vector2(-5,3));
            yield return TwoFrames();
            ArmLightShot();
            yield return null;
            Assert.That(game.LightPhase,Is.Not.EqualTo(RallyLightPhase.None));
            BeginHit(leftBarrier);
            Assert.That(game.LightPhase,Is.EqualTo(RallyLightPhase.None),"Any hand-barrier contact must cancel Light.");
            Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.None));
            Assert.That(game.BallPresentationVisible,Is.True);
        }

        [UnityTest]
        public IEnumerator LightSuspensionFreezesClocksAndReplaysTheWarningBeforeRemainingHidden()
        {
            HitAndRelease(leftBarrier);
            MoveBall(new Vector2(-5,3));
            ArmLightShot();
            yield return TwoFrames();
            Assert.That(game.LightPhase,Is.EqualTo(RallyLightPhase.Warning));
            float warning=game.LightPhaseSecondsRemaining;
            game.ExternalOverlayOpen=true;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(game.LightPhase,Is.EqualTo(RallyLightPhase.Warning));
            Assert.That(game.LightPhaseSecondsRemaining,Is.EqualTo(warning).Within(.01f));
            Assert.That(game.BallPresentationVisible,Is.True);
            game.ExternalOverlayOpen=false;
            game.SetInputReady(false);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(game.LightPhase,Is.EqualTo(RallyLightPhase.Warning));
            Assert.That(game.LightPhaseSecondsRemaining,Is.EqualTo(warning).Within(.01f));
            Assert.That(game.BallPresentationVisible,Is.True);
            game.SetInputReady(true);
            game.SetPaused(true);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(game.LightPhase,Is.EqualTo(RallyLightPhase.Warning));
            Assert.That(game.LightPhaseSecondsRemaining,Is.EqualTo(warning).Within(.01f));
            Assert.That(game.BallPresentationVisible,Is.True,"Suspension must force the hidden ball visible.");
            game.SetPaused(false);

            yield return WaitFor(() => game.LightPhase==RallyLightPhase.Hidden,2,"Light must hide after its warning.");
            Assert.That(game.BallPresentationVisible,Is.False);
            float hidden=game.LightPhaseSecondsRemaining;
            game.SetPaused(true);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(game.LightPhase,Is.EqualTo(RallyLightPhase.Hidden));
            Assert.That(game.LightPhaseSecondsRemaining,Is.EqualTo(hidden).Within(.01f),"Pause must freeze the hidden clock.");
            Assert.That(game.BallPresentationVisible,Is.True);

            game.SetPaused(false);
            yield return TwoFrames();
            Assert.That(game.LightPhase,Is.EqualTo(RallyLightPhase.Warning),
                "Resuming a hidden phase must replay the full warning first.");
            Assert.That(game.LightPhaseSecondsRemaining,Is.EqualTo(RallyVersusState.LightWarningSeconds).Within(.1f));
            Assert.That(game.BallPresentationVisible,Is.True);
            yield return WaitFor(() => game.LightPhase==RallyLightPhase.Hidden,2,"The replayed warning must finish.");

            game.RestartGame();
            Assert.That(game.LightPhase,Is.EqualTo(RallyLightPhase.None),"Restart must clear and restore Light immediately.");
            Assert.That(game.ActiveSpell,Is.EqualTo(RallySpell.None));
            Assert.That(game.BallPresentationVisible,Is.True);
            Assert.That(ball.GetComponent<SpriteRenderer>().enabled,Is.True);
        }

        float HitAndRelease(ElasticBarrier barrier,float handMotion=0)
        {
            barrier.ResetSpring();
            if(handMotion>0) barrier.SetHandMotion(Vector2.up*handMotion,Vector2.up*handMotion,Time.unscaledTimeAsDouble);
            MoveBall((Vector2)barrier.transform.position+Vector2.up);
            ball.Body.linearVelocity=Vector2.down*8;
            for(int i=0;i<20 && !ball.IsInBarrierImpact;i++) scene.GetPhysicsScene2D().Simulate(.02f);
            Assert.That(ball.IsInBarrierImpact,Is.True,"The test must enter a real barrier collision.");
            barrier.Step(.25f);
            Assert.That(ball.IsInBarrierImpact,Is.False);
            Assert.That(ball.Body.simulated,Is.True);
            return ball.Body.linearVelocity.magnitude;
        }

        void ArmLightShot()
        {
            Assert.That(game.TryCollectSpell(RallySpell.Light),Is.True);
            int owner=game.VersusState.LastHitter;
            // These cases isolate the post-release Light safety/pause rules;
            // AllThreeItemsWaitForTheirCollectorsNextRealRelease covers real capture and release.
            game.ApplyPendingSpellToRebound(Vector2.up*8,owner);
            Assert.That(game.PendingSpell(owner),Is.EqualTo(RallySpell.None));
        }

        void BeginHit(ElasticBarrier barrier)
        {
            barrier.ResetSpring();
            MoveBall((Vector2)barrier.transform.position+Vector2.up);
            ball.Body.linearVelocity=Vector2.down*8;
            for(int i=0;i<20 && !ball.IsInBarrierImpact;i++) scene.GetPhysicsScene2D().Simulate(.02f);
            Assert.That(ball.IsInBarrierImpact,Is.True,"The test must enter a real barrier collision.");
        }

        RallyPickupSnapshot FindPickup(out int slot,int excluded=-99)
        {
            for(int i=0;i<CooperativeCoinGame.PickupSlotCount;i++)
            {
                if(i==excluded) continue;
                var snapshot=game.GetPickupSnapshot(i);
                if(!snapshot.Visible) continue;
                slot=i;
                return snapshot;
            }
            slot=-1;
            return default;
        }

        SpriteRenderer FindRenderer(string path) => game.transform.Find(path).GetComponent<SpriteRenderer>();

        void MoveBall(Vector2 position)
        {
            ball.Body.position=position; ball.transform.position=position;
            ball.Body.linearVelocity=Vector2.zero;
            Physics2D.SyncTransforms();
        }

        SpriteRenderer Coin(int index) => game.CollectionTargetRenderer(index);
        static bool IsVisible(SpriteRenderer renderer) => renderer.enabled && renderer.gameObject.activeInHierarchy && renderer.color.a>.001f;

        static IEnumerator TwoFrames()
        {
            // EditMode test-runner ticks may resume an enumerator before the next player
            // frame. Wait for real Update/LateUpdate frames before inspecting their state.
            int frame=Time.frameCount;
            double deadline=Time.realtimeSinceStartupAsDouble+3;
            while(Time.frameCount<frame+2 && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
            Assert.That(Time.frameCount,Is.GreaterThanOrEqualTo(frame+2),"The player loop must advance two frames.");
        }
        static IEnumerator WaitFor(Func<bool> condition,float timeout,string message)
        {
            double deadline=Time.realtimeSinceStartupAsDouble+timeout;
            while(!condition() && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
            Assert.That(condition(),Is.True,message);
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if(Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            game=null; ball=null; leftBarrier=rightBarrier=null;
        }
    }
}
