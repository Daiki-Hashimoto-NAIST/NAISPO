using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using System.Collections.Generic;
using Naispo.Interaction;

namespace Naispo.Tests
{
    public sealed class PhysicsTests
    {
        Scene scene;
        PhysicsScene2D physics;
        PhysicsMaterial2D material;
        [SetUp]public void Setup()
        {
            scene=EditorSceneManager.NewPreviewScene();
            physics=scene.GetPhysicsScene2D();material=new PhysicsMaterial2D("Test bounce"){friction=0,bounciness=0.9f};
        }
        [TearDown]public void Cleanup(){if(scene.IsValid())EditorSceneManager.ClosePreviewScene(scene);if(material!=null)Object.DestroyImmediate(material);}
        GameObject Go(string name,Vector2 p){var g=new GameObject(name);SceneManager.MoveGameObjectToScene(g,scene);g.transform.position=p;return g;}
        Rigidbody2D Ball(Vector2 p)
        {
            var g=Go("Test ball",p);var c=g.AddComponent<CircleCollider2D>();c.radius=0.25f;c.sharedMaterial=material;
            var b=g.AddComponent<Rigidbody2D>();b.gravityScale=0;b.collisionDetectionMode=CollisionDetectionMode2D.Continuous;b.linearDamping=0;return b;
        }
        PolygonCollider2D Person()
        {
            var g=Go("Test person",Vector2.zero);var b=g.AddComponent<Rigidbody2D>();b.bodyType=RigidbodyType2D.Kinematic;
            var c=g.AddComponent<PolygonCollider2D>();c.sharedMaterial=material;SetShape(c,0);return c;
        }
        static void SetShape(PolygonCollider2D c,float right)
        {c.SetPath(0,new[]{new Vector2(-2,-2),new Vector2(right,-2),new Vector2(right,2),new Vector2(-2,2)});}
        [Test]public void FastBallBouncesFromWallWithoutTunnelling()
        {
            var wall=Go("Test wall",new Vector2(3,0)).AddComponent<BoxCollider2D>();wall.size=new Vector2(0.4f,8);wall.sharedMaterial=material;
            var ball=Ball(Vector2.zero);ball.linearVelocity=new Vector2(12,0);
            for(int i=0;i<20;i++)physics.Simulate(0.02f);
            Assert.That(ball.linearVelocity.x,Is.LessThan(-5));Assert.That(ball.position.x,Is.LessThan(2.8f));
        }
        [Test]public void MovingKinematicBodyPushesStationaryBall()
        {
            var person=Person();var ball=Ball(new Vector2(0.6f,0));
            for(int i=0;i<30;i++){person.attachedRigidbody.MovePosition(new Vector2((i+1)*0.025f,0));physics.Simulate(0.02f);}
            Assert.That(ball.position.x,Is.GreaterThan(0.85f));Assert.That(ball.linearVelocity.x,Is.GreaterThan(0.2f));
        }
        [Test]public void ChangingOnlyPolygonPathReportsSurfaceMotionGap()
        {
            var person=Person();var ball=Ball(new Vector2(0.6f,0));
            for(int i=0;i<30;i++){SetShape(person,(i+1)*0.025f);physics.Simulate(0.02f);}
            Debug.Log($"Naispo deformation-only diagnostic: ball x={ball.position.x:F3}, vx={ball.linearVelocity.x:F3}");
            Assert.That(ball.position.x,Is.GreaterThan(0.8f),"Expanding shape should separate the ball.");
            // This records the baseline, allowing an independently tested surface-motion
            // helper only when the native solver does not convey deformation velocity.
        }
        [Test]public void SurfaceMotionEstimatesExpansionAndRejectsStaleOrStaticShapes()
        {
            var g=Go("Motion test",Vector2.zero);var m=g.AddComponent<SilhouetteSurfaceMotion>();
            var p=new List<Vector2>{new Vector2(-2,-2),new Vector2(0,-2),new Vector2(0,2),new Vector2(-2,2)};
            m.BeginSample(0,true);m.AddPath(p);
            p[1]=new Vector2(0.1f,-2);p[2]=new Vector2(0.1f,2);
            m.BeginSample(0.04,false);m.AddPath(p);
            Assert.That(m.EstimateOutwardSpeed(new Vector2(0.1f,0),0.04,6),Is.EqualTo(2.5f).Within(0.001));
            Assert.That(m.EstimateOutwardSpeed(new Vector2(0.1f,0),0.4,6),Is.Zero);
            m.BeginSample(0.08,false);m.AddPath(p);
            Assert.That(m.EstimateOutwardSpeed(new Vector2(0.1f,0),0.08,6),Is.Zero);
        }
    }
}
