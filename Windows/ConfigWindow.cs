using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace Slackingway.Windows
{
    public class ConfigWindow : Window, IDisposable
    {
        private readonly Configuration configuration;
        private readonly Plugin plugin;

        public ConfigWindow(Plugin plugin)
            : base("Slackingway Config", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            this.Size = new Vector2(400, 300);
            this.SizeCondition = ImGuiCond.FirstUseEver;
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(300, 200),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };

            this.configuration = plugin.Configuration;
            this.plugin = plugin;
        }

        public void Dispose()
        {
        }

        public override void Draw()
        {
            var enabled = this.configuration.IsEnabled;
            if (ImGui.Checkbox("Enable Limiter", ref enabled))
            {
                this.configuration.IsEnabled = enabled;
                this.configuration.Save();
            }

            ImGui.SameLine();

            var logging = this.configuration.EnableLogging;
            if (ImGui.Checkbox("Enable Verbose Logging", ref logging))
            {
                this.configuration.EnableLogging = logging;
                this.configuration.Save();
            }

            ImGui.Spacing();

            var targetPercentage = (int)this.configuration.TargetPercentage;
            if (ImGui.SliderInt("Target Performance %", ref targetPercentage, 10, 99, "%d%%"))
            {
                this.configuration.TargetPercentage = targetPercentage;
                this.configuration.Save();
            }

            ImGui.TextWrapped("Lower until other processes have enough headroom to function properly.");
            ImGui.TextWrapped("Recommended to set this in a GPU limited scene while playing a video on a secondary monitor.");

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (this.plugin.IsCalibrating)
            {
                ImGui.TextColored(new Vector4(1, 1, 0, 1), "Calibrating...");
            }
            else
            {
                if (ImGui.Button("Calibrate Baseline"))
                {
                    this.plugin.StartCalibration();
                }

                if (this.plugin.ShowCalibrationSuccess)
                {
                    ImGui.TextColored(new Vector4(0, 1, 0, 1), "Calibration Complete!");
                }

                ImGui.TextWrapped("Set this in a GPU limited scene to calibrate peak usage.");
            }

            ImGui.Text($"Current GPU Usage: {this.plugin.LastGpuUsage:F1}%");
            ImGui.Text($"Baseline Max Usage: {this.configuration.BaselineGpuUsage:F0}%");
        }
    }
}
