﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Configuration;
using System.Reflection;
using Microsoft.WindowsAPICodePack.Taskbar;
using System.Linq;

namespace DrawnImageSuperscaler
{
    class Program
    {
        static void Main(string[] args)
        {
            // Load the config file.
            ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

            // Setup console related things.
            TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.NoProgress);
            Console.Title = "Initializing Drawn Image Superscaler";
            AppDomain.CurrentDomain.SetData("APP_CONFIG_FILE",
                Path.Combine(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "App.config"));
            ResetConfigMechanism();

            Console.SetWindowSize(150, 40);
            Console.SetWindowPosition(0, 0);
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            // Print start information.
            DateTime startTime = DateTime.Now;
            Console.WriteLine("Drawn Image Superscaler started at".PadRight(45) + ": " + DateTime.Now.ToString("hh:mm:ss tt"));

            // Start a timer.
            var watch = Stopwatch.StartNew();
            // Do a sequence of high-quality upscales on the images using Waifu2x - Caffe.
            ImageScaler.ImageScalerPrime(Directory.GetCurrentDirectory());
            watch.Stop();
            DateTime endTime = DateTime.Now;
            
            // Print end information.
            Console.WriteLine();
            Console.WriteLine("Drawn Image Superscaler finished at".PadRight(45) + ": " + DateTime.Now.ToString("hh:mm:ss tt"));
            Console.WriteLine("Image operations completed in".PadRight(45) + ": " +
                ReadableTime(TimeSpan.FromMilliseconds(watch.ElapsedMilliseconds)));
            Console.WriteLine("All done.");
            Console.ReadLine();
        }

        /// <summary>
        /// Allows usage of custom config files via resetting the
        /// config mechanism by deleting the cached information.
        /// </summary>
        private static void ResetConfigMechanism()
        {
            typeof(ConfigurationManager)
            .GetField("s_initState", BindingFlags.NonPublic | BindingFlags.Static)
            .SetValue(null, 0);

            typeof(ConfigurationManager)
            .GetField("s_configSystem", BindingFlags.NonPublic | BindingFlags.Static)
            .SetValue(null, null);

            typeof(ConfigurationManager)
            .Assembly.GetTypes()
            .Where(x => x.FullName ==
               "System.Configuration.ClientConfigPaths")
            .First()
            .GetField("s_current", BindingFlags.NonPublic | BindingFlags.Static)
            .SetValue(null, null);
        }

        /// <summary>
        /// Converts number of bytes to human readable format.
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns>string with suffix of highest order byte class</returns>
        private static string FormatBytes(ulong bytes)
        {
            string[] Suffix = { "B", "KB", "MB", "GB", "TB" };
            int i;
            double dblSByte = bytes;
            for (i = 0; i < Suffix.Length && bytes >= 1024; i++, bytes /= 1024)
            {
                dblSByte = bytes / 1024.0;
            }

            return string.Format("{0:0.##} {1}", dblSByte, Suffix[i]);
        }

        /// <summary>
        /// Converts timespan(milliseconds) to human readable format.
        /// </summary>
        /// <param name="ts"></param>
        /// <returns>string with format: hh:mm:ss:ms</returns>
        private static string ReadableTime(TimeSpan ts)
        {
            string readableTime = string.Format("{0:D2}h:{1:D2}m:{2:D2}s:{3:D3}ms",
                ts.Hours,
                ts.Minutes,
                ts.Seconds,
                ts.Milliseconds,
                CultureInfo.InvariantCulture);

            return readableTime;
        }
    }
}
