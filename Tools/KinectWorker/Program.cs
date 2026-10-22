using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Kinect.Sensor;
using Microsoft.Azure.Kinect.BodyTracking;
using Naispo.MultiKinect;

static class Program
{
    static readonly JsonSerializerOptions Json=new(){WriteIndented=true,Converters={new JsonStringEnumConverter()}};
    [DllImport("kernel32",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string file,IntPtr reserved,uint flags);
    static void Load(string folder,bool tracking)
    {
        foreach(var name in tracking?new[]{"k4a.dll","depthengine_2_0.dll","directml.dll","onnxruntime.dll","onnxruntime_providers_shared.dll","k4abt.dll"}:new[]{"k4a.dll"})
        {string path=Path.Combine(folder,name);if(!File.Exists(path))throw new FileNotFoundException("Missing native DLL",path);if(LoadLibraryEx(path,IntPtr.Zero,8)==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),path);}
    }
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if(args.Length==2&&args[0]=="--list")
            {
                Load(Path.GetFullPath(args[1]),false);
                string inventory=WithDeviceAccess(()=>
                {
                    var devices=new List<object>();int count=Device.GetInstalledCount();
                    for(int i=0;i<count;i++){try{using var d=Device.Open(i);devices.Add(new{index=i,serial=d.SerialNum,error=""});}catch(Exception ex){devices.Add(new{index=i,serial="",error=ex.Message});}}
                    return JsonSerializer.Serialize(new{count,devices},Json);
                });
                Console.WriteLine(inventory);return 0;
            }
            if(args.Length==1&&args[0]=="--self-test")return SelfTest.Run();
            if(args.Length!=2||args[0]!="--config")throw new ArgumentException("Usage: --list <native directory> | --config <json> | --self-test");
            var config=JsonSerializer.Deserialize<WorkerConfig>(File.ReadAllText(args[1]),Json);config.Validate();
            string lockPath=Path.Combine(Path.GetTempPath(),"NaispoKinect_"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(config.Serial)))+".lock");
            using var serialLock=new FileStream(lockPath,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            using var cancel=new CancellationTokenSource();Console.CancelKeyPress+=(s,e)=>{e.Cancel=true;cancel.Cancel();};
            _=Task.Run(()=>{while(!cancel.IsCancellationRequested){string line=Console.ReadLine();if(line==null)break;if(line.Trim()=="stop"){cancel.Cancel();break;}}});
            if(config.RunSeconds>0)cancel.CancelAfter(TimeSpan.FromSeconds(config.RunSeconds));
            if(config.ParentPid>0)_=Task.Run(async()=>{while(!cancel.IsCancellationRequested){try{using var parent=Process.GetProcessById(config.ParentPid);if(parent.HasExited)break;}catch{break;}await Task.Delay(500);}cancel.Cancel();await Task.Delay(6000);Environment.Exit(2);}); // Bounds orphan lifetime even if native Tracker.Create is stuck.
            using var pipe=new NamedPipeServerStream(config.Pipe,PipeDirection.Out,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,0,131072);
            using var registration=cancel.Token.Register(()=>pipe.Dispose());
            Console.WriteLine("Waiting for Unity pipe: "+config.Pipe+" serial="+config.Serial);
            await pipe.WaitForConnectionAsync(cancel.Token);
            var latest=new LatestPlayerFrame();var sender=Task.Run(()=>Send(pipe,latest,cancel));
            try {if(config.Synthetic)Synthetic(config,latest,cancel.Token);else Capture(config,latest,cancel.Token);}
            finally{cancel.Cancel();try{await sender;}catch(OperationCanceledException){}catch(ObjectDisposedException){}}
            return 0;
        }
        catch(OperationCanceledException){return 0;}
        catch(Exception ex){Console.Error.WriteLine(ex.ToString());return 1;}
    }
    static void Send(Stream pipe,LatestPlayerFrame latest,CancellationTokenSource cancel)
    {
        var frame=new TrackedPlayerFrame();using var memory=new MemoryStream(KinectWire.MaxMessage);using var writer=new BinaryWriter(memory);using var output=new BinaryWriter(pipe,System.Text.Encoding.UTF8,true);
        try{while(!cancel.IsCancellationRequested){if(!latest.Take(ref frame)){Thread.Sleep(2);continue;}memory.Position=0;memory.SetLength(0);KinectWire.Write(writer,frame);output.Write((int)memory.Length);pipe.Write(memory.GetBuffer(),0,(int)memory.Length);}}
        catch(Exception ex){Console.Error.WriteLine("Pipe sender stopped: "+ex.Message);cancel.Cancel();}
    }
    // SDK Device.Open scans color/IMU interfaces of other attached devices too.
    // Serialize only discovery/open, so two processes cannot temporarily claim each
    // other's interface during that scan. Capture/tracking remain fully independent.
    static T WithDeviceAccess<T>(Func<T> action)
    {
        using var access=new Mutex(false,"Local\\NaispoKinectEnumeration");
        bool acquired=false;
        try
        {
            try{acquired=access.WaitOne(5000);}catch(AbandonedMutexException){acquired=true;}
            if(!acquired)throw new TimeoutException("Kinect device discovery/open is busy. No automatic retry.");
            return action();
        }
        finally{if(acquired)access.ReleaseMutex();}
    }
    static Device OpenSerial(string expected,int index)=>WithDeviceAccess(()=>
    {
        if(index<0||index>=Device.GetInstalledCount())throw new InvalidOperationException("Requested Kinect "+expected+" is unavailable. Run serial discovery again; check USB/power.");
        var device=Device.Open(index);
        try
        {
            if(device.SerialNum!=expected)throw new InvalidOperationException("Serial mismatch at index "+index+": expected "+expected+", found "+device.SerialNum+". No camera was started; run discovery again.");
            Console.WriteLine("Verified serial="+expected+" index="+index);return device;
        }catch{device.Dispose();throw;}
    });
    static ulong Session()=>BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(),0);
    static void Capture(WorkerConfig c,LatestPlayerFrame latest,CancellationToken token)
    {
        Console.WriteLine("Loading Kinect runtime: "+c.NativeDirectory);
        Load(c.NativeDirectory,true);if(!File.Exists(c.ModelPath))throw new FileNotFoundException("Missing body tracking model",c.ModelPath);
        Console.WriteLine("Opening Kinect: serial="+c.Serial+" index="+c.DeviceIndex);
        using var device=OpenSerial(c.Serial,c.DeviceIndex);bool started=false;Tracker tracker=null;
        try
        {
            token.ThrowIfCancellationRequested();
            Console.WriteLine("Starting depth camera: "+c.DepthMode+" "+c.Fps);
            device.StartCameras(new DeviceConfiguration{DepthMode=c.DepthMode,CameraFPS=c.Fps,ColorResolution=ColorResolution.Off,WiredSyncMode=WiredSyncMode.Standalone,SynchronizedImagesOnly=false});started=true;
            var calibration=device.GetCalibration();var tc=TrackerConfiguration.Default;tc.SensorOrientation=SensorOrientation.Default;tc.ProcessingMode=c.ProcessingMode;tc.ModelPath=c.ModelPath;
            tracker=TrackerStartup.Create(c,mode=>{tc.ProcessingMode=mode;return Tracker.Create(calibration,tc);},token,Console.WriteLine,out var activeMode);
            tracker.SetTemporalSmooting(0);
            Console.WriteLine("Tracker ready: "+activeMode+" "+c.DepthMode+" "+c.Fps);
            var frame=new TrackedPlayerFrame();var selector=new BodySelector();var ids=new uint[255];var xs=new float[255];var zs=new float[255];var conf=new int[255];
            var clock=Stopwatch.StartNew();var enqueueTimes=new Dictionary<long,long>(2);ulong session=Session();long sequence=0;int captured=0,tracked=0,inflight=0;double lastCapture=0,lastResult=0,measure=0;float captureFps=0,trackingFps=0,captureMs=0,trackingMs=0;
            int reportedCount=-1;uint reportedId=uint.MaxValue;string reportedHands="";double nextSelectionReport=0,lastSelectionReport=-10;
            while(!token.IsCancellationRequested)
            {
                if(inflight<2)try{long t=Stopwatch.GetTimestamp();using var capture=device.GetCapture(TimeSpan.FromMilliseconds(5));captureMs=(float)Stopwatch.GetElapsedTime(t).TotalMilliseconds;captured++;lastCapture=clock.Elapsed.TotalSeconds;tracker.EnqueueCapture(capture,TimeSpan.FromMilliseconds(5));using(var depth=capture.Depth)enqueueTimes[depth.DeviceTimestamp.Ticks]=Stopwatch.GetTimestamp();inflight++;}catch(TimeoutException){}
                using var result=tracker.PopResult(TimeSpan.FromMilliseconds(5),false);
                if(result!=null)
                {
                    inflight=Math.Max(0,inflight-1);tracked++;lastResult=clock.Elapsed.TotalSeconds;
                    int count=Math.Min(255,(int)result.NumberOfBodies);
                    for(uint i=0;i<count;i++){var pelvis=result.GetBodySkeleton(i).GetJoint(JointId.Pelvis);ids[i]=result.GetBodyId(i);xs[i]=pelvis.Position.X*.001f;zs[i]=pelvis.Position.Z*.001f;conf[i]=(int)pelvis.ConfidenceLevel;}
                    int selected=selector.Choose(ids,xs,zs,conf,count,lastResult,c);
                    uint selectedId=selected>=0?ids[selected]:uint.MaxValue;
                    using var map=result.BodyIndexMap;frame.Resize(map.WidthPixels,map.HeightPixels);frame.Slot=c.Slot;frame.Serial=c.Serial;frame.Session=session;frame.Sequence=++sequence;frame.TrackingTimestamp=result.DeviceTimestamp.TotalSeconds;
                    using(var capture=result.Capture)using(var depth=capture.Depth){frame.CaptureTimestamp=depth.DeviceTimestamp.TotalSeconds;if(enqueueTimes.TryGetValue(depth.DeviceTimestamp.Ticks,out long enqueued)){trackingMs=(float)Stopwatch.GetElapsedTime(enqueued).TotalMilliseconds;enqueueTimes.Remove(depth.DeviceTimestamp.Ticks);}}
                    frame.Tracked=selected>=0;frame.BodyId=selected>=0?ids[selected]:uint.MaxValue;
                    var bytes=map.Memory.Span;for(int y=0;y<frame.Height;y++)for(int x=0;x<frame.Width;x++)frame.Mask[y*frame.Width+x]=(byte)(selected>=0&&bytes[y*map.StrideBytes+x]==selected?1:0);
                    Array.Clear(frame.Joints);
                    if(selected>=0){var skeleton=result.GetBodySkeleton((uint)selected);for(int j=0;j<32;j++){var joint=skeleton.GetJoint((JointId)j);var w=new WireJoint{X=joint.Position.X,Y=joint.Position.Y,Z=joint.Position.Z,Qx=joint.Quaternion.X,Qy=joint.Quaternion.Y,Qz=joint.Quaternion.Z,Qw=joint.Quaternion.W,Confidence=(byte)joint.ConfidenceLevel};if(KinectWire.Finite(w.X)&&KinectWire.Finite(w.Y)&&KinectWire.Finite(w.Z)&&w.Z>0){w.Flags=1;var pixel=calibration.TransformTo2D(joint.Position,CalibrationDeviceType.Depth,CalibrationDeviceType.Depth);if(pixel.HasValue&&KinectWire.Finite(pixel.Value.X)&&KinectWire.Finite(pixel.Value.Y)){w.U=pixel.Value.X;w.V=pixel.Value.Y;w.Flags|=2;}}frame.Joints[j]=w;}}
                    string hands=selected>=0?$"left(confidence={frame.Joints[8].Confidence}, projected={(frame.Joints[8].Flags&2)!=0}) right(confidence={frame.Joints[15].Confidence}, projected={(frame.Joints[15].Flags&2)!=0})":"none";
                    if(lastResult>=nextSelectionReport&&(count!=reportedCount||selectedId!=reportedId||hands!=reportedHands||lastResult-lastSelectionReport>=5))
                    {
                        string candidates=string.Join("; ",Enumerable.Range(0,Math.Min(count,4)).Select(i=>$"id={ids[i]} x={xs[i]:F2}m z={zs[i]:F2}m confidence={conf[i]}"));
                        Console.WriteLine($"Tracking: detected={count} selected={(selected>=0?selectedId.ToString():"none")} hands={hands} regionZ={c.MinDistance:F1}-{c.MaxDistance:F1}m absX<={c.MaxAbsX:F1}m | {candidates}");
                        reportedCount=count;reportedId=selectedId;reportedHands=hands;lastSelectionReport=lastResult;nextSelectionReport=lastResult+1;
                    }
                    frame.CaptureFps=captureFps;frame.TrackingFps=trackingFps;frame.CaptureMs=captureMs;frame.TrackingMs=trackingMs;latest.Publish(ref frame);
                }
                double now=clock.Elapsed.TotalSeconds;if(now-measure>=1){captureFps=(float)(captured/(now-measure));trackingFps=(float)(tracked/(now-measure));captured=tracked=0;measure=now;}
                if(inflight==0&&now-lastCapture>10)throw new TimeoutException("No depth captures for 10 seconds. Check Kinect power/USB and reconnect.");
                if(now-lastResult>20)throw new TimeoutException("Body tracker produced no results for 20 seconds. Check the tracker startup log and reconnect.");
            }
        }
        finally{try{if(tracker!=null){tracker.Shutdown();tracker.Dispose();}}finally{if(started)device.StopCameras();}}
    }
    static void Synthetic(WorkerConfig c,LatestPlayerFrame latest,CancellationToken token)
    {
        var frame=new TrackedPlayerFrame();ulong session=Session();var timer=Stopwatch.StartNew();long sequence=0;int hz=c.Fps==FPS.FPS30?30:c.Fps==FPS.FPS5?5:15;
        while(!token.IsCancellationRequested)
        {
            double t=timer.Elapsed.TotalSeconds;frame.Resize(320,288);frame.Slot=c.Slot;frame.Serial=c.Serial;frame.Session=session;frame.Sequence=++sequence;frame.BodyId=42;frame.Tracked=true;frame.CaptureTimestamp=frame.TrackingTimestamp=t;frame.CaptureFps=frame.TrackingFps=hz;
            Array.Clear(frame.Mask);for(int y=55;y<265;y++)for(int x=120;x<200;x++)frame.Mask[y*320+x]=1;
            for(int j=0;j<32;j++)frame.Joints[j]=new WireJoint{X=(j%2==0?-100:100),Y=(j/2-8)*50,Z=3000,U=160+(j%2==0?-15:15),V=60+(j/2)*12,Qw=1,Confidence=2,Flags=3};
            double cycle=t%8;float distance=cycle<.5?.1f:cycle<.8?(float)(.1+(cycle-.5)*1.8):.64f;float spread=distance*120;
            frame.Joints[8]=new WireJoint{X=-distance*500,Z=3000,U=160-spread,V=140-(float)Math.Sin(Math.Min(1,Math.Max(0,cycle-.6))*Math.PI)*50,Qw=1,Confidence=2,Flags=3};
            frame.Joints[15]=frame.Joints[8];frame.Joints[15].X*=-1;frame.Joints[15].U=160+spread;
            latest.Publish(ref frame);double due=sequence/(double)hz;int wait=(int)Math.Max(1,(due-timer.Elapsed.TotalSeconds)*1000);token.WaitHandle.WaitOne(wait);
        }
    }
}
