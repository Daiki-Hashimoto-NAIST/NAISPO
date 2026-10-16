using UnityEngine;
using Naispo.Kinect;

namespace Naispo.Interaction
{
    // A damped string mode with pinned ends. Renderer and collision use the same samples.
    [DefaultExecutionOrder(100)]
    public sealed class ElasticBarrier : MonoBehaviour
    {
        public KinectSettings settings;
        const int Samples = 25;
        readonly Vector2[] points = new Vector2[Samples];
        EdgeCollider2D edge;
        LineRenderer line;
        PhysicsMaterial2D material;
        float length = 1, softness, offset, velocity;
        float bendLimit, frequency;
        public float Deflection => offset;
        public float Restitution => material.bounciness;
        public Vector2[] Shape => points;

        public void Initialize(EdgeCollider2D collider, LineRenderer renderer)
        {
            edge = collider; line = renderer;
            material = new PhysicsMaterial2D("Barrier elasticity") { friction = 0,
                bounceCombine = PhysicsMaterialCombine2D.Minimum };
            edge.sharedMaterial = material;
            line.positionCount = Samples;
            SetSpan(1,0.3f,0.16f,true);
        }
        public void SetSpan(float worldLength, float meters, float width, bool reset)
        {
            length = Mathf.Max(0.05f,worldLength);
            float narrow = settings != null ? settings.barrierNarrowMeters : 0.3f;
            float wide = Mathf.Max(narrow+0.05f, settings != null ? settings.barrierWideMeters : 1.5f);
            // Synthetic/legacy frames have no metric span. Live frames always provide it.
            softness = Mathf.InverseLerp(narrow,wide,meters > 0 ? meters : worldLength*0.2f);
            material.bounciness = Mathf.Lerp(settings != null ? settings.barrierNarrowBounce : 1f,
                settings != null ? settings.barrierWideBounce : 0.4f, softness);
            bendLimit = Mathf.Min(length*0.25f, Mathf.Lerp(settings != null ? settings.barrierNarrowBend : 0.08f,
                settings != null ? settings.barrierWideBend : 0.65f, softness));
            frequency = Mathf.Lerp(7f,2.3f,softness)*2*Mathf.PI;
            edge.edgeRadius = width*0.5f;
            if(reset) ResetSpring();
            offset = Mathf.Clamp(offset,-bendLimit,bendLimit);
            Draw();
        }
        public void Kick(float speed, float direction)
        {
            velocity += Mathf.Sign(direction)*Mathf.Clamp(speed,0,12)*Mathf.Lerp(0.5f,1.8f,softness);
            velocity = Mathf.Clamp(velocity,-bendLimit*frequency*2,bendLimit*frequency*2);
        }
        public void Step(float dt)
        {
            int steps = Mathf.Max(1,Mathf.CeilToInt(dt/0.004f));
            float h = dt/steps;
            for(int i=0;i<steps;i++)
            {
                velocity += (-frequency*frequency*offset - 5f*velocity)*h;
                offset += velocity*h;
                if(Mathf.Abs(offset)>bendLimit) { offset = Mathf.Sign(offset)*bendLimit; velocity *= 0.4f; }
            }
            Draw();
        }
        void FixedUpdate() { if(edge != null && edge.enabled) Step(Time.fixedDeltaTime); }
        void Draw()
        {
            for(int i=0;i<Samples;i++)
            {
                float t = (float)i/(Samples-1);
                float y = i==0 || i==Samples-1 ? 0 : offset*Mathf.Sin(Mathf.PI*t);
                points[i] = new Vector2((t-0.5f)*length,y);
                line.SetPosition(i,new Vector3(points[i].x,y,-0.01f));
            }
            edge.points = points;
        }
        void OnCollisionEnter2D(Collision2D collision)
        {
            var ball = collision.rigidbody;
            if(ball == null || ball.bodyType != RigidbodyType2D.Dynamic || collision.contactCount == 0) return;
            float speed = Mathf.Abs(Vector2.Dot(collision.relativeVelocity,transform.up));
            float side = transform.InverseTransformPoint(ball.position).y;
            Kick(speed,side > 0 ? -1 : 1);
        }
        public void ResetSpring() { offset=velocity=0; }
        void OnDestroy()
        {
            if(material == null) return;
            if(Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        }
    }
}
