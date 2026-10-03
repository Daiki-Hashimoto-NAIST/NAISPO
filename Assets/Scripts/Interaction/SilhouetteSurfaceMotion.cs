using System.Collections.Generic;
using UnityEngine;

namespace Naispo.Interaction
{
    // Replaceable contact-motion provider: stores only world-space polygons and time.
    // Kinect acquisition, image pixels, and SDK identities are intentionally absent.
    public sealed class SilhouetteSurfaceMotion : MonoBehaviour
    {
        List<Vector2>[] previous=CreatePaths(),current=CreatePaths();
        int previousCount,currentCount;
        double sampleTime,previousTime;
        bool hasSample,valid;
        static List<Vector2>[] CreatePaths()
        {var paths=new List<Vector2>[8];for(int i=0;i<8;i++)paths[i]=new List<Vector2>(256);return paths;}
        public void BeginSample(double time,bool reset)
        {
            var recycle=previous;previous=current;current=recycle;
            previousCount=currentCount;currentCount=0;
            previousTime=sampleTime;sampleTime=time;
            valid=hasSample&&!reset&&time>previousTime&&time-previousTime<0.3;
            hasSample=true;
        }
        public void AddPath(List<Vector2> worldPoints)
        {if(currentCount>=current.Length)return;var path=current[currentCount++];path.Clear();path.AddRange(worldPoints);}
        public float EstimateOutwardSpeed(Vector2 contact,double now,float maximumSpeed)
        {
            if(!valid||now-sampleTime>0.15||previousCount==0)return 0;
            float distanceSquared=float.MaxValue;bool inside=false;
            for(int path=0;path<previousCount;path++)
            {
                var p=previous[path];bool inPath=false;
                for(int i=0,j=p.Count-1;i<p.Count;j=i++)
                {
                    Vector2 a=p[j],b=p[i],v=b-a;
                    float t=v.sqrMagnitude>0?Mathf.Clamp01(Vector2.Dot(contact-a,v)/v.sqrMagnitude):0;
                    distanceSquared=Mathf.Min(distanceSquared,(contact-(a+t*v)).sqrMagnitude);
                    if((a.y>contact.y)!=(b.y>contact.y)&&contact.x<(b.x-a.x)*(contact.y-a.y)/(b.y-a.y)+a.x)inPath=!inPath;
                }
                inside|=inPath;
            }
            if(inside)return 0;
            return Mathf.Min(maximumSpeed,Mathf.Sqrt(distanceSquared)/(float)(sampleTime-previousTime));
        }
        public void Clear(){hasSample=valid=false;currentCount=previousCount=0;}
    }
}
