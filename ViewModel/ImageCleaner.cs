using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MURDOC_2024.ViewModel
{
    /// <summary>
    /// Provides functionality to clear image files from a specified directory.
    /// </summary>
    public static class ImageCleaner
    {
        /// <summary>
        /// Deletes all image files in the specified folder.
        /// </summary>
        /// <param name="folderPath">The full path to the folder containing images to be deleted.</param>
        public static void ClearImages(string folderPath)
        {
            try
            {
                // Get all files in the specified directory and filter for image file extensions
                var imageFiles = Directory.GetFiles(folderPath, "*.*")
                    .Where(file => new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" }
                        .Contains(Path.GetExtension(file).ToLower()));

                // Iterate through each image file and delete it
                foreach (var file in imageFiles)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                // Handle or log any exceptions that occur during the process
                // In a production environment, consider using a proper logging framework
                Console.WriteLine($"Error clearing images: {ex.Message}");
            }
        }
    }
}
