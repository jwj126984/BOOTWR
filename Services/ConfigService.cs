using System;
using System.IO;
using System.Text.Json;

namespace BOOTWR.Services
{
    public class CanConfig
    {
        public string SelectedDeviceTypeName { get; set; } = "USBCANFD-200U";
        public int DeviceIndex { get; set; } = 0;
        public int ChannelIndex { get; set; } = 0;
        public string SelectedBaudRateName { get; set; } = "250 kbps";
        public string UdsPhysicalAddress { get; set; } = "7E0";
        public string UdsResponseAddress { get; set; } = "7E8";
    }

    public static class ConfigService
    {
        private static readonly string ConfigFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BOOTWR",
            "config.json");

        public static CanConfig LoadConfig()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    return JsonSerializer.Deserialize<CanConfig>(json) ?? new CanConfig();
                }
            }
            catch
            {
            }

            return new CanConfig();
        }

        public static void SaveConfig(CanConfig config)
        {
            try
            {
                string directory = Path.GetDirectoryName(ConfigFilePath)!;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigFilePath, json);
            }
            catch
            {
            }
        }
    }
}