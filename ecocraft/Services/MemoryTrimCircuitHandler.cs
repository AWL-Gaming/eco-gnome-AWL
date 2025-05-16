// File: Services/MemoryTrimCircuitHandler.cs
using Microsoft.AspNetCore.Components.Server.Circuits;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace ecocraft.Services
{
    /// <summary>
    /// CircuitHandler that compacts the LOH and triggers a full GC when a circuit closes.
    /// </summary>
    public class MemoryTrimCircuitHandler : CircuitHandler
    {
        public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
        {
            // Schedule LOH compaction
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;

            // Force a blocking full GC
            GC.Collect(
                GC.MaxGeneration,
                GCCollectionMode.Forced,
                blocking: true,
                compacting: true
            );

            return Task.CompletedTask;
        }
    }
}
