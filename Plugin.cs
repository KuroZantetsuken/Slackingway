using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using Slackingway.Windows;

[assembly: SupportedOSPlatform("windows")]

namespace Slackingway
{
    public sealed class Plugin : IDalamudPlugin
    {
        [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
        [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] internal static IFramework Framework { get; private set; } = null!;
        [PluginService] internal static IPluginLog Log { get; private set; } = null!;

        private const string CommandName = "/slackingway";

        public Configuration Configuration { get; init; }

        public readonly WindowSystem WindowSystem = new("RelativePerformanceLimiter");
        private ConfigWindow ConfigWindow { get; init; }

        public GpuMonitor GpuMonitor { get; init; }

        private Stopwatch frameStopwatch = new Stopwatch();
        private double previousSleepTimeMs = 0;

        private Stopwatch gpuPollStopwatch = new Stopwatch();
        public bool IsCalibrating { get; private set; } = false;
        private Stopwatch calibrationStopwatch = new Stopwatch();
        private List<float> calibrationSamples = new();

        public float LastGpuUsage { get; private set; } = 0;

        private double smoothedFrameTimeMs = 16.6;
        private double targetFrameTimeMs = 16.6;
        
        // PI Controller state (Velocity form)
        private double lastError = 0;
        private bool isControllerReset = true;
        private const double Kp = 0.05; // ms per % change in error
        private const double Ki = 0.10; // ms per % error per second

        private Stopwatch logStopwatch = new Stopwatch();

        // Used for gradual transition when re-enabling
        private bool wasDisabled = true;

        public bool ShowCalibrationSuccess { get; private set; } = false;
        private Stopwatch calibrationSuccessStopwatch = new Stopwatch();

        public Plugin()
        {
            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

            GpuMonitor = new GpuMonitor();
            GpuMonitor.Initialize();

            ConfigWindow = new ConfigWindow(this);
            WindowSystem.AddWindow(ConfigWindow);

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the configuration window for the Slackingway."
            });

            PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
            PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

            this.frameStopwatch.Start();
            this.gpuPollStopwatch.Start();
            this.logStopwatch.Start();
            Framework.Update += OnUpdate;
        }

        public void StartCalibration()
        {
            IsCalibrating = true;
            ShowCalibrationSuccess = false;
            calibrationSamples.Clear();
            calibrationStopwatch.Restart();
        }

        public void Dispose()
        {
            Framework.Update -= OnUpdate;

            PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
            PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;

            WindowSystem.RemoveAllWindows();
            ConfigWindow.Dispose();

            CommandManager.RemoveHandler(CommandName);
            GpuMonitor.Dispose();
        }

        private void OnCommand(string command, string args)
        {
            ToggleConfigUi();
        }

        public void ToggleConfigUi() => ConfigWindow.Toggle();

        private void OnUpdate(IFramework framework)
        {
            double elapsedMs = this.frameStopwatch.Elapsed.TotalMilliseconds;
            this.frameStopwatch.Restart();

            if (elapsedMs > 0 && elapsedMs < 1000)
            {
                this.smoothedFrameTimeMs = (this.smoothedFrameTimeMs * 0.85) + (elapsedMs * 0.15);
            }

            if (this.gpuPollStopwatch.ElapsedMilliseconds >= 1000)
            {
                this.gpuPollStopwatch.Restart();
                this.LastGpuUsage = this.GpuMonitor.GetCurrentUtilization();

                if (IsCalibrating)
                {
                    calibrationSamples.Add(this.LastGpuUsage);
                    if (calibrationStopwatch.ElapsedMilliseconds > 3000)
                    {
                        IsCalibrating = false;
                        calibrationStopwatch.Stop();
                        if (calibrationSamples.Count > 0)
                        {
                            // Use max usage seen during calibration
                            float maxUsage = calibrationSamples.Max();

                            if (maxUsage <= 0) maxUsage = 100f; // fallback
                            this.Configuration.BaselineGpuUsage = (float)Math.Round(maxUsage);
                            this.Configuration.Save();
                        }
                        ShowCalibrationSuccess = true;
                        calibrationSuccessStopwatch.Restart();
                    }
                }
                
                if (ShowCalibrationSuccess && calibrationSuccessStopwatch.ElapsedMilliseconds > 2000)
                {
                    ShowCalibrationSuccess = false;
                    calibrationSuccessStopwatch.Stop();
                }

                if (this.Configuration.IsEnabled && this.LastGpuUsage > 0)
                {
                    float targetGpuUsage = this.Configuration.TargetGpuUsage;
                    if (targetGpuUsage <= 0.01f) targetGpuUsage = 0.01f;

                    // PI Controller (Velocity Form)
                    // Output directly modulates targetFrameTimeMs. Windup is naturally bounded by clamping targetFrameTimeMs,
                    // though recovery from prolonged saturation still requires stepping back down.
                    // Positive error (usage > target) means we need more sleep (larger frame time).
                    double error = this.LastGpuUsage - targetGpuUsage;
                    
                    // Prevent deltaError spike on the very first tick after re-enabling
                    if (this.isControllerReset)
                    {
                        this.lastError = error;
                        this.isControllerReset = false;
                    }

                    double deltaError = error - this.lastError;
                    this.lastError = error;

                    double adjustmentMs = (Kp * deltaError) + (Ki * error);
                    this.targetFrameTimeMs += adjustmentMs;

                    if (this.targetFrameTimeMs > 1000) this.targetFrameTimeMs = 1000;
                    if (this.targetFrameTimeMs < 1) this.targetFrameTimeMs = 1;
                }
            }

            if (!this.Configuration.IsEnabled || IsCalibrating)
            {
                this.previousSleepTimeMs = 0;
                this.isControllerReset = true;
                this.wasDisabled = true;
                // Keep target frame time roughly aligned with current performance
                this.targetFrameTimeMs = this.smoothedFrameTimeMs;
                return;
            }

            if (this.wasDisabled)
            {
                // Gradual transition: blend the target frame time from the smoothed real time
                // so it doesn't snap abruptly
                this.targetFrameTimeMs = (this.targetFrameTimeMs * 0.8) + (this.smoothedFrameTimeMs * 0.2);
                // When they get close enough, consider transition done
                if (Math.Abs(this.targetFrameTimeMs - this.smoothedFrameTimeMs) < 1.0)
                {
                    this.wasDisabled = false;
                }
            }

            double activeTimeMs = elapsedMs - this.previousSleepTimeMs;

            // Handle abnormal frame times
            if (activeTimeMs < 0) activeTimeMs = 0;
            if (activeTimeMs > 1000)
            {
                this.previousSleepTimeMs = 0;
                return;
            }

            // Sleep for the remaining time required to hit the target frame time
            double requiredSleepMs = this.targetFrameTimeMs - activeTimeMs;

            if (requiredSleepMs > 1000)
            {
                requiredSleepMs = 1000; // Cap at 1 FPS to prevent complete freezes
            }

            double actualSleepMs = 0;
            if (requiredSleepMs > 0)
            {
                double startSleepMs = this.frameStopwatch.Elapsed.TotalMilliseconds;
                HybridSleep(requiredSleepMs);
                double endSleepMs = this.frameStopwatch.Elapsed.TotalMilliseconds;
                actualSleepMs = endSleepMs - startSleepMs;
                this.previousSleepTimeMs = actualSleepMs;
            }
            else
            {
                this.previousSleepTimeMs = 0;
            }

            if (this.Configuration.EnableLogging)
            {
                if (this.logStopwatch.ElapsedMilliseconds >= 1000) // Log every 1 second
                {
                    this.logStopwatch.Restart();
                    double currentFps = 1000.0 / elapsedMs;
                    Log.Info($"[Slackingway] FPS: {currentFps:F1} | GPU Usage: {this.LastGpuUsage:F1}% | Baseline: {this.Configuration.BaselineGpuUsage:F0}% | TargetFrame: {this.targetFrameTimeMs:F2}ms | Active: {activeTimeMs:F2}ms | ReqSleep: {requiredSleepMs:F2}ms | ActualSleep: {actualSleepMs:F2}ms");
                }
            }
        }

        private void HybridSleep(double milliseconds)
        {
            if (milliseconds <= 0) return;

            long startTicks = Stopwatch.GetTimestamp();
            long targetTicks = startTicks + (long)(milliseconds * Stopwatch.Frequency / 1000.0);

            int sleepTime = (int)Math.Floor(milliseconds) - 2;
            if (sleepTime > 0)
            {
                Thread.Sleep(sleepTime);
            }

            while (Stopwatch.GetTimestamp() < targetTicks)
            {
                long remainingTicks = targetTicks - Stopwatch.GetTimestamp();
                double remainingMs = remainingTicks * 1000.0 / Stopwatch.Frequency;
                
                if (remainingMs > 1.5)
                {
                    Thread.Sleep(1);
                }
                else
                {
                    Thread.SpinWait(10);
                }
            }
        }
    }
}
