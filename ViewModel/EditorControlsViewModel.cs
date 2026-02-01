using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using MURDOC_2024.Model;

namespace MURDOC_2024.ViewModel
{
    public class EditorControlsPaneViewModel : ViewModelBase
    {
        // Feedback tracking
        private ObservableCollection<DetectionFeedback> _feedbackHistory;
        private int _confirmedCount;
        private int _rejectedCount;
        private int _correctionCount;
        private bool _isCorrectionModeActive;

        // Display options
        private bool _showROIs = true;
        private bool _showConfidence = true;
        private bool _highlightFeedback = true;
        private bool _showAttentionMaps = false;

        // Session info
        private DateTime _sessionStartTime;

        // ROI (placeholders for now)
        private int _roiCount;
        private bool _isDrawingROI;

        // Commands
        public ICommand EnableCorrectionModeCommand { get; }
        public ICommand ViewFeedbackHistoryCommand { get; }
        public ICommand ExportFeedbackCommand { get; }
        public ICommand ResetSessionCommand { get; }

        // ROI Commands (placeholders)
        public ICommand SetPolygonModeCommand { get; }
        public ICommand SetFreehandModeCommand { get; }
        public ICommand ClearROIsCommand { get; }
        public ICommand ExportROIMasksCommand { get; }

        // Events for communication with parent
        public event EventHandler CorrectionModeToggled;
        public event EventHandler FeedbackHistoryViewRequested;
        public event EventHandler FeedbackExportRequested;
        public event EventHandler SessionResetRequested;

        #region Properties

        // Feedback Properties
        public int ConfirmedCount
        {
            get => _confirmedCount;
            set
            {
                if (SetProperty(ref _confirmedCount, value))
                {
                    OnPropertyChanged(nameof(TotalInteractions));
                }
            }
        }

        public int RejectedCount
        {
            get => _rejectedCount;
            set
            {
                if (SetProperty(ref _rejectedCount, value))
                {
                    OnPropertyChanged(nameof(TotalInteractions));
                }
            }
        }

        public int CorrectionCount
        {
            get => _correctionCount;
            set
            {
                if (SetProperty(ref _correctionCount, value))
                {
                    OnPropertyChanged(nameof(TotalInteractions));
                }
            }
        }

        public bool IsCorrectionModeActive
        {
            get => _isCorrectionModeActive;
            set => SetProperty(ref _isCorrectionModeActive, value);
        }

        // Display Options
        public bool ShowROIs
        {
            get => _showROIs;
            set => SetProperty(ref _showROIs, value);
        }

        public bool ShowConfidence
        {
            get => _showConfidence;
            set => SetProperty(ref _showConfidence, value);
        }

        public bool HighlightFeedback
        {
            get => _highlightFeedback;
            set => SetProperty(ref _highlightFeedback, value);
        }

        public bool ShowAttentionMaps
        {
            get => _showAttentionMaps;
            set => SetProperty(ref _showAttentionMaps, value);
        }

        // Session Info
        public DateTime SessionStartTime
        {
            get => _sessionStartTime;
            set => SetProperty(ref _sessionStartTime, value);
        }

        public int TotalInteractions => ConfirmedCount + RejectedCount + CorrectionCount;

        // ROI Properties (placeholders)
        public int ROICount
        {
            get => _roiCount;
            set => SetProperty(ref _roiCount, value);
        }

        public bool IsDrawingROI
        {
            get => _isDrawingROI;
            set => SetProperty(ref _isDrawingROI, value);
        }

        #endregion

        public EditorControlsPaneViewModel()
        {
            _feedbackHistory = new ObservableCollection<DetectionFeedback>();
            SessionStartTime = DateTime.Now;

            // Initialize commands
            EnableCorrectionModeCommand = new RelayCommand(ToggleCorrectionMode);
            ViewFeedbackHistoryCommand = new RelayCommand(ViewFeedbackHistory);
            ExportFeedbackCommand = new RelayCommand(ExportFeedback, CanExportFeedback);
            ResetSessionCommand = new RelayCommand(ResetSession);

            // ROI Commands (placeholders - will implement later)
            SetPolygonModeCommand = new RelayCommand(() => { /* TODO */ });
            SetFreehandModeCommand = new RelayCommand(() => { /* TODO */ });
            ClearROIsCommand = new RelayCommand(() => { /* TODO */ });
            ExportROIMasksCommand = new RelayCommand(() => { /* TODO */ });
        }

        #region Command Methods

        private void ToggleCorrectionMode()
        {
            IsCorrectionModeActive = !IsCorrectionModeActive;
            CorrectionModeToggled?.Invoke(this, EventArgs.Empty);
        }

        private void ViewFeedbackHistory()
        {
            FeedbackHistoryViewRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ExportFeedback()
        {
            FeedbackExportRequested?.Invoke(this, EventArgs.Empty);
        }

        private bool CanExportFeedback()
        {
            return _feedbackHistory != null && _feedbackHistory.Count > 0;
        }

        private void ResetSession()
        {
            SessionResetRequested?.Invoke(this, EventArgs.Empty);
            ResetCounts();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Add feedback and update counts
        /// </summary>
        public void AddFeedback(DetectionFeedback feedback)
        {
            if (feedback == null)
                return;

            _feedbackHistory.Add(feedback);
            UpdateFeedbackCounts();

            // Raise CanExecute for export command
            (ExportFeedbackCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Update feedback counts from current history
        /// </summary>
        public void UpdateFeedbackCounts()
        {
            if (_feedbackHistory == null)
                return;

            ConfirmedCount = _feedbackHistory.Count(f => f.Type == FeedbackType.Confirmation);
            RejectedCount = _feedbackHistory.Count(f => f.Type == FeedbackType.Rejection);
            CorrectionCount = _feedbackHistory.Count(f => f.Type == FeedbackType.Correction);
        }

        /// <summary>
        /// Get all feedback history
        /// </summary>
        public ObservableCollection<DetectionFeedback> GetFeedbackHistory()
        {
            return _feedbackHistory;
        }

        /// <summary>
        /// Clear all feedback
        /// </summary>
        public void ClearFeedback()
        {
            _feedbackHistory?.Clear();
            ResetCounts();
        }

        /// <summary>
        /// Reset all counts to zero
        /// </summary>
        private void ResetCounts()
        {
            ConfirmedCount = 0;
            RejectedCount = 0;
            CorrectionCount = 0;
            SessionStartTime = DateTime.Now;

            (ExportFeedbackCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        #endregion
    }
}