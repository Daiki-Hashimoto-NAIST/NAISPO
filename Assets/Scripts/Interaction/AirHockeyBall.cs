using UnityEngine;

namespace Naispo.Interaction
{
    [RequireComponent(typeof(Rigidbody2D),typeof(CircleCollider2D),typeof(SpriteRenderer))]
    public sealed class AirHockeyBall : MonoBehaviour
    {
        public Vector2 initialVelocity=new Vector2(2.2f,1.3f);
        [Min(0.1f)] public float maximumSpeed=12;
        [Range(0,1)] public float linearDamping=0.025f;
        public PhysicsMaterial2D physicsMaterial;
        public Rigidbody2D Body {get;private set;}
        public bool GravityGame { get; set; }
        public CooperativeCoinGame CooperativeGame { get; set; }
        public bool IsInBarrierImpact => impactOwner != null;
        PhysicsMaterial2D runtimeMaterial;
        ElasticBarrier impactOwner, previousImpact;
        RigidbodyInterpolation2D freeInterpolation;
        bool gameplaySimulation = true;
        float impactCooldown;
        void Awake()
        {
            Body=GetComponent<Rigidbody2D>();
            Body.bodyType=RigidbodyType2D.Dynamic;
            Body.gravityScale=0;Body.linearDamping=linearDamping;
            Body.collisionDetectionMode=CollisionDetectionMode2D.Continuous;
            Body.interpolation=RigidbodyInterpolation2D.Interpolate;
            Body.constraints=RigidbodyConstraints2D.FreezeRotation;
            runtimeMaterial=ConfigureContact(GetComponent<CircleCollider2D>(),physicsMaterial);
            Body.linearVelocity=initialVelocity;
        }
        void FixedUpdate()
        { if(Body.linearVelocity.sqrMagnitude>maximumSpeed*maximumSpeed)Body.linearVelocity=Body.linearVelocity.normalized*maximumSpeed; }

        // The game and the impact each own one reason to suspend the same body.
        // Neither may accidentally resume physics while the other is still paused.
        public void SetGameplaySimulation(bool running)
        {
            gameplaySimulation=running;
            if(Body != null) Body.simulated=running && isActiveAndEnabled && !IsInBarrierImpact;
        }
        internal bool BeginBarrierImpact(ElasticBarrier owner)
        {
            if(IsInBarrierImpact || !Body.simulated || CooperativeGame == null || !CooperativeGame.CanAdvanceBall ||
                (previousImpact==owner && Time.fixedTime<impactCooldown)) return false;
            impactOwner=owner;
            freeInterpolation=Body.interpolation;
            Body.interpolation=RigidbodyInterpolation2D.None;
            Body.linearVelocity=Vector2.zero;
            Body.simulated=false;
            return true;
        }
        internal void FinishBarrierImpact(ElasticBarrier owner,Vector2 rebound)
        {
            if(impactOwner!=owner) return;
            impactOwner=null;
            previousImpact=owner; impactCooldown=Time.fixedTime+.12f;
            Body.interpolation=freeInterpolation;
            // One narrow versus hook consumes an armed return spell immediately
            // before the existing final speed clamp. A cancelled zero rebound
            // never reaches it, so the spell is not wasted.
            if(CooperativeGame!=null)rebound=CooperativeGame.ApplyPendingSpellToRebound(rebound);
            Body.linearVelocity=Vector2.ClampMagnitude(rebound,maximumSpeed);
            SetGameplaySimulation(gameplaySimulation && (CooperativeGame==null || CooperativeGame.CanAdvanceBall));
        }
        // A court restriction can invalidate an otherwise valid hold while the
        // barrier moves. Detach without a reflected return, boost, spell use, or
        // release notification; restore the pre-contact motion instead.
        public void AbortCourtImpact(ElasticBarrier owner,Vector2 incoming)
        {
            if(impactOwner!=owner) return;
            impactOwner=null;
            previousImpact=owner; impactCooldown=Time.fixedTime+.12f;
            Body.interpolation=freeInterpolation;
            Body.linearVelocity=Vector2.ClampMagnitude(incoming,maximumSpeed);
            SetGameplaySimulation(gameplaySimulation && (CooperativeGame==null || CooperativeGame.CanAdvanceBall));
        }
        public void CancelBarrierImpact()
        {
            if(impactOwner!=null) impactOwner.CancelImpact(false);
            previousImpact=null; impactCooldown=0;
        }
        void OnDisable() { CancelBarrierImpact(); }
        public static PhysicsMaterial2D ConfigureContact(Collider2D collider, PhysicsMaterial2D material)
        {
            var instance=material!=null ? new PhysicsMaterial2D("Ball contact") { friction=material.friction,bounciness=material.bounciness,frictionCombine=material.frictionCombine }
                : new PhysicsMaterial2D("Ball contact") { friction=0,bounciness=0.9f };
            // Respect a soft barrier instead of the original ball material's Maximum rule.
            instance.bounceCombine=PhysicsMaterialCombine2D.Minimum;
            collider.sharedMaterial=instance;
            return instance;
        }
        void OnDestroy() { if(runtimeMaterial!=null) { if(Application.isPlaying)Destroy(runtimeMaterial);else DestroyImmediate(runtimeMaterial); } }
    }
}
