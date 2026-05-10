using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit;
using KamiToolKit.Nodes;
using KamiToolKit.Classes;
using Lumina.Text.ReadOnly;
using KamiToolKit.Premade.Node.Simple;

namespace Slackingway.Windows
{
    public class ConfigWindow : NativeAddon
    {
        private readonly Configuration configuration;
        private readonly Plugin plugin;

        private CheckboxNode enableLimiterNode = null!;
        private CheckboxNode enableLoggingNode = null!;
        private TextNode targetGpuUsageLabelNode = null!;
        private NumericInputNode targetGpuUsageNode = null!;
        private TextNode lowerTextNode = null!;
        private TextNode recommendedTextNode = null!;
        private HorizontalLineNode separatorNode = null!;
        private TextButtonNode calibrateButtonNode = null!;
        private TextNode calibrationStatusNode = null!;
        private TextNode calibrateDescNode = null!;
        private TextNode currentGpuUsageNode = null!;
        private TextNode baselineMaxUsageNode = null!;

        public ConfigWindow(Plugin plugin)
        {
            this.configuration = plugin.Configuration;
            this.plugin = plugin;
            this.Size = new Vector2(450, 360);
        }

        protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
        {
            var contentStart = ContentStartPosition + new Vector2(10, 10);
            var innerWidth = Size.X - 20f - ContentStartPosition.X;

            enableLimiterNode = new CheckboxNode
            {
                String = "Enable Limiter",
                IsChecked = configuration.IsEnabled,
                Position = contentStart,
                Size = new Vector2(150, 24),
                OnClick = val =>
                {
                    configuration.IsEnabled = val;
                    configuration.Save();
                }
            };
            enableLimiterNode.AttachNode(this);

            enableLoggingNode = new CheckboxNode
            {
                String = "Enable Verbose Logging",
                IsChecked = configuration.EnableLogging,
                Position = contentStart + new Vector2(160, 0),
                Size = new Vector2(200, 24),
                OnClick = val =>
                {
                    configuration.EnableLogging = val;
                    configuration.Save();
                }
            };
            enableLoggingNode.AttachNode(this);

            targetGpuUsageLabelNode = new TextNode
            {
                String = "Target GPU Usage %",
                Position = contentStart + new Vector2(0, 40),
                Size = new Vector2(200, 20),
                AlignmentType = AlignmentType.TopLeft,
                FontSize = 14,
            };
            targetGpuUsageLabelNode.AttachNode(this);

            targetGpuUsageNode = new NumericInputNode
            {
                Min = 1,
                Max = 100,
                Step = 1,
                Value = (int)configuration.TargetGpuUsage,
                Position = contentStart + new Vector2(innerWidth - 100, 35),
                Size = new Vector2(100, 28),
                OnValueUpdate = val =>
                {
                    configuration.TargetGpuUsage = val;
                    configuration.Save();
                }
            };
            targetGpuUsageNode.AttachNode(this);

            lowerTextNode = new TextNode
            {
                String = "Lower until other processes have enough headroom to function properly.",
                Position = contentStart + new Vector2(0, 75),
                Size = new Vector2(innerWidth, 40),
                AlignmentType = AlignmentType.TopLeft,
                FontSize = 14,
                LineSpacing = 20,
                TextFlags = TextFlags.WordWrap | TextFlags.MultiLine,
            };
            lowerTextNode.AttachNode(this);

            recommendedTextNode = new TextNode
            {
                String = "Recommended to set this in a GPU limited scene while playing a video on a secondary monitor.",
                Position = contentStart + new Vector2(0, 115),
                Size = new Vector2(innerWidth, 40),
                AlignmentType = AlignmentType.TopLeft,
                FontSize = 14,
                LineSpacing = 20,
                TextFlags = TextFlags.WordWrap | TextFlags.MultiLine,
            };
            recommendedTextNode.AttachNode(this);

            separatorNode = new HorizontalLineNode
            {
                Position = contentStart + new Vector2(0, 165),
                Size = new Vector2(innerWidth, 2)
            };
            separatorNode.AttachNode(this);

            calibrateButtonNode = new TextButtonNode
            {
                String = "Calibrate Baseline",
                Position = contentStart + new Vector2(0, 175),
                Size = new Vector2(150, 28),
                OnClick = () => plugin.StartCalibration()
            };
            calibrateButtonNode.AttachNode(this);

            calibrationStatusNode = new TextNode
            {
                Position = contentStart + new Vector2(160, 177),
                Size = new Vector2(200, 24),
                AlignmentType = AlignmentType.Left,
                FontSize = 14,
                IsVisible = false
            };
            calibrationStatusNode.AttachNode(this);

            calibrateDescNode = new TextNode
            {
                String = "Set this in a GPU limited scene to calibrate peak usage.",
                Position = contentStart + new Vector2(0, 210),
                Size = new Vector2(innerWidth, 40),
                AlignmentType = AlignmentType.TopLeft,
                FontSize = 14,
                LineSpacing = 20,
                TextFlags = TextFlags.WordWrap | TextFlags.MultiLine,
            };
            calibrateDescNode.AttachNode(this);

            currentGpuUsageNode = new TextNode
            {
                Position = contentStart + new Vector2(0, 250),
                Size = new Vector2(innerWidth, 20),
                AlignmentType = AlignmentType.TopLeft,
                FontSize = 14,
            };
            currentGpuUsageNode.AttachNode(this);

            baselineMaxUsageNode = new TextNode
            {
                Position = contentStart + new Vector2(0, 270),
                Size = new Vector2(innerWidth, 20),
                AlignmentType = AlignmentType.TopLeft,
                FontSize = 14,
            };
            baselineMaxUsageNode.AttachNode(this);
        }

        protected override unsafe void OnUpdate(AtkUnitBase* addon)
        {
            calibrateButtonNode.IsEnabled = !plugin.IsCalibrating;

            if (plugin.IsCalibrating)
            {
                calibrationStatusNode.String = "Calibrating...";
                calibrationStatusNode.TextColor = new Vector4(1, 1, 0, 1);
                calibrationStatusNode.IsVisible = true;
            }
            else
            {
                if (plugin.ShowCalibrationSuccess)
                {
                    calibrationStatusNode.String = "Calibration Complete!";
                    calibrationStatusNode.TextColor = new Vector4(0, 1, 0, 1);
                    calibrationStatusNode.IsVisible = true;
                }
                else
                {
                    calibrationStatusNode.IsVisible = false;
                }
            }

            currentGpuUsageNode.String = $"Current GPU Usage: {plugin.LastGpuUsage:F1}%";
            baselineMaxUsageNode.String = $"Baseline Max Usage: {configuration.BaselineGpuUsage:F0}%";
        }
    }
}
