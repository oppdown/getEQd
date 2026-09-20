using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GetEQd.Audio;
using GetEQd.Ui;

namespace GetEQd.Diagnostics
{
    /// <summary>
    /// Renders the console off-screen to a PNG. This exists so the interface can be
    /// reviewed as an image without a human having to open a window, and so a build can
    /// prove the XAML parses and lays out.
    /// </summary>
    public static class PreviewRenderer
    {
        public static int Run(string[] args, TextWriter output)
        {
            string? path = args.Length > 1 ? args[1] : null;
            if (string.IsNullOrWhiteSpace(path))
            {
                output.WriteLine("usage: getEQd.exe --render-preview <output.png> [--preset id] [--route 5.1|2.1|headphones] [--playing] [--advanced] [--width n] [--height n] [--scale n]");
                return 2;
            }

            string? preset = null;
            Route? route = null;
            bool playing = false;
            bool advanced = false;
            double width = 1340;
            double height = 912;
            double scale = 1.0;
            List<Int32Rect> probes = new List<Int32Rect>();

            for (int i = 2; i < args.Length - 1; i++)
            {
                string key = args[i].ToLowerInvariant();
                string value = args[i + 1];

                switch (key)
                {
                    case "--preset":
                        preset = value.ToLowerInvariant();
                        break;

                    case "--route":
                        route = value.ToLowerInvariant() switch
                        {
                            "5.1" or "surround" or "surround51" => Route.Surround51,
                            "2.1" or "stereo" or "stereo21" => Route.Stereo21,
                            "headphones" or "phones" => Route.Headphones,
                            _ => null
                        };
                        break;

                    case "--width":
                        width = Parse(value, width);
                        break;

                    case "--height":
                        height = Parse(value, height);
                        break;

                    case "--scale":
                        scale = Parse(value, scale);
                        break;

                    case "--probe":
                        // "x,y" in bitmap pixels. Lets a build report exact colours so
                        // styling can be checked without a person looking at the image.
                        string[] parts = value.Split(',');
                        if (parts.Length == 2
                            && int.TryParse(parts[0], out int probeX)
                            && int.TryParse(parts[1], out int probeY))
                        {
                            probes.Add(new Int32Rect(probeX, probeY, 1, 1));
                        }
                        break;
                }
            }

            foreach (string argument in args)
            {
                if (string.Equals(argument, "--playing", StringComparison.OrdinalIgnoreCase)) playing = true;
                if (string.Equals(argument, "--advanced", StringComparison.OrdinalIgnoreCase)) advanced = true;
            }

            try
            {
                string fullPath = Path.GetFullPath(path);
                string? directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                MainView view = new MainView
                {
                    Width = width,
                    Height = height
                };

                Layout(view, width, height);
                view.StageForPreview(preset, route, playing);
                if (advanced)
                {
                    view.Settings.AdvancedMode = true;
                    view.RefreshAll();
                }
                Layout(view, width, height);

                int pixelWidth = Math.Max(1, (int)Math.Round(width * scale));
                int pixelHeight = Math.Max(1, (int)Math.Round(height * scale));

                RenderTargetBitmap bitmap = new RenderTargetBitmap(
                    pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(view);

                PngBitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));

                using (FileStream stream = File.Create(fullPath))
                {
                    encoder.Save(stream);
                }

                view.Shutdown();

                output.WriteLine("Rendered " + pixelWidth + "x" + pixelHeight + " to " + fullPath);

                foreach (Int32Rect probe in probes)
                {
                    if (probe.X < 0 || probe.Y < 0 || probe.X >= pixelWidth || probe.Y >= pixelHeight)
                    {
                        output.WriteLine($"  probe {probe.X},{probe.Y}: outside the image");
                        continue;
                    }

                    byte[] pixel = new byte[4];
                    bitmap.CopyPixels(probe, pixel, 4, 0);

                    // Pbgra32 stores blue, green, red, alpha.
                    output.WriteLine($"  probe {probe.X},{probe.Y}: #{pixel[2]:X2}{pixel[1]:X2}{pixel[0]:X2} (alpha {pixel[3]:X2})");
                }

                return 0;
            }
            catch (Exception error)
            {
                output.WriteLine("Preview render failed: " + error);
                return 1;
            }
        }

        private static void Layout(MainView view, double width, double height)
        {
            view.Measure(new Size(width, height));
            view.Arrange(new Rect(0, 0, width, height));
            view.UpdateLayout();

            // Flush any queued layout work so templates are applied before rendering.
            view.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
            view.UpdateLayout();
        }

        private static double Parse(string value, double fallback)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : fallback;
        }
    }
}
