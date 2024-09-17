using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MURDOC_2024.ViewModel
{
    public static class ImageCleaner
    {
        public static void ClearImages(string folderPath)
        {
            try
            {
                var imageFiles = Directory.GetFiles(folderPath, "*.*")
                    .Where(file => new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp" }
                        .Contains(Path.GetExtension(file).ToLower()));

                foreach (var file in imageFiles)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                // Handle or log the exception
                Console.WriteLine($"Error clearing images: {ex.Message}");
            }
        }
    }
}
