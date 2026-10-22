using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
namespace Naispo.MultiKinect
{
    public static class KinectDiscovery
    {
        [Serializable]public sealed class DeviceInfo{public int index;public string serial,error;}
        [Serializable]public sealed class Result{public int count;public DeviceInfo[] devices;}
        public static Task<string> Run(string executable,string native,CancellationToken token)=>Task.Run(async()=>
        {
            using var p=new Process{StartInfo=new ProcessStartInfo(executable,"--list \""+native+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
            token.ThrowIfCancellationRequested();p.Start();
            using var cancel=token.Register(()=>{try{if(!p.HasExited)p.Kill();}catch{}});
            var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();
            if(!p.WaitForExit(10000)){p.Kill();throw new TimeoutException("Kinect enumeration timed out; no automatic retry");}
            string text=await output,error=await errors;token.ThrowIfCancellationRequested();if(p.ExitCode!=0)throw new InvalidOperationException(error);return text;
        },token);
    }
}
