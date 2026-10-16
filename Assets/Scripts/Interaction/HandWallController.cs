using UnityEngine;
using Naispo.Core;
using Naispo.Kinect;
using Naispo.Silhouette;

namespace Naispo.Interaction
{
    // One independent wall per persistent person ID; never connect different people.
    public sealed class HandWallController : MonoBehaviour
    {
        public float thickness = 0.16f;
        KinectSilhouetteManager manager;
        readonly EdgeCollider2D[] walls = new EdgeCollider2D[3];
        readonly ElasticBarrier[] barriers = new ElasticBarrier[3];
        readonly LineRenderer[] lines = new LineRenderer[3];
        readonly Material[] materials = new Material[3];
        readonly Texture2D[] textures = new Texture2D[3];
        readonly uint[] owners = new uint[3];
        readonly bool[] active = new bool[3];
        public EdgeCollider2D Wall(int slot) => walls[slot];

        void Start()
        {
            manager = GetComponent<KinectSilhouetteManager>();
            Initialize(manager.silhouetteRenderer.silhouetteMaterial, manager.settings.barrierColors,
                manager.colliderController.contactMaterial);
            for(int i=0;i<3;i++) barriers[i].settings = manager.settings;
        }

        public void Initialize(Material template, Color[] colors, PhysicsMaterial2D physics)
        {
            if (walls[0] != null) return;
            for (int i = 0; i < 3; i++)
            {
                var go = new GameObject("Hand wall " + (i + 1));
                go.transform.SetParent(transform, false);
                var body = go.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                walls[i] = go.AddComponent<EdgeCollider2D>();
                walls[i].enabled = false;
                lines[i] = go.AddComponent<LineRenderer>();
                textures[i] = new Texture2D(1,1);
                textures[i].SetPixel(0,0, colors != null && i < colors.Length ? colors[i] : Color.white);
                textures[i].Apply();
                materials[i] = new Material(template) { mainTexture = textures[i] };
                lines[i].sharedMaterial = materials[i];
                lines[i].useWorldSpace = false;
                lines[i].positionCount = 2;
                lines[i].sortingOrder = 10;
                lines[i].enabled = false;
                barriers[i] = go.AddComponent<ElasticBarrier>();
                barriers[i].Initialize(walls[i], lines[i]);
            }
        }

        void FixedUpdate()
        {
            if (manager == null) return;
            Apply(manager.CurrentFrame, manager.Registry, manager.mapper, manager.IsFresh && manager.useHandWalls);
        }

        public static bool TryEndpoints(BodyFrameData.HandPair hands, KinectViewportMapper mapper, out Vector2 left, out Vector2 right)
        {
            left = right = default;
            if (!hands.Valid || !Finite(hands.Left.X) || !Finite(hands.Left.Y) ||
                !Finite(hands.Right.X) || !Finite(hands.Right.Y)) return false;
            if (hands.Left.X < 0 || hands.Left.X >= mapper.Width || hands.Left.Y < 0 || hands.Left.Y >= mapper.Height ||
                hands.Right.X < 0 || hands.Right.X >= mapper.Width || hands.Right.Y < 0 || hands.Right.Y >= mapper.Height) return false;
            left = mapper.PixelToWorld(new Vector2(hands.Left.X + 0.5f, hands.Left.Y + 0.5f));
            right = mapper.PixelToWorld(new Vector2(hands.Right.X + 0.5f, hands.Right.Y + 0.5f));
            return (right-left).sqrMagnitude >= 0.0025f;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        public void Apply(BodyFrameData frame, BodyColorRegistry registry, KinectViewportMapper mapper, bool fresh)
        {
            for (int slot = 0; slot < 3; slot++)
            {
                int index = -1;
                if (fresh && frame != null)
                    for (int i = 0; i < frame.BodyCount; i++)
                        if (registry.BodyIndexToSlot[i] == slot) { index = i; break; }
                if (index < 0 || !TryEndpoints(frame.Hands[index], mapper, out var left, out var right))
                { Hide(slot); continue; }
                var delta = right-left;
                var center = (left+right)*0.5f;
                float angle = Mathf.Atan2(delta.y, delta.x)*Mathf.Rad2Deg;
                var body = walls[slot].attachedRigidbody;
                bool reset = !active[slot] || owners[slot] != frame.BodyIds[index] || (body.position-center).sqrMagnitude > 4;
                if (reset) { body.position = center; body.rotation = angle; }
                else { body.MovePosition(center); body.MoveRotation(angle); }
                owners[slot] = frame.BodyIds[index]; active[slot] = true;
                barriers[slot].SetSpan(delta.magnitude, frame.Hands[index].SeparationMeters, thickness, reset);
                walls[slot].enabled = true;
                lines[slot].startWidth = lines[slot].endWidth = thickness;
                lines[slot].enabled = true;
            }
        }
        void Hide(int i)
        {
            active[i] = false;
            if (walls[i] == null) return;
            walls[i].enabled = lines[i].enabled = false;
            barriers[i].ResetSpring();
            walls[i].attachedRigidbody.linearVelocity = Vector2.zero;
            walls[i].attachedRigidbody.angularVelocity = 0;
        }
        void OnDisable() { for (int i=0;i<3;i++) Hide(i); }
        void OnDestroy()
        {
            for(int i=0;i<3;i++)
            {
                if(walls[i]!=null) Release(walls[i].gameObject);
                if(materials[i]!=null) Release(materials[i]);
                if(textures[i]!=null) Release(textures[i]);
            }
        }
        static void Release(Object obj) { if(Application.isPlaying) Destroy(obj); else DestroyImmediate(obj); }
    }
}
