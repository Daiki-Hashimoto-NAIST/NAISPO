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
        const int MaxRuns = Samples - 1;
        // Keep the collision skin comfortably above the default 2D contact offset.
        const float CourtSkin = .025f;
        static float GapSkin => Mathf.Max(CourtSkin,2*Mathf.Max(0,Physics2D.defaultContactOffset)+.001f);
        readonly Vector2[] points = new Vector2[Samples];
        readonly Vector2[] worldPoints = new Vector2[Samples];
        readonly Vector2[] runScratch = new Vector2[MaxRuns * Samples];
        readonly Vector2[][] runCache = new Vector2[MaxRuns][];
        readonly int[] runCounts = new int[MaxRuns];
        readonly float[] runTMin = new float[MaxRuns], runTMax = new float[MaxRuns];
        readonly EdgeCollider2D[] pool = new EdgeCollider2D[MaxRuns];
        EdgeCollider2D edge;
        LineRenderer line;
        PhysicsMaterial2D material;
        float length = 1, softness, offset, velocity;
        float bendLimit, frequency;
        AirHockeyBall heldBall;
        Vector2 impactIncoming;
        Vector2 leftHandVelocity, rightHandVelocity;
        double handMotionAt=double.NegativeInfinity;
        float impactBoost;
        float impactAge, impactDuration, impactDepth, impactStart, impactSide, impactPoint=.5f, ballPoint, ballGap;
        float bendPoint=.5f;
        bool steppingFromPhysics, releaseAfterPhysics;
        float physicsDt;
        Vector2 preparedBodyPosition;
        float preparedBodyRotation;
        bool hasPreparedPose;
        readonly ContactPoint2D[] ballContacts=new ContactPoint2D[32];
        CooperativeCoinGame courtGame;
        int courtSide=1, poolCount, runTotal;
        float courtCenterX, courtHalfWidth;
        bool courtConfigured, tracking, hasPoseTarget;
        Vector2 poseTarget;
        float poseTargetRotation;
        Vector3 lastAcceptedBallPosition;
        public float Deflection => offset;
        public float Restitution => material.bounciness;
        public Vector2[] Shape => points;
        public bool IsHoldingBall => heldBall != null;

        public void SetHandMotion(Vector2 leftVelocity,Vector2 rightVelocity,double sampledAt)
        {
            leftHandVelocity=leftVelocity;rightHandVelocity=rightVelocity;handMotionAt=sampledAt;
        }
        public void ClearHandMotion()
        {
            leftHandVelocity=rightHandVelocity=Vector2.zero;
            handMotionAt=double.NegativeInfinity;impactBoost=0;
        }
        public float HandMotionBoost(float along,Vector2 outwardNormal,double now)
        {
            if(now<handMotionAt || now-handMotionAt>.15) return 0;
            float outward=Vector2.Dot(Vector2.Lerp(leftHandVelocity,rightHandVelocity,Mathf.Clamp01(along)),outwardNormal.normalized);
            return Mathf.InverseLerp(.15f,2f,outward);
        }

        // Opt into dual-court restriction; single-player keeps its full collider.
        public void ConfigureCourt(CooperativeCoinGame game,int side,float centerX,float halfWidth)
        {
            courtGame=game;
            PlayerSide=side;
            courtSide=side>=0 ? 1 : -1;
            courtCenterX=centerX;
            courtHalfWidth=Mathf.Max(0,halfWidth);
            courtConfigured=true;
            runTotal=0;
            EnsurePool();
            DisableColliders();
        }
        // Tracking is independent of whether clipping currently retains any geometry.
        public void SetTracking(bool value)
        {
            tracking=value;
            if(courtConfigured && !value) DisableColliders();
        }
        // The upcoming kinematic pose, before MovePosition/MoveRotation reaches physics.
        public void SetPoseTarget(Vector2 position,float rotationDegrees)
        {
            poseTarget=position; poseTargetRotation=rotationDegrees; hasPoseTarget=true;
        }

        public void Initialize(EdgeCollider2D collider, LineRenderer renderer)
        {
            edge = collider; line = renderer;
            material = new PhysicsMaterial2D("Barrier elasticity") { friction = 0,
                bounceCombine = PhysicsMaterialCombine2D.Minimum };
            edge.sharedMaterial = material;
            line.positionCount = Samples;
            SetSpan(1,0.3f,0.16f,true);
            if(courtConfigured) { EnsurePool(); DisableColliders(); }
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
            if(courtConfigured) for(int i=0;i<poolCount;i++) if(pool[i]!=null) pool[i].edgeRadius=edge.edgeRadius;
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
                if(edge==null || (!courtConfigured && !edge.enabled) || (courtConfigured && !tracking) ||
                    !heldBall.isActiveAndEnabled) { CancelImpact(true); return; }
                if(heldBall.CooperativeGame==null) { CancelImpact(true); return; }
                if(!heldBall.CooperativeGame.CanAdvanceBall) { ClearHandMotion();return; }
                UpdateImpactBoost();
                impactAge+=Mathf.Max(0,dt);
                float t=Mathf.Clamp01(impactAge/impactDuration);
                // Compress, linger at full load, then snap back. The whole game
                // keeps running; only the ball and this contact are held.
                float compression=t<.45f ? Mathf.Sin(t/.45f*Mathf.PI*.5f)
                    : t<.7f ? 1 : 1-Mathf.SmoothStep(0,1,(t-.7f)/.3f);
                offset=t<.45f ? Mathf.Lerp(impactStart,impactDepth,compression) : impactDepth*compression;
                Draw();
                // PositionHeldBall validates against the freshly deformed runs, not
                // the shape retained before this step.
                if(courtConfigured) BuildCourtGeometry(Mathf.Max(0,dt));
                if(!PositionHeldBall()) return;
                if(t>=1)
                {
                    velocity=-impactDepth*frequency*.18f;
                    // MovePosition/MoveRotation queued by the hands are not applied
                    // until after FixedUpdate. Keep the ball attached through that
                    // final move, then release from the pose the player actually sees.
                    if(steppingFromPhysics) releaseAfterPhysics=true;
                    else ReleaseImpact();
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
        // Production ordering: hand input, then this method, then Physics2D.Simulate.
        // It advances the spring before rebuilding the restricted collision shape.
        public void PreparePhysics(float dt)
        {
            if(edge==null) return;
            if(courtConfigured && !tracking) { DisableColliders(); return; }
            // This flag describes the release timing of the physics step the caller
            // is about to simulate, so it must also be owned by direct/manual calls.
            bool wasStepping=steppingFromPhysics;
            steppingFromPhysics=true;
            try
            {
                float stepDt=Mathf.Max(0,dt);
                physicsDt=stepDt;
                CapturePreparedPose();
                // Held-ball placement validates against the retained runs, so refresh
                // them before Step as well as after the spring/placement changes.
                if(courtConfigured) BuildCourtGeometry(stepDt);
                Step(stepDt);
                if(courtConfigured) BuildCourtGeometry(stepDt);
            }
            finally { steppingFromPhysics=wasStepping; }
        }
        void CapturePreparedPose()
        {
            hasPreparedPose=courtConfigured && edge.attachedRigidbody!=null && physicsDt>0;
            if(hasPreparedPose)
            {
                preparedBodyPosition=edge.attachedRigidbody.position;
                preparedBodyRotation=edge.attachedRigidbody.rotation;
            }
        }
        void FixedUpdate()
        {
            if(edge==null) return;
            if(courtConfigured)
            {
                if(!tracking) { DisableColliders(); return; }
            }
            else if(!edge.enabled) return;
            PreparePhysics(Time.fixedDeltaTime);
        }
        void LateUpdate()
        {
            // Kinematic hand movement is applied by physics after FixedUpdate.
            if(IsHoldingBall && heldBall.CooperativeGame!=null && heldBall.CooperativeGame.CanAdvanceBall)
            {
                if(!PositionHeldBall()) return;
                if(releaseAfterPhysics) ReleaseImpact();
            }
        }
        float ShapeAt(float t)
        {
            return ShapeAt(t,bendPoint);
        }
        static float ShapeAt(float t,float peak)
        {
            if(t<=0 || t>=1) return 0;
            return t<=peak ? Mathf.Sin(t/peak*Mathf.PI*.5f)
                : Mathf.Sin((1-t)/(1-peak)*Mathf.PI*.5f);
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
            // Court mode owns the collider points: they are the clipped runs.
            if(!courtConfigured && edge!=null) edge.points = points;
        }
        void OnCollisionEnter2D(Collision2D collision)
        {
            var ball = collision.rigidbody;
            if(ball == null || ball.bodyType != RigidbodyType2D.Dynamic || collision.contactCount == 0) return;
            float speed = Mathf.Abs(Vector2.Dot(collision.relativeVelocity,transform.up));
            float side = ContactSide(collision,ball);
            var gameBall = ball.GetComponent<AirHockeyBall>();
            if(gameBall != null && gameBall.GravityGame && gameBall.CooperativeGame != null)
            {
                float attachment=0;
                Vector2 incoming=RecoveredIncoming(collision,gameBall);
                if(courtConfigured && !ValidateCourtContact(gameBall,collision,out attachment))
                {
                    // The pair should already be ignored by admission. If a solver
                    // contact still slips through, undo its impulse without Light,
                    // attribution, capture, reflection, or spell consumption.
                    RemoveUnfilteredImpulse(gameBall,incoming);
                    return;
                }
                // A geometrically invalid hold must not cancel Light. Validate the
                // prospective placement with locals only, before any notification.
                // A genuine contact that merely loses the same-owner cooldown still
                // ends the spell, because its geometry is valid.
                if(courtConfigured && !CanHoldProspectively(gameBall,attachment,side))
                {
                    RemoveUnfilteredImpulse(gameBall,incoming);
                    return;
                }
                // A contact that cannot be captured still ends Light.
                gameBall.CooperativeGame.NotifyBarrierContact();
                // Recover the incoming world velocity, before the solver's moving-wall
                // impulse. Only our filtered hand samples may add swing acceleration.
                if(TryHoldBall(gameBall,side,speed,incoming,attachment)) return;
                RemoveUnfilteredImpulse(gameBall,incoming);
                return;
            }
            Kick(speed,side > 0 ? -1 : 1);
            if(gameBall != null && gameBall.GravityGame)
            {
                Vector2 normal = (Vector2)transform.up * (side >= 0 ? 1 : -1);
                ball.linearVelocity = gameBall.CooperativeGame != null
                    ? gameBall.CooperativeGame.BarrierBounce(ball.linearVelocity,normal,softness,ball.position)
                    : LightCoinGame.BoostReflection(ball.linearVelocity,normal,softness);
            }
        }
        Vector2 IncomingVelocity(Collision2D collision)
        {
            // MovePosition has already cleared the hand body's temporary velocity
            // by the time collision callbacks run. Adding its current velocity to
            // contact.relativeVelocity therefore invents a backwards ball motion.
            // Instead undo this contact's solver impulse on the dynamic ball.
            Vector2 incoming=collision.rigidbody.linearVelocity;
            float inverseMass=1/collision.rigidbody.mass;
            for(int i=0;i<collision.contactCount;i++)
            {
                var contact=collision.GetContact(i);
                Vector2 tangent=new Vector2(contact.normal.y,-contact.normal.x);
                incoming+=(contact.normal*contact.normalImpulse+tangent*contact.tangentImpulse)*inverseMass;
            }
            return incoming;
        }
        Vector2 RecoveredIncoming(Collision2D collision,AirHockeyBall ball)
        {
            // Rounded clipping caps can change their normal during CCD, so undoing
            // the solver impulse using the final normal distorts the incoming path.
            // Collision.relativeVelocity includes this step's forces. Restore world
            // velocity with the wall's actual applied movement (queued targets may
            // be speed limited, and the callback's wall velocity may already be zero).
            if(courtConfigured && hasPreparedPose && ball!=null && courtGame!=null && ball==courtGame.Ball)
            {
                Rigidbody2D wallBody=edge.attachedRigidbody;
                Vector2 wallVelocity=(wallBody.position-preparedBodyPosition)/physicsDt;
                // Relative velocity includes the wall's motion at the contact,
                // including rotation. Rebuild that motion from the applied pose;
                // MovePosition/MoveRotation may clear the reported body velocities.
                float wallAngularVelocity=Mathf.DeltaAngle(preparedBodyRotation,wallBody.rotation)
                    *Mathf.Deg2Rad/physicsDt;
                Vector2 wallCenter=wallBody.worldCenterOfMass;
                Vector2 contactPoint=wallCenter;
                if(collision.contactCount>0)
                {
                    contactPoint=Vector2.zero;
                    for(int c=0;c<collision.contactCount;c++)
                        contactPoint+=collision.GetContact(c).point;
                    contactPoint=contactPoint/collision.contactCount;
                }
                Vector2 lever=contactPoint-wallCenter;
                Vector2 rotationVelocity=new Vector2(-wallAngularVelocity*lever.y,
                    wallAngularVelocity*lever.x);
                Vector2 incoming=collision.relativeVelocity+wallVelocity+rotationVelocity;
                // Preserve impulses from other surfaces touched in the same step.
                // GetContacts on the ball returns normals directed toward the ball.
                int count=ball.Body.GetContacts(ballContacts);
                for(int i=0;i<count;i++)
                {
                    var contact=ballContacts[i];
                    if(contact.collider==null || contact.otherCollider==null) continue;
                    var other=contact.collider.attachedRigidbody==ball.Body ? contact.otherCollider : contact.collider;
                    if(other.attachedRigidbody==edge.attachedRigidbody || other.GetComponent<ElasticBarrier>()!=null) continue;
                    Vector2 tangent=new Vector2(contact.normal.y,-contact.normal.x);
                    incoming+=(contact.normal*contact.normalImpulse+tangent*contact.tangentImpulse)/ball.Body.mass;
                }
                return incoming;
            }
            return IncomingVelocity(collision);
        }
        Vector2 HandNormal(float side)
        {
            return ((Vector2)transform.up*(side>=0 ? 1 : -1)).normalized;
        }
        Vector2 SurfacePoint(float t,float peak)
        {
            return transform.TransformPoint(new Vector3((t-.5f)*length,
                offset*ShapeAt(t,peak),0));
        }
        Vector3 CurrentCandidate(float t,float side,float gap)
        {
            return (Vector3)SurfacePoint(t,bendPoint)+(Vector3)(HandNormal(side)*gap);
        }
        Vector3 PendingCandidate(float t,float side,float gap)
        {
            return PendingCandidate(SurfacePoint(t,bendPoint),side,gap);
        }
        Vector3 PendingCandidate(Vector3 surface,float side,float gap)
        {
            // Apply the current->target delta with the current scale. This matches
            // the kinematic MovePosition/MoveRotation that physics has not applied
            // yet, without assuming the absolute target transform is identical.
            Vector2 normal=HandNormal(side);
            if(!hasPoseTarget) return surface+(Vector3)(normal*gap);
            Vector3 deltaPoint=surface-transform.position;
            float theta=Mathf.DeltaAngle(transform.eulerAngles.z,poseTargetRotation)*Mathf.Deg2Rad;
            float c=Mathf.Cos(theta),s=Mathf.Sin(theta);
            Vector3 rotated=new Vector3(deltaPoint.x*c-deltaPoint.y*s,deltaPoint.x*s+deltaPoint.y*c,0);
            Vector2 pendingNormal=new Vector2(normal.x*c-normal.y*s,normal.x*s+normal.y*c);
            return (Vector3)poseTarget+rotated+(Vector3)(pendingNormal*gap);
        }
        bool CanHoldProspectively(AirHockeyBall ball,float attachment,float side)
        {
            if(!courtConfigured) return true;
            if(ball==null || ball.Body==null || !tracking) return false;
            float t=Mathf.Clamp01(attachment);
            if(!IsParameterRetained(t)) return false;
            // Starting a hold moves the bend's peak to the new attachment. Check
            // that prospective shape before changing Light, ownership or ball state.
            float peak=Mathf.Clamp(t,.04f,.96f),radius=0;
            for(int i=0;i<Samples;i++)
                radius=Mathf.Max(radius,Vector2.Distance(SurfacePoint((float)i/(Samples-1),peak),transform.position));
            CourtBounds(radius,out float minX,out float maxX);
            Vector3 surface=SurfacePoint(t,peak);
            if(surface.x<minX-.0001f || surface.x>maxX+.0001f) return false;
            float gap=BallRadius(ball)+EffectiveEdgeRadius()+GapSkin;
            Vector3 candidate=surface+(Vector3)(HandNormal(side)*gap);
            candidate.z=ball.transform.position.z;
            if(!IsCenterAdmitted(candidate)) return false;
            Vector3 pending=PendingCandidate(surface,side,gap);
            pending.z=candidate.z;
            return IsCenterAdmitted(pending);
        }
        float ContactSide(Collision2D collision,Rigidbody2D ball)
        {
            // The callback normal faces this barrier. Use its opposite only to
            // identify the contacted face; the rebound still uses the straight
            // hand-to-hand normal, never the slope of the visual deformation.
            float facing=0;
            for(int i=0;i<collision.contactCount;i++)
            {
                float candidate=Vector2.Dot(-collision.GetContact(i).normal,transform.up);
                if(Mathf.Abs(candidate)>Mathf.Abs(facing)) facing=candidate;
            }
            if(Mathf.Abs(facing)>.001f) return Mathf.Sign(facing);
            Vector2 local=transform.InverseTransformPoint(ball.position);
            float surfaceY=offset*ShapeAt(Mathf.Clamp01(local.x/length+.5f));
            return local.y>=surfaceY ? 1 : -1;
        }
        void OnCollisionStay2D(Collision2D collision)
        {
            if(collision.rigidbody==null || collision.contactCount==0) return;
            var ball=collision.rigidbody.GetComponent<AirHockeyBall>();
            if(ball!=null && ball.GravityGame && ball.CooperativeGame!=null)
            {
                float attachment=0;
                Vector2 incoming=RecoveredIncoming(collision,ball);
                if(courtConfigured && (!ValidateCourtContact(ball,collision,out attachment) ||
                    !CanHoldProspectively(ball,attachment,ContactSide(collision,ball.Body))))
                {
                    RemoveUnfilteredImpulse(ball,incoming);
                    return;
                }
                // Stay is the only callback for a contact that began before Light
                // was armed, so a genuine retained contact must also cancel the spell.
                ball.CooperativeGame.NotifyBarrierContact();
                RemoveUnfilteredImpulse(ball,incoming);
            }
        }
        static void RemoveUnfilteredImpulse(AirHockeyBall ball,Vector2 incoming)
        {
            // During the same-contact cooldown a moving barrier can catch the ball
            // again. Enter and Stay both run after the solver, so undo its impulse.
            if(!ball.IsInBarrierImpact && ball.CooperativeGame.CanAdvanceBall)
                ball.Body.linearVelocity=Vector2.ClampMagnitude(incoming,ball.maximumSpeed);
        }
        bool TryHoldBall(AirHockeyBall ball,float side,float speed,Vector2 incoming,float attachment)
        {
            if(IsHoldingBall) return false;
            if(ball.CooperativeGame.TryScoreFloorBeforeBarrier(ball.Body.position)) return true;
            // Refuse to consume the ball's impact state for a hold that cannot be
            // placed. Light notification has already used the same prospective test.
            if(courtConfigured && !CanHoldProspectively(ball,attachment,side)) return false;
            if(!ball.BeginBarrierImpact(this)) return false;
            heldBall=ball;impactIncoming=incoming;
            // The contact parameter is full-line, even when only a clipped run
            // produced the contact. This keeps deformation and boost placement
            // in the original hand-to-hand coordinate space.
            ballPoint=courtConfigured ? Mathf.Clamp01(attachment)
                : Mathf.Clamp01(transform.InverseTransformPoint(ball.Body.position).x/length+.5f);
            impactPoint=Mathf.Clamp(ballPoint,.04f,.96f);
            bendPoint=impactPoint;
            impactSide=side>=0 ? 1 : -1;
            impactBoost=0;UpdateImpactBoost();
            ballGap=BallRadius(ball)+EffectiveEdgeRadius()+GapSkin;
            impactStart=offset;
            float depth=Mathf.Min(length*.22f,Mathf.Lerp(.22f,.48f,softness));
            depth*=Mathf.Lerp(.55f,1,Mathf.Sin(Mathf.PI*impactPoint))*Mathf.Lerp(.7f,1.1f,Mathf.InverseLerp(2,10,speed));
            impactDepth=-impactSide*depth;
            impactAge=0; impactDuration=Mathf.Lerp(.17f,.20f,softness);
            velocity=0;
            lastAcceptedBallPosition=ball.Body.position;
            Draw();
            // bendPoint changed the retained shape even though offset has not moved.
            if(courtConfigured) BuildCourtGeometry(Mathf.Max(0,physicsDt));
            if(!PositionHeldBall()) return true;
            // Attribution only: spell strength is consumed once at release.
            ball.CooperativeGame.RegisterBarrierHit(PlayerSide);
            return true;
        }
        void UpdateImpactBoost()
        {
            double now=Time.unscaledTimeAsDouble;
            if(now<handMotionAt || now-handMotionAt>.15) { impactBoost=0;return; }
            Vector2 normal=(Vector2)transform.up*impactSide;
            impactBoost=Mathf.Max(impactBoost,HandMotionBoost(ballPoint,normal,now));
        }
        Vector2 CurrentRebound()
        {
            if(heldBall==null || heldBall.CooperativeGame==null) return Vector2.zero;
            UpdateImpactBoost();
            Vector2 normal=(Vector2)transform.up*impactSide;
            return heldBall.CooperativeGame.BarrierBounce(impactIncoming,normal,softness,heldBall.Body.position,impactBoost);
        }
        bool PositionHeldBall()
        {
            if(heldBall==null) return false;
            // The surface point is world space and the world-normal gap is applied
            // once; adding it to the local y would be scaled a second time.
            Vector3 position=CurrentCandidate(ballPoint,impactSide,ballGap);
            position.z=heldBall.transform.position.z;
            if(courtConfigured)
            {
                Vector3 pending=PendingCandidate(ballPoint,impactSide,ballGap);
                pending.z=position.z;
                if(!tracking || !IsParameterRetained(ballPoint) ||
                    !IsCenterAdmitted(position) || !IsCenterAdmitted(pending))
                {
                    // Compression escaped the court or the retained run. Detach at the
                    // last accepted position; never clamp or drag along the centre.
                    AbortHeldImpact();
                    return false;
                }
            }
            lastAcceptedBallPosition=position;
            heldBall.Body.position=position;
            heldBall.transform.position=position;
            return true;
        }
        void AbortHeldImpact()
        {
            var ball=heldBall;
            if(ball==null) return;
            heldBall=null; releaseAfterPhysics=false;
            Vector3 position=lastAcceptedBallPosition;
            position.z=ball.transform.position.z;
            ball.Body.position=position;
            ball.transform.position=position;
            ball.AbortCourtImpact(this,impactIncoming);
            offset=velocity=0; impactBoost=0; bendPoint=.5f;
            Draw();
        }
        void CompleteImpact(Vector2 outgoing)
        {
            releaseAfterPhysics=false;
            var ball=heldBall; heldBall=null;
            if(ball!=null) ball.FinishBarrierImpact(this,outgoing);
        }
        void ReleaseImpact()
        {
            if(heldBall==null) { releaseAfterPhysics=false; return; }
            var game=heldBall.CooperativeGame;
            Vector2 outgoing=CurrentRebound();
            Vector2 position=heldBall.Body.position;
            float boost=impactBoost;
            CompleteImpact(outgoing);
            if(game!=null) game.NotifyBarrierReleased(position,PlayerSide,boost);
        }
        public void CancelImpact(bool release)
        {
            if(!IsHoldingBall) return;
            offset=velocity=0;
            if(release && (heldBall.CooperativeGame==null || heldBall.CooperativeGame.CanAdvanceBall))
            {
                if(!PositionHeldBall()) return;
            }
            if(!IsHoldingBall) return;
            CompleteImpact(release ? CurrentRebound() : Vector2.zero);
            Draw();
        }
        public void ResetSpring()
        {
            ClearHandMotion();
            CancelImpact(true);
            offset=velocity=0; bendPoint=.5f;
            if(edge!=null && line!=null) Draw();
        }
        void OnDisable()
        {
            if(courtConfigured) DisableColliders();
            ResetSpring();
        }
        void OnDestroy()
        {
            if(courtConfigured) DisableColliders();
            CancelImpact(true);
            if(material == null) return;
            if(Application.isPlaying) Destroy(material); else DestroyImmediate(material);
        }

        void EnsurePool(int required=1)
        {
            if(edge==null) return;
            if(poolCount==0) pool[poolCount++]=edge;
            while(poolCount<Mathf.Min(pool.Length,required))
            {
                var collider=gameObject.AddComponent<EdgeCollider2D>();
                collider.enabled=false;
                collider.edgeRadius=edge.edgeRadius;
                collider.sharedMaterial=material;
                pool[poolCount++]=collider;
            }
        }
        void DisableColliders()
        {
            for(int i=0;i<poolCount;i++) if(pool[i]!=null) pool[i].enabled=false;
        }
        void BuildCourtGeometry(float dt)
        {
            if(edge==null) return;
            if(poolCount==0) EnsurePool();
            if(poolCount==0) return;
            float radius=0;
            for(int i=0;i<Samples;i++)
            {
                worldPoints[i]=transform.TransformPoint(points[i]);
                radius=Mathf.Max(radius,Vector2.Distance(worldPoints[i],transform.position));
            }
            CourtBounds(radius,out float xMin,out float xMax);
            runTotal=0;
            if(xMin<=xMax) ClipRuns(xMin,xMax);
            ApplyRuns();
            ApplyAdmission(dt,courtGame!=null ? courtGame.Ball : null);
        }
        void CourtBounds(float radius,out float xMin,out float xMax)
        {
            Vector2 currentPosition=transform.position;
            float currentRotation=transform.eulerAngles.z;
            Vector2 targetPosition=hasPoseTarget ? poseTarget : currentPosition;
            float targetRotation=hasPoseTarget ? poseTargetRotation : currentRotation;
            float rotation=Mathf.Abs(Mathf.DeltaAngle(currentRotation,targetRotation))*Mathf.Deg2Rad;
            Vector2 translation=targetPosition-currentPosition;
            // Moving toward the outer line needs outward clearance; moving toward the
            // centre needs inward clearance. They are separate swept allowances.
            float inwardTranslation=Mathf.Max(0,-courtSide*translation.x);
            float outwardTranslation=Mathf.Max(0,courtSide*translation.x);
            float sweptRotation=2*radius*Mathf.Sin(Mathf.Min(rotation,Mathf.PI)*.5f);
            float inwardSwept=inwardTranslation+sweptRotation;
            float outwardSwept=outwardTranslation+sweptRotation;
            float skin=GapSkin;
            float edgeRadius=EffectiveEdgeRadius();
            AirHockeyBall ball=courtGame!=null ? courtGame.Ball : null;
            float ballRadius=BallRadius(ball);
            float centerClear=ballRadius+edgeRadius+skin+inwardSwept;
            float outerClear=edgeRadius+skin+outwardSwept;
            if(courtSide>0)
            {
                xMin=courtCenterX+centerClear;
                xMax=courtHalfWidth>0 ? courtCenterX+courtHalfWidth-outerClear : float.PositiveInfinity;
            }
            else
            {
                xMin=courtHalfWidth>0 ? courtCenterX-courtHalfWidth+outerClear : float.NegativeInfinity;
                xMax=courtCenterX-centerClear;
            }
        }
        void ClipRuns(float xMin,float xMax)
        {
            int current=-1;
            bool previousRetainedEnd=false;
            for(int i=0;i<Samples-1;i++)
            {
                Vector2 a=worldPoints[i], b=worldPoints[i+1];
                float dx=b.x-a.x;
                float t0=0,t1=1;
                if(Mathf.Abs(dx)<1e-6f)
                {
                    if(a.x<xMin || a.x>xMax) { previousRetainedEnd=false; continue; }
                }
                else
                {
                    float ta=(xMin-a.x)/dx;
                    float tb=(xMax-a.x)/dx;
                    if(ta>tb) { float swap=ta;ta=tb;tb=swap; }
                    if(ta>t0) t0=ta;
                    if(tb<t1) t1=tb;
                    if(t1<t0) { previousRetainedEnd=false; continue; }
                }
                if(t1-t0<1e-6f) { previousRetainedEnd=false; continue; }
                Vector2 p0=Vector2.LerpUnclamped(a,b,t0);
                Vector2 p1=Vector2.LerpUnclamped(a,b,t1);
                if((p1-p0).sqrMagnitude<1e-10f) { previousRetainedEnd=false; continue; }
                bool connect=t0<=1e-5f && previousRetainedEnd && current>=0;
                if(connect) AddRunPoint(current,p1);
                else
                {
                    if(runTotal>=MaxRuns) break;
                    current=runTotal++;
                    runCounts[current]=0;
                    AddRunPoint(current,p0);
                    AddRunPoint(current,p1);
                }
                previousRetainedEnd=t1>=1f-1e-5f;
            }
        }
        void AddRunPoint(int run,Vector2 world)
        {
            Vector2 local=transform.InverseTransformPoint(world);
            int count=runCounts[run];
            if(count>0 && (runScratch[run*Samples+count-1]-local).sqrMagnitude<1e-10f) return;
            if(count>=Samples) return;
            runScratch[run*Samples+count]=local;
            float t=Mathf.Clamp01(local.x/length+.5f);
            if(count==0) runTMin[run]=runTMax[run]=t;
            else
            {
                if(t<runTMin[run]) runTMin[run]=t;
                if(t>runTMax[run]) runTMax[run]=t;
            }
            runCounts[run]=count+1;
        }
        void ApplyRuns()
        {
            EnsurePool(runTotal);
            for(int r=0;r<runTotal;r++)
            {
                var collider=pool[r];
                int count=runCounts[r];
                if(collider==null || count<2) { if(collider!=null) collider.enabled=false; continue; }
                var cached=runCache[r];
                if(cached==null || cached.Length!=count)
                {
                    cached=new Vector2[count];
                    runCache[r]=cached;
                }
                System.Array.Copy(runScratch,r*Samples,cached,0,count);
                collider.points=cached;
                collider.edgeRadius=edge.edgeRadius;
                collider.sharedMaterial=material;
                collider.offset=Vector2.zero;
                collider.enabled=true;
            }
            for(int r=runTotal;r<poolCount;r++) if(pool[r]!=null) pool[r].enabled=false;
        }
        void ApplyAdmission(float dt,AirHockeyBall ball)
        {
            if(ball==null) return;
            var ballCollider=ball.GetComponent<CircleCollider2D>();
            if(ballCollider==null) return;
            bool admit=AdmitBall(ball,dt);
            for(int i=0;i<poolCount;i++)
            {
                var collider=pool[i];
                if(collider!=null) Physics2D.IgnoreCollision(ballCollider,collider,!admit);
            }
        }
        bool AdmitBall(AirHockeyBall ball,float dt)
        {
            if(ball==null || ball.Body==null) return true;
            Vector2 position=ball.Body.position;
            float own=courtSide*(position.x-courtCenterX);
            if(own>=-0.0001f) return true;
            float radius=BallRadius(ball);
            float speed=Mathf.Max(ball.maximumSpeed,ball.Body.linearVelocity.magnitude);
            // Conservative: include a small correction allowance. If uncertain,
            // leave the pair admitted; the clipped clearance provides the guarantee.
            float travel=speed*Mathf.Max(0,dt)+0.05f;
            return -own-radius<=travel;
        }
        static float BallRadius(AirHockeyBall ball)
        {
            if(ball==null) return 0;
            var collider=ball.GetComponent<CircleCollider2D>();
            if(collider==null) return 0;
            Vector3 scale=ball.transform.lossyScale;
            return collider.radius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y));
        }
        float EffectiveEdgeRadius()
        {
            if(edge==null) return 0;
            Vector3 scale=transform.lossyScale;
            return edge.edgeRadius*Mathf.Max(Mathf.Abs(scale.x),Mathf.Abs(scale.y));
        }
        bool ValidateCourtContact(AirHockeyBall ball,Collision2D collision,out float attachment)
        {
            attachment=0;
            if(!courtConfigured) return true;
            if(ball==null || ball.Body==null) return false;
            Vector2 center=ball.Body.position;
            float own=courtSide*(center.x-courtCenterX);
            if(own<-0.0001f) return false;
            if(courtHalfWidth>0 && own>courtHalfWidth+0.0001f) return false;
            if(runTotal==0) return false;
            float contact=0;
            int count=0;
            for(int i=0;i<collision.contactCount;i++)
            {
                Vector3 local=transform.InverseTransformPoint(collision.GetContact(i).point);
                contact+=Mathf.Clamp01(local.x/length+.5f);
                count++;
            }
            if(count==0) return false;
            // The ball centre can sit outside the clipped run while its rounded
            // end is still touched. Use the actual contact, then snap to the
            // closest retained run in full-line parameter space.
            return TryRetainedParameter(contact/count,out attachment);
        }
        bool TryRetainedParameter(float t,out float retained)
        {
            retained=Mathf.Clamp01(t);
            if(runTotal==0) return false;
            float tolerance=ParameterTolerance();
            float best=float.MaxValue, nearest=retained;
            for(int i=0;i<runTotal;i++)
            {
                float min=runTMin[i], max=runTMax[i];
                if(t>=min-tolerance && t<=max+tolerance)
                {
                    retained=Mathf.Clamp(t,min,max);
                    return true;
                }
                float distance=t<min ? min-t : t-max;
                if(distance<best) { best=distance;nearest=t<min ? min : max; }
            }
            retained=Mathf.Clamp01(nearest);
            return true;
        }
        bool IsParameterRetained(float t)
        {
            if(runTotal==0) return false;
            float tolerance=ParameterTolerance();
            for(int i=0;i<runTotal;i++)
                if(t>=runTMin[i]-tolerance && t<=runTMax[i]+tolerance) return true;
            return false;
        }
        float ParameterTolerance()
        {
            float worldLength=transform.TransformVector(Vector3.right*length).magnitude;
            return Mathf.Max(0.001f,EffectiveEdgeRadius()/Mathf.Max(worldLength,0.001f)+0.01f);
        }
        bool IsCenterAdmitted(Vector3 position)
        {
            float own=courtSide*(position.x-courtCenterX);
            if(own<-0.0001f) return false;
            return courtHalfWidth<=0 || own<=courtHalfWidth+0.0001f;
        }
    }
}
