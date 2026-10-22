using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace Naispo.MultiKinect
{
    public static class KinectDiscovery
    {
        [Serializable]public sealed class DeviceInfo{public int index;public string serial,error;}
        [Serializable]public sealed class Result{public int count;public DeviceInfo[] devices;}
        // Reserve explicit choices first, then assign each empty slot a different connected device.
        public static bool TrySelect(Result inventory,string[] requested,out DeviceInfo[] selected,out string error)
        {
            selected=new DeviceInfo[2];error="";
            if(requested==null||requested.Length!=2) { error="左右2台のカメラ設定が必要です。";return false; }
            var serials=requested.Select(s=>(s??"").Trim()).ToArray();
            if(serials[0].Length>0&&serials[0]==serials[1]) { error="左右に同じカメラ番号が指定されています。";return false; }
            var available=(inventory?.devices??Array.Empty<DeviceInfo>())
                .Where(d=>d!=null&&d.index>=0&&!string.IsNullOrWhiteSpace(d.serial)&&string.IsNullOrEmpty(d.error))
                .OrderBy(d=>d.index).ToArray();
            for(int slot=0;slot<2;slot++)
            {
                if(serials[slot].Length==0)continue;
                selected[slot]=available.FirstOrDefault(d=>d.serial==serials[slot]);
                if(selected[slot]==null)
                {
                    error=$"{(slot==0?"左":"右")}のカメラ {serials[slot]} が見つかりません。カメラ設定で「自動設定」を選ぶか、番号を確認してください。";
                    return false;
                }
            }
            for(int slot=0;slot<2;slot++)
            {
                if(selected[slot]!=null)continue;
                var assigned=selected;
                selected[slot]=available.FirstOrDefault(d=>!assigned.Any(s=>s!=null&&(s.index==d.index||s.serial==d.serial)));
                if(selected[slot]==null)
                {
                    error=$"使用できるKinectが2台そろっていません（接続 {inventory?.count??0} 台）。電源・USBを確認し、他のKinectアプリを閉じて再接続してください。";
                    return false;
                }
            }
            if(selected[0].index==selected[1].index||selected[0].serial==selected[1].serial)
            { error="左右に別々のカメラが必要です。";return false; }
            return true;
        }
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
