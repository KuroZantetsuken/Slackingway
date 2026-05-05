using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Interface.Windowing;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Slackingway.Windows;

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

        private Stopwatch frameStopwatch = new Stopwatch();
        private double previousSleepTimeMs = 0;

        private double baselineFrameTimeMs = 16.6; // Default to 60 FPS
        private bool isBaselineValid = false;
        private int baselineCalibrationFrames = 0;

        private int logCounter = 0;

        public Plugin()
        {
            Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

            ConfigWindow = new ConfigWindow(this);
            WindowSystem.AddWindow(ConfigWindow);

            CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "Opens the configuration window for the Slackingway."
            });

            PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
            PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

            this.frameStopwatch.Start();
            Framework.Update += OnUpdate;
        }

        public void Dispose()
        {
            Framework.Update -= OnUpdate;

            PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
            PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;

            WindowSystem.RemoveAllWindows();
            ConfigWindow.Dispose();

            CommandManager.RemoveHandler(CommandName);
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

            if (!this.Configuration.IsEnabled)
            {
                this.previousSleepTimeMs = 0;

                // Track natural frame time while disabled
                if (elapsedMs > 0 && elapsedMs < 100)
                {
                    this.baselineFrameTimeMs = (this.baselineFrameTimeMs * 0.95) + (elapsedMs * 0.05);
                    this.isBaselineValid = true;
                }
                return;
            }

            // If enabled but we have no valid baseline, spend a few frames calibrating
            if (!this.isBaselineValid)
            {
                this.previousSleepTimeMs = 0;
                if (elapsedMs > 0 && elapsedMs < 100)
                {
                    this.baselineFrameTimeMs = (this.baselineFrameTimeMs * 0.95) + (elapsedMs * 0.05);
                    this.baselineCalibrationFrames++;
                    if (this.baselineCalibrationFrames > 60)
                    {
                        this.isBaselineValid = true;
                    }
                }
                return;
            }

            double activeTimeMs = elapsedMs - this.previousSleepTimeMs;

            // Handle abnormal frame times
            if (activeTimeMs < 0) activeTimeMs = 0;
            if (activeTimeMs > 100)
            {
                this.previousSleepTimeMs = 0;
                return;
            }

            double targetRatio = this.Configuration.TargetPercentage / 100.0;
            if (targetRatio <= 0.01) targetRatio = 0.01;
            if (targetRatio >= 0.999)
            {
                this.previousSleepTimeMs = 0;
                return;
            }

            // Calculate target frame time based on the locked natural baseline
            double targetFrameTimeMs = this.baselineFrameTimeMs / targetRatio;

            // Sleep for the remaining time required to hit the target frame time
            double requiredSleepMs = targetFrameTimeMs - activeTimeMs;

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
                    Log.Info($"[Slackingway] FPS: {currentFps:F1} | Baseline: {this.baselineFrameTimeMs:F2}ms | TargetRatio: {targetRatio:P0} | TargetFrame: {targetFrameTimeMs:F2}ms | Active: {activeTimeMs:F2}ms | ReqSleep: {requiredSleepMs:F2}ms | ActualSleep: {actualSleepMs:F2}ms");
                }
            }
        }

        private void HybridSleep(double milliseconds)
        {
            if (milliseconds <= 0) return;

            int sleepTime = (int)Math.Round(milliseconds);
            if (sleepTime > 0)
            {
                Thread.Sleep(sleepTime);
            }
        }
    }
}
