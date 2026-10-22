using Microsoft.Azure.Kinect.Sensor;
using Microsoft.Azure.Kinect.BodyTracking;

public sealed class WorkerConfig
{
    public int Slot {get;set;}
    public int DeviceIndex {get;set;}=-1; // Resolved by discovery immediately before launch; serial still authoritative.
    public string Serial {get;set;}="";
    public string Pipe {get;set;}="NaispoCoinP1";
    public string NativeDirectory {get;set;}="";
    public string ModelPath {get;set;}="";
    public DepthMode DepthMode {get;set;}=DepthMode.NFOV_2x2Binned;
    public FPS Fps {get;set;}=FPS.FPS15;
    public TrackerProcessingMode ProcessingMode {get;set;}=TrackerProcessingMode.DirectML;
    public bool AllowCpuFallback {get;set;}=true;
    public float MinDistance {get;set;}=0.5f;
    public float MaxDistance {get;set;}=4.5f;
    public float MaxAbsX {get;set;}=1.2f;
    public float LostTimeout {get;set;}=1.0f;
    public int MinimumConfidence {get;set;}=1;
    public bool Synthetic {get;set;}
    public int ParentPid {get;set;}
    public int RunSeconds {get;set;}
    public void Validate()
    {
        if(Slot<0||Slot>1||string.IsNullOrWhiteSpace(Serial)||Serial.Length>32||Serial.Any(c=>c<33||c>126)||string.IsNullOrWhiteSpace(Pipe)||Pipe.Any(c=>!char.IsLetterOrDigit(c)&&c!='_'&&c!='-'))throw new ArgumentException("Invalid slot, serial or pipe");
        if(!Synthetic&&DeviceIndex<0)throw new ArgumentException("Run discovery/launcher before starting a real worker (DeviceIndex missing)");
        if(MinDistance<0||MaxDistance<=MinDistance||MaxAbsX<=0||LostTimeout<0||MinimumConfidence<1||MinimumConfidence>3)throw new ArgumentException("Invalid selection region/confidence");
        if(DepthMode==DepthMode.Off||DepthMode==DepthMode.PassiveIR||(DepthMode==DepthMode.WFOV_Unbinned&&Fps==FPS.FPS30))throw new ArgumentException("Invalid depth/FPS combination");
    }
}

public sealed class BodySelector
{
    uint? selected; double lastSeen;
    public int Choose(uint[] ids,float[] xs,float[] zs,int[] confidence,int count,double now,WorkerConfig config)
    {
        bool Eligible(int i)=>confidence[i]>=config.MinimumConfidence&&Math.Abs(xs[i])<=config.MaxAbsX&&zs[i]>=config.MinDistance&&zs[i]<=config.MaxDistance;
        if(selected.HasValue)
        {
            for(int i=0;i<count;i++)if(ids[i]==selected.Value&&Eligible(i)){lastSeen=now;return i;}
            if(now-lastSeen<config.LostTimeout)return -1;
            selected=null;
        }
        int best=-1;float score=float.MaxValue;
        for(int i=0;i<count;i++)if(Eligible(i)&&Math.Abs(xs[i])<score){score=Math.Abs(xs[i]);best=i;}
        if(best>=0){selected=ids[best];lastSeen=now;}return best;
    }
}
