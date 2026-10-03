using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using Naispo.Interaction;

public static class ValidatePlayModePhysics
{
    public static async Task<string> Run()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Requires Play Mode");
        var scene=SceneManager.CreateScene("Kinect isolated contact validation",new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var physics=scene.GetPhysicsScene2D();
        var persons=new PolygonCollider2D[2];var balls=new Rigidbody2D[2];SilhouetteSurfaceMotion motion=null;
        var points=new List<Vector2>(4);
        try
        {
            for(int i=0;i<2;i++)
            {
                float y=i*20;
                var person=new GameObject("Probe person "+i);SceneManager.MoveGameObjectToScene(person,scene);person.transform.position=new Vector3(0,y,0);
                person.AddComponent<Rigidbody2D>().bodyType=RigidbodyType2D.Kinematic;
                persons[i]=person.AddComponent<PolygonCollider2D>();
                var ball=new GameObject("Probe ball "+i);SceneManager.MoveGameObjectToScene(ball,scene);ball.transform.position=new Vector3(0.6f,y,0);
                balls[i]=ball.AddComponent<Rigidbody2D>();balls[i].gravityScale=0;balls[i].linearDamping=0;balls[i].collisionDetectionMode=CollisionDetectionMode2D.Continuous;
                ball.AddComponent<CircleCollider2D>().radius=0.25f;
                if(i==1){motion=person.AddComponent<SilhouetteSurfaceMotion>();ball.AddComponent<SilhouetteBallPush>();}
            }
            for(int step=0;step<=30;step++)
            {
                float right=step*0.025f;
                points.Clear();points.Add(new Vector2(-2,-2));points.Add(new Vector2(right,-2));points.Add(new Vector2(right,2));points.Add(new Vector2(-2,2));
                persons[0].SetPath(0,points);persons[1].SetPath(0,points);
                for(int p=0;p<points.Count;p++)points[p]+=new Vector2(0,20);
                motion.BeginSample(Time.fixedTimeAsDouble,step==0);motion.AddPath(points);
                physics.Simulate(0.02f);
                await Task.Delay(25);
            }
            float baseline=balls[0].linearVelocity.x,assisted=balls[1].linearVelocity.x;
            string result=$"Play Mode deformation collision: baseline vx={baseline:F3}, assisted vx={assisted:F3}, baseline x={balls[0].position.x:F3}, assisted x={balls[1].position.x:F3}";
            if(assisted<0.2f)throw new Exception(result+"; expected positive impulse from SilhouetteBallPush.");
            return result;
        }
        finally{var unloading=SceneManager.UnloadSceneAsync(scene);while(!unloading.isDone)await Task.Delay(1);}
    }
}
