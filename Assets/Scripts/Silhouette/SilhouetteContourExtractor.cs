using System;
using System.Collections.Generic;
using UnityEngine;

namespace Naispo.Silhouette
{
    // Reusable CPU mask -> 4-connected components -> pixel-edge boundary -> closed RDP.
    // Only exterior loops are emitted, so holes are intentionally solid.
    // This class knows nothing about SDK handles, colors, GameObjects or physics.
    public sealed class SilhouetteContourExtractor
    {
        public sealed class Path
        {
            public readonly List<Vector2> Points = new List<Vector2>(256);
            public int Area;
        }
        struct Component { public int Label, Area, Start; public long XSum, YSum; }
        public readonly Path[] Paths = new Path[8];
        public int PathCount { get; private set; }
        public int TotalPoints { get; private set; }
        public int RejectedContours { get; private set; }
        public Vector2 Centroid { get; private set; }
        byte[] mask = Array.Empty<byte>();
        int[] labels = Array.Empty<int>(), queue = Array.Empty<int>(), stack = Array.Empty<int>();
        bool[] keep = Array.Empty<bool>();
        readonly Component[] best = new Component[8];
        readonly List<Vector2> raw = new List<Vector2>(4096);
        int width, height, bestCount;
        static readonly int[] Dx = {1,0,-1,0}, Dy = {0,1,0,-1};

        public SilhouetteContourExtractor() { for (int i=0;i<8;i++) Paths[i]=new Path(); }

        public void Extract(byte[] indices, int imageWidth, int imageHeight, int[] indexSlots, int slot,
            int minimumArea, int neighborThreshold, float epsilon, int maximumPoints, int maximumComponents)
        {
            width=imageWidth; height=imageHeight;
            int size=checked(width*height);
            if(mask.Length!=size) { mask=new byte[size]; labels=new int[size]; queue=new int[size]; }
            Array.Clear(labels,0,size);
            PathCount=TotalPoints=RejectedContours=bestCount=0;
            maximumPoints=Math.Max(16,maximumPoints);
            maximumComponents=Math.Max(1,Math.Min(Math.Min(8,maximumComponents),maximumPoints/3));
            for(int y=0;y<height;y++) for(int x=0;x<width;x++)
            {
                int p=y*width+x;
                bool inside=indexSlots[indices[p]]==slot;
                if(inside && neighborThreshold>0)
                {
                    int neighbors=0;
                    for(int oy=-1;oy<=1;oy++) for(int ox=-1;ox<=1;ox++)
                        if((ox!=0||oy!=0)&&x+ox>=0&&x+ox<width&&y+oy>=0&&y+oy<height&&indexSlots[indices[(y+oy)*width+x+ox]]==slot) neighbors++;
                    inside=neighbors>=neighborThreshold;
                }
                mask[p]=(byte)(inside?1:0);
            }
            int label=0;
            for(int p=0;p<size;p++)
            {
                if(mask[p]==0||labels[p]!=0) continue;
                var c=new Component{Label=++label,Start=p};
                int head=0,tail=0; queue[tail++]=p; labels[p]=label;
                while(head<tail)
                {
                    int q=queue[head++], x=q%width,y=q/width;
                    c.Area++; c.XSum+=x; c.YSum+=y;
                    if(x>0) Visit(q-1,label,ref tail);
                    if(x+1<width) Visit(q+1,label,ref tail);
                    if(y>0) Visit(q-width,label,ref tail);
                    if(y+1<height) Visit(q+width,label,ref tail);
                }
                if(c.Area<minimumArea) continue;
                int insert=bestCount;
                while(insert>0&&best[insert-1].Area<c.Area) insert--;
                if(insert>=maximumComponents) continue;
                int end=Math.Min(bestCount,maximumComponents-1);
                for(int j=end;j>insert;j--) best[j]=best[j-1];
                best[insert]=c; bestCount=Math.Min(maximumComponents,bestCount+1);
            }
            double sx=0,sy=0,totalArea=0;
            for(int i=0;i<bestCount;i++)
            {
                var c=best[i];
                if(!Trace(c)) { RejectedContours++; continue; }
                var path=Paths[PathCount];
                int remaining=maximumPoints-TotalPoints;
                int budget=Math.Max(3,remaining-3*(bestCount-i-1));
                bool valid=false;
                float e=Math.Max(0.1f,epsilon);
                for(int attempt=0;attempt<12;attempt++)
                {
                    Simplify(e,path.Points);
                    if(path.Points.Count>=3&&path.Points.Count<=budget&&IsSimple(path.Points)) {valid=true;break;}
                    e*=1.35f;
                }
                if(!valid) { path.Points.Clear(); RejectedContours++; continue; }
                path.Area=c.Area;
                TotalPoints+=path.Points.Count;
                PathCount++;
                sx+=c.XSum+c.Area*0.5; sy+=c.YSum+c.Area*0.5; totalArea+=c.Area;
            }
            Centroid=totalArea>0?new Vector2((float)(sx/totalArea),(float)(sy/totalArea)):Vector2.zero;
        }

        void Visit(int p,int label,ref int tail)
        { if(mask[p]!=0&&labels[p]==0){labels[p]=label;queue[tail++]=p;} }
        int LabelAt(int x,int y) => x>=0&&x<width&&y>=0&&y<height?labels[y*width+x]:0;
        bool HasEdge(int x,int y,int direction,int label)
        {
            switch(direction)
            {
                case 0:return LabelAt(x,y)==label&&LabelAt(x,y-1)!=label;
                case 1:return LabelAt(x-1,y)==label&&LabelAt(x,y)!=label;
                case 2:return LabelAt(x-1,y-1)==label&&LabelAt(x-1,y)!=label;
                default:return LabelAt(x,y-1)==label&&LabelAt(x-1,y-1)!=label;
            }
        }
        bool Trace(Component c)
        {
            raw.Clear();
            int startX=c.Start%width,startY=c.Start/width,x=startX,y=startY,dir=0;
            int limit=c.Area*4+4;
            do
            {
                raw.Add(new Vector2(x,y));
                x+=Dx[dir]; y+=Dy[dir];
                int right=(dir+1)&3,left=(dir+3)&3,back=(dir+2)&3;
                if(HasEdge(x,y,right,c.Label))dir=right;
                else if(!HasEdge(x,y,dir,c.Label))
                { if(HasEdge(x,y,left,c.Label))dir=left; else if(HasEdge(x,y,back,c.Label))dir=back; else return false; }
                if(--limit==0)return false;
            }while(x!=startX||y!=startY||dir!=0);
            return raw.Count>=4;
        }
        void Simplify(float epsilon,List<Vector2> result)
        {
            int n=raw.Count;
            if(keep.Length<n+1){int capacity=Mathf.NextPowerOfTwo(n+1);keep=new bool[capacity];stack=new int[capacity*2];}
            Array.Clear(keep,0,n+1);
            int split=1;float far=0;
            for(int i=1;i<n;i++){float d=(raw[i]-raw[0]).sqrMagnitude;if(d>far){far=d;split=i;}}
            raw.Add(raw[0]);
            keep[0]=keep[split]=keep[n]=true;
            Reduce(0,split,epsilon*epsilon);Reduce(split,n,epsilon*epsilon);
            result.Clear();for(int i=0;i<n;i++)if(keep[i])result.Add(raw[i]);
            raw.RemoveAt(n);
        }
        void Reduce(int first,int last,float epsilonSquared)
        {
            int top=0;stack[top++]=first;stack[top++]=last;
            while(top>0)
            {
                int b=stack[--top],a=stack[--top],index=-1;float largest=epsilonSquared;
                Vector2 start=raw[a],v=raw[b]-start;float length=v.sqrMagnitude;
                for(int i=a+1;i<b;i++)
                {
                    float t=length>0?Mathf.Clamp01(Vector2.Dot(raw[i]-start,v)/length):0;
                    float distance=(raw[i]-(start+t*v)).sqrMagnitude;
                    if(distance>largest){largest=distance;index=i;}
                }
                if(index<0)continue;
                keep[index]=true;
                stack[top++]=a;stack[top++]=index;stack[top++]=index;stack[top++]=b;
            }
        }
        public static bool IsSimple(List<Vector2> p)
        {
            double area=0;
            for(int i=0;i<p.Count;i++)
            {
                Vector2 a=p[i],b=p[(i+1)%p.Count];area+=(double)a.x*b.y-(double)b.x*a.y;
                for(int j=i+2;j<p.Count;j++)
                {
                    if(i==0&&j==p.Count-1)continue;
                    Vector2 c=p[j],d=p[(j+1)%p.Count];
                    if(Intersects(a,b,c,d))return false;
                }
            }
            return Math.Abs(area)>0.5;
        }
        static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        static bool Intersects(Vector2 a,Vector2 b,Vector2 c,Vector2 d)
        {
            if(Mathf.Max(a.x,b.x)<Mathf.Min(c.x,d.x)||Mathf.Max(c.x,d.x)<Mathf.Min(a.x,b.x)||Mathf.Max(a.y,b.y)<Mathf.Min(c.y,d.y)||Mathf.Max(c.y,d.y)<Mathf.Min(a.y,b.y))return false;
            return Cross(b-a,c-a)*Cross(b-a,d-a)<=0&&Cross(d-c,a-c)*Cross(d-c,b-c)<=0;
        }
    }
}
