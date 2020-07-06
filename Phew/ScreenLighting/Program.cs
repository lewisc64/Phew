using Phew;
using System;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Configuration;
using MongoDB.Bson;
using System.Collections.Generic;
using System.Globalization;
using System.ComponentModel;

namespace ScreenLighting
{
    class Program
    {
        private static readonly Dictionary<string, double> _defaultConfig = new Dictionary<string, double>()
        {
            { "transitionTimeMilliseconds", 200 },
            { "captureIntervalMilliseconds", 0 },
            { "lightOffThresholdPercent", 0 },
            { "brightnessMultiplier", 4 },
            { "saturationMultiplier", 2.5 },
        };

        static void Main(string[] args)
        {
            var bridge = GetBridge();
            if (bridge == null)
            {
                return;
            }

            var light = SelectLight(bridge);

            var config = GetConfig();

            light.On = true;
            light.Effect = "none";
            light.TransitionTime = (int)config["transitionTimeMilliseconds"] / 100;

            light.AutoUpdateState = false;

            while (true)
            {
                var color = GetAverageColorInRegion(0, 0, 1920, 1080);

                var hue = color.GetHue();
                var saturation = Math.Min(color.GetSaturation() * config["saturationMultiplier"], 1) * 100;
                var brightness = Math.Min(color.GetBrightness() * config["brightnessMultiplier"], 1) * 100;

                light.Hue = hue;
                light.Saturation = saturation;
                light.Brightness = brightness;
                light.On = light.Brightness >= config["lightOffThresholdPercent"];

                light.UpdateState();

                Thread.Sleep((int)config["captureIntervalMilliseconds"]);
            }
        }

        private static Light SelectLight(Bridge bridge)
        {
            var lights = bridge.GetLights();

            Console.Write($"Which light?:\n{string.Join("\n", lights.Select(x => $"{x.Number}. {x.Name}"))}\n>");
            var chosen = Convert.ToInt32(Console.ReadLine());

            return lights.Single(x => x.Number == chosen);
        }

        private static Bridge GetBridge()
        {
            var bridges = Bridge.GetBridges();

            if (bridges.Count == 0)
            {
                Console.WriteLine("Failed to find any Philips Hue Bridges.");
                Console.ReadLine();
                return null;
            }

            string bridgeId;

            if (bridges.Count == 1)
            {
                bridgeId = bridges.Single().Key;
            }
            else
            {
                throw new NotImplementedException();
            }

            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var bridgesConfigString = config.AppSettings.Settings["bridges"]?.Value;
            var bridgesConfig = bridgesConfigString == null ? new BsonDocument() : BsonDocument.Parse(bridgesConfigString);

            Bridge bridge;

            if (bridgesConfig.Contains(bridgeId))
            {
                bridge = new Bridge(bridgeId, bridgesConfig[bridgeId].AsString);
            }
            else
            {
                bridge = new Bridge(bridgeId);
                bridge.RegisterIfNotRegistered(() => { Console.WriteLine("Please press the link button."); });
                bridgesConfig[bridgeId] = bridge.Username;
                if (!config.AppSettings.Settings.AllKeys.Contains("bridges"))
                {
                    config.AppSettings.Settings.Add(new KeyValueConfigurationElement("bridges", null));
                }
                config.AppSettings.Settings["bridges"].Value = bridgesConfig.ToString();
            }

            config.Save();

            return bridge;
        }

        private static Dictionary<string, double> GetConfig()
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            var result = new Dictionary<string, double>();
            foreach (var key in _defaultConfig.Keys)
            {
                var configValue = config.AppSettings.Settings[key]?.Value;
                if (configValue == null)
                {
                    result[key] = _defaultConfig[key];
                    if (!config.AppSettings.Settings.AllKeys.Contains(key))
                    {
                        config.AppSettings.Settings.Add(key, null);
                    }
                    config.AppSettings.Settings[key].Value = _defaultConfig[key].ToString(CultureInfo.InvariantCulture);
                }
                else
                {
                    result[key] = double.Parse(configValue, CultureInfo.InvariantCulture);
                }
            }
            config.Save();
            return result;
        }

        private static Color GetAverageColorInRegion(int x, int y, int width, int height)
        {
            try
            {
                Bitmap bmpScreenCapture = new Bitmap(width, height);
                Graphics g = Graphics.FromImage(bmpScreenCapture);
                g.CopyFromScreen(x, y, 0, 0, bmpScreenCapture.Size, CopyPixelOperation.SourceCopy);
                g.Dispose();

                Bitmap bmp = new Bitmap(1, 1);
                using (Graphics g2 = Graphics.FromImage(bmp))
                {
                    g2.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g2.DrawImage(bmpScreenCapture, new Rectangle(0, 0, 1, 1));
                }
                Color pixel = bmp.GetPixel(0, 0);
                bmpScreenCapture.Dispose();
                bmp.Dispose();
                return pixel;
            }
            catch (Win32Exception)
            {
                return Color.Red;
            }
        }

        private static Color AverageColor(params Color[] colors)
        {
            int r = 0;
            int g = 0;
            int b = 0;
            foreach (var color in colors)
            {
                r += color.R;
                g += color.G;
                b += color.B;
            }
            return Color.FromArgb(r / colors.Length, g / colors.Length, b / colors.Length);
        }
    }
}
