using ImageProcessor;
using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.Services
{
    public class ImageService
    {
        private readonly string _tempDir;

        public ImageService()
        {
            _tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Temp");

            if (!Directory.Exists(_tempDir))
                Directory.CreateDirectory(_tempDir);
        }

        /// <summary>
        /// Adjust the image and save to a temp JPG — always overwrite safely.
        /// </summary>
        /// <summary>
        /// Adjusts brightness/contrast/saturation and returns a new BitmapImage in memory.
        /// </summary>
        public BitmapImage AdjustImage(BitmapImage original, int brightness, int contrast, int saturation)
        {
            if (original == null)
                return null;

            // Convert BitmapImage → bytes
            byte[] inputBytes;
            using (MemoryStream ms = new MemoryStream())
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(original));
                encoder.Save(ms);
                inputBytes = ms.ToArray();
            }

            byte[] outputBytes;

            using (ImageFactory factory = new ImageFactory(preserveExifData: true))
            using (MemoryStream inStream = new MemoryStream(inputBytes))
            using (MemoryStream outStream = new MemoryStream())
            {
                factory.Load(inStream)
                       .Brightness(brightness)
                       .Contrast(contrast)
                       .Saturation(saturation)
                       .Save(outStream);

                outputBytes = outStream.ToArray();
            }

            // Convert bytes → BitmapImage
            BitmapImage adjusted = new BitmapImage();
            using (MemoryStream ms = new MemoryStream(outputBytes))
            {
                adjusted.BeginInit();
                adjusted.CacheOption = BitmapCacheOption.OnLoad;
                adjusted.StreamSource = ms;
                adjusted.EndInit();
            }

            adjusted.Freeze(); // important for UI thread safety
            return adjusted;
        }

        public BitmapImage LoadBitmapFully(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // ⭐ Load FULLY into memory
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze(); // ⭐ Make it safe for cross-thread use
                return bitmap;
            }
        }

        public string SaveBitmapToTemp(BitmapImage bitmap)
        {
            if (bitmap == null)
                return null;

            string tempPath = Path.Combine(
                _tempDir,
                "adjusted_input_" + Guid.NewGuid().ToString("N") + ".jpg"
            );

            // Save BitmapImage → JPEG
            using (var fileStream = new FileStream(tempPath, FileMode.Create))
            {
                BitmapEncoder encoder = new JpegBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(fileStream);
            }

            return tempPath;
        }

    }
}
