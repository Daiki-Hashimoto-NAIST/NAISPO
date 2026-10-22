using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using Naispo.Kinect;
using UnityEngine;

namespace Naispo.MultiKinect
{
    public sealed class KinectPipeReceiver : BodyTrackingSource
    {
        public int playerSlot;
        public string serialNumber="",pipeName="NaispoCoinP1";
        public float staleTimeout=0.5f;
        volatile bool running,connected,failed;
        string status="Stopped";
        Thread thread;NamedPipeClientStream pipe;readonly object pipeGate=new object();
        LatestPlayerFrame latest=new LatestPlayerFrame();TrackedPlayerFrame current=new TrackedPlayerFrame();
        long receivedAt,bytesReceived,receivedFrames;
        ulong session;uint rawId,gameId;bool hadIdentity;
        static int nextGameId;
        int generation;float captureFps;
        public override string Status=>status;
        public override bool WorkerAlive=>running&&connected&&!failed&&AgeSeconds<=staleTimeout;
        public override bool HasFailed=>failed;
        public override bool IsReconfiguring=>false;
        public override int CaptureGeneration=>generation;
        public override float CaptureFps=>captureFps;
        public bool Connected=>connected;
        public double AgeSeconds=>Interlocked.Read(ref receivedAt)==0?double.PositiveInfinity:(System.Diagnostics.Stopwatch.GetTimestamp()-Interlocked.Read(ref receivedAt))/(double)System.Diagnostics.Stopwatch.Frequency;
        public long BytesReceived=>Interlocked.Read(ref bytesReceived);
        public long ReceivedFrames=>Interlocked.Read(ref receivedFrames);
        public TrackedPlayerFrame LatestMetadata=>current;
        void OnEnable()=>StartReceiving();
        public void StartReceiving()
        {
            if(thread!=null&&thread.IsAlive)return;
            if(playerSlot<0||playerSlot>1||string.IsNullOrWhiteSpace(serialNumber)||string.IsNullOrWhiteSpace(pipeName)){failed=true;status="Configure serial and pipe before starting";return;}
            running=true;failed=false;connected=false;receivedAt=0;hadIdentity=false;generation++;latest=new LatestPlayerFrame();
            receivedFrames=bytesReceived=0;captureFps=0;current=new TrackedPlayerFrame();
            string expected=serialNumber,name=pipeName;int slot=playerSlot;
            thread=new Thread(()=>Receive(name,expected,slot)){IsBackground=true,Name="Kinect pipe "+slot};thread.Start();
        }
        void Receive(string name,string expected,int slot)
        {
            var frame=new TrackedPlayerFrame();var prefix=new byte[4];var bytes=new byte[KinectWire.MaxMessage];
            using var memory=new MemoryStream(bytes,0,bytes.Length,false,true);using var reader=new BinaryReader(memory);
            while(running)
            {
                try
                {
                    using var client=new NamedPipeClientStream(".",name,PipeDirection.In,PipeOptions.Asynchronous);
                    lock(pipeGate){if(!running)return;pipe=client;}status="Waiting for worker";client.Connect(500);connected=true;status="Connected";
                    ulong lastSession=0;long lastSequence=-1;
                    while(running)
                    {
                        KinectWire.ReadExact(client,prefix,0,4);int length=BitConverter.ToInt32(prefix,0);
                        if(length<100||length>KinectWire.MaxMessage)throw new InvalidDataException("Rejected message length "+length);
                        KinectWire.ReadExact(client,bytes,0,length);memory.Position=0;KinectWire.Read(reader,frame,length);
                        if(frame.Slot!=slot||frame.Serial!=expected)throw new InvalidDataException("Serial / PlayerSlot mismatch");
                        if(frame.Session==lastSession&&frame.Sequence<=lastSequence)throw new InvalidDataException("Non-increasing sequence");
                        lastSession=frame.Session;lastSequence=frame.Sequence;
                        Interlocked.Exchange(ref receivedAt,System.Diagnostics.Stopwatch.GetTimestamp());Interlocked.Add(ref bytesReceived,length+4);Interlocked.Increment(ref receivedFrames);latest.Publish(ref frame);
                    }
                }
                catch(InvalidDataException e){failed=true;status=e.Message;break;}
                catch(Exception e)when(e is IOException||e is TimeoutException||e is ObjectDisposedException){if(running)status=e is TimeoutException?"Waiting for worker":e.Message;}
                finally{connected=false;lock(pipeGate)pipe=null;}
                if(running)Thread.Sleep(200); // Reconnect pipe only, never restart a device/process.
            }
            connected=false;
        }
        public override bool TryGetLatest(ref BodyFrameData destination)
        {
            if(!latest.Take(ref current)||!WorkerAlive)return false;
            if(!hadIdentity||session!=current.Session||(current.Tracked&&rawId!=current.BodyId))
            {generation++;session=current.Session;rawId=current.BodyId;gameId=unchecked((uint)Interlocked.Increment(ref nextGameId));hadIdentity=true;}
            if(current.Tracked)rawId=current.BodyId;
            KinectHandFrameAdapter.CopyTo(current,destination,gameId);
            captureFps=current.CaptureFps;return true;
        }
        public void StopReceiving()
        {
            running=false;lock(pipeGate)pipe?.Dispose();
            if(thread!=null&&thread.IsAlive&&!thread.Join(1000)){status="Stopping receiver";return;}
            thread=null;connected=false;captureFps=0;status="Stopped";
        }
        void OnDisable()=>StopReceiving();
        void OnDestroy()=>StopReceiving();
    }
}
