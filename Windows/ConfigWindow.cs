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

        private readonly Vector2 windowSize = new Vector2(260, 320);

        private CheckboxNode enableLimiterNode = null!;
        private CheckboxNode enableLoggingNode = null!;
        private TextNode targetGpuUsageLabelNode = null!;
        private NumericInputNode targetGpuUsageNode = null!;
        private TextNode lowerTextNode = null!;
        private HorizontalLineNode separatorNode = null!;
        private HorizontalLineNode separatorNode2 = null!;
        private TextNode guideHeaderNode = null!;
        private TextNode guideTextNode = null!;
        private TextNode currentGpuUsageNode = null!;

        public ConfigWindow(Plugin plugin)
        {
            this.configuration = plugin.Configuration;
            this.plugin = plugin;
            this.Size = windowSize;
        }

        protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> atkValueSpan)
        {
            var innerWidth = windowSize.X - 10f - ContentStartPosition.X;

            enableLimiterNode = new CheckboxNode
            {
                String = "Enable Limiter",
                IsChecked = configuration.IsEnabled,
                Position = ContentStartPosition,
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
                String = "Enable Logging",
                IsChecked = configuration.EnableLogging,
                Position = ContentStartPosition + new Vector2(0, 20),
                Size = new Vector2(200, 24),
                OnClick = val =>
                {
                    configuration.EnableLogging = val;
                    configuration.Save();
                }
            };
            enableLoggingNode.AttachNode(this);

            separatorNode2 = new HorizontalLineNode
            {
                Position = ContentStartPosition + new Vector2(0, 50),
                Size = new Vector2(innerWidth, 2)
            };
            separatorNode2.AttachNode(this);

            currentGpuUsageNode = new TextNode
            {
                Position = ContentStartPosition + new Vector2(0, 60),
                Size = new Vector2(innerWidth, 20),
                AlignmentType = AlignmentType.TopLeft,
                FontSize = 14,
            };
            currentGpuUsageNode.AttachNode(this);

            targetGpuUsageLabelNode = new TextNode
            {
                String = "Target GPU Usage:",
                Position = ContentStartPosition + new Vector2(0, 80),
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
                Position = ContentStartPosition + new Vector2(innerWidth - 100, 75),
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
                String = "Lower until other apps have enough headroom to function properly.",
                Position = ContentStartPosition + new Vector2(0, 110),
                Size = new Vector2(innerWidth, 40),
                AlignmentType = AlignmentType.TopLeft,
                LineSpacing = 16,
                TextFlags = TextFlags.WordWrap | TextFlags.MultiLine,
            };
            lowerTextNode.AttachNode(this);

            separatorNode = new HorizontalLineNode
            {
                Position = ContentStartPosition + new Vector2(0, 150),
                Size = new Vector2(innerWidth, 2)
            };
            separatorNode.AttachNode(this);

            guideHeaderNode = new TextNode
            {
                String = "Recommended setup:",
                Position = ContentStartPosition + new Vector2(0, 160),
                Size = new Vector2(innerWidth, 20),
                AlignmentType = AlignmentType.TopLeft,
                FontSize = 14,
            };
            guideHeaderNode.AttachNode(this);

            guideTextNode = new TextNode
            {
                String = "1. Stand in an inn.\n2. Play YouTube with FFXIV in focus.\n3. Lower the 'Target GPU Usage' until the video stops stuttering.\n",
                Position = ContentStartPosition + new Vector2(0, 185),
                Size = new Vector2(innerWidth, windowSize.Y - 230),
                AlignmentType = AlignmentType.TopLeft,
                LineSpacing = 16,
                TextFlags = TextFlags.WordWrap | TextFlags.MultiLine,
            };
            guideTextNode.AttachNode(this);
        }

        protected override unsafe void OnUpdate(AtkUnitBase* addon)
        {
            currentGpuUsageNode.String = $"Current GPU Usage: {plugin.LastGpuUsage:F1}%";
        }
    }
}
