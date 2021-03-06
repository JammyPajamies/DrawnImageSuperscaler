﻿using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Configuration;
using Microsoft.WindowsAPICodePack.Taskbar;
using System.Linq;

namespace DrawnImageSuperscaler
{
    public static class ImageScaler
    {
        // Helper enumerator for the ImageOperationType class.
        private enum ImageResolutionClassification : int { VerySmall, Small, Normal, Large, VeryLarge };

        /// <summary>
        /// This struct holds the name of the image and 
        /// the enumerator code which determines the order
        /// and type of operations to run on it, based on resolution.
        /// </summary>
        private struct ImageOperationInfo
        {
            public ImageResolutionClassification resolutionClass;
            public string ImagePath { get; }

            public ImageOperationInfo(string path, ImageResolutionClassification passedOpType)
            {
                ImagePath = path;
                this.resolutionClass = passedOpType;
            }
        }

        private static int numScaledImages = 0;
        private static int numProcessableImages = 0;

        /// <summary>
        /// This is the primary working function of the WaifuScaler class.
        /// It sets up, directs, and monitors the core workflow of the program.
        /// </summary>
        /// <param name="directory"></param>
        public static void ImageScalerPrime(string directory)
        {
            TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Normal);
            
            string currentDirectory = Directory.GetCurrentDirectory();
            if(Directory.Exists(ConfigurationManager.AppSettings["BaseFolderPath"].ToString()))
            {
                currentDirectory = ConfigurationManager.AppSettings["BaseFolderPath"];
            }
            string sourceFolderPath = Path.Combine(currentDirectory, ConfigurationManager.AppSettings["SourceFolderName"]);
            string temporaryFolderPath = Path.Combine(currentDirectory, ConfigurationManager.AppSettings["TempFolderName"]);
            string destinationFolderPath = Path.Combine(currentDirectory, ConfigurationManager.AppSettings["DestinationFolderName"]);

            CancellationTokenSource pingerCancelToken = new CancellationTokenSource();

            // Make the temp folder and output folder if necessary.
            MakeDirectory(temporaryFolderPath);
            MakeDirectory(destinationFolderPath);

            // Print working folder details.
            Console.WriteLine("Source folder path: " + sourceFolderPath);
            Console.WriteLine();

            // Get the images in the S&R folder and put their paths in a list
            // in natural order (Windows sort by name ascending).
            List<ImageOperationInfo> imageOpList = SortImageListByResolution(ImageAcquirer.GetImagesFromDirectory(sourceFolderPath));
            int maxLength = 0;
            numProcessableImages = imageOpList.Count;

            // Get the length of the longest filename in the list.
            foreach (ImageOperationInfo image in imageOpList)
            {
                try
                {
                    int test = Path.GetFileName(image.ImagePath).Length;
                    if (test > maxLength)
                        maxLength = test;
                }
                catch (Exception e)
                {
                    InnerExceptionPrinter.GetExceptionMessages(e);
                }
            }

            // Start a background process for doing work.
            Task optimizationBackgroundTask = Task.Run(() => ImageOptimizer.ConvertIndividualToPNGAsync(pingerCancelToken.Token));

            // Allow the user to request the program to stop further processing.
            bool userRequestCancelRemainingOperations = false;
            bool optimizationsCompleted = false;
            Task getCancelInput = Task.Factory.StartNew(() =>
            {
                while (Console.ReadKey(true).Key != ConsoleKey.C && !optimizationsCompleted)
                {
                    Thread.Sleep(250);
                }
                TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Indeterminate);
                userRequestCancelRemainingOperations = true;
            });

            // Waifu2x - Caffee conversion loop.
            TaskbarManager.Instance.SetProgressValue(numScaledImages, numProcessableImages);
            Console.Title = GetScalerCompletionPercentage() + "% " +
                "(" + numScaledImages + "/" + numProcessableImages + ") Images Scaled";

            int dotIncrementer = 0;
            foreach (ImageOperationInfo image in imageOpList)
            {
                if (userRequestCancelRemainingOperations)
                {
                    break;
                }

                Task<string> anJob = Task.Run(() => Waifu2xJobController(image));
                string currentImageName = Path.GetFileName(image.ImagePath);

                while (!anJob.IsCompleted)
                {
                    if (!userRequestCancelRemainingOperations)
                    {
                        Console.Write(("\rUpscaling" + new string('.', (dotIncrementer % 10) + 1) +
                            new string(' ', 9 - (dotIncrementer % 10))).PadRight(46) + ": " +
                            GetScalerCompletionPercentage() + "% " +
                            "(" + numScaledImages + "/" + numProcessableImages + ") " +
                            "Now processing: " + currentImageName +
                            new string(' ', maxLength + 10) + new string('\b', maxLength + 11));
                        dotIncrementer++;
                    }
                    else
                    {
                        Console.Write(("\rFinishing current jobs" + new string('.', (dotIncrementer % 10) + 1) +
                            new string(' ', 9 - (dotIncrementer % 10))).PadRight(46) + ": " +
                            GetScalerCompletionPercentage() + "% " +
                            "(" + numScaledImages + "/" + numProcessableImages + ") " +
                            "Now processing: " + currentImageName +
                            new string(' ', maxLength + 10) + new string('\b', maxLength + 11));
                        dotIncrementer++;
                    }

                    Thread.Sleep(100);
                }
                anJob.Wait();
                Debug.WriteLine("");

                numScaledImages++;
                try
                {
                    File.Delete(Path.Combine(temporaryFolderPath, Path.GetFileName(anJob.Result)));
                }
                catch (Exception e)
                {
                    InnerExceptionPrinter.GetExceptionMessages(e);
                }
                // Now enqueue an optimization task.
                ImageOptimizer.EnqueueImageForOptimization(anJob.Result);

                TaskbarManager.Instance.SetProgressValue(numScaledImages, numProcessableImages);
                Console.Title = GetScalerCompletionPercentage() + "% " +
                            "(" + numScaledImages + "/" + numProcessableImages + ") Images Scaled";
            }
            if (numScaledImages == numProcessableImages)
            {
                Console.Write("\rFiles converted".PadRight(46) + ": " +
                    GetScalerCompletionPercentage() + "% " +
                    "(" + numScaledImages + "/" + numProcessableImages + ") Completed Normally" +
                    new string(' ', maxLength + 40) +
                    new string('\b', maxLength + 41));
                Console.WriteLine();
            }
            else
            {
                Console.Write("\rFiles converted".PadRight(46) + ": " +
                    GetScalerCompletionPercentage() + "% " +
                    "(" + numScaledImages + "/" + numProcessableImages + ")" +
                    new string(' ', maxLength + 40) +
                    new string('\b', maxLength + 41));
                Console.WriteLine();
            }

            // *********
            // Initiate, display status of, and check for cancellation of image optimization.
            dotIncrementer = 0;

            while (!userRequestCancelRemainingOperations && !optimizationBackgroundTask.IsCompleted)
            {
                // Scaling is done at this point, so we are only waiting
                // for the ConcurrentImageQueueCount to reach zero and
                // the remaining thread count to reach zero.
                if (ImageOptimizer.GetPingerImageQueueCount() == 0 && ImageOptimizer.GetRunningThreadCount() == 0)
                {
                    // Once that is done, send a cancel request to the task to end it. 
                    pingerCancelToken.Cancel();
                    break;
                }

                TaskbarManager.Instance.SetProgressValue(GetOptmizedImagesCount(), numProcessableImages);
                Console.Write(("\rOptimizer pass in progress" +
                    new string('.', (dotIncrementer % 10) + 1) +
                    new string(' ', 9 - (dotIncrementer % 10))).PadRight(46) +
                    ": " + GetOptimizationCompletionPercentage() + "% " +
                    "(" + GetOptmizedImagesCount() + "/" + numProcessableImages + ") Images Optimized");
                dotIncrementer++;
                Console.Title = GetOptimizationCompletionPercentage() + "% " +
                    "(" + GetOptmizedImagesCount() + "/" + numProcessableImages + ") Images Optimized";
                Thread.Sleep(100);
            }

            // If the user cancels, send a cancel token and wait for the remaining operations to finish.
            if (userRequestCancelRemainingOperations && (!optimizationBackgroundTask.IsCanceled || !optimizationBackgroundTask.IsCompleted))
            {
                dotIncrementer = 0;
                pingerCancelToken.Cancel();

                // Update the taskbar to show that the operations are cancelling.
                TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Indeterminate);
                TaskbarManager.Instance.SetProgressValue(GetOptmizedImagesCount(), numProcessableImages);

                // Wait for remaining active optimizations to finish.
                while (!optimizationBackgroundTask.IsCompleted && (ImageOptimizer.GetRunningThreadCount() > 0))
                {
                    Console.Write("\rFinishing active optimizations" + new string('.', (dotIncrementer % 10) + 1) +
                        new string(' ', 55) + new string('\b', 56));
                    dotIncrementer++;
                    Thread.Sleep(100);
                }
                
                // Change the taskbar color to red to indicate user stoppage and print final details.
                TaskbarManager.Instance.SetProgressState(TaskbarProgressBarState.Error);
                TaskbarManager.Instance.SetProgressValue(GetOptmizedImagesCount(), numProcessableImages);
                Console.Write("\rOptimizer pass cancelled." + new string(' ', 55) + new string('\b', 56));
                Console.WriteLine();
                Console.Title = GetOptimizationCompletionPercentage() + "% " +
                    "(" + GetOptmizedImagesCount() + "/" + numProcessableImages + ") (User Cancelled)";
            }
            else
            {
                // Update the taskbar and print normal completion messages.
                TaskbarManager.Instance.SetProgressValue(GetOptmizedImagesCount(), numProcessableImages);
                Console.Write("\rOptimizer pass completed".PadRight(46) + ": " +
                    GetOptimizationCompletionPercentage() + "% " +
                    "(" + GetOptmizedImagesCount() + "/" + numProcessableImages + ") Images Optimized" +
                    new string(' ', 55) + new string('\b', 56));
                Console.WriteLine();
                Console.Title = GetOptimizationCompletionPercentage() + "% " +
                    "(" + GetOptmizedImagesCount() + "/" + numProcessableImages + ") (Completed Normally)";
            }
            optimizationsCompleted = true;
            CleanupFolders();
        }

        /// <summary>
        /// Takes a list of ImageOperationInfo, checks images for resolution sizes, and sorts them accordingly.
        /// </summary>
        /// <param name="imagePaths"></param>
        /// <returns>A list of ImageOperationInfo with ImageResolutionClassification calculated per image</returns>
        private static List<ImageOperationInfo> SortImageListByResolution(List<string> imagePaths)
        {
            // Get the images in the S&R folder and put their paths in a list
            // in natural order (Windows sort by name ascending).
            List<ImageOperationInfo> imageOpList = new List<ImageOperationInfo>();

            foreach (string image in imagePaths)
            {
                int pixelCount = 0;
                pixelCount = GetImageResolution(image);
                if (pixelCount >= 100000000)
                {
                    // At least 10000x10000
                    imageOpList.Add(new ImageOperationInfo(image, ImageResolutionClassification.VeryLarge));
                }
                else if (pixelCount >= 22500000)
                {
                    // At least 5000x4500
                    imageOpList.Add(new ImageOperationInfo(image, ImageResolutionClassification.Large));
                }
                else if (pixelCount >= 786432)
                {
                    // At least 1024x768
                    imageOpList.Add(new ImageOperationInfo(image, ImageResolutionClassification.Normal));
                }
                else if (pixelCount >= 172800)
                {
                    // At least 480x360
                    imageOpList.Add(new ImageOperationInfo(image, ImageResolutionClassification.Small));
                }
                else
                {
                    // Smaller than 480x360
                    imageOpList.Add(new ImageOperationInfo(image, ImageResolutionClassification.VerySmall));
                }
            }

            return imageOpList;
        }

        /// <summary>
        /// Takes a single image and converts it according to hard-coded rules.
        /// </summary>
        /// <param name="image"></param>
        /// <returns>Path of the scaled image.</returns>
        private static async Task<string> Waifu2xJobController(ImageOperationInfo image)
        {
            string currentDirectory = Directory.GetCurrentDirectory();
            if (Directory.Exists(ConfigurationManager.AppSettings["BaseFolderPath"]))
            {
                currentDirectory = ConfigurationManager.AppSettings["BaseFolderPath"];
            }
            string temporaryFolderPath = Path.Combine(currentDirectory, ConfigurationManager.AppSettings["TempFolderName"]);
            string destinationFolderPath = Path.Combine(currentDirectory, ConfigurationManager.AppSettings["DestinationFolderName"]);
            string tempImagePath = null;
            string workingFolder = Path.Combine(currentDirectory, ConfigurationManager.AppSettings["SourceFolderName"]);
            string fileName = Path.GetFullPath(image.ImagePath);
            fileName = fileName.Replace(workingFolder, "");
            //Debug.WriteLine("temp path: " + temporaryFolderPath);
            //Debug.WriteLine("working folder: " + workingFolder);
            //Debug.WriteLine("file name: " + fileName);
            //Debug.WriteLine("dest temp path: " + temporaryFolderPath + fileName); 

            // Sequential scale and quality operations depending on flag.
            switch (image.resolutionClass)
            {
                case ImageResolutionClassification.VeryLarge:
                    {
                        // VeryLarge case. Quality pass before doing a 2x scale.
                        tempImagePath = Waifu2xTask(image.ImagePath, temporaryFolderPath + fileName, 1, 2, 256);
                        return Waifu2xTask(tempImagePath, destinationFolderPath + fileName, 2, 2, 256);
                    }
                case ImageResolutionClassification.Large:
                    {
                        // Large case. Quality pass before doing a 2x scale.
                        tempImagePath = Waifu2xTask(image.ImagePath, temporaryFolderPath + fileName, 1, 2, 256);
                        return Waifu2xTask(tempImagePath, destinationFolderPath + fileName, 2, 2, 256);
                    }
                case ImageResolutionClassification.Normal:
                    {
                        // Normal case. 2x scale before doing a quality pass.
                        tempImagePath = Waifu2xTask(image.ImagePath, temporaryFolderPath + fileName, 2, 4, 128);
                        return Waifu2xTask(tempImagePath, destinationFolderPath + fileName, 1, 4, 128);
                    }
                case ImageResolutionClassification.Small:
                    {
                        // Small case. 2x scale before doing a quality pass.
                        tempImagePath = Waifu2xTask(image.ImagePath, temporaryFolderPath + fileName, 2, 4, 128);
                        return Waifu2xTask(tempImagePath, destinationFolderPath + fileName, 1, 4, 128);
                    }
                case ImageResolutionClassification.VerySmall:
                    {
                        // Image is WAY too small and may use silly amounts of vram to process. Just return.
                        return fileName;
                    }
                default:
                    {
                        break;
                    }
            }
            return null;
        }

        /// <summary>
        /// This function organizes parameters for and then launches a process
        /// of Waifu2x - Caffe on the given folder.
        /// </summary>
        /// <param name="inputFile"></param>
        /// <param name="outputPath"></param>
        /// <param name="magnificationSize"></param>
        /// <param name="batch"></param>
        /// <param name="split"></param>
        /// <returns>Path of the scaled output image.</returns>
        private static string Waifu2xTask(string inputFile, string outputPath,
            int magnificationSize = 2, int batch = 6, int split = 128)
        {
            // Create the directory for the output path if it does not exist.
            if (!Directory.Exists(Path.GetDirectoryName(outputPath)))
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)));

            outputPath = Path.Combine(Path.GetDirectoryName(outputPath), Path.GetFileNameWithoutExtension(outputPath) + ".png");
            Debug.WriteLine("w2x inp: " + inputFile);
            Debug.WriteLine("w2x out: " + outputPath);
            
            // Load the correct settings from the config file.
            string modelDir = Path.Combine(ConfigurationManager.AppSettings["Waifu2xCaffeDirectory"],
                ConfigurationManager.AppSettings["ModelDirectory"]);
            string waifuExec = Path.Combine(ConfigurationManager.AppSettings["Waifu2xCaffeDirectory"],
                ConfigurationManager.AppSettings["Waifu2xExecutableName"]);

            if (!File.Exists(waifuExec))
            {
                Console.WriteLine("Waifu2x - Caffe executable not found! Enter a key to exit.");
                CleanupFolders();
                Console.ReadKey();
                Environment.Exit(-1);
            }

            if (!File.Exists(inputFile))
            {
                // Empty directory. Just return.
                Console.WriteLine("\nERROR - No such file".PadRight(46) + ": " + inputFile);
                return null;
            }

            // Adaptive scaling. If an attemp fails, try lowering the batch; if that still doesn't work, divide the split size by 2.
            int exitCode = -1;
            do
            {
                // Initialize Waifu2x-Caffe information for 2x scale denoise+magnify 3.
                ProcessStartInfo magDenoiseInfo = new ProcessStartInfo
                {
                    FileName = waifuExec,
                    WindowStyle = ProcessWindowStyle.Hidden,

                    // Setup arguments.
                    Arguments = "--gpu 0" +
                        " -b " + batch +
                        " -c " + split +
                        " -d 8" +
                        " -p cudnn" +
                        " --model_dir \"" + modelDir + "\"" +
                        " -s " + magnificationSize +
                        " -n " + ConfigurationManager.AppSettings["DenoiseLevel"] +
                        " -m " + ConfigurationManager.AppSettings["ConversionMode"] +
                        " -e .png" +
                        " -l png" +
                        " -o \"" + outputPath + "\"" +
                        " -i \"" + inputFile + "\""
                };
                // Start Waifu2x-Caffe and wait for it to exit.
                Process magDenoise = Process.Start(magDenoiseInfo);
                magDenoise.WaitForExit();

                exitCode = magDenoise.ExitCode;

                // Check the exit code. Continue looping while reducing resource requirements until success or cannot reduce resource usage further.
                if (exitCode < 0)
                {
                    if (batch > 1)
                    {
                        // Try reducing the batch size first.
                        Console.WriteLine("\r" + DateTime.Now.ToString("hh: mm:ss tt") + " ERROR - Could not convert. Changing batch size from " + batch + " to " + (batch / 2) +
                            " for : " + Path.GetFileName(inputFile) + " resolution: " + GetImageResolution(inputFile).ToString() +
                            new string(' ', GetImageResolution(inputFile).ToString().Length) +
                            new string('\b', GetImageResolution(inputFile).ToString().Length + 1));
                        batch /= 2;
                    }
                    else if (split > 1)
                    {
                        // If we still can't convert, try lowering the split size.
                        Console.WriteLine("\r" + DateTime.Now.ToString("hh: mm:ss tt") + " ERROR - Could not convert. Changing split size from " + split + " to " + (split / 2) +
                            " for : " + Path.GetFileName(inputFile) + " resolution: " + GetImageResolution(inputFile).ToString() +
                            new string(' ', GetImageResolution(inputFile).ToString().Length) +
                            new string('\b', GetImageResolution(inputFile).ToString().Length + 1));
                        split /= 2;
                    }
                    else
                    {
                        // If we hit 1/1 for both batch size and split size, then we can't scale the image.
                        Console.WriteLine("\rERROR - Could not convert. Split/batch options exhausted.".PadRight(46) + ": " + Path.GetFileName(inputFile));
                        Console.WriteLine("Last exit code".PadRight(46) + ": " + exitCode);
                        Console.ReadLine();
                        Environment.Exit(-1);
                    }
                }

            } while (exitCode < 0);

            Thread.Sleep(250);
            GC.Collect();

            Debug.WriteLine("w2x ret: " + outputPath);
            return outputPath;
        }


        #region Helper Functions
        /// <summary>
        /// Returns percentage to one decimal place of total images scaled as a string.
        /// </summary>
        /// <returns>Percentage to one decimal place of total images scaled as a string.</returns>
        private static string GetScalerCompletionPercentage()
        {
            if (numProcessableImages == 0)
                return "0.0";
            return ((double)numScaledImages / numProcessableImages * 100).ToString("0.0"); ;
        }

        /// <summary>
        /// Returns the number
        /// </summary>
        /// <returns></returns>
        private static int GetOptmizedImagesCount()
        {
            return numProcessableImages - ImageOptimizer.GetPingerImageQueueCount() - ImageOptimizer.GetRunningThreadCount();
        }

        /// <summary>
        /// Returns percentage to one decimal place of total images optimized as a string.
        /// </summary>
        /// <returns>Percentage to one decimal place of total images optimized as a string.</returns>
        private static string GetOptimizationCompletionPercentage()
        {
            if (numProcessableImages == 0)
                return "0.0";
            return ((double)GetOptmizedImagesCount() / numProcessableImages * 100).ToString("0.0"); ;
        }

        /// <summary>
        /// Helper function to safely create a directory.
        /// </summary>
        /// <param name="directory"></param>
        private static void MakeDirectory(string directory)
        {
            // Check for and make proper directories.
            if (!Directory.Exists(directory))
            {
                // No input directory. Make it for future use, then return.
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception e)
                {
                    InnerExceptionPrinter.GetExceptionMessages(e);
                }
            }
        }

        /// <summary>
        /// Function cleans up processing files and folders.
        /// </summary>
        private static void CleanupFolders()
        {
            // Cleanup folders.
            string currentDirectory = Directory.GetCurrentDirectory();
            if (Directory.Exists(ConfigurationManager.AppSettings["BaseFolderPath"]))
            {
                currentDirectory = ConfigurationManager.AppSettings["BaseFolderPath"];
            }

            string tempImageFolder = Path.Combine(currentDirectory, ConfigurationManager.AppSettings["TempFolderName"]);

            try
            {
                Directory.Delete(tempImageFolder, true);
            }
            catch (Exception e)
            {
                InnerExceptionPrinter.GetExceptionMessages(e);
            }
            // Delete error logs
            bool deleteLogs = false;
            bool.TryParse(ConfigurationManager.AppSettings["DeleteErrorLogs"] ?? "false", out deleteLogs);
            if(deleteLogs)
            {
                List<string> errorLogPathsList = new List<string>(); ;
                if (!Directory.Exists(currentDirectory))
                {
                    return;
                }

                var collectionOfFiles = Directory.EnumerateFiles(currentDirectory, "*.*", SearchOption.TopDirectoryOnly)
                .Where(s => s.StartsWith("error_log_"));
                
                int count = collectionOfFiles.Count();
                string[] arrayOfFiles;

                if (count > 0)
                {
                    arrayOfFiles = new string[count - 1];
                    arrayOfFiles = collectionOfFiles.ToArray();
                }
                else
                {
                    arrayOfFiles = new string[0];
                }

                errorLogPathsList = arrayOfFiles.ToList();

                foreach(string errorLog in errorLogPathsList)
                {
                    try
                    {
                        File.Delete(errorLog);
                    }
                    catch (Exception e)
                    {
                        InnerExceptionPrinter.GetExceptionMessages(e);
                    }
                }
            }
        }

        private static int GetImageResolution(string image)
        {
            int resolution = 0;
            try
            {
                using (Stream stream = File.OpenRead(image))
                {
                    using (Image sourceImage = Image.FromStream(stream, false, false))
                    {
                        // Set the operation type for the image to be processed based on the
                        // dimensions of the image.
                        resolution = sourceImage.Width * sourceImage.Height;
                    }
                }
            }
            catch (Exception e)
            {
                InnerExceptionPrinter.GetExceptionMessages(e);
            }

            return resolution;
        }
        #endregion
    }
}
