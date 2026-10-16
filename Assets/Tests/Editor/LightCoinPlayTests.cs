using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor.SceneManagement;
using Naispo.Kinect;
using Naispo.Interaction;

namespace Naispo.Tests
{
    public class LightCoinPlayTests
    {
        [UnityTest] public IEnumerator OpenFloorRespawnsSameBallWithGravity()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/KinectSilhouetteDemo.unity");
            Object.FindFirstObjectByType<AzureKinectBodySource>().enabled=false;
            yield return new EnterPlayMode();
            yield return null;
            var ball=Object.FindFirstObjectByType<AirHockeyBall>();
            var walls=Object.FindFirstObjectByType<ScreenBoundaryController>();
            Assert.IsFalse(walls.bottomWall.gameObject.activeSelf);
            Assert.IsFalse(walls.enabled);
            Assert.That(walls.rightWall.transform.position.x-walls.leftWall.transform.position.x,Is.LessThan(8));
            Assert.That(ball.Body.gravityScale,Is.GreaterThan(0));
            ball.Body.position=new Vector2(0,-7);
            yield return null;
            yield return null;
            Assert.IsFalse(ball.Body.simulated);
            float deadline=Time.time+1.2f;
            while(!ball.Body.simulated && Time.time<deadline) yield return null;
            float settle=Time.time+0.1f;
            while(Time.time<settle) yield return null;
            Assert.IsTrue(ball.Body.simulated);
            Assert.That(ball.Body.position.y,Is.GreaterThan(2));
            Assert.That(ball.Body.linearVelocity.y,Is.LessThan(0));
            yield return new ExitPlayMode();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(Application.isPlaying) yield return new ExitPlayMode();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        }
    }
}
