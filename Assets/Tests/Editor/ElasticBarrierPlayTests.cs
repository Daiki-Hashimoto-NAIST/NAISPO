using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEditor.SceneManagement;
using Naispo.Interaction;

namespace Naispo.Tests
{
    public class ElasticBarrierPlayTests
    {
        [UnityTest] public IEnumerator CollisionDrivesTheSpringInPlayMode()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            yield return new EnterPlayMode();
            var scene=SceneManager.CreateScene("Elastic collision",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
            var wall=new GameObject("Barrier"); SceneManager.MoveGameObjectToScene(wall,scene);
            wall.AddComponent<Rigidbody2D>().bodyType=RigidbodyType2D.Kinematic;
            var edge=wall.AddComponent<EdgeCollider2D>();
            var line=wall.AddComponent<LineRenderer>();
            var elastic=wall.AddComponent<ElasticBarrier>(); elastic.Initialize(edge,line);
            elastic.SetSpan(4,1.5f,0.16f,true);
            var ball=new GameObject("Ball"); SceneManager.MoveGameObjectToScene(ball,scene);
            ball.transform.position=Vector2.down;
            ball.AddComponent<CircleCollider2D>().radius=0.15f;
            var body=ball.AddComponent<Rigidbody2D>(); body.gravityScale=0;body.linearVelocity=Vector2.up*8;
            body.collisionDetectionMode=CollisionDetectionMode2D.Continuous;
            for(int i=0;i<10;i++)scene.GetPhysicsScene2D().Simulate(0.02f);
            elastic.Step(0.02f);
            Assert.That(elastic.Deflection,Is.GreaterThan(0.001f),"Collision must bend the barrier away from the incoming ball.");
            Assert.That(body.linearVelocity.y,Is.LessThan(0));
            yield return new ExitPlayMode();
        }
        [UnityTearDown] public IEnumerator Cleanup()
        {
            if(Application.isPlaying) yield return new ExitPlayMode();
        }
    }
}
