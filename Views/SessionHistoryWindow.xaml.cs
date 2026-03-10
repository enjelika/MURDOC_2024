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

            // Collect summary JSON files from both locations:
            // New: training_sessions/session_{id}/session_summary.json
            // Old: training_sessions/session_{id}_summary.json (backward compat)
            var summaryFiles = new List<string>();

            // New location: inside each session folder
            foreach (var dir in Directory.GetDirectories(_sessionsDir, "session_*"))
            {
                string insideSummary = Path.Combine(dir, "session_summary.json");
                if (File.Exists(insideSummary))
                    summaryFiles.Add(insideSummary);
            }

            // Old location: alongside session folders (backward compat)
            summaryFiles.AddRange(Directory.GetFiles(_sessionsDir, "*_summary.json"));

            // Deduplicate by SessionId
            var seenIds = new HashSet<string>();

            foreach (var file in summaryFiles.OrderByDescending(f => File.GetLastWriteTime(f)))
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

                    // Skip if we already loaded this session (new location takes priority)
                    if (!seenIds.Add(entry.SessionId))
                        continue;

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
            if (!(SessionGrid.SelectedItem is SessionEntry session))
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

            // Fallback: if JSON has no per-image data, scan session folder for images
            if (imageEntries.Count == 0 && Directory.Exists(_selectedSessionFolder))
            {
                var discoveredImages = new HashSet<string>();

                // Scan bi_gt, fix, img subfolders for image files
                foreach (var subfolder in new[] { "bi_gt", "fix", "img" })
                {
                    string folder = Path.Combine(_selectedSessionFolder, subfolder);
                    if (Directory.Exists(folder))
                    {
                        foreach (var file in Directory.GetFiles(folder))
                        {
                            string name = Path.GetFileNameWithoutExtension(file);
                            discoveredImages.Add(name);
                        }
                    }
                }

                foreach (var name in discoveredImages.OrderBy(n => n))
                {
                    string biGtPath = Path.Combine(_selectedSessionFolder, "bi_gt", $"{name}.png");
                    string fixPath = Path.Combine(_selectedSessionFolder, "fix", $"{name}.png");

                    imageEntries.Add(new ImageEntry
                    {
                        ImageName = name,
                        MaskEdited = File.Exists(biGtPath) ? "✓" : "—",
                        RankEdited = File.Exists(fixPath) ? "✓" : "—",
                        Sensitivity = "—",
                        ResponseBias = "—",
                        Confirmed = 0,
                        Rejected = 0,
                        Corrections = 0
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
            if (!(ImageGrid.SelectedItem is ImageEntry imageEntry))
                return;

            if (string.IsNullOrEmpty(_selectedSessionFolder))
                return;

            string imageName = Path.GetFileNameWithoutExtension(imageEntry.ImageName);
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;

            ImageViewerHeader.Text = $"Preview: {imageEntry.ImageName}";
            ImageViewerGrid.Visibility = Visibility.Visible;

            int loaded = 0;

            // Load original image
            OriginalImageView.Source = null;
            string origPath = FindFile(new[]
            {
                Path.Combine(_selectedSessionFolder, "img", $"{imageName}.jpg"),
                Path.Combine(_selectedSessionFolder, "img", $"{imageName}.png"),
                Path.Combine(_selectedSessionFolder, "img", $"{imageName}.bmp"),
            }, imageName, "img");
            if (origPath != null)
            {
                OriginalImageView.Source = LoadImageSafe(origPath);
                loaded++;
            }

            // Load binary mask: session bi_gt first, then outputs folder
            BinaryMaskView.Source = null;
            string maskPath = FindFile(new[]
            {
                Path.Combine(_selectedSessionFolder, "bi_gt", $"{imageName}.png"),
                Path.Combine(exeDir, "outputs", imageName, "binary_image.png"),
                Path.Combine(exeDir, "outputs", imageName, "binary_image_modified.png"),
            });
            if (maskPath != null)
            {
                BinaryMaskView.Source = LoadImageSafe(maskPath);
                loaded++;
            }

            // Load rank map: session fix first, then outputs folder
            RankMapView.Source = null;
            string rankPath = FindFile(new[]
            {
                Path.Combine(_selectedSessionFolder, "fix", $"{imageName}.png"),
                Path.Combine(exeDir, "outputs", imageName, "fixation_image.png"),
            });
            if (rankPath != null)
            {
                RankMapView.Source = LoadImageSafe(rankPath);
                loaded++;
            }

            ImageStatusLabel.Text = $"{loaded}/3 images available";
            System.Diagnostics.Debug.WriteLine($"Image preview for {imageName}: {loaded}/3 loaded" +
                $"\n  Original: {origPath ?? "(not found)"}" +
                $"\n  Mask: {maskPath ?? "(not found)"}" +
                $"\n  Rank: {rankPath ?? "(not found)"}");
        }

        /// <summary>
        /// Returns the first path that exists from the candidates list.
        /// Optionally searches a folder in the session directory with wildcard matching.
        /// </summary>
        private string FindFile(string[] candidates, string imageName = null, string sessionSubfolder = null)
        {
            // Check explicit paths first
            foreach (var path in candidates)
            {
                if (File.Exists(path))
                    return path;
            }

            // Wildcard search in session subfolder (e.g., img/animal-59.*)
            if (imageName != null && sessionSubfolder != null && !string.IsNullOrEmpty(_selectedSessionFolder))
            {
                string folder = Path.Combine(_selectedSessionFolder, sessionSubfolder);
                if (Directory.Exists(folder))
                {
                    var match = Directory.GetFiles(folder, $"{imageName}.*").FirstOrDefault();
                    if (match != null) return match;
                }
            }

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
