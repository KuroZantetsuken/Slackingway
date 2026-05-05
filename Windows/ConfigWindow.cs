using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Numerics;

namespace Slackingway.Windows
{
    public class ConfigWindow : Window, IDisposable
    {
        private readonly Configuration configuration;

        public ConfigWindow(Plugin plugin)
            : base("Relative Performance Limiter Config", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
        {
            this.Size = new Vector2(400, 200);
            this.SizeCondition = ImGuiCond.FirstUseEver;
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(400, 200),
                MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
            };

            this.configuration = plugin.Configuration;
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

            var targetPercentage = this.configuration.TargetPercentage;
            if (ImGui.SliderFloat("Target Performance %", ref targetPercentage, 10.0f, 99.0f, "%.1f%%"))
            {
                this.configuration.TargetPercentage = targetPercentage;
                this.configuration.Save();
            }

            ImGui.TextWrapped("Lower percentages will reduce GPU load further but increase input latency and decrease framerate.");
        }
    }
}
