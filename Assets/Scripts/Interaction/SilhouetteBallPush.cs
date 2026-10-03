using UnityEngine;

namespace Naispo.Interaction
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class SilhouetteBallPush : MonoBehaviour
    {
        [Range(0,2)] public float strength=1;
        [Min(0)] public float maximumSurfaceSpeed=6;
        [Min(0)] public float maximumAddedSpeed=3;
        Rigidbody2D body;
        void Awake(){body=GetComponent<Rigidbody2D>();}
        void OnCollisionEnter2D(Collision2D collision)=>Push(collision);
        void OnCollisionStay2D(Collision2D collision)=>Push(collision);
        void Push(Collision2D collision)
        {
            var motion=collision.collider.GetComponent<SilhouetteSurfaceMotion>();
            if(motion==null||collision.contactCount==0)return;
            var contact=collision.GetContact(0);
            float desired=motion.EstimateOutwardSpeed(contact.point,Time.fixedTimeAsDouble,maximumSurfaceSpeed)*strength;
            float missing=desired-Vector2.Dot(body.linearVelocity,contact.normal);
            if(desired>0.02f&&missing>0)
                body.AddForce(contact.normal*(Mathf.Min(missing,maximumAddedSpeed)*body.mass),ForceMode2D.Impulse);
        }
    }
}
