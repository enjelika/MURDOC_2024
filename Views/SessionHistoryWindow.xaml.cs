using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Newtonsoft.Json.Linq;

namespace MURDOC_2024.Views
{
    /// <summary>
    /// Session History window that displays all past session summaries
    /// with per-image details and image preview (original, bi_gt, fix).
    /// Opened from the Session Information panel.
    /// </summary>
    public partial class SessionHistoryWindow : Window
    {
        private readonly string _sessionsDir;
        private List<SessionEntry> _sessions;

        // Track which session folder is selected for image lookup
        private string _selectedSessionFolder;

        public SessionHistoryWindow()
        {
            InitializeComponent();

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            _sessionsDir = Path.Combine(exeDir, "training_sessions");

            LoadSessions();
        }

        /// <summary>
        /// Scans training_sessions directory for _summary.json files and
        /// populates the left-pane DataGrid.
        /// </summary>
        private void LoadSessions()
        {
            _sessions = new List<SessionEntry>();

            if (!Directory.Exists(_sessionsDir))
            {
                SessionCountLabel.Text = "No sessions directory found";
                return;
            }

            var summaryFiles = Directory.GetFiles(_sessionsDir, "*_summary.json")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToArray();

            foreach (var file in summaryFiles)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    var obj = JObject.Parse(json);

                    var entry = new SessionEntry
                    {
                        FilePath = file,
                        SessionId = obj["SessionId"]?.ToString() ?? "",
                        Date = ParseDate(obj["StartTime"]?.ToString()),
                        Time = ParseTime(obj["StartTime"]?.ToString()),
                        Duration = obj["Duration"]?["Formatted"]?.ToString() ?? "—",
                        ImagesAnalyzed = obj["Statistics"]?["TotalImagesAnalyzed"]?.Value<int>() ?? 0,
                        TotalEdits = (obj["Statistics"]?["ImagesWithBinaryEdits"]?.Value<int>() ?? 0)
                                   + (obj["Statistics"]?["ImagesWithRankEdits"]?.Value<int>() ?? 0),
                        TotalFeedback = (obj["Statistics"]?["TotalConfirmed"]?.Value<int>() ?? 0)
                                      + (obj["Statistics"]?["TotalRejected"]?.Value<int>() ?? 0)
                                      + (obj["Statistics"]?["TotalCorrections"]?.Value<int>() ?? 0),
                        JsonData = obj
                    };

                    _sessions.Add(entry);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading session file {file}: {ex.Message}");
                }
            }

            SessionGrid.ItemsSource = _sessions;
            SessionCountLabel.Text = $"{_sessions.Count} session{(_sessions.Count != 1 ? "s" : "")} found";
        }

        /// <summary>
        /// Handles session selection in the left DataGrid.
        /// Populates the right pane with session stats and per-image data.
        /// </summary>
        private void SessionGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SessionGrid.SelectedItem is not SessionEntry session)
                return;

            var data = session.JsonData;

            // Show detail panel, hide placeholder
            NoSelectionLabel.Visibility = Visibility.Collapsed;
            DetailPanel.Visibility = Visibility.Visible;

            // Header
            DetailHeader.Text = $"Session: {session.Date} {session.Time}";

            // Overview stats
            StatDuration.Text = session.Duration;
            StatImages.Text = session.ImagesAnalyzed.ToString();

            int totalFeedback = (data["Statistics"]?["TotalConfirmed"]?.Value<int>() ?? 0)
                              + (data["Statistics"]?["TotalRejected"]?.Value<int>() ?? 0)
                              + (data["Statistics"]?["TotalCorrections"]?.Value<int>() ?? 0);
            StatFeedback.Text = totalFeedback.ToString();

            // Feedback breakdown
            StatConfirmed.Text = $"\u2713 {data["Statistics"]?["TotalConfirmed"]?.Value<int>() ?? 0} Confirmed";
            StatRejected.Text = $"\u2717 {data["Statistics"]?["TotalRejected"]?.Value<int>() ?? 0} Rejected";
            StatCorrections.Text = $"+ {data["Statistics"]?["TotalCorrections"]?.Value<int>() ?? 0} Corrections";

            // Find the corresponding session folder for image lookup
            _selectedSessionFolder = Path.Combine(_sessionsDir, $"session_{session.SessionId}");

            // Per-image data
            var images = data["Images"] as JArray;
            var imageEntries = new List<ImageEntry>();

            if (images != null)
            {
                foreach (var img in images)
                {
                    imageEntries.Add(new ImageEntry
                    {
                        ImageName = img["ImageName"]?.ToString() ?? "—",
                        MaskEdited = img["Modifications"]?["BinaryMaskEdited"]?.Value<bool>() == true ? "✓" : "—",
                        RankEdited = img["Modifications"]?["RankMapEdited"]?.Value<bool>() == true ? "✓" : "—",
                        Sensitivity = img["DetectionParameters"]?["Sensitivity_dPrime"]?.Value<double>().ToString("F2") ?? "—",
                        ResponseBias = img["DetectionParameters"]?["ResponseBias_Beta"]?.Value<double>().ToString("F2") ?? "—",
                        Confirmed = img["DetectionFeedback"]?["Confirmed"]?.Value<int>() ?? 0,
                        Rejected = img["DetectionFeedback"]?["Rejected"]?.Value<int>() ?? 0,
                        Corrections = img["DetectionFeedback"]?["Corrections"]?.Value<int>() ?? 0
                    });
                }
            }

            ImageGrid.ItemsSource = imageEntries;

            // Reset image viewer
            ImageViewerGrid.Visibility = Visibility.Collapsed;
            ImageViewerHeader.Text = "Select an image above to preview";
            ImageStatusLabel.Text = "";
            OriginalImageView.Source = null;
            BinaryMaskView.Source = null;
            RankMapView.Source = null;
        }

        /// <summary>
        /// Handles image selection in the per-image DataGrid.
        /// Loads original, bi_gt, and fix images for the selected image.
        /// </summary>
        private void ImageGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImageGrid.SelectedItem is not ImageEntry imageEntry)
                return;

            if (string.IsNullOrEmpty(_selectedSessionFolder))
                return;

            string imageName = Path.GetFileNameWithoutExtension(imageEntry.ImageName);

            ImageViewerHeader.Text = $"Preview: {imageEntry.ImageName}";
            ImageViewerGrid.Visibility = Visibility.Visible;

            int loaded = 0;

            // Load original image (check session img folder, then outputs folder)
            OriginalImageView.Source = null;
            string origPath = FindOriginalImage(imageName);
            if (origPath != null)
            {
                OriginalImageView.Source = LoadImageSafe(origPath);
                loaded++;
            }

            // Load binary mask from bi_gt folder
            BinaryMaskView.Source = null;
            string maskPath = Path.Combine(_selectedSessionFolder, "bi_gt", $"{imageName}.png");
            if (File.Exists(maskPath))
            {
                BinaryMaskView.Source = LoadImageSafe(maskPath);
                loaded++;
            }

            // Load rank map from fix folder
            RankMapView.Source = null;
            string rankPath = Path.Combine(_selectedSessionFolder, "fix", $"{imageName}.png");
            if (File.Exists(rankPath))
            {
                RankMapView.Source = LoadImageSafe(rankPath);
                loaded++;
            }

            ImageStatusLabel.Text = $"{loaded}/3 images available";
        }

        /// <summary>
        /// Searches for the original image in the session img folder first,
        /// then falls back to the outputs folder.
        /// </summary>
        private string FindOriginalImage(string imageName)
        {
            if (!string.IsNullOrEmpty(_selectedSessionFolder))
            {
                // Check session img folder (any extension)
                string imgFolder = Path.Combine(_selectedSessionFolder, "img");
                if (Directory.Exists(imgFolder))
                {
                    var match = Directory.GetFiles(imgFolder, $"{imageName}.*").FirstOrDefault();
                    if (match != null) return match;
                }
            }

            // Fallback: check outputs folder for the original
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string outputOriginal = Path.Combine(exeDir, "outputs", imageName, "original.png");
            if (File.Exists(outputOriginal)) return outputOriginal;

            return null;
        }

        /// <summary>
        /// Loads a BitmapImage with cache bypass to avoid WPF file locking.
        /// </summary>
        private static BitmapImage LoadImageSafe(string path)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image {path}: {ex.Message}");
                return null;
            }
        }

        private static string ParseDate(string dateTimeStr)
        {
            if (DateTime.TryParse(dateTimeStr, out DateTime dt))
                return dt.ToString("yyyy-MM-dd");
            return "—";
        }

        private static string ParseTime(string dateTimeStr)
        {
            if (DateTime.TryParse(dateTimeStr, out DateTime dt))
                return dt.ToString("HH:mm");
            return "—";
        }

        // ─── Data classes for DataGrid binding ──────────────────────────────────

        private class SessionEntry
        {
            public string FilePath { get; set; }
            public string SessionId { get; set; }
            public string Date { get; set; }
            public string Time { get; set; }
            public string Duration { get; set; }
            public int ImagesAnalyzed { get; set; }
            public int TotalEdits { get; set; }
            public int TotalFeedback { get; set; }
            public JObject JsonData { get; set; }
        }

        private class ImageEntry
        {
            public string ImageName { get; set; }
            public string MaskEdited { get; set; }
            public string RankEdited { get; set; }
            public string Sensitivity { get; set; }
            public string ResponseBias { get; set; }
            public int Confirmed { get; set; }
            public int Rejected { get; set; }
            public int Corrections { get; set; }
        }
    }
}
