using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Skua.Core.Utils;

public static class MemoryUtils
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, UIntPtr minimumWorkingSetSize, UIntPtr maximumWorkingSetSize);

    /// <summary>
    /// Forces a Garbage Collection pass and trims the process working set, 
    /// significantly reducing the memory footprint of COM objects like the Flash Player.
    /// </summary>
    public static void TrimWorkingSet()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false, compacting: true);
                SetProcessWorkingSetSize(GetCurrentProcess(), UIntPtr.MaxValue, UIntPtr.MaxValue);
            }
        }
        catch 
        { 
            // Suppress errors if run on non-Windows platforms or insufficient privileges
        }
    }
}
