using System.IO;
using Newtonsoft.Json;
using KinectCaptureApp.Models;

namespace KinectCaptureApp.Services
{
    public static class ConfigLoader
    {
        public static DeviceConfig Load(string path)
        {
            string json = File.ReadAllText(path);

            return JsonConvert.DeserializeObject<DeviceConfig>(json);
        }
    }
}