using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Threading;

namespace MURDOC_2024.ViewModel
{
    public class SessionInfoPaneViewModel : ViewModelBase
    {
        private string _currentImageName;
        private DateTime _sessionStartTime;
        private string _sessionDuration;
        private bool _hasBinaryMaskEdits;
        private bool _hasRankMapEdits;
        private bool _hasActiveSession;

        /// <summary>
        /// The timestamp when the current session started.
        /// Used by MainWindowViewModel to derive the canonical session ID
        /// shared across SessionInfoPaneViewModel and FinalPredictionPaneViewModel.
        /// </summary>
        public DateTime SessionStartTime => _sessionStartTime;
        private int _imagesAnalyzed;
        private DispatcherTimer _durationTimer;

        // Track detailed image data
        private List<ImageSessionData> _imageDataList;

        public string CurrentImageName
        {
            get => _currentImageName;
            set => SetProperty(ref _currentImageName, value);
        }

        public string SessionDuration
        {
            get => _sessionDuration;
            set => SetProperty(ref _sessionDuration, value);
        }

        public bool HasBinaryMaskEdits
        {
            get => _hasBinaryMaskEdits;
            set => SetProperty(ref _hasBinaryMaskEdits, value);
        }

        public bool HasRankMapEdits
        {
            get => _hasRankMapEdits;
            set => SetProperty(ref _hasRankMapEdits, value);
        }

        public bool HasActiveSession
        {
            get => _hasActiveSession;
            set
            {
                if (SetProperty(ref _hasActiveSession, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        public bool HasAnyModifications => HasBinaryMaskEdits || HasRankMapEdits;

        public int ImagesAnalyzed
        {
            get => _imagesAnalyzed;
            set => SetProperty(ref _imagesAnalyzed, value);
        }

        public ICommand EndSessionCommand { get; }

        public event EventHandler EndSessionRequested;

        public SessionInfoPaneViewModel()
        {
            _imageDataList = new List<ImageSessionData>();

            // CHANGED: Only require active session, not modifications
            EndSessionCommand = new RelayCommand(EndSession, () => HasActiveSession);

            _durationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _durationTimer.Tick += UpdateDuration;
        }

        /// <summary>
        /// Starts the session clock on first call, then updates the current image name
        /// and resets per-image edit flags. Safe to call on every image load.
        /// </summary>
        public void StartSession(string imageName)
        {
            if (!HasActiveSession)
            {
                // Starting a new session
                _sessionStartTime = DateTime.Now;
                HasActiveSession = true;
                ImagesAnalyzed = 0;
                _imageDataList.Clear();

                _durationTimer.Start();
                UpdateDuration(null, null);

                System.Diagnostics.Debug.WriteLine($"New session started");
            }

            // Set current image (whether new session or continuing)
            CurrentImageName = imageName;
            HasBinaryMaskEdits = false;
            HasRankMapEdits = false;

            System.Diagnostics.Debug.WriteLine($"Current image: {imageName}");
        }

        /// <summary>Increments the count of images analyzed during this session.</summary>
        public void IncrementImageCount()
        {
            ImagesAnalyzed++;
            System.Diagnostics.Debug.WriteLine($"Images analyzed: {ImagesAnalyzed}");
        }

        /// <summary>
        /// Records or updates session data for a specific image, including edit flags,
        /// feedback counts, image adjustments, and MICA detection parameters.
        /// </summary>
        public void RecordImageData(string imageName, bool hasBinaryEdit, bool hasRankEdit,
            int confirmedCount, int rejectedCount, int correctionCount,
            int brightness, int contrast, int saturation,
            double sensitivity, double responseBias)
        {
            // Check if we already have data for this image
            var existing = _imageDataList.Find(d => d.ImageName == imageName);

            if (existing != null)
            {
                // Update existing entry
                existing.BinaryMaskEdited = hasBinaryEdit;
                existing.RankMapEdited = hasRankEdit;
                existing.ConfirmedDetections = confirmedCount;
                existing.RejectedDetections = rejectedCount;
                existing.CorrectionsMade = correctionCount;
                existing.Brightness = brightness;
                existing.Contrast = contrast;
                existing.Saturation = saturation;
                existing.Sensitivity = sensitivity;
                existing.ResponseBias = responseBias;
            }
            else
            {
                // Add new entry
                _imageDataList.Add(new ImageSessionData
                {
                    ImageName = imageName,
                    BinaryMaskEdited = hasBinaryEdit,
                    RankMapEdited = hasRankEdit,
                    ConfirmedDetections = confirmedCount,
                    RejectedDetections = rejectedCount,
                    CorrectionsMade = correctionCount,
                    Brightness = brightness,
                    Contrast = contrast,
                    Saturation = saturation,
                    Sensitivity = sensitivity,
                    ResponseBias = responseBias,
                    AnalyzedAt = DateTime.Now
                });
            }
        }

        /// <summary>Returns a snapshot of the current session including all per-image data records.</summary>
        public SessionSummaryData GetSessionSummary()
        {
            return new SessionSummaryData
            {
                SessionStartTime = _sessionStartTime,
                SessionEndTime = DateTime.Now,
                TotalDuration = DateTime.Now - _sessionStartTime,
                ImagesAnalyzed = ImagesAnalyzed,
                Images = new List<ImageSessionData>(_imageDataList)
            };
        }

        /// <summary>Stops the timer, resets all session state, and disables session-dependent commands.</summary>
        public void EndSessionInternal()
        {
            _durationTimer.Stop();
            HasActiveSession = false;
            CurrentImageName = string.Empty;
            SessionDuration = "00:00:00";
            HasBinaryMaskEdits = false;
            HasRankMapEdits = false;
            ImagesAnalyzed = 0;
            _imageDataList.Clear();

            UpdateCommandStates();

            System.Diagnostics.Debug.WriteLine("Session ended");
        }

        /// <summary>Updates the binary mask and rank map edit flags and notifies the UI of the composite HasAnyModifications property.</summary>
        public void UpdateModificationStatus(bool hasBinaryEdits, bool hasRankEdits)
        {
            HasBinaryMaskEdits = hasBinaryEdits;
            HasRankMapEdits = hasRankEdits;
            OnPropertyChanged(nameof(HasAnyModifications));
            UpdateCommandStates();
        }

        /// <summary>Timer tick handler: updates SessionDuration display while an active session is running.</summary>
        private void UpdateDuration(object sender, EventArgs e)
        {
            if (HasActiveSession)
            {
                var elapsed = DateTime.Now - _sessionStartTime;
                SessionDuration = $"{elapsed:hh\\:mm\\:ss}";
            }
        }

        /// <summary>Fires <see cref="EndSessionRequested"/> so the parent ViewModel can orchestrate clean-up and LoRA retraining.</summary>
        private void EndSession()
        {
            EndSessionRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Forces EndSessionCommand to re-evaluate its CanExecute state.</summary>
        public void UpdateCommandStates()
        {
            (EndSessionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    // Data classes for session tracking
    public class ImageSessionData
    {
        public string ImageName { get; set; }
        public bool BinaryMaskEdited { get; set; }
        public bool RankMapEdited { get; set; }
        public int ConfirmedDetections { get; set; }
        public int RejectedDetections { get; set; }
        public int CorrectionsMade { get; set; }
        public DateTime AnalyzedAt { get; set; }

        // Image adjustment parameters
        public int Brightness { get; set; }
        public int Contrast { get; set; }
        public int Saturation { get; set; }

        // Detection parameters
        public double Sensitivity { get; set; }  // d' value
        public double ResponseBias { get; set; }  // β value
    }

    public class SessionSummaryData
    {
        public DateTime SessionStartTime { get; set; }
        public DateTime SessionEndTime { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public int ImagesAnalyzed { get; set; }
        public List<ImageSessionData> Images { get; set; }
    }
}