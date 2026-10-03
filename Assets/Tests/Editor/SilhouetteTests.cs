using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Naispo.Kinect;
using Naispo.Silhouette;

namespace Naispo.Tests
{
    public sealed class SilhouetteTests
    {
        static byte[] Empty(int w,int h) { var a=new byte[w*h];for(int i=0;i<a.Length;i++)a[i]=255;return a; }
        static void Rect(byte[] a,int w,int x,int y,int rw,int rh,byte value=0)
        {for(int j=y;j<y+rh;j++)for(int i=x;i<x+rw;i++)a[j*w+i]=value;}
        static SilhouetteContourExtractor Extract(byte[] a,int w,int h,int min=10,float eps=1,int max=128)
        {
            var slots=Enumerable.Repeat(-1,256).ToArray();slots[0]=0;
            var e=new SilhouetteContourExtractor();e.Extract(a,w,h,slots,0,min,0,eps,max,4);return e;
        }
        [Test] public void RectTouchingImageEdgesHasExactPixelBounds()
        {
            var a=Empty(32,24);Rect(a,32,0,0,32,24);var e=Extract(a,32,24);
            Assert.That(e.PathCount,Is.EqualTo(1));Assert.That(e.TotalPoints,Is.EqualTo(4));
            Assert.That(e.Paths[0].Points,Does.Contain(new Vector2(0,0)));
            Assert.That(e.Paths[0].Points,Does.Contain(new Vector2(32,24)));
            Assert.That(e.Centroid,Is.EqualTo(new Vector2(16,12)));
        }
        [Test] public void HolesAreSolidAndTinyComponentsAreIgnored()
        {
            var a=Empty(80,60);Rect(a,80,5,5,30,40);Rect(a,80,10,10,12,12,255);a[0]=0;
            var e=Extract(a,80,60);Assert.That(e.PathCount,Is.EqualTo(1));Assert.That(e.TotalPoints,Is.EqualTo(4));
        }
        [Test] public void DisconnectedBodyPiecesProduceSeparatePaths()
        {
            var a=Empty(80,60);Rect(a,80,5,5,20,30);Rect(a,80,50,10,15,20);
            var e=Extract(a,80,60);Assert.That(e.PathCount,Is.EqualTo(2));Assert.That(e.TotalPoints,Is.EqualTo(8));
        }
        [Test] public void DiagonalComponentsStaySeparate()
        {
            var a=Empty(40,40);Rect(a,40,5,5,10,10);Rect(a,40,15,15,10,10);
            var e=Extract(a,40,40);Assert.That(e.PathCount,Is.EqualTo(2));
        }
        [Test] public void ConcavityAndGlobalPointBudgetArePreserved()
        {
            var a=Empty(320,288);Rect(a,320,50,40,100,200);
            for(int y=40;y<240;y+=6)Rect(a,320,150,y,20,3);
            Rect(a,320,200,100,60,100);
            var e=Extract(a,320,288,10,1,64);
            Assert.That(e.PathCount,Is.EqualTo(2));Assert.That(e.TotalPoints,Is.LessThanOrEqualTo(64));
            for(int i=0;i<e.PathCount;i++)Assert.That(SilhouetteContourExtractor.IsSimple(e.Paths[i].Points),Is.True);
        }
        [Test] public void StableIdsSurviveIndexReorderingAndTimeout()
        {
            var r=new BodyColorRegistry();var f=new BodyFrameData{BodyCount=3};f.BodyIds[0]=10;f.BodyIds[1]=20;f.BodyIds[2]=30;
            r.Update(f,0,3,0.75);f.BodyIds[0]=30;f.BodyIds[1]=10;f.BodyIds[2]=20;r.Update(f,0.1,3,0.75);
            Assert.That(r.BodyIndexToSlot.Take(3).ToArray(),Is.EqualTo(new[]{2,0,1}));
            f.BodyCount=0;r.Update(f,0.5,3,0.75);Assert.That(r.Slots[0].Assigned,Is.True);
            r.Update(f,1,3,0.75);Assert.That(r.Slots.All(s=>!s.Assigned),Is.True);
        }
        [Test] public void FourthBodyIsExcludedAndNewIdWaitsForSlotExpiry()
        {
            var r=new BodyColorRegistry();var f=new BodyFrameData{BodyCount=4};for(int i=0;i<4;i++)f.BodyIds[i]=(uint)(i+1);
            r.Update(f,0,3,0.75);Assert.That(r.PresentCount,Is.EqualTo(3));Assert.That(r.BodyIndexToSlot[3],Is.EqualTo(-1));
            f.BodyCount=1;f.BodyIds[0]=4;r.Update(f,0.2,3,0.75);Assert.That(r.PresentCount,Is.Zero);
            r.Update(f,1,3,0.75);Assert.That(r.Slots[0].BodyId,Is.EqualTo(4));
        }
        [TestCase(16f/9f,true)] [TestCase(1f,false)] [TestCase(9f/16f,true)]
        public void MapperUsesUniformScaleAndInvertibleMirror(float aspect,bool mirror)
        {
            var go=new GameObject("Mapper test");var settings=ScriptableObject.CreateInstance<KinectSettings>();
            try
            {
                var c=go.AddComponent<Camera>();c.orthographic=true;c.orthographicSize=5;c.aspect=aspect;
                var m=go.AddComponent<KinectViewportMapper>();m.targetCamera=c;m.settings=settings;settings.mirrorX=mirror;m.Refresh(320,288);
                Vector2 p=new Vector2(64.5f,100.5f),w=m.PixelToWorld(p);
                Assert.That(Vector2.Distance(m.WorldToPixel(w),p),Is.LessThan(0.001f));
                float dx=Vector2.Distance(w,m.PixelToWorld(p+Vector2.right)),dy=Vector2.Distance(w,m.PixelToWorld(p+Vector2.up));
                Assert.That(dx,Is.EqualTo(dy).Within(0.0001f));
                Assert.That(m.UnitsPerPixel*320,Is.LessThanOrEqualTo(10*aspect+0.0001f));
                Assert.That(m.UnitsPerPixel*288,Is.LessThanOrEqualTo(10.0001f));
                Assert.That(m.PixelToWorld(Vector2.zero).x>m.PixelToWorld(new Vector2(320,0)).x,Is.EqualTo(mirror));
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(settings);}
        }
        [Test] public void LatestBufferNeverOverwritesConsumerOrKeepsOldBacklog()
        {
            var buffer=new LatestBodyFrameBuffer();var producer=new BodyFrameData();var consumer=new BodyFrameData();
            producer.Sequence=1;buffer.Publish(ref producer);Assert.That(buffer.TryTake(ref consumer),Is.True);
            producer.Sequence=2;buffer.Publish(ref producer);producer.Sequence=3;buffer.Publish(ref producer);
            Assert.That(consumer.Sequence,Is.EqualTo(1));Assert.That(buffer.TryTake(ref consumer),Is.True);
            Assert.That(consumer.Sequence,Is.EqualTo(3));Assert.That(buffer.TryTake(ref consumer),Is.False);
        }
    }
}
