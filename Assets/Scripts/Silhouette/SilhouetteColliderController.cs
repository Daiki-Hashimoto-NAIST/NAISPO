using System.Collections.Generic;
using UnityEngine;
using Naispo.Core;
using Naispo.Kinect;
using Naispo.Interaction;
using Unity.Profiling;

namespace Naispo.Silhouette
{
    public sealed class SilhouetteColliderController : MonoBehaviour
    {
        public KinectSettings settings;
        public KinectSilhouetteManager manager;
        public KinectViewportMapper mapper;
        public PolygonCollider2D[] personColliders = new PolygonCollider2D[3];
        public PhysicsMaterial2D contactMaterial;
        [SerializeField] float colliderFps, lastRebuildMilliseconds;
        [SerializeField] int totalPoints, rejectedContours;
        public float ColliderFps => colliderFps;
        public float LastRebuildMilliseconds => lastRebuildMilliseconds;
        public int TotalPoints => totalPoints;
        readonly SilhouetteContourExtractor extractor = new SilhouetteContourExtractor();
        readonly List<Vector2> worldPoints = new List<Vector2>(256);
        readonly uint[] bodyIds = new uint[3];
        readonly bool[] initialized = new bool[3];
        readonly System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
        long lastSequence=-1;
        int lastRevision=-1, updates;
        double nextUpdate, measureStart;
        static readonly ProfilerMarker RebuildMarker=new ProfilerMarker("Naispo.Silhouette.ColliderRebuild");

        void FixedUpdate()
        {
            if(manager==null||manager.useHandWalls||!manager.IsFresh) {Clear();return;}
            double now=Time.fixedUnscaledTimeAsDouble;
            var frame=manager.CurrentFrame;
            bool mappingChanged=lastRevision!=mapper.Revision;
            // Missing people lose collision immediately; their color slot survives briefly.
            for(int i=0;i<3;i++)if(!manager.Registry.Slots[i].Present)Disable(i);
            if(!mappingChanged&&(now+0.000001<nextUpdate||frame.Sequence==lastSequence))return;
            nextUpdate=System.Math.Max(nextUpdate+1.0/settings.colliderUpdateRate,now);
            lastSequence=frame.Sequence;lastRevision=mapper.Revision;
            using var sample=RebuildMarker.Auto();
            timer.Restart();totalPoints=rejectedContours=0;
            for(int slot=0;slot<3;slot++)
            {
                if(!manager.Registry.Slots[slot].Present)continue;
                var collider=personColliders[slot];
                if(collider==null)continue;
                extractor.Extract(frame.BodyIndexMap,frame.Width,frame.Height,manager.Registry.BodyIndexToSlot,slot,
                    settings.minimumComponentArea,settings.noiseNeighborThreshold,settings.contourEpsilon,
                    settings.maximumContourPoints,settings.maximumComponentsPerBody);
                rejectedContours+=extractor.RejectedContours;
                if(extractor.PathCount==0){Disable(slot);continue;}
                var body=collider.attachedRigidbody;
                Vector2 center=mapper.PixelToWorld(extractor.Centroid);
                uint id=manager.Registry.Slots[slot].BodyId;
                bool reset=!initialized[slot]||bodyIds[slot]!=id||mappingChanged||(body.position-center).sqrMagnitude>4;
                bodyIds[slot]=id;initialized[slot]=true;
                var motion=collider.GetComponent<SilhouetteSurfaceMotion>();
                if(motion!=null)motion.BeginSample(Time.fixedTimeAsDouble,reset);
                collider.pathCount=extractor.PathCount;
                for(int path=0;path<extractor.PathCount;path++)
                {
                    worldPoints.Clear();
                    var points=extractor.Paths[path].Points;
                    for(int p=0;p<points.Count;p++)worldPoints.Add(mapper.PixelToWorld(points[p]));
                    if(motion!=null)motion.AddPath(worldPoints);
                    for(int p=0;p<worldPoints.Count;p++)worldPoints[p]-=center;
                    collider.SetPath(path,worldPoints);
                }
                collider.sharedMaterial=contactMaterial;
                if(reset)body.position=center;else body.MovePosition(center);
                collider.enabled=true;
                totalPoints+=extractor.TotalPoints;
            }
            timer.Stop();lastRebuildMilliseconds=(float)timer.Elapsed.TotalMilliseconds;
            updates++;
            if(now-measureStart>=1){colliderFps=(float)(updates/(now-measureStart));updates=0;measureStart=now;}
        }
        void Disable(int slot)
        {
            if(slot>=personColliders.Length||personColliders[slot]==null)return;
            personColliders[slot].enabled=false;initialized[slot]=false;
            var motion=personColliders[slot].GetComponent<SilhouetteSurfaceMotion>();
            if(motion!=null)motion.Clear();
            if(personColliders[slot].attachedRigidbody!=null)personColliders[slot].attachedRigidbody.linearVelocity=Vector2.zero;
        }
        public void Clear(){for(int i=0;i<3;i++)Disable(i);totalPoints=0;colliderFps=0;}
        void OnDisable()=>Clear();
    }
}
