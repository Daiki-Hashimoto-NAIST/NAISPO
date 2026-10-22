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
    public sealed class CooperativeBarrierImpactTests
    {
        Scene scene;
        CooperativeCoinGame game;
        AirHockeyBall ball;
        ElasticBarrier barrier;

        [UnitySetUp]
        public IEnumerator CreateCourt()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            yield return new EnterPlayMode();
            scene=SceneManager.CreateScene("Barrier impact test",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            var view=Create("Camera").AddComponent<Camera>();
            view.orthographic=true; view.orthographicSize=5; view.aspect=16f/9f;
            ball=Create("Ball").AddComponent<AirHockeyBall>();
            ball.GetComponent<CircleCollider2D>().radius=.22f;
            ball.Body.interpolation=RigidbodyInterpolation2D.None;
            game=Create("Game").AddComponent<CooperativeCoinGame>(); game.Configure(ball,view);
            double deadline=Time.realtimeSinceStartupAsDouble+3;
            while(game.WaitingToServe && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
            Assert.IsTrue(game.CanAdvanceBall);

            var wall=Create("Hand barrier"); wall.transform.position=new Vector2(-3,-1.8f);
            wall.AddComponent<Rigidbody2D>().bodyType=RigidbodyType2D.Kinematic;
            var edge=wall.AddComponent<EdgeCollider2D>();
            var line=wall.AddComponent<LineRenderer>(); line.useWorldSpace=false;
            barrier=wall.AddComponent<ElasticBarrier>(); barrier.Initialize(edge,line);
            barrier.SetSpan(4,1.5f,.16f,true);
            ball.Body.gravityScale=0; ball.Body.linearDamping=0;
        }

        GameObject Create(string name)
        {
            var result=new GameObject(name); SceneManager.MoveGameObjectToScene(result,scene); return result;
        }

        void Hit(float localX=0,bool underside=false)
        {
            Vector2 position=(Vector2)barrier.transform.position+new Vector2(localX,underside ? -1 : 1);
            ball.Body.position=position; ball.transform.position=position;
            ball.Body.linearVelocity=Vector2.up*(underside ? 8 : -8);
            Physics2D.SyncTransforms();
            for(int i=0;i<20 && !ball.IsInBarrierImpact;i++) scene.GetPhysicsScene2D().Simulate(.02f);
            Assert.IsTrue(ball.IsInBarrierImpact,"An actual physics collision must enter the compression phase.");
            Assert.IsTrue(barrier.IsHoldingBall);
            Assert.IsFalse(ball.Body.simulated,"Normal motion must wait until the spring releases.");
        }

        [UnityTest]
        public IEnumerator ContactCompressesHoldsThenReleasesWithAWideBarrierSoftReturn()
        {
            Hit();
            float startY=ball.Body.position.y;
            barrier.Step(.04f);
            Assert.That(ball.Body.position.y,Is.LessThan(startY-.12f));
            Assert.That(barrier.Deflection,Is.LessThan(-.12f));
            barrier.Step(.06f);
            float deepest=ball.Body.position.y;
            barrier.Step(.03f);
            Assert.That(ball.Body.position.y,Is.EqualTo(deepest).Within(.001f),"The loaded spring should briefly linger.");
            Assert.IsTrue(ball.IsInBarrierImpact);
            barrier.Step(.04f);
            Assert.That(ball.Body.position.y,Is.GreaterThan(deepest+.1f),"The ball must follow the returning barrier.");
            barrier.Step(.04f);
            Assert.IsFalse(ball.IsInBarrierImpact);
            Assert.IsTrue(ball.Body.simulated);
            Assert.That(ball.Body.linearVelocity.y,Is.GreaterThan(0));
            Assert.That(ball.Body.linearVelocity.magnitude,Is.InRange(game.BallSpeed*.5f,game.BallSpeed*.65f),"Wide hands should absorb a substantial part of the launch speed.");
            Vector2 released=ball.Body.position;
            for(int i=0;i<6;i++) { barrier.Step(.02f); scene.GetPhysicsScene2D().Simulate(.02f); }
            Assert.IsFalse(ball.IsInBarrierImpact,"Release must not immediately capture the same ball again.");
            Assert.That(ball.Body.position.y,Is.GreaterThan(released.y+.4f));
            yield break;
        }

        [UnityTest]
        public IEnumerator RealHandWidthsKeepDistinctReturnSpeedsAfterReleaseAndTheNextPhysicsTick()
        {
            foreach(float baseSpeed in new[] {6.5f,10.5f})
            {
                game.BallSpeed=baseSpeed;
                float narrowSpeed=0;
                foreach(float meters in new[] {.35f,1.45f})
                {
                    game.RestartGame();
                    double deadline=Time.realtimeSinceStartupAsDouble+3;
                    while(game.WaitingToServe && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
                    Assert.IsTrue(game.CanAdvanceBall);
                    barrier.SetSpan(4,meters,.16f,true);
                    Hit(); barrier.Step(.25f);
                    Assert.IsFalse(ball.IsInBarrierImpact);
                    float releasedSpeed=ball.Body.linearVelocity.magnitude;
                    yield return new WaitForFixedUpdate();
                    Assert.That(ball.Body.linearVelocity.magnitude,Is.EqualTo(releasedSpeed).Within(.01f),"The speed limiter must not erase hand-width tuning after release.");
                    if(meters<1) narrowSpeed=releasedSpeed;
                    else
                    {
                        Assert.That(narrowSpeed,Is.GreaterThan(releasedSpeed*1.75f),"Realistic narrow and wide spans must feel clearly different.");
                        Assert.That(releasedSpeed,Is.GreaterThan(baseSpeed*.55f),"A wide barrier must still return the ball.");
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator PauseAndTrackingWaitFreezeTheImpactWithoutPrematurePhysicsResume()
        {
            Hit(); barrier.Step(.04f);
            Vector2 held=ball.Body.position;
            game.SetPaused(true); barrier.Step(.5f);
            Assert.That(ball.Body.position,Is.EqualTo(held));
            Assert.IsTrue(ball.IsInBarrierImpact);
            game.SetPaused(false);
            Assert.IsFalse(ball.Body.simulated,"Unpausing must preserve an unfinished impact.");
            game.SetInputReady(false); barrier.Step(.5f);
            Assert.That(ball.Body.position,Is.EqualTo(held));
            Assert.IsTrue(ball.IsInBarrierImpact);
            game.SetInputReady(true);
            Assert.IsFalse(ball.Body.simulated);
            barrier.Step(.2f);
            Assert.IsFalse(ball.IsInBarrierImpact);
            Assert.IsTrue(ball.Body.simulated);
            yield break;
        }

        [UnityTest]
        public IEnumerator RestartCancelsTheOldContactBeforePlacingTheNewServe()
        {
            Hit(); barrier.Step(.06f);
            game.RestartGame();
            Vector2 spawn=ball.Body.position;
            Assert.IsFalse(ball.IsInBarrierImpact);
            Assert.IsFalse(barrier.IsHoldingBall);
            Assert.IsTrue(game.WaitingToServe);
            Assert.IsFalse(ball.Body.simulated);
            barrier.Step(.5f);
            Assert.That(ball.Body.position,Is.EqualTo(spawn));
            Assert.That(ball.Body.linearVelocity,Is.EqualTo(Vector2.zero));
            Assert.That(barrier.Deflection,Is.Zero);
            yield break;
        }

        [UnityTest]
        public IEnumerator NearEndHitBendsAtTheContactAndDisablingBarrierReleasesOnlyOnce()
        {
            Hit(1.45f); barrier.Step(.1f);
            int deepest=0;
            for(int i=1;i<barrier.Shape.Length;i++) if(barrier.Shape[i].y<barrier.Shape[deepest].y) deepest=i;
            Assert.That(deepest,Is.GreaterThan(18),"The deformation should follow an off-center contact.");
            Assert.That(barrier.Shape[0].y,Is.Zero);
            Assert.That(barrier.Shape[barrier.Shape.Length-1].y,Is.Zero);
            barrier.enabled=false;
            Assert.IsFalse(ball.IsInBarrierImpact);
            Assert.IsFalse(barrier.IsHoldingBall);
            Assert.IsTrue(ball.Body.simulated);
            Vector2 velocity=ball.Body.linearVelocity;
            barrier.ResetSpring(); barrier.Step(.2f);
            Assert.That(ball.Body.linearVelocity,Is.EqualTo(velocity),"Repeated cleanup must never apply another launch.");
            yield break;
        }

        [UnityTest]
        public IEnumerator UndersideContactLoadsUpwardAndReleasesDownward()
        {
            Hit(0,true); float startY=ball.Body.position.y;
            barrier.Step(.1f);
            Assert.That(barrier.Deflection,Is.GreaterThan(.1f));
            Assert.That(ball.Body.position.y,Is.GreaterThan(startY+.1f));
            barrier.Step(.11f);
            Assert.IsFalse(ball.IsInBarrierImpact);
            Assert.That(ball.Body.linearVelocity.y,Is.LessThan(-1));
            Assert.That(ball.Body.position.y,Is.LessThan(barrier.transform.position.y));
            yield break;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if(Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            game=null; ball=null; barrier=null;
        }
    }
}
