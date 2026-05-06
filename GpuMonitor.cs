using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Slackingway
{
    public class GpuMonitor
    {
        private List<PerformanceCounter> gpuCounters = new();
        private int currentPid;

        public void Initialize()
        {
            try
            {
                this.currentPid = Process.GetCurrentProcess().Id;
                RefreshCounters();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Failed to initialize GpuMonitor.");
            }
        }

        public void RefreshCounters()
        {
            foreach (var counter in this.gpuCounters)
            {
                counter.Dispose();
            }
            this.gpuCounters.Clear();

            try
            {
                if (!PerformanceCounterCategory.Exists("GPU Engine"))
                {
                    Plugin.Log.Warning("[Slackingway] 'GPU Engine' PerformanceCounterCategory does not exist.");
                    return;
                }

                var category = new PerformanceCounterCategory("GPU Engine");
                var instanceNames = category.GetInstanceNames();

                var processInstances = instanceNames.Where(i => i.Contains($"pid_{this.currentPid}_") && i.Contains("engtype_3D")).ToList();

                foreach (var instance in processInstances)
                {
                    var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, true);
                    // Read it once to initialize it
                    counter.NextValue();
                    this.gpuCounters.Add(counter);
                }
                Plugin.Log.Info($"[Slackingway] Found {this.gpuCounters.Count} 3D GPU Engine instances for PID {this.currentPid}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Failed to refresh GPU counters.");
            }
        }

        public float GetCurrentUtilization()
        {
            float totalUtilization = 0;
            try
            {
                foreach (var counter in this.gpuCounters)
                {
                    totalUtilization += counter.NextValue();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "Error reading GPU counters. Attempting to refresh.");
                RefreshCounters();
            }
            return totalUtilization;
        }

        public void Dispose()
        {
            foreach (var counter in this.gpuCounters)
            {
                counter.Dispose();
            }
            this.gpuCounters.Clear();
        }
    }
}