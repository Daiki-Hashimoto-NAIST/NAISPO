using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEditor.SceneManagement;
using Naispo.Interaction;

namespace Naispo.Tests
{
    public sealed class CooperativeCoinPlayTests
    {
        [UnityTest] public IEnumerator NetStopsLowBallAndAllowsAnAboveNetPass()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            yield return new EnterPlayMode();
            var scene=SceneManager.CreateScene("Cooperative net test",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            var cameraObject=new GameObject("Test camera"); SceneManager.MoveGameObjectToScene(cameraObject,scene);
            var camera=cameraObject.AddComponent<Camera>(); camera.orthographic=true; camera.orthographicSize=5; camera.aspect=16f/9f;
            var ballObject=new GameObject("Shared ball"); SceneManager.MoveGameObjectToScene(ballObject,scene);
            var ball=ballObject.AddComponent<AirHockeyBall>(); ball.GetComponent<CircleCollider2D>().radius=.22f;
            var gameObject=new GameObject("Cooperative game"); SceneManager.MoveGameObjectToScene(gameObject,scene);
            var game=gameObject.AddComponent<CooperativeCoinGame>(); game.Configure(ball,camera);
            Assert.IsFalse(game.NetCollider.isTrigger);
            Assert.That(game.NetCollider.gameObject.scene,Is.EqualTo(scene),"The net must share the ball's local physics scene.");
            ball.Body.interpolation=RigidbodyInterpolation2D.None;
            ball.Body.simulated=true; ball.Body.gravityScale=0;
            Vector2 lowStart=new Vector2(-1,game.ArenaFloor+.7f);
            ball.transform.position=lowStart; ball.Body.position=lowStart;
            Physics2D.SyncTransforms();
            Assert.That(game.NetCollider.bounds.min.y,Is.EqualTo(game.ArenaFloor).Within(.01));
            Assert.That(game.NetCollider.bounds.max.y,Is.EqualTo(game.ArenaFloor+game.NetHeight).Within(.01));
            Assert.That(Vector2.Distance(ball.Body.position,lowStart),Is.LessThan(.01));
            ball.Body.linearVelocity=Vector2.right*9;
            for(int i=0;i<20;i++) scene.GetPhysicsScene2D().Simulate(.02f);
            Assert.That(ball.Body.linearVelocity.x,Is.LessThan(0));
            Assert.That(ball.Body.position.x,Is.LessThan(-.3f),"Low pass must rebound from the center net.");
            Vector2 highStart=new Vector2(-1,game.ArenaFloor+game.NetHeight+.5f);
            ball.transform.position=highStart; ball.Body.position=highStart;
            Physics2D.SyncTransforms();
            ball.Body.linearVelocity=Vector2.right*9;
            for(int i=0;i<20;i++) scene.GetPhysicsScene2D().Simulate(.02f);
            Assert.That(ball.Body.position.x,Is.GreaterThan(1));
            Assert.That(ball.Body.linearVelocity.x,Is.GreaterThan(0));
            game.SetInputReady(false);
            Assert.IsFalse(ball.Body.simulated,"Loss of either input must freeze the same shared ball.");
            yield return new ExitPlayMode();
        }

        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        }
    }
}
