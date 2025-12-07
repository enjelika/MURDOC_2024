using ImageProcessor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.Services
{
    public class ImageService
    {
        /// <summary>
        /// Loads an image from the user selected image path or sets a default placeholder image.
        /// </summary>
        public void LoadImage()
        {
        //    if (hasUserModifiedImage)
        //    {
        //        BitmapImage tempModifiedImage = new BitmapImage();
        //        tempModifiedImage.BeginInit();
        //        tempModifiedImage.CacheOption = BitmapCacheOption.OnLoad;
        //        tempModifiedImage.StreamSource = _modifiedImageStream;
        //        tempModifiedImage.EndInit();
        //        SelectedImage = tempModifiedImage;
        //    }
        //    else if (!string.IsNullOrEmpty(SelectedImagePath))
        //    {
        //        SelectedImage = new BitmapImage(new Uri(SelectedImagePath));
        //    }
        //    else
        //    {
        //        // Set the default placeholder image
        //        SelectedImage = new BitmapImage(new Uri("pack://application:,,,/MURDOC_2024;component/Assets/image_placeholder.png"));
        //    }
        }

        /// <summary>
        /// Updates the selected image with user defined brightnesss, contrast, and saturation values
        /// </summary>
        /// <param name="brightness">User Defined Brightness</param>
        /// <param name="contrast">User Defined Contrast</param>
        /// <param name="saturation">User Defined Saturation</param>
        internal void UpdateSelectedImage(int brightness, int contrast, int saturation)
        {
            //if (!string.IsNullOrEmpty(SelectedImagePath))
            //{
            //    byte[] imageBytes = File.ReadAllBytes(SelectedImagePath);
            //    using (MemoryStream inStream = new MemoryStream(imageBytes))
            //    {
            //        using (ImageFactory imageFactory = new ImageFactory(preserveExifData: true))
            //        {
            //            hasUserModifiedImage = true;
            //            imageFactory.Load(inStream);
            //            imageFactory.Brightness(brightness);
            //            imageFactory.Contrast(contrast);
            //            imageFactory.Saturation(saturation);
            //            imageFactory.Save(_modifiedImageStream);
            //            LoadImage();
            //        }
            //    }
            //}
        }

        /// <summary>
        /// Saves the temporary image to a temp folder in JPG format
        /// </summary>
        /// <returns>String of location saved</returns>
        public string AdjustImage(BitmapImage original, int brightness, int contrast, int saturation)
        {
            string tempPath = string.Empty;
            //if (hasUserModifiedImage)
            //{
            //    byte[] imageBytes = File.ReadAllBytes(SelectedImagePath);
            //    using (MemoryStream inStream = new MemoryStream(imageBytes))
            //    {
            //        using (ImageFactory imageFactory = new ImageFactory(preserveExifData: true))
            //        {
            //            tempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Temp", SelectedImageName + ".jpg");
            //            imageFactory.Load(inStream);
            //            imageFactory.Brightness(_sliderBrightness);
            //            imageFactory.Contrast(_sliderContrast);
            //            imageFactory.Saturation(_sliderSaturation);
            //            // Delete old temp file if exists since it won't overwrite
            //            File.Delete(tempPath);
            //            imageFactory.Save(tempPath);
            //        }
            //    }
            //}
            return tempPath;
        }
    }
}
