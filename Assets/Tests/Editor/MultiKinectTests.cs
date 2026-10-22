using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Naispo.MultiKinect;
using Naispo.Kinect;
using Naispo.Silhouette;
public sealed class MultiKinectTests
{
    static byte[] Packet()
    {
        var f=new TrackedPlayerFrame{Slot=1,Serial="12345",Session=12,Sequence=1,Tracked=true,BodyId=42};f.Resize(320,288);f.Mask[4]=1;
        using var m=new MemoryStream();using var w=new BinaryWriter(m);KinectWire.Write(w,f);return m.ToArray();
    }
    [Test]public void BinaryRoundTripOwnsImageAndAll32Joints()
    {var bytes=Packet();var f=new TrackedPlayerFrame();using var r=new BinaryReader(new MemoryStream(bytes));KinectWire.Read(r,f,bytes.Length);Assert.AreEqual(1,f.Slot);Assert.AreEqual("12345",f.Serial);Assert.AreEqual(42,f.BodyId);Assert.AreEqual(1,f.Mask[4]);Assert.AreEqual(32,f.Joints.Length);}
    [TestCase(-1)][TestCase(0)][TestCase(50)][TestCase(KinectWire.MaxMessage+1)]
    public void InvalidMessageLengthsRejected(int length)
    {using var r=new BinaryReader(new MemoryStream(Packet()));Assert.Throws<InvalidDataException>(()=>KinectWire.Read(r,new TrackedPlayerFrame(),length));}
    [Test]public void WrongVersionRejected(){var b=Packet();b[4]=99;using var r=new BinaryReader(new MemoryStream(b));Assert.Throws<InvalidDataException>(()=>KinectWire.Read(r,new TrackedPlayerFrame(),b.Length));}
    [Test]public void TruncatedPayloadRejected(){var b=Packet();using var r=new BinaryReader(new MemoryStream(b,0,b.Length-1));Assert.Throws<EndOfStreamException>(()=>KinectWire.Read(r,new TrackedPlayerFrame(),b.Length));}
    [Test]public void FalseImageLengthRejected(){var b=Packet();
        using var r=new BinaryReader(new MemoryStream(b)); // mask length follows the 96-byte header
        b[96]=255;b[97]=255;b[98]=255;b[99]=127;
        Assert.Throws<InvalidDataException>(()=>KinectWire.Read(r,new TrackedPlayerFrame(),b.Length));}
    sealed class Fragmented:MemoryStream
    {public Fragmented(byte[] b):base(b){}public override int Read(byte[] b,int o,int c)=>base.Read(b,o,Math.Min(3,c));}
    [Test]public void ReadExactHandlesFragmentsAndEof(){var bytes=Packet();using var s=new Fragmented(bytes);var copy=new byte[bytes.Length];KinectWire.ReadExact(s,copy,0,copy.Length);CollectionAssert.AreEqual(bytes,copy);Assert.Throws<EndOfStreamException>(()=>KinectWire.ReadExact(s,copy,0,1));}
    [Test]public void LatestFrameDropsBacklogAndKeepsConsumerSafe()
    {var queue=new LatestPlayerFrame();var producer=new TrackedPlayerFrame();var consumer=new TrackedPlayerFrame();for(int i=0;i<100;i++){producer.Sequence=i;queue.Publish(ref producer);}Assert.IsTrue(queue.Take(ref consumer));Assert.AreEqual(99,consumer.Sequence);producer.Sequence=100;queue.Publish(ref producer);Assert.AreEqual(99,consumer.Sequence);}
    static TrackedPlayerFrame HandsFrame()
    {
        var f=new TrackedPlayerFrame {Slot=0,Serial="TEST",Tracked=true,BodyId=42,Sequence=10,TrackingTimestamp=2.5};
        f.Resize(320,288);f.Mask[0]=1;
        f.Joints[KinectHandFrameAdapter.LeftHandJoint]=new WireJoint {X=-300,Y=50,Z=2000,U=100,V=140,Confidence=2,Flags=3};
        f.Joints[KinectHandFrameAdapter.RightHandJoint]=new WireJoint {X=300,Y=50,Z=2800,U=220,V=140,Confidence=3,Flags=3};
        return f;
    }
    [Test]public void HandAdapterPreservesCalibratedPixelsAndThreeDimensionalMetricSpan()
    {
        var f=HandsFrame();var d=new BodyFrameData();KinectHandFrameAdapter.CopyTo(f,d,123);
        Assert.IsTrue(d.Hands[0].Valid);Assert.AreEqual(123,d.BodyIds[0]);Assert.AreEqual(1,d.BodyCount);
        Assert.That(d.Hands[0].SeparationMeters,Is.EqualTo(1).Within(.00001));
        Assert.AreEqual(100,d.Hands[0].Left.X);Assert.AreEqual(220,d.Hands[0].Right.X);
        Assert.AreEqual(0,d.BodyIndexMap[0]);Assert.AreEqual(255,d.BodyIndexMap[1]);Assert.IsFalse(d.HasColor);
        Assert.AreEqual(f.Sequence,d.Sequence);Assert.AreEqual(f.TrackingTimestamp,d.TimestampSeconds);
        f.Mask[0]=0;Assert.AreEqual(0,d.BodyIndexMap[0]);
    }
    [TestCase(0,3)][TestCase(1,3)][TestCase(2,1)][TestCase(2,2)]
    public void HandAdapterRejectsLowConfidenceOrIncompleteProjection(int confidence,int flags)
    {
        var f=HandsFrame();var left=f.Joints[KinectHandFrameAdapter.LeftHandJoint];left.Confidence=(byte)confidence;left.Flags=(byte)flags;
        f.Joints[KinectHandFrameAdapter.LeftHandJoint]=left;var d=new BodyFrameData();KinectHandFrameAdapter.CopyTo(f,d,1);
        Assert.IsFalse(d.Hands[0].Valid);
    }
    [Test]public void LostPlayerClearsOldHandsAndMask()
    {
        var f=HandsFrame();var d=new BodyFrameData();KinectHandFrameAdapter.CopyTo(f,d,1);Assert.IsTrue(d.Hands[0].Valid);
        f.Tracked=false;KinectHandFrameAdapter.CopyTo(f,d,1);
        Assert.IsFalse(d.Hands[0].Valid);Assert.AreEqual(0,d.BodyCount);Assert.AreEqual(255,d.BodyIndexMap[0]);
    }
    [TestCase(float.NaN)][TestCase(float.PositiveInfinity)][TestCase(-1f)][TestCase(320f)]
    public void HandAdapterRejectsInvalidPixels(float pixelX)
    {
        var f=HandsFrame();var left=f.Joints[KinectHandFrameAdapter.LeftHandJoint];left.U=pixelX;
        f.Joints[KinectHandFrameAdapter.LeftHandJoint]=left;var d=new BodyFrameData();KinectHandFrameAdapter.CopyTo(f,d,1);
        Assert.IsFalse(d.Hands[0].Valid);
    }
    [TestCase(16f/9,true)][TestCase(1f,false)][TestCase(9f/16,true)]
    public void HalfViewportsPreserveAspectAndMirror(float aspect,bool mirror)
    {
        var go=new GameObject();var settings=ScriptableObject.CreateInstance<KinectSettings>();try
        {
            settings.mirrorX=mirror;var camera=go.AddComponent<Camera>();camera.orthographic=true;camera.orthographicSize=5;camera.aspect=aspect;
            var m=go.AddComponent<KinectViewportMapper>();m.settings=settings;m.targetCamera=camera;
            for(int slot=0;slot<2;slot++){m.viewport=new Rect(slot*.5f,0,.5f,1);m.Refresh(320,288);Assert.That(m.Center.x,Is.EqualTo((slot==0?-1:1)*5*aspect*.5f).Within(.0001));var p=new Vector2(45,99);Assert.That(Vector2.Distance(p,m.WorldToPixel(m.PixelToWorld(p))),Is.LessThan(.001));float x=(m.PixelToWorld(new Vector2(1,0))-m.PixelToWorld(Vector2.zero)).magnitude;float y=(m.PixelToWorld(new Vector2(0,1))-m.PixelToWorld(Vector2.zero)).magnitude;Assert.That(x,Is.EqualTo(y).Within(.0001));Assert.AreEqual(mirror,m.PixelToWorld(Vector2.zero).x>m.PixelToWorld(new Vector2(320,0)).x);}
        }finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(settings);}
    }
}
