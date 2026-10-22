using Naispo.MultiKinect;
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
        Console.WriteLine("PASS: binary roundtrip, central selection, sticky identity, loss timeout");return 0;
    }
}
