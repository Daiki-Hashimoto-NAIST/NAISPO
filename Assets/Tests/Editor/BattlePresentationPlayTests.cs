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
    public sealed class BattlePresentationPlayTests
    {
        Scene scene;
        CooperativeCoinGame game;
        AirHockeyBall ball;

        [UnitySetUp]
        public IEnumerator CreateCourt()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            yield return new EnterPlayMode();
            scene=SceneManager.CreateScene("Battle presentation court",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            var view=Create("Camera").AddComponent<Camera>();
            view.orthographic=true; view.orthographicSize=5; view.aspect=16f/9f;
            ball=Create("Ball").AddComponent<AirHockeyBall>();
            ball.GetComponent<CircleCollider2D>().radius=.22f;
            ball.Body.interpolation=RigidbodyInterpolation2D.None;
            game=Create("Game").AddComponent<CooperativeCoinGame>();
            game.Configure(ball,view);
            game.SetMode(RallyMode.Versus);
            ball.Body.gravityScale=0; ball.Body.linearDamping=0;
            yield return WaitFor(() => game.CanAdvanceBall,3,"The versus serve must start.");
            MoveBall(new Vector2(-6,3));
            yield return TwoFrames();
            Assert.That(game.BattleEffects,Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator RealFloorPointsCelebrateAndVictoryKeepsAnimatingAfterPlayStops()
        {
            for(int point=1;point<=RallyVersusState.PointsToWin;point++)
            {
                yield return WaitFor(() => game.CanAdvanceBall,3,"The next rally must serve.");
                MoveBall(new Vector2(-2,game.MatchFloor+.6f));
                yield return TwoFrames();
                MoveBall(new Vector2(-2,game.MatchFloor-.4f));
                yield return TwoFrames();

                Assert.That(game.VersusState.RightScore,Is.EqualTo(point));
                Assert.That(game.BattleEffects.ActiveParticleCount,Is.GreaterThan(0),
                    "A real floor crossing must emit the scoring celebration.");
                Assert.That(game.BattleEffects.IsVictory,Is.EqualTo(point==RallyVersusState.PointsToWin),
                    "Only the match-winning point receives the victory celebration.");
            }

            Assert.That(game.VersusState.Winner,Is.EqualTo(1));
            Assert.That(game.CanAdvanceBall,Is.False);
            Assert.That(ball.Body.simulated,Is.False);
            float victoryAge=game.BattleEffects.EffectAge;
            Vector2 finalBallPosition=ball.Body.position;
            yield return new WaitForSecondsRealtime(.15f);
            yield return TwoFrames();
            Assert.That(game.BattleEffects.EffectAge,Is.GreaterThan(victoryAge+.05f),
                "Ending the match must stop gameplay without freezing its victory animation.");
            Assert.That(game.BattleEffects.ActiveParticleCount,Is.GreaterThan(0));
            Assert.That(ball.Body.position,Is.EqualTo(finalBallPosition));
        }

        [UnityTest]
        public IEnumerator RealBarrierReleaseEmitsAndEveryGameplayBlockerFreezesThenResumesEffects()
        {
            var wall=Create("Left hand barrier");
            wall.transform.position=new Vector2(-3,-1.8f);
            wall.AddComponent<Rigidbody2D>().bodyType=RigidbodyType2D.Kinematic;
            var edge=wall.AddComponent<EdgeCollider2D>();
            var line=wall.AddComponent<LineRenderer>(); line.useWorldSpace=false;
            var barrier=wall.AddComponent<ElasticBarrier>();
            barrier.Initialize(edge,line); barrier.SetSpan(3,.3f,.16f,true); barrier.PlayerSide=-1;
            MoveBall((Vector2)wall.transform.position+Vector2.up);
            ball.Body.linearVelocity=Vector2.down*8;
            for(int i=0;i<20 && !ball.IsInBarrierImpact;i++) scene.GetPhysicsScene2D().Simulate(.02f);
            Assert.That(ball.IsInBarrierImpact,Is.True,"The regression must use a real barrier collision.");
            barrier.Step(.04f);
            Assert.That(ball.IsInBarrierImpact,Is.True);
            Assert.That(game.BattleEffects.ActiveParticleCount,Is.Zero,
                "Return sparks must occur on release, not on first contact during compression.");
            barrier.Step(.25f);
            Assert.That(ball.IsInBarrierImpact,Is.False);
            Assert.That(game.BattleEffects.ActiveParticleCount,Is.GreaterThan(0));
            MoveBall(new Vector2(-6,3));

            yield return AssertClockStopsAndResumes(() => game.SetPaused(true),() => game.SetPaused(false),"pause");
            yield return AssertClockStopsAndResumes(() => game.SetInputReady(false),() => game.SetInputReady(true),"tracking loss");
            yield return AssertClockStopsAndResumes(() => SetSettingsOpen(true),() => SetSettingsOpen(false),"settings");
            yield return AssertClockStopsAndResumes(() => game.ExternalOverlayOpen=true,() => game.ExternalOverlayOpen=false,"camera overlay");
        }

        [UnityTest]
        public IEnumerator RestartAndModeChangesClearEffectsAndRejectReturnsOutsideLiveVersus()
        {
            game.NotifyBarrierReleased(new Vector2(-3,-1.8f),-1,1);
            Assert.That(game.BattleEffects.ActiveParticleCount,Is.GreaterThan(0));
            game.RestartGame();
            Assert.That(game.BattleEffects.ActiveParticleCount,Is.Zero);
            Assert.That(game.BattleEffects.IsVictory,Is.False);
            game.NotifyBarrierReleased(new Vector2(-3,-1.8f),-1,1);
            Assert.That(game.BattleEffects.ActiveParticleCount,Is.Zero,
                "Waiting for a serve must not accept a return event.");

            yield return WaitFor(() => game.CanAdvanceBall,3,"Restart must allow a new rally.");
            game.NotifyBarrierReleased(new Vector2(-3,-1.8f),-1,1);
            Assert.That(game.BattleEffects.ActiveParticleCount,Is.GreaterThan(0));
            game.SetMode(RallyMode.StarCollection);
            Assert.That(game.BattleEffects.ActiveParticleCount,Is.Zero,
                "Switching modes must remove the previous battle's particles immediately.");
            yield return WaitFor(() => game.CanAdvanceBall,3,"The collection-mode serve must start.");
            game.NotifyBarrierReleased(new Vector2(-3,-1.8f),-1,1);
            Assert.That(game.BattleEffects.ActiveParticleCount,Is.Zero,
                "Collection mode must not emit battle effects even while its ball is playable.");

            game.SetMode(RallyMode.Versus);
            yield return WaitFor(() => game.CanAdvanceBall,3,"Switching back must start a fresh battle.");
            game.SetPaused(true);
            game.NotifyBarrierReleased(new Vector2(-3,-1.8f),-1,1);
            Assert.That(game.BattleEffects.ActiveParticleCount,Is.Zero);
            game.SetPaused(false);
            game.NotifyBarrierReleased(new Vector2(-3,-1.8f),-1,1);
            Assert.That(game.BattleEffects.ActiveParticleCount,Is.GreaterThan(0),
                "Returning to a live battle must re-enable its presentation.");
        }

        IEnumerator AssertClockStopsAndResumes(Action block,Action resume,string reason)
        {
            game.NotifyBarrierReleased(new Vector2(-3,-1.8f),-1,1);
            Assert.That(game.BattleEffects.ActiveParticleCount,Is.GreaterThan(0));
            block();
            yield return TwoFrames();
            float frozenAge=game.BattleEffects.EffectAge;
            int frozenCount=game.BattleEffects.ActiveParticleCount;
            game.NotifyBarrierReleased(new Vector2(3,-1.8f),1,1);
            yield return new WaitForSecondsRealtime(.12f);
            yield return TwoFrames();
            Assert.That(game.BattleEffects.EffectAge,Is.EqualTo(frozenAge).Within(.0001f),reason+" must freeze the effect clock.");
            Assert.That(game.BattleEffects.ActiveParticleCount,Is.EqualTo(frozenCount),reason+" must retain the frozen presentation and reject new return effects.");
            resume();
            yield return new WaitForSecondsRealtime(.08f);
            yield return TwoFrames();
            Assert.That(game.BattleEffects.EffectAge,Is.GreaterThan(frozenAge+.02f),reason+" must resume the effect clock.");
        }

        void SetSettingsOpen(bool value)
        {
            // The settings button owns this private flag; use that same gate without
            // coupling a play-mode regression to screen coordinates or IMGUI events.
            var field=typeof(CooperativeCoinGame).GetField("settingsOpen",BindingFlags.Instance|BindingFlags.NonPublic);
            Assert.That(field,Is.Not.Null);
            field.SetValue(game,value);
        }

        GameObject Create(string name)
        {
            var result=new GameObject(name);
            SceneManager.MoveGameObjectToScene(result,scene);
            return result;
        }

        void MoveBall(Vector2 position)
        {
            ball.Body.position=position; ball.transform.position=position;
            ball.Body.linearVelocity=Vector2.zero;
            Physics2D.SyncTransforms();
        }

        static IEnumerator TwoFrames()
        {
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
            game=null; ball=null;
        }
    }
}
