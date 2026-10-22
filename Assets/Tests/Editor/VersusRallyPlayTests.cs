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
            Assert.That(game.ItemVisible,Is.False);
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
        public IEnumerator ModeSwitchClearsItemsAndEffectsAndRestoresLitStars()
        {
            HitAndRelease(leftBarrier);
            MoveBall(new Vector2(-6,3));
            yield return WaitFor(() => game.ItemVisible,3,"A returned rally should introduce an item.");
            game.SetPaused(true);
            MoveBall(Coin(4).transform.position);
            yield return TwoFrames();
            for(int i=0;i<CooperativeCoinGame.CoinsPerRound;i++)
            {
                Assert.That(IsVisible(Coin(i)),Is.False,"Versus must hide collection-mode stars.");
                Assert.That(IsVisible(Coin(i).transform.Find("Warm halo").GetComponent<SpriteRenderer>()),Is.False);
                Assert.That(game.CoinVisibility(i),Is.Zero);
            }
            // Set a pending effect independently of its artwork so both reset paths are exercised.
            Assert.That(game.VersusState.CollectItem(),Is.True);
            Assert.That(game.VersusState.SoftenedSide,Is.EqualTo(1));
            game.SetMode(RallyMode.StarCollection);
            Assert.That(game.ItemVisible,Is.False);
            Assert.That(game.VersusState.SoftenedSide,Is.Zero);
            Assert.That(game.VersusState.LastHitter,Is.Zero);
            Assert.That(game.VersusState.Winner,Is.Zero);
            Assert.That(game.TeamScore,Is.Zero);
            game.SetPaused(true);
            MoveBall(Coin(4).transform.position);
            yield return TwoFrames();
            Assert.That(IsVisible(Coin(4)),Is.True,$"Switching back must restore stars inside the ball's light. Body {ball.Body.position}, transform {ball.transform.position}, coin {Coin(4).transform.position}, visibility {game.CoinVisibility(4)}, mode {game.Mode}, light {game.BallLightEnabled}");
            Assert.That(game.CoinVisibility(4),Is.EqualTo(1).Within(.001f));

            game.SetMode(RallyMode.Versus);
            Assert.That(game.ItemVisible,Is.False);
            Assert.That(game.WaitingToServe,Is.True);
            Assert.That(game.VersusState.LeftScore+game.VersusState.RightScore,Is.Zero);
            yield return WaitFor(() => game.CanAdvanceBall,3,"Switching modes must start a fresh match.");
            HitAndRelease(leftBarrier);
            Assert.That(game.VersusState.CollectItem(),Is.True);
            game.RestartGame();
            Assert.That(game.VersusState.SoftenedSide,Is.Zero);
            Assert.That(game.VersusState.LastHitter,Is.Zero);
            Assert.That(game.ItemVisible,Is.False);
        }

        [UnityTest]
        public IEnumerator ItemClockFreezesAndSweptPickupWeakensExactlyOneRealOpponentReturn()
        {
            float beforeFirstReturn=game.ItemSecondsUntilSpawn;
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(game.ItemVisible,Is.False);
            Assert.That(game.ItemSecondsUntilSpawn,Is.EqualTo(beforeFirstReturn).Within(.001f),
                "The serve cannot start item spawns before anyone returns the ball.");
            float ordinarySpeed=HitAndRelease(leftBarrier);
            Assert.That(game.VersusState.LastHitter,Is.EqualTo(-1));
            MoveBall(new Vector2(-6,3));
            float remaining=game.ItemSecondsUntilSpawn;
            game.SetPaused(true);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(game.ItemSecondsUntilSpawn,Is.EqualTo(remaining).Within(.001f));
            game.SetInputReady(false); game.SetPaused(false);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(game.ItemSecondsUntilSpawn,Is.EqualTo(remaining).Within(.001f));
            game.SetInputReady(true);
            yield return WaitFor(() => game.ItemVisible,3,"The first item must appear after a real return.");

            Assert.That(game.ItemCollectible,Is.False,"New items must announce themselves before pickup.");
            game.SetPaused(true);
            Vector2 warningPosition=game.ItemPosition;
            yield return new WaitForSecondsRealtime(.7f);
            Assert.That(game.ItemCollectible,Is.False,"Pause must freeze the warning age.");
            Assert.That(game.ItemPosition,Is.EqualTo(warningPosition));
            game.SetInputReady(false); game.SetPaused(false);
            yield return new WaitForSecondsRealtime(.2f);
            Assert.That(game.ItemCollectible,Is.False,"Tracking loss must also freeze the warning age.");
            Assert.That(game.ItemPosition,Is.EqualTo(warningPosition));
            game.SetInputReady(true);
            yield return WaitFor(() => game.ItemCollectible,1.5f,"An unpaused item must become collectible.");

            Vector2 target=game.ItemPosition;
            MoveBall(target+Vector2.left*1.2f);
            yield return TwoFrames();
            Assert.That(game.ItemVisible,Is.True,"The first sweep endpoint must be outside pickup range.");
            MoveBall(target+Vector2.right*1.2f);
            yield return TwoFrames();
            Assert.That(game.ItemVisible,Is.False,$"A fast crossing must collect even with both endpoints outside the item. Target {target}, item {game.ItemPosition}, body {ball.Body.position}, transform {ball.transform.position}, last {game.VersusState.LastHitter}, collectible {game.ItemCollectible}, advance {game.CanAdvanceBall}");
            Assert.That(game.VersusState.SoftenedSide,Is.EqualTo(1),"The last left return must weaken the right player's next return.");

            float weakenedSpeed=HitAndRelease(rightBarrier);
            Assert.That(game.VersusState.LastHitter,Is.EqualTo(1));
            Assert.That(game.VersusState.SoftenedSide,Is.Zero);
            Assert.That(weakenedSpeed,Is.EqualTo(ordinarySpeed*RallyVersusState.SoftReturnMultiplier).Within(.03f));
            yield return new WaitForSecondsRealtime(.18f);
            float restoredSpeed=HitAndRelease(rightBarrier);
            Assert.That(restoredSpeed,Is.EqualTo(ordinarySpeed).Within(.03f),"The same opponent's following return must regain normal strength.");
        }

        float HitAndRelease(ElasticBarrier barrier)
        {
            barrier.ResetSpring();
            MoveBall((Vector2)barrier.transform.position+Vector2.up);
            ball.Body.linearVelocity=Vector2.down*8;
            for(int i=0;i<20 && !ball.IsInBarrierImpact;i++) scene.GetPhysicsScene2D().Simulate(.02f);
            Assert.That(ball.IsInBarrierImpact,Is.True,"The test must enter a real barrier collision.");
            barrier.Step(.25f);
            Assert.That(ball.IsInBarrierImpact,Is.False);
            Assert.That(ball.Body.simulated,Is.True);
            return ball.Body.linearVelocity.magnitude;
        }

        void MoveBall(Vector2 position)
        {
            ball.Body.position=position; ball.transform.position=position;
            ball.Body.linearVelocity=Vector2.zero;
            Physics2D.SyncTransforms();
        }

        SpriteRenderer Coin(int index) => game.transform.Find("Cooperative court/Shared coin "+(index+1)).GetComponent<SpriteRenderer>();
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
