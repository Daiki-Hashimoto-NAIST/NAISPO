using System;
using System.IO;
using System.Text;

namespace Naispo.MultiKinect
{
    // Shared source compiled by Unity and the standalone worker. Little endian.
    public struct WireJoint
    {
        public float X,Y,Z,U,V,Qx,Qy,Qz,Qw;
        public byte Confidence,Flags; // bit0 position valid; bit1 projection valid
    }
    public sealed class TrackedPlayerFrame
    {
        public int Slot,Width,Height;
        public string Serial="";
        public ulong Session;
        public long Sequence;
        public double CaptureTimestamp,TrackingTimestamp;
        public uint BodyId;
        public bool Tracked;
        public float CaptureFps,TrackingFps,CaptureMs,TrackingMs;
        public byte[] Mask=Array.Empty<byte>();
        public readonly WireJoint[] Joints=new WireJoint[32];
        public void Resize(int w,int h){if(Mask.Length!=w*h)Mask=new byte[w*h];Width=w;Height=h;}
    }
    public sealed class LatestPlayerFrame
    {
        readonly object gate=new object(); TrackedPlayerFrame pending=new TrackedPlayerFrame();bool ready;
        public void Publish(ref TrackedPlayerFrame frame){lock(gate){var old=pending;pending=frame;frame=old;ready=true;}}
        public bool Take(ref TrackedPlayerFrame frame){lock(gate){if(!ready)return false;var old=frame;frame=pending;pending=old;ready=false;return true;}}
    }
    public static class KinectWire
    {
        public const ushort Version=1;
        public const int MaxPixels=1024*1024, MaxMessage=MaxPixels+4096;
        const uint Magic=0x4B534E50;
        public static void ReadExact(Stream stream,byte[] bytes,int offset,int count)
        {while(count>0){int n=stream.Read(bytes,offset,count);if(n==0)throw new EndOfStreamException();offset+=n;count-=n;}}
        public static void Write(BinaryWriter w,TrackedPlayerFrame f)
        {
            Validate(f);w.Write(Magic);w.Write(Version);w.Write((byte)f.Slot);
            for(int i=0;i<32;i++)w.Write((byte)(i<f.Serial.Length?f.Serial[i]:0));
            w.Write(f.Session);w.Write(f.Sequence);w.Write(f.CaptureTimestamp);w.Write(f.TrackingTimestamp);
            w.Write(f.BodyId);w.Write(f.Tracked);w.Write((ushort)f.Width);w.Write((ushort)f.Height);
            w.Write(f.CaptureFps);w.Write(f.TrackingFps);w.Write(f.CaptureMs);w.Write(f.TrackingMs);
            w.Write(f.Mask.Length);w.Write(f.Mask);
            for(int i=0;i<32;i++){var j=f.Joints[i];w.Write((byte)i);w.Write(j.X);w.Write(j.Y);w.Write(j.Z);w.Write(j.U);w.Write(j.V);w.Write(j.Qx);w.Write(j.Qy);w.Write(j.Qz);w.Write(j.Qw);w.Write(j.Confidence);w.Write(j.Flags);}
        }
        public static void Read(BinaryReader r,TrackedPlayerFrame f,int length)
        {
            if(length<100||length>MaxMessage)throw new InvalidDataException("Invalid message size");
            long start=r.BaseStream.Position;
            if(r.ReadUInt32()!=Magic||r.ReadUInt16()!=Version)throw new InvalidDataException("Protocol magic/version mismatch");
            f.Slot=r.ReadByte();var chars=new char[32];int n=0;bool end=false;
            for(int i=0;i<32;i++){byte c=r.ReadByte();if(c==0){end=true;continue;}if(end||c<33||c>126)throw new InvalidDataException("Invalid serial");chars[n++]=(char)c;}
            // Serial is tiny; image and joint storage remain reused.
            string serial=new string(chars,0,n);if(f.Serial!=serial)f.Serial=serial;
            f.Session=r.ReadUInt64();f.Sequence=r.ReadInt64();f.CaptureTimestamp=r.ReadDouble();f.TrackingTimestamp=r.ReadDouble();
            f.BodyId=r.ReadUInt32();f.Tracked=r.ReadBoolean();int width=r.ReadUInt16(),height=r.ReadUInt16();
            f.CaptureFps=r.ReadSingle();f.TrackingFps=r.ReadSingle();f.CaptureMs=r.ReadSingle();f.TrackingMs=r.ReadSingle();
            int bytes=r.ReadInt32();if(width<1||height<1||width>1024||height>1024||bytes!=width*height||bytes>MaxPixels||length-(r.BaseStream.Position-start)!=bytes+32*39)throw new InvalidDataException("Invalid image/joint payload size");
            f.Resize(width,height);ReadExact(r.BaseStream,f.Mask,0,bytes);
            for(int i=0;i<bytes;i++)if(f.Mask[i]>1)throw new InvalidDataException("Mask must be binary");
            for(int i=0;i<32;i++)
            {
                if(r.ReadByte()!=i)throw new InvalidDataException("Joint ID order mismatch");
                var j=new WireJoint{X=r.ReadSingle(),Y=r.ReadSingle(),Z=r.ReadSingle(),U=r.ReadSingle(),V=r.ReadSingle(),Qx=r.ReadSingle(),Qy=r.ReadSingle(),Qz=r.ReadSingle(),Qw=r.ReadSingle(),Confidence=r.ReadByte(),Flags=r.ReadByte()};
                if(j.Confidence>3||j.Flags>3||!Finite(j.X)||!Finite(j.Y)||!Finite(j.Z)||!Finite(j.U)||!Finite(j.V)||!Finite(j.Qx)||!Finite(j.Qy)||!Finite(j.Qz)||!Finite(j.Qw))throw new InvalidDataException("Invalid joint");
                f.Joints[i]=j;
            }
            Validate(f);
        }
        public static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
        static void Validate(TrackedPlayerFrame f)
        {
            if(f.Slot<0||f.Slot>1||f.Serial.Length<1||f.Serial.Length>32||f.Width<1||f.Width>1024||f.Height<1||f.Height>1024||f.Mask.Length!=f.Width*f.Height||!Finite(f.CaptureTimestamp)||!Finite(f.TrackingTimestamp)||!Finite(f.CaptureFps)||!Finite(f.TrackingFps)||!Finite(f.CaptureMs)||!Finite(f.TrackingMs))throw new InvalidDataException("Invalid frame header");
        }
    }
}
