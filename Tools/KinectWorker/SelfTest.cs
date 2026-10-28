using Naispo.MultiKinect;
using Microsoft.Azure.Kinect.BodyTracking;
static class SelfTest
{
    public static int Run()
    {
        var f=new TrackedPlayerFrame{Slot=1,Serial="TEST",Session=123,Sequence=5,BodyId=42,Tracked=true};f.Resize(320,288);f.Mask[0]=1;
        using var m=new MemoryStream();using var w=new BinaryWriter(m);KinectWire.Write(w,f);int length=(int)m.Length;m.Position=0;var g=new TrackedPlayerFrame();KinectWire.Read(new BinaryReader(m),g,length);
        if(g.Slot!=1||g.BodyId!=42||g.Mask[0]!=1)throw new Exception("Roundtrip failed");
        var selector=new BodySelector();var c=new WorkerConfig();var ids=new uint[]{1,2};var xs=new float[]{.2f,.1f};var zs=new float[]{3,3};var conf=new int[]{2,2};
        if(selector.Choose(ids,xs,zs,conf,2,0,c)!=1)throw new Exception("Selection failed");xs[0]=0;
        if(selector.Choose(ids,xs,zs,conf,2,.1,c)!=1)throw new Exception("Sticky ID failed");conf[1]=0;
        if(selector.Choose(ids,xs,zs,conf,2,.2,c)!=-1||selector.Choose(ids,xs,zs,conf,2,2,c)!=0)throw new Exception("Loss timeout failed");
        BodySelectionHysteresis();
        BodySelectionReservation();
        BodySelectionEntryRegion();
        TrackerFallback();
        Console.WriteLine("PASS: binary roundtrip, central selection, sticky identity, selection hysteresis/entry region/reacquisition, loss timeout, tracker startup/fallback/cancellation");return 0;
    }

    static void BodySelectionHysteresis()
    {
        var config=new WorkerConfig();var selector=new BodySelector();
        var ids=new uint[]{1,2};var xs=new float[]{0,.1f};var zs=new float[]{3,3};var confidence=new int[]{2,2};
        if(selector.Choose(ids,xs,zs,confidence,2,0,config)!=0)throw new Exception("Hysteresis setup failed");
        xs[0]=1.4f;
        if(selector.Choose(ids,xs,zs,confidence,2,.1,config)!=0)throw new Exception("Selected body must remain tracked in right retention margin");
        xs[0]=-1.4f;
        if(selector.Choose(ids,xs,zs,confidence,2,.2,config)!=0)throw new Exception("Selected body must remain tracked in left retention margin");
        xs[0]=0;zs[0]=.35f;
        if(selector.Choose(ids,xs,zs,confidence,2,.3,config)!=0)throw new Exception("Selected body must remain tracked in near retention margin");
        zs[0]=4.65f;
        if(selector.Choose(ids,xs,zs,confidence,2,.4,config)!=0)throw new Exception("Selected body must remain tracked in far retention margin");
        zs[0]=.25f;
        if(selector.Choose(ids,xs,zs,confidence,2,.5,config)!=-1)throw new Exception("Body beyond near retention limit must be invalid immediately");
        zs[0]=4.75f;
        if(selector.Choose(ids,xs,zs,confidence,2,.6,config)!=-1)throw new Exception("Body beyond far retention limit must be invalid immediately");
        zs[0]=3;xs[0]=1.51f;
        if(selector.Choose(ids,xs,zs,confidence,2,.7,config)!=-1)throw new Exception("Body beyond horizontal retention limit must be invalid immediately");
        xs[0]=-1.51f;
        if(selector.Choose(ids,xs,zs,confidence,2,.8,config)!=-1)throw new Exception("Left retention limit must match the right limit");
    }

    static void BodySelectionReservation()
    {
        var config=new WorkerConfig();var selector=new BodySelector();
        var ids=new uint[]{1,2};var xs=new float[]{0,.1f};var zs=new float[]{3,3};var confidence=new int[]{2,2};
        if(selector.Choose(ids,xs,zs,confidence,2,0,config)!=0)throw new Exception("Reservation setup failed");
        xs[0]=1.6f;
        if(selector.Choose(ids,xs,zs,confidence,2,.1,config)!=-1)throw new Exception("An eligible newcomer must not replace a reserved identity");
        xs[0]=1.4f;
        if(selector.Choose(ids,xs,zs,confidence,2,.2,config)!=0)throw new Exception("Same identity must reacquire inside the retention-only margin after a brief exit");
        confidence[0]=0;
        if(selector.Choose(ids,xs,zs,confidence,2,.3,config)!=-1)throw new Exception("Retention margin must not bypass confidence");
        if(selector.Choose(ids,xs,zs,confidence,2,1.3,config)!=1)throw new Exception("Expired reservation must allow a newcomer in the entry region");
    }

    static void BodySelectionEntryRegion()
    {
        var config=new WorkerConfig();var selector=new BodySelector();
        var ids=new uint[]{1,2,3,4};var xs=new float[]{1.4f,-1.4f,0,0};var zs=new float[]{3,3,.35f,4.65f};var confidence=new int[]{2,2,2,2};
        if(selector.Choose(ids,xs,zs,confidence,4,0,config)!=-1)throw new Exception("Retention-only margin must not admit new identities");
        xs[0]=1;
        if(selector.Choose(ids,xs,zs,confidence,4,.1,config)!=0)throw new Exception("New identity must still enter inside the original region");
        xs[0]=1.6f;
        if(selector.Choose(ids,xs,zs,confidence,4,2,config)!=-1)throw new Exception("Reservation expiry must not widen newcomer entry region");

        config.MinDistance=.1f;selector=new BodySelector();xs[0]=0;zs[0]=.1f;
        if(selector.Choose(ids,xs,zs,confidence,1,0,config)!=0)throw new Exception("Near-camera setup failed");
        zs[0]=-.01f;
        if(selector.Choose(ids,xs,zs,confidence,1,.1,config)!=-1)throw new Exception("Depth retention must not extend behind the camera");
    }

    static void TrackerFallback()
    {
        var config=new WorkerConfig();
        var marker=new object();var attempts=new List<TrackerProcessingMode>();var logs=new List<string>();
        var gpuError=new InvalidOperationException("Test GPU initialization failure");
        var cpuError=new InvalidOperationException("Test CPU initialization failure");
        object Create(TrackerProcessingMode mode)
        {
            attempts.Add(mode);
            if(mode!=TrackerProcessingMode.Cpu)throw gpuError;
            return marker;
        }
        var tracker=TrackerStartup.Create(config,Create,CancellationToken.None,logs.Add,out var active);
        if(tracker!=marker||active!=TrackerProcessingMode.Cpu||attempts.Count!=2||attempts[0]!=TrackerProcessingMode.DirectML||attempts[1]!=TrackerProcessingMode.Cpu||!logs.Any(line=>line.Contains(gpuError.Message)))
            throw new Exception("GPU failure must retry CPU and report the original error");

        attempts.Clear();
        var successful=TrackerStartup.Create(config,mode=>{attempts.Add(mode);return marker;},CancellationToken.None,logs.Add,out active);
        if(successful!=marker||active!=TrackerProcessingMode.DirectML||attempts.Count!=1)throw new Exception("Successful GPU startup must not start a second tracker");

        try
        {
            TrackerStartup.Create<object>(config,mode=>throw (mode==TrackerProcessingMode.Cpu?cpuError:gpuError),CancellationToken.None,logs.Add,out active);
            throw new Exception("Both failures must be surfaced");
        }
        catch(AggregateException error)
        {
            if(error.InnerExceptions.Count!=2||error.InnerExceptions[0]!=gpuError||error.InnerExceptions[1]!=cpuError)throw new Exception("GPU/CPU failure details were lost");
        }

        foreach(var mode in new[]{TrackerProcessingMode.DirectML,TrackerProcessingMode.Cpu})
        {
            config.ProcessingMode=mode;config.AllowCpuFallback=mode==TrackerProcessingMode.Cpu;attempts.Clear();
            try { TrackerStartup.Create<object>(config,requested=>{attempts.Add(requested);throw gpuError;},CancellationToken.None,logs.Add,out active);throw new Exception("Startup should fail without fallback"); }
            catch(InvalidOperationException error) when(error==gpuError) { }
            if(attempts.Count!=1)throw new Exception("Explicit/no-fallback startup was retried");
        }

        config.ProcessingMode=TrackerProcessingMode.DirectML;config.AllowCpuFallback=true;attempts.Clear();
        using var cancel=new CancellationTokenSource();
        try { TrackerStartup.Create<object>(config,mode=>{attempts.Add(mode);cancel.Cancel();throw gpuError;},cancel.Token,logs.Add,out active);throw new Exception("Cancelled startup should stop"); }
        catch(InvalidOperationException error) when(error==gpuError) { }
        if(attempts.Count!=1)throw new Exception("Cancellation must prevent a CPU retry");
        attempts.Clear();
        try { TrackerStartup.Create<object>(config,mode=>{attempts.Add(mode);throw new OperationCanceledException();},CancellationToken.None,logs.Add,out active);throw new Exception("Cancelled startup should stop"); }
        catch(OperationCanceledException) { }
        if(attempts.Count!=1)throw new Exception("OperationCanceledException must not start CPU fallback");
    }
}
