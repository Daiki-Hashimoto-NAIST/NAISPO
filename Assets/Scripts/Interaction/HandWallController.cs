using UnityEngine;
using Naispo.Core;
using Naispo.Kinect;
using Naispo.Silhouette;

namespace Naispo.Interaction
{
    // One independent wall per persistent person ID; never connect different people.
    [DefaultExecutionOrder(-600)]
    public sealed class HandWallController : MonoBehaviour
    {
        public float thickness = 0.16f;
        public int PlayerSide { get; set; }
        readonly Color[] baseColors = new Color[3];
        bool presentationSet, gardenPresentation;
        Color presentationColor;
        bool softened;
        public bool Softened
        {
            get=>softened;
            set
            {
                if(softened==value)return;
                softened=value;
                for(int i=0;i<textures.Length;i++) if(textures[i]!=null)
                { textures[i].SetPixel(0,0,softened ? new Color(.74f,.46f,1) : baseColors[i]); textures[i].Apply(); }
            }
        }
        KinectSilhouetteManager manager;
        readonly EdgeCollider2D[] walls = new EdgeCollider2D[3];
        readonly ElasticBarrier[] barriers = new ElasticBarrier[3];
        readonly LineRenderer[] lines = new LineRenderer[3];
        readonly Material[] materials = new Material[3];
        readonly Texture2D[] textures = new Texture2D[3];
        readonly uint[] owners = new uint[3];
        readonly bool[] active = new bool[3];
        readonly HandPoseStabilizer[] stabilized = {new HandPoseStabilizer(),new HandPoseStabilizer(),new HandPoseStabilizer()};
        BodyTrackingSource lastSource;
        int lastGeneration=-1,lastMapRevision=-1;
        bool stabilizeTracking;
        [Min(0)] public float trackingGraceSeconds=.25f;
        [Min(0)] public float recoverySeconds=.20f;
        public bool StabilizeTracking
        {
            get=>stabilizeTracking;
            set { if(stabilizeTracking==value)return;stabilizeTracking=value;ResetTracking(); }
        }
        public bool HasUsableWall => isActiveAndEnabled&&active[0]&&walls[0]!=null&&walls[0].enabled;
        public bool HoldingLastPose => stabilizeTracking&&stabilized[0].Holding;
        public bool AcquiringHands => stabilizeTracking&&stabilized[0].Acquiring;
        public EdgeCollider2D Wall(int slot) => walls[slot];

        public void SetPresentation(Color color,bool garden)
        {
            presentationSet=true; presentationColor=color; gardenPresentation=garden;
            for(int i=0;i<walls.Length;i++)
            {
                if(walls[i]==null)continue;
                baseColors[i]=color;
                textures[i].SetPixel(0,0,softened ? new Color(.74f,.46f,1) : color);
                textures[i].Apply();
                lines[i].numCapVertices=garden ? 8 : 2;
                lines[i].numCornerVertices=4;
                var glow=walls[i].GetComponent<RallyBarrierGlow>();
                if(glow==null)glow=walls[i].gameObject.AddComponent<RallyBarrierGlow>();
                if(glow!=null)glow.Configure(lines[i],garden);
            }
        }

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
                baseColors[i]=colors != null && i < colors.Length ? colors[i] : Color.white;
                textures[i].SetPixel(0,0,softened ? new Color(.74f,.46f,1) : baseColors[i]);
                textures[i].Apply();
                materials[i] = new Material(template) { mainTexture = textures[i] };
                lines[i].sharedMaterial = materials[i];
                lines[i].useWorldSpace = false;
                lines[i].positionCount = 2;
                lines[i].sortingOrder = 10;
                lines[i].enabled = false;
                barriers[i] = go.AddComponent<ElasticBarrier>();
                barriers[i].PlayerSide=PlayerSide;
                barriers[i].Initialize(walls[i], lines[i]);
            }
            if(presentationSet)SetPresentation(presentationColor,gardenPresentation);
        }

        void FixedUpdate()
        {
            if (manager == null) return;
            if(stabilizeTracking)ApplyStabilized(Time.unscaledTimeAsDouble);
            else Apply(manager.CurrentFrame, manager.Registry, manager.mapper, manager.IsFresh && manager.useHandWalls);
        }

        void ApplyStabilized(double now)
        {
            var source=manager.source;
            if(!manager.isActiveAndEnabled||!manager.useHandWalls||source==null||!source.isActiveAndEnabled||!source.WorkerAlive||source.HasFailed||source.IsReconfiguring)
            { ResetTracking();return; }
            if(source!=lastSource||source.CaptureGeneration!=lastGeneration||manager.mapper.Revision!=lastMapRevision)
            {
                ResetTracking();lastSource=source;lastGeneration=source.CaptureGeneration;lastMapRevision=manager.mapper.Revision;
            }
            var frame=manager.CurrentFrame;
            for(int slot=0;slot<3;slot++)
            {
                int index=-1;
                if(frame!=null)for(int i=0;i<frame.BodyCount;i++)
                    if(manager.Registry.BodyIndexToSlot[i]==slot) { index=i;break; }
                Vector2 left=default,right=default;
                bool valid=manager.IsFresh&&index>=0&&TryEndpoints(frame.Hands[index],manager.mapper,out left,out right);
                var filtered=stabilized[slot];
                filtered.Observe(manager.FrameRevision,index>=0 ? frame.BodyIds[index] : (uint?)null,valid,left,right,
                    index>=0?frame.Hands[index].SeparationMeters:0,manager.LastFrameReceivedAt,now,trackingGraceSeconds,recoverySeconds);
                if(!filtered.Ready) { Hide(slot);continue; }
                if(filtered.Holding)
                {
                    // Keep the actual last applied pose, with no continuing kinematic impulse.
                    walls[slot].attachedRigidbody.linearVelocity=Vector2.zero;
                    walls[slot].attachedRigidbody.angularVelocity=0;
                    continue;
                }
                ApplyPose(slot,filtered.Left,filtered.Right,filtered.SeparationMeters,filtered.Owner);
            }
        }

        public void ResetTracking()
        {
            for(int slot=0;slot<3;slot++) { stabilized[slot].Reset();Hide(slot); }
            lastSource=null;lastGeneration=lastMapRevision=-1;
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
                ApplyPose(slot,left,right,frame.Hands[index].SeparationMeters,frame.BodyIds[index]);
            }
        }
        void ApplyPose(int slot,Vector2 left,Vector2 right,float meters,uint owner)
        {
            var delta=right-left;var center=(left+right)*.5f;
            float angle=Mathf.Atan2(delta.y,delta.x)*Mathf.Rad2Deg;
            var body=walls[slot].attachedRigidbody;
            bool reset=!active[slot]||owners[slot]!=owner||(body.position-center).sqrMagnitude>4;
            if(reset) { body.position=center;body.rotation=angle; }
            else { body.MovePosition(center);body.MoveRotation(angle); }
            owners[slot]=owner;active[slot]=true;
            barriers[slot].SetSpan(delta.magnitude,meters,thickness,reset);
            walls[slot].enabled=true;
            lines[slot].startWidth=lines[slot].endWidth=thickness*(presentationSet ? (gardenPresentation ? .55f : .40f) : 1);
            lines[slot].enabled=true;
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
        void OnDisable() { ResetTracking(); }
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
