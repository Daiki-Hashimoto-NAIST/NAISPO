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
            HitWithVelocity(Vector2.up*(underside ? 8 : -8),localX,underside);
        }

        void HitWithVelocity(Vector2 incoming,float localX=0,bool underside=false)
        {
            Vector2 position=barrier.transform.TransformPoint(new Vector2(localX,underside ? -1 : 1));
            ball.Body.position=position; ball.transform.position=position;
            ball.Body.linearVelocity=incoming;
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
                        Assert.That(narrowSpeed,Is.GreaterThan(releasedSpeed*1.4f),"Narrow hands must still feel stronger, including when the safety limit caps a fast incoming ball.");
                        Assert.That(releasedSpeed,Is.GreaterThan(baseSpeed*.5f),"A wide barrier must still return the ball.");
                        Assert.That(narrowSpeed,Is.LessThanOrEqualTo(baseSpeed*1.14f+.01f));
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator HorizontalBarrierReflectsVerticalIncidenceUpWithoutPassingToTheOtherCourt()
        {
            Hit(); barrier.Step(.25f);
            Assert.That(ball.Body.linearVelocity.x,Is.EqualTo(0).Within(.001f));
            Assert.That(ball.Body.linearVelocity.y,Is.EqualTo(8*.58f).Within(.03f),"The collision solver must not discard the incoming speed before the custom bounce.");
            yield break;
        }

        [UnityTest]
        public IEnumerator HorizontalBarrierPreservesTheAngleOfAnObliqueIncomingBall()
        {
            HitWithVelocity(new Vector2(-3,-6)); barrier.Step(.25f);
            Assert.That(ball.Body.linearVelocity.x,Is.LessThan(0),"A ball arriving toward the outer wall must not be redirected to the center.");
            Assert.That(Vector2.Angle(ball.Body.linearVelocity,new Vector2(-3,6)),Is.LessThan(.05f));
            yield break;
        }

        [UnityTest]
        public IEnumerator TiltedStationaryBarrierReflectsAroundItsOwnNormal()
        {
            barrier.transform.rotation=Quaternion.Euler(0,0,-26.56505f);
            Hit(); barrier.Step(.25f);
            Assert.That(ball.Body.linearVelocity.normalized.x,Is.EqualTo(.8f).Within(.002f));
            Assert.That(ball.Body.linearVelocity.normalized.y,Is.EqualTo(.6f).Within(.002f));
            yield break;
        }

        [UnityTest]
        public IEnumerator ContactAboveASaggingBarrierStillUsesItsFrontSide()
        {
            barrier.Kick(12,-1); barrier.Step(.04f);
            Assert.That(barrier.Deflection,Is.LessThan(-.4f),"Exercise a visible surface below the undeformed hand line.");
            Hit(); barrier.Step(.25f);
            Assert.That(ball.Body.linearVelocity.x,Is.EqualTo(0).Within(.001f));
            Assert.That(ball.Body.linearVelocity.y,Is.GreaterThan(1),"A front-side hit must not become a downward return just because the string is sagging.");
            yield break;
        }

        [UnityTest]
        public IEnumerator ContactBelowAnUpwardBentBarrierStillUsesItsBackSide()
        {
            barrier.Kick(12,1); barrier.Step(.04f);
            Assert.That(barrier.Deflection,Is.GreaterThan(.4f));
            Hit(0,true); barrier.Step(.25f);
            Assert.That(ball.Body.linearVelocity.x,Is.EqualTo(0).Within(.001f));
            Assert.That(ball.Body.linearVelocity.y,Is.LessThan(-1),"A genuine underside hit stays underneath even when the bent surface is above the hand line.");
            yield break;
        }

        [UnityTest]
        public IEnumerator OffCenterContactWithASaggingBarrierUsesTheStraightHandLineForReflection()
        {
            barrier.Kick(12,-1); barrier.Step(.04f);
            Assert.That(barrier.Shape[18].y-barrier.Shape[17].y,Is.GreaterThan(.04f),"Hit a sloped section of the deformed surface.");
            Hit(1); barrier.Step(.25f);
            Assert.That(ball.Body.linearVelocity.x,Is.EqualTo(0).Within(.005f),"The curve's local slope must not bend the launch away from the hand-to-hand normal.");
            Assert.That(ball.Body.linearVelocity.y,Is.EqualTo(8*.58f).Within(.03f));
            yield break;
        }

        [UnityTest]
        public IEnumerator MovePositionDuringContactPreservesTheIncomingWorldDirection()
        {
            var handBody=barrier.GetComponent<Rigidbody2D>();
            Vector2 position=(Vector2)barrier.transform.position+Vector2.up;
            ball.Body.position=position; ball.transform.position=position;
            ball.Body.linearVelocity=new Vector2(-3,-6);
            Physics2D.SyncTransforms();
            for(int i=0;i<20 && !ball.IsInBarrierImpact;i++)
            {
                handBody.MovePosition(handBody.position+new Vector2(.2f,.05f));
                scene.GetPhysicsScene2D().Simulate(.02f);
            }
            Assert.IsTrue(ball.IsInBarrierImpact,"Exercise the same queued movement used by live hand tracking.");
            handBody.linearVelocity=Vector2.zero;
            barrier.Step(.25f);
            Assert.That(Vector2.Angle(ball.Body.linearVelocity,new Vector2(-3,6)),Is.LessThan(.1f),"Forward hand translation must not reverse the ball's incoming horizontal direction.");
            yield break;
        }

        [UnityTest]
        public IEnumerator ReleaseUsesThePoseAppliedByPhysicsThatFrame()
        {
            Hit(); barrier.Step(.19f);
            var handBody=barrier.GetComponent<Rigidbody2D>();
            Vector2 destination=handBody.position+new Vector2(.3f,.6f);
            handBody.MovePosition(destination);
            handBody.MoveRotation(-26.56505f);
            // Production ordering: hand commands, barrier FixedUpdate, physics,
            // then LateUpdate. The ball must follow the last commanded hand pose.
            barrier.SendMessage("FixedUpdate");
            scene.GetPhysicsScene2D().Simulate(.02f);
            barrier.SendMessage("LateUpdate");
            Assert.IsFalse(ball.IsInBarrierImpact);
            Assert.That(Vector2.Distance(handBody.position,destination),Is.LessThan(.001f));
            Vector2 local=barrier.transform.InverseTransformPoint(ball.Body.position);
            Assert.That(local.y,Is.GreaterThan(.3f),"The final hand movement must not pass through a ball released at the previous pose.");
            Assert.That(ball.Body.linearVelocity.normalized.x,Is.EqualTo(.8f).Within(.002f));
            Assert.That(ball.Body.linearVelocity.normalized.y,Is.EqualTo(.6f).Within(.002f));
            yield break;
        }

        [UnityTest]
        public IEnumerator TurningTheBarrierDuringCompressionChangesTheReleaseDirection()
        {
            Hit(); barrier.Step(.07f);
            barrier.transform.rotation=Quaternion.Euler(0,0,-26.56505f);
            Physics2D.SyncTransforms();
            barrier.Step(.18f);
            Assert.IsFalse(ball.IsInBarrierImpact);
            Assert.That(ball.Body.linearVelocity.normalized.x,Is.EqualTo(.8f).Within(.002f));
            Assert.That(ball.Body.linearVelocity.normalized.y,Is.EqualTo(.6f).Within(.002f));
            yield break;
        }

        [UnityTest]
        public IEnumerator FastMovingBarrierCannotAddMoreThanTheControlledTwentyPercentBoost()
        {
            barrier.SetSpan(4,.3f,.16f,true);
            barrier.SetHandMotion(Vector2.up*2,Vector2.up*2,Time.unscaledTimeAsDouble);
            var handBody=barrier.GetComponent<Rigidbody2D>();
            handBody.linearVelocity=Vector2.up*6;
            Hit(); handBody.linearVelocity=Vector2.zero;
            barrier.Step(.25f);
            float returned=ball.Body.linearVelocity.magnitude;
            Assert.That(returned,Is.EqualTo(8*1.14f*1.2f).Within(.03f),"Kinematic collision impulses must not add an uncontrolled second hand-speed bonus.");
            Assert.That(ball.Body.linearVelocity.x,Is.EqualTo(0).Within(.001f));
            yield return new WaitForFixedUpdate();
            Assert.That(ball.Body.linearVelocity.magnitude,Is.EqualTo(returned).Within(.01f),"The global limiter must preserve the intended small hand boost.");
        }

        [UnityTest]
        public IEnumerator ContinuingTheHandMovementCannotAddAnotherBoostOnRecontactOrWhileTouching()
        {
            var handBody=barrier.GetComponent<Rigidbody2D>();
            var edge=barrier.GetComponent<EdgeCollider2D>();
            foreach(bool useHandBoost in new[] {false,true})
            {
                handBody.linearVelocity=Vector2.zero;
                game.RestartGame();
                double deadline=Time.realtimeSinceStartupAsDouble+3;
                while(game.WaitingToServe && Time.realtimeSinceStartupAsDouble<deadline) yield return null;
                Assert.IsTrue(game.CanAdvanceBall);
                barrier.transform.position=new Vector2(-3,-1.8f);
                handBody.position=barrier.transform.position;
                barrier.SetSpan(4,1.5f,.16f,true);
                if(useHandBoost) barrier.SetHandMotion(Vector2.up*2,Vector2.up*2,Time.unscaledTimeAsDouble);
                handBody.linearVelocity=Vector2.up*6;
                Hit(); barrier.Step(.25f);
                float releasedSpeed=ball.Body.linearVelocity.magnitude;
                Assert.That(releasedSpeed,Is.EqualTo(8*.58f*(useHandBoost ? 1.2f : 1)).Within(.03f));

                // The wide barrier returns slower than the moving hands, which catch
                // it during the same-contact cooldown and remain in contact afterward.
                float startY=handBody.position.y;
                int touchingSteps=0;
                for(int step=0;step<12;step++)
                {
                    scene.GetPhysicsScene2D().Simulate(.01f);
                    if(ball.Body.IsTouching(edge)) touchingSteps++;
                    Assert.IsFalse(ball.IsInBarrierImpact,"The recent contact must not start a second hit-stop.");
                    Assert.That(ball.Body.linearVelocity.magnitude,Is.LessThanOrEqualTo(releasedSpeed+.03f),
                        $"Recontact must not add raw solver acceleration (boost {useHandBoost}, step {step}).");
                }
                Assert.That(touchingSteps,Is.GreaterThanOrEqualTo(2),"Exercise both the new contact and subsequent touching frames.");
                Assert.That(handBody.position.y,Is.GreaterThan(startY+.65f),"Hands must keep moving throughout the recontact check.");
            }
            handBody.linearVelocity=Vector2.zero;
        }

        [UnityTest]
        public IEnumerator HandMotionUsesTheContactPositionAndOnlyFreshOutwardMovement()
        {
            barrier.SetHandMotion(Vector2.up*2,Vector2.up*.1f,100);
            Assert.That(barrier.HandMotionBoost(0,Vector2.up,100.05),Is.EqualTo(1).Within(.001f));
            Assert.That(barrier.HandMotionBoost(1,Vector2.up,100.05),Is.Zero);
            Assert.That(barrier.HandMotionBoost(.5f,Vector2.up,100.05),Is.InRange(.4f,.6f));
            Assert.That(barrier.HandMotionBoost(0,Vector2.down,100.05),Is.Zero,"Pulling away must not strengthen a return.");
            Assert.That(barrier.HandMotionBoost(0,Vector2.right,100.05),Is.Zero,"Sliding parallel to the surface must not strengthen a return.");
            Assert.That(barrier.HandMotionBoost(0,Vector2.up,100.16),Is.Zero,"A previous tracked position must not remain an active swing.");
            barrier.ClearHandMotion();
            Assert.That(barrier.HandMotionBoost(0,Vector2.up,100.05),Is.Zero);
            yield break;
        }

        [UnityTest]
        public IEnumerator ClearingTrackingDuringCompressionCancelsTheCapturedHandBoost()
        {
            barrier.SetHandMotion(Vector2.up*2,Vector2.up*2,Time.unscaledTimeAsDouble);
            Hit(); barrier.Step(.07f);
            barrier.ClearHandMotion();
            barrier.Step(.18f);
            Assert.That(ball.Body.linearVelocity.magnitude,Is.EqualTo(8*.58f).Within(.03f));
            yield break;
        }

        [UnityTest]
        public IEnumerator MotionDuringCompressionAddsASmallBoostEvenWhenHandsWereInitiallyStill()
        {
            Hit(); barrier.Step(.07f);
            barrier.SetHandMotion(Vector2.up*2,Vector2.up*2,Time.unscaledTimeAsDouble);
            barrier.Step(.18f);
            Assert.That(ball.Body.linearVelocity.magnitude,Is.EqualTo(8*.58f*1.2f).Within(.03f));
            yield break;
        }

        [UnityTest]
        public IEnumerator OutdatedHandMovementCancelsAPreviouslyCapturedBoost()
        {
            barrier.SetHandMotion(Vector2.up*2,Vector2.up*2,Time.unscaledTimeAsDouble);
            Hit(); barrier.Step(.07f);
            barrier.SetHandMotion(Vector2.up*2,Vector2.up*2,Time.unscaledTimeAsDouble-.2);
            barrier.Step(.18f);
            Assert.That(ball.Body.linearVelocity.magnitude,Is.EqualTo(8*.58f).Within(.03f));
            yield break;
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
