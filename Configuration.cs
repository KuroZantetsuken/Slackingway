using Dalamud.Configuration;
using System;

namespace Slackingway
{
    [Serializable]
    public class Configuration : IPluginConfiguration
    {
        public int Version { get; set; } = 0;

        public bool IsEnabled { get; set; } = true;
        public float TargetPercentage { get; set; } = 90.0f;
        public bool EnableLogging { get; set; } = false;

        public void Save()
        {
            Plugin.PluginInterface.SavePluginConfig(this);
        }
    }
}
