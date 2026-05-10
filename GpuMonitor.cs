using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace Slackingway
{
    /// <summary>
    /// Monitors GPU usage for the current process using the native Windows Performance Data Helper (PDH) API.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class GpuMonitor : IDisposable
    {
        #region P/Invoke Definitions for PDH.dll

        // PdhOpenQuery: Creates a new query that is used to manage the collection of performance data.
        [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint PdhOpenQuery(string? szDataSource, IntPtr dwUserData, out IntPtr phQuery);

        // PdhAddCounter: Adds the specified counter to the query.
        [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint PdhAddCounter(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

        // PdhCollectQueryData: Collects the current raw data value for all counters in the specified query.
        [DllImport("pdh.dll", SetLastError = true)]
        private static extern uint PdhCollectQueryData(IntPtr hQuery);

        // PdhGetFormattedCounterValue: Computes a displayable value for the specified counter.
        [DllImport("pdh.dll", SetLastError = true)]
        private static extern uint PdhGetFormattedCounterValue(IntPtr hCounter, uint dwFormat, out uint lpdwType, out PDH_FMT_COUNTERVALUE pValue);

        // PdhExpandWildCardPath: Examines the specified computer or log file and returns those counter paths that match the given counter path which contains wildcard characters.
        [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint PdhExpandWildCardPath(
            string? szDataSource,
            string szWildCardPath,
            IntPtr mszExpandedPathList,
            ref uint pcchPathListLength,
            uint dwFlags);

        // PdhCloseQuery: Closes all counters contained in the specified query, closes all handles related to the query, and frees all memory associated with the query.
        [DllImport("pdh.dll", SetLastError = true)]
        private static extern uint PdhCloseQuery(IntPtr hQuery);

        [StructLayout(LayoutKind.Sequential)]
        private struct PDH_FMT_COUNTERVALUE
        {
            public uint CStatus;
            public double doubleValue;
        }

        // PDH_FMT_DOUBLE: Return data as a double-precision floating point real.
        private const uint PDH_FMT_DOUBLE = 0x00000200;

        // PDH_MORE_DATA: The specified buffer is not large enough to hold the data.
        private const uint PDH_MORE_DATA = 0x800007D2;

        #endregion

        // The ID of the current process, used to find GPU engine instances belonging to this game.
        private readonly int currentPid;

        // A lock object used to ensure thread safety when swapping out query and counter handles.
        private readonly object stateLock = new();

        // Handles for the native PDH API.
        private IntPtr activeQueryHandle = IntPtr.Zero;
        private List<IntPtr> activeCounterHandles = new();

        // State tracking to determine when a background refresh is needed.
        private bool isRefreshing = false;
        private DateTime lastRefreshTime = DateTime.MinValue;
        private int consecutiveZeroReads = 0;

        public GpuMonitor()
        {
            this.currentPid = Process.GetCurrentProcess().Id;
        }

        /// <summary>
        /// Initializes the monitor by kicking off a background task to discover the GPU engines.
        /// This is done in the background to prevent hanging the main game thread.
        /// </summary>
        public void Initialize()
        {
            Task.Run(RefreshCountersAsync);
        }

        /// <summary>
        /// Reads the current utilization across all discovered 3D GPU engines for this process.
        /// </summary>
        /// <returns>The total GPU utilization percentage.</returns>
        public float GetCurrentUtilization()
        {
            float totalUtilization = 0;

            // Step 1: Safely grab references to the current query and counters.
            IntPtr queryHandle;
            IntPtr[] counters;

            lock (this.stateLock)
            {
                queryHandle = this.activeQueryHandle;
                counters = this.activeCounterHandles.ToArray();
            }

            // Step 2: If we have an active query, collect the data.
            if (queryHandle != IntPtr.Zero && counters.Length > 0)
            {
                uint result = PdhCollectQueryData(queryHandle);
                if (result == 0) // 0 means ERROR_SUCCESS
                {
                    foreach (IntPtr counter in counters)
                    {
                        if (PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE, out uint _, out PDH_FMT_COUNTERVALUE value) == 0)
                        {
                            totalUtilization += (float)value.doubleValue;
                        }
                    }
                }
            }

            // Step 3: Check if we need to refresh the counters.
            // A refresh is needed if the game spawned a new GPU engine, or if the current engines went dormant.
            if (totalUtilization <= 0)
            {
                this.consecutiveZeroReads++;
            }
            else
            {
                this.consecutiveZeroReads = 0;
            }

            bool needsRefresh = (this.consecutiveZeroReads >= 5 && (DateTime.UtcNow - this.lastRefreshTime).TotalSeconds > 10) ||
                                ((DateTime.UtcNow - this.lastRefreshTime).TotalSeconds > 60);

            if (needsRefresh)
            {
                this.consecutiveZeroReads = 0;
                Task.Run(RefreshCountersAsync);
            }

            return totalUtilization;
        }

        /// <summary>
        /// Discovers the 3D GPU engines for the current process and sets up a new PDH query to monitor them.
        /// </summary>
        private void RefreshCountersAsync()
        {
            // Prevent multiple simultaneous refreshes.
            lock (this.stateLock)
            {
                if (this.isRefreshing) return;
                this.isRefreshing = true;
            }

            try
            {
                // Create a new PDH query.
                if (PdhOpenQuery(null, IntPtr.Zero, out IntPtr newQueryHandle) != 0)
                {
                    Plugin.Log.Error("Failed to open a new PDH query.");
                    return;
                }

                var newCounters = new List<IntPtr>();

                // Define the wildcard path to find all 3D GPU engines belonging to the current process.
                string wildcardPath = $"\\GPU Engine(pid_{this.currentPid}_*_engtype_3D)\\Utilization Percentage";

                // First pass: Determine the required buffer size for the expanded paths.
                uint bufferSize = 0;
                uint expandResult = PdhExpandWildCardPath(null, wildcardPath, IntPtr.Zero, ref bufferSize, 0);

                if (expandResult == 0 || expandResult == PDH_MORE_DATA)
                {
                    // Allocate unmanaged memory for the paths string.
                    IntPtr pPaths = Marshal.AllocHGlobal((int)bufferSize * 2); // * 2 because characters are unicode (2 bytes)

                    // Second pass: Actually retrieve the expanded paths.
                    if (PdhExpandWildCardPath(null, wildcardPath, pPaths, ref bufferSize, 0) == 0)
                    {
                        // Read the unmanaged string and split it by the null terminator.
                        string pathsStr = Marshal.PtrToStringUni(pPaths, (int)bufferSize);
                        string[] paths = pathsStr.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);

                        foreach (string path in paths)
                        {
                            // Add each discovered engine to the new query.
                            if (PdhAddCounter(newQueryHandle, path, IntPtr.Zero, out IntPtr counterHandle) == 0)
                            {
                                newCounters.Add(counterHandle);
                            }
                        }
                    }

                    Marshal.FreeHGlobal(pPaths);
                }

                // Do one initial collect so the next call to GetFormattedCounterValue has a baseline to work with.
                PdhCollectQueryData(newQueryHandle);

                Plugin.Log.Info($"[Slackingway] GPU Monitor Refreshed. Found {newCounters.Count} 3D engines.");

                // Safely swap the new query and counters into the active state, and grab the old ones to clean up.
                IntPtr oldQueryHandle;
                lock (this.stateLock)
                {
                    oldQueryHandle = this.activeQueryHandle;
                    this.activeQueryHandle = newQueryHandle;
                    this.activeCounterHandles = newCounters;
                }

                // Cleanup the old query if it existed. This safely disposes of the old native resources.
                if (oldQueryHandle != IntPtr.Zero)
                {
                    PdhCloseQuery(oldQueryHandle);
                }

                this.lastRefreshTime = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error(ex, "An error occurred while refreshing GPU counters.");
            }
            finally
            {
                lock (this.stateLock)
                {
                    this.isRefreshing = false;
                }
            }
        }

        public void Dispose()
        {
            lock (this.stateLock)
            {
                if (this.activeQueryHandle != IntPtr.Zero)
                {
                    PdhCloseQuery(this.activeQueryHandle);
                    this.activeQueryHandle = IntPtr.Zero;
                    this.activeCounterHandles.Clear();
                }
            }
        }
    }
}
