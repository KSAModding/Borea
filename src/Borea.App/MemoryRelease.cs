using System;
using System.Runtime;

namespace Borea.App;

internal static class MemoryRelease
{
    /// <summary>
    /// An aggressive, compacting collection also gives the freed memory back to the system, which a normal
    /// collection keeps for later use. The large object heap is compacted too, because it holds the buffers
    /// of the start and no other collection compacts it.
    /// </summary>
    public static void Run()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }
}
