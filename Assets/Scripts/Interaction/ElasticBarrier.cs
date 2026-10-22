using UnityEngine;
using Naispo.Kinect;

namespace Naispo.Interaction
{
    // A damped string mode with pinned ends. Renderer and collision use the same samples.
    [DefaultExecutionOrder(100)]
    public sealed class ElasticBarrier : MonoBehaviour
    {
        public KinectSettings settings;
        public int PlayerSide { get; set; }
        const int Samples = 25;
        readonly Vector2[] points = new Vector2[Samples];
        EdgeCollider2D edge;
        LineRenderer line;
        PhysicsMaterial2D material;
        float length = 1, softness, offset, velocity;
        float bendLimit, frequency;
        AirHockeyBall heldBall;
        Vector2 rebound;
        float impactAge, impactDuration, impactDepth, impactStart, impactSide, impactPoint=.5f, ballPoint, ballGap;
        float bendPoint=.5f;
        public float Deflection => offset;
        public float Restitution => material.bounciness;
        public Vector2[] Shape => points;
        public bool IsHoldingBall => heldBall != null;

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
            if(!IsHoldingBall) offset = Mathf.Clamp(offset,-bendLimit,bendLimit);
            Draw();
        }
        public void Kick(float speed, float direction)
        {
            velocity += Mathf.Sign(direction)*Mathf.Clamp(speed,0,12)*Mathf.Lerp(0.5f,1.8f,softness);
            velocity = Mathf.Clamp(velocity,-bendLimit*frequency*2,bendLimit*frequency*2);
        }
        public void Step(float dt)
        {
            if(IsHoldingBall)
            {
                if(edge==null || !edge.enabled || !heldBall.isActiveAndEnabled) { CancelImpact(true); return; }
                if(heldBall.CooperativeGame==null) { CancelImpact(true); return; }
                if(!heldBall.CooperativeGame.CanAdvanceBall) return;
                impactAge+=Mathf.Max(0,dt);
                float t=Mathf.Clamp01(impactAge/impactDuration);
                // Compress, linger at full load, then snap back. The whole game
                // keeps running; only the ball and this contact are held.
                float compression=t<.45f ? Mathf.Sin(t/.45f*Mathf.PI*.5f)
                    : t<.7f ? 1 : 1-Mathf.SmoothStep(0,1,(t-.7f)/.3f);
                offset=t<.45f ? Mathf.Lerp(impactStart,impactDepth,compression) : impactDepth*compression;
                Draw(); PositionHeldBall();
                if(t>=1)
                {
                    velocity=-impactDepth*frequency*.18f;
                    CompleteImpact(rebound);
                }
                return;
            }
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
        void LateUpdate()
        {
            // Kinematic hand movement is applied by physics after FixedUpdate.
            if(IsHoldingBall && heldBall.CooperativeGame!=null && heldBall.CooperativeGame.CanAdvanceBall)
                PositionHeldBall();
        }
        float ShapeAt(float t)
        {
            if(t<=0 || t>=1) return 0;
            return t<=bendPoint ? Mathf.Sin(t/bendPoint*Mathf.PI*.5f)
                : Mathf.Sin((1-t)/(1-bendPoint)*Mathf.PI*.5f);
        }
        void Draw()
        {
            for(int i=0;i<Samples;i++)
            {
                float t = (float)i/(Samples-1);
                float y = offset*ShapeAt(t);
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
            var gameBall = ball.GetComponent<AirHockeyBall>();
            if(gameBall != null && gameBall.GravityGame && gameBall.CooperativeGame != null &&
                TryHoldBall(gameBall,side,speed)) return;
            Kick(speed,side > 0 ? -1 : 1);
            if(gameBall != null && gameBall.GravityGame)
            {
                Vector2 normal = (Vector2)transform.up * (side >= 0 ? 1 : -1);
                ball.linearVelocity = gameBall.CooperativeGame != null
                    ? gameBall.CooperativeGame.BarrierBounce(ball.linearVelocity,normal,softness,ball.position)
                    : LightCoinGame.BoostReflection(ball.linearVelocity,normal,softness);
            }
        }
        bool TryHoldBall(AirHockeyBall ball,float side,float speed)
        {
            if(IsHoldingBall) return false;
            if(ball.CooperativeGame.TryScoreFloorBeforeBarrier(ball.Body.position)) return true;
            Vector2 normal=(Vector2)transform.up*(side>=0 ? 1 : -1);
            Vector2 incoming=ball.Body.linearVelocity;
            if(!ball.BeginBarrierImpact(this)) return false;
            Vector2 outgoing=ball.CooperativeGame.PlayerBarrierBounce(incoming,normal,softness,ball.Body.position,PlayerSide);
            heldBall=ball; rebound=outgoing;
            Vector2 local=transform.InverseTransformPoint(ball.Body.position);
            ballPoint=Mathf.Clamp01(local.x/length+.5f);
            impactPoint=Mathf.Clamp(ballPoint,.04f,.96f);
            bendPoint=impactPoint;
            impactSide=side>=0 ? 1 : -1;
            Vector3 scale=ball.transform.lossyScale;
            ballGap=ball.GetComponent<CircleCollider2D>().radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y))+edge.edgeRadius+.025f;
            impactStart=offset;
            float depth=Mathf.Min(length*.22f,Mathf.Lerp(.22f,.48f,softness));
            depth*=Mathf.Lerp(.55f,1,Mathf.Sin(Mathf.PI*impactPoint))*Mathf.Lerp(.7f,1.1f,Mathf.InverseLerp(2,10,speed));
            impactDepth=-impactSide*depth;
            impactAge=0; impactDuration=Mathf.Lerp(.17f,.20f,softness);
            velocity=0;
            Draw(); PositionHeldBall();
            return true;
        }
        void PositionHeldBall()
        {
            Vector3 position=transform.TransformPoint(new Vector3((ballPoint-.5f)*length,
                offset*ShapeAt(ballPoint)+impactSide*ballGap,0));
            position.z=heldBall.transform.position.z;
            heldBall.Body.position=position;
            heldBall.transform.position=position;
        }
        void CompleteImpact(Vector2 outgoing)
        {
            var ball=heldBall; heldBall=null;
            if(ball!=null) ball.FinishBarrierImpact(this,outgoing);
        }
        public void CancelImpact(bool release)
        {
            if(!IsHoldingBall) return;
            offset=velocity=0;
            if(release && (heldBall.CooperativeGame==null || heldBall.CooperativeGame.CanAdvanceBall)) PositionHeldBall();
            CompleteImpact(release ? rebound : Vector2.zero);
            Draw();
        }
        public void ResetSpring()
        {
            CancelImpact(true);
            offset=velocity=0; bendPoint=.5f;
            if(edge!=null && line!=null) Draw();
        }
        void OnDisable() { ResetSpring(); }
        void OnDestroy()
        {
            CancelImpact(true);
            if(material == null) return;
            if(Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        }
    }
}
