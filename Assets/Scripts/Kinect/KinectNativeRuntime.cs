using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Naispo.Kinect
{
    public static class KinectNativeRuntime
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandle(string name);

        public static void EnsureLoaded(string dataPath, bool editor)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT || IntPtr.Size != 8)
                throw new PlatformNotSupportedException("Azure Kinect requires Windows x64.");
            string folder = Path.Combine(dataPath, editor ? "Plugins/AzureKinect/x86_64" : "Plugins/x86_64");
            // Load dependencies from their explicit directory, without changing PATH.
            // Native modules deliberately remain loaded across Editor domain reloads.
            foreach (string name in new[] { "k4a.dll", "depthengine_2_0.dll", "directml.dll", "onnxruntime.dll", "onnxruntime_providers_shared.dll", "k4abt.dll" })
            {
                string file = Path.Combine(folder, name);
                if (!File.Exists(file)) throw new FileNotFoundException("Missing Azure Kinect native library", file);
                if (GetModuleHandle(name) == IntPtr.Zero && LoadLibraryEx(file, IntPtr.Zero, 0x8) == IntPtr.Zero)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to load " + file);
            }
        }
    }
}
