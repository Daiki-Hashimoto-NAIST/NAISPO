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
        PhysicsMaterial2D runtimeMaterial;
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
