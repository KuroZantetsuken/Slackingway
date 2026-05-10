using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        private int logCounter = 0;

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
            Framework.Update += OnUpdate;
        }

        public void StartCalibration()
        {
            IsCalibrating = true;
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
                this.smoothedFrameTimeMs = (this.smoothedFrameTimeMs * 0.95) + (elapsedMs * 0.05);
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
                            float maxUsage = 0;
                            foreach (var sample in calibrationSamples)
                                if (sample > maxUsage) maxUsage = sample;

                            if (maxUsage <= 0) maxUsage = 100f; // fallback
                            this.Configuration.BaselineGpuUsage = maxUsage;
                            this.Configuration.Save();
                        }
                    }
                }
                else if (this.Configuration.IsEnabled && this.LastGpuUsage > 0)
                {
                    float targetGpuUsage = (this.Configuration.TargetPercentage / 100f) * this.Configuration.BaselineGpuUsage;
                    if (targetGpuUsage <= 0.01f) targetGpuUsage = 0.01f;

                    double newTargetFrameTimeMs = this.smoothedFrameTimeMs * (this.LastGpuUsage / targetGpuUsage);
                    
                    // Smooth the target frame time update
                    this.targetFrameTimeMs = (this.targetFrameTimeMs * 0.7) + (newTargetFrameTimeMs * 0.3);

                    if (this.targetFrameTimeMs > 1000) this.targetFrameTimeMs = 1000;
                    if (this.targetFrameTimeMs < 1) this.targetFrameTimeMs = 1;
                }
            }

            if (!this.Configuration.IsEnabled || IsCalibrating)
            {
                this.previousSleepTimeMs = 0;
                // Keep target frame time somewhat updated when disabled
                this.targetFrameTimeMs = this.smoothedFrameTimeMs;
                return;
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
                this.logCounter++;
                if (this.logCounter >= 60)
                {
                    this.logCounter = 0;
                    double currentFps = 1000.0 / elapsedMs;
                    Log.Info($"[Slackingway] FPS: {currentFps:F1} | GPU Usage: {this.LastGpuUsage:F1}% | Baseline: {this.Configuration.BaselineGpuUsage:F1}% | TargetFrame: {this.targetFrameTimeMs:F2}ms | Active: {activeTimeMs:F2}ms | ReqSleep: {requiredSleepMs:F2}ms | ActualSleep: {actualSleepMs:F2}ms");
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
                Thread.SpinWait(10);
            }
        }
    }
}
