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
        private ObservableCollection<DetectionResult> _currentDetections;
        private int _confirmedCount;
        private int _rejectedCount;
        private int _correctionCount;
        private bool _isCorrectionModeActive;
        private DateTime _sessionStartTime;

        // ROI tracking
        private int _roiCount;
        private bool _isDrawingROI;

        // Selection state
        private bool _hasSelectedDetection;
        private string _selectedDetectionLabel;
        private double _selectedDetectionConfidence;
        private object _selectedDetection;
        private DetectionResult _selectedDetectionResult;

        // Commands
        public ICommand ConfirmDetectionCommand { get; }
        public ICommand RejectDetectionCommand { get; }
        public ICommand EnableCorrectionModeCommand { get; }
        public ICommand ViewFeedbackHistoryCommand { get; }
        public ICommand ExportFeedbackCommand { get; }
        public ICommand ResetSessionCommand { get; }

        // ROI Commands (Placeholders)
        public ICommand SetPolygonModeCommand { get; }
        public ICommand SetFreehandModeCommand { get; }
        public ICommand ClearROIsCommand { get; }
        public ICommand ExportROIMasksCommand { get; }

        // Events for MainWindowViewModel communication
        public event EventHandler CorrectionModeToggled;
        public event EventHandler FeedbackHistoryViewRequested;
        public event EventHandler FeedbackExportRequested;
        public event EventHandler SessionResetRequested;
        public event EventHandler<DetectionFeedback> DetectionFeedbackProvided;
        public event EventHandler PolygonModeRequested;
        public event EventHandler FreehandModeRequested;
        public event EventHandler ClearROIsRequested;
        public event EventHandler ExportROIMasksRequested;

        public EditorControlsPaneViewModel()
        {
            FeedbackHistory = new ObservableCollection<DetectionFeedback>();
            CurrentDetections = new ObservableCollection<DetectionResult>();
            _sessionStartTime = DateTime.Now;

            // Initialize commands
            ConfirmDetectionCommand = new RelayCommand(ExecuteConfirm, () => HasSelectedDetection);
            RejectDetectionCommand = new RelayCommand(ExecuteReject, () => HasSelectedDetection);

            EnableCorrectionModeCommand = new RelayCommand(ToggleCorrectionMode);
            ViewFeedbackHistoryCommand = new RelayCommand(ViewFeedbackHistory);
            ExportFeedbackCommand = new RelayCommand(ExportFeedback, () => FeedbackHistory.Count > 0);
            ResetSessionCommand = new RelayCommand(ResetSession);

            // Initialize ROI command placeholders
            SetPolygonModeCommand = new RelayCommand(EnablePolygonMode);
            SetFreehandModeCommand = new RelayCommand(EnableFreehandMode);
            ClearROIsCommand = new RelayCommand(ClearROIs);
            ExportROIMasksCommand = new RelayCommand(ExportROIMasks, () => ROICount > 0);
        }

        #region Properties

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

        public ObservableCollection<DetectionFeedback> FeedbackHistory
        {
            get => _feedbackHistory;
            set => SetProperty(ref _feedbackHistory, value);
        }

        public ObservableCollection<DetectionResult> CurrentDetections
        {
            get => _currentDetections;
            set => SetProperty(ref _currentDetections, value);
        }

        public object SelectedDetection
        {
            get => _selectedDetection;
            set
            {
                if (SetProperty(ref _selectedDetection, value))
                {
                    _selectedDetectionResult = value as DetectionResult;
                    UpdateSelectedDetectionDisplay();
                }
            }
        }

        public bool HasSelectedDetection
        {
            get => _hasSelectedDetection;
            set => SetProperty(ref _hasSelectedDetection, value);
        }

        public string SelectedDetectionLabel
        {
            get => _selectedDetectionLabel;
            set => SetProperty(ref _selectedDetectionLabel, value);
        }

        public double SelectedDetectionConfidence
        {
            get => _selectedDetectionConfidence;
            set => SetProperty(ref _selectedDetectionConfidence, value);
        }

        public int ConfirmedCount
        {
            get => _confirmedCount;
            set { if (SetProperty(ref _confirmedCount, value)) OnPropertyChanged(nameof(TotalInteractions)); }
        }

        public int RejectedCount
        {
            get => _rejectedCount;
            set { if (SetProperty(ref _rejectedCount, value)) OnPropertyChanged(nameof(TotalInteractions)); }
        }

        public int CorrectionCount
        {
            get => _correctionCount;
            set { if (SetProperty(ref _correctionCount, value)) OnPropertyChanged(nameof(TotalInteractions)); }
        }

        public int TotalInteractions => ConfirmedCount + RejectedCount + CorrectionCount;

        public bool IsCorrectionModeActive
        {
            get => _isCorrectionModeActive;
            set => SetProperty(ref _isCorrectionModeActive, value);
        }

        public DateTime SessionStartTime
        {
            get => _sessionStartTime;
            set => SetProperty(ref _sessionStartTime, value);
        }

        #endregion

        #region Public Methods

        private void EnablePolygonMode()
        {
            IsDrawingROI = true;
            PolygonModeRequested?.Invoke(this, EventArgs.Empty);
            System.Diagnostics.Debug.WriteLine("Polygon mode requested");
        }

        private void EnableFreehandMode()
        {
            IsDrawingROI = true;
            FreehandModeRequested?.Invoke(this, EventArgs.Empty);
            System.Diagnostics.Debug.WriteLine("Freehand mode requested");
        }

        private void ClearROIs()
        {
            ROICount = 0;
            IsDrawingROI = false;
            ClearROIsRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ExportROIMasks()
        {
            ExportROIMasksRequested?.Invoke(this, EventArgs.Empty);
        }

        public void OnROICompleted()
        {
            IsDrawingROI = false;
            ROICount++;
            UpdateCommandStates();
        }

        public void OnROICancelled()
        {
            IsDrawingROI = false;
            UpdateCommandStates();
        }

        /// <summary>
        /// Called by MainWindowViewModel to add feedback from external sources
        /// Fixes CS1061 error
        /// </summary>
        public void AddFeedback(DetectionFeedback feedback)
        {
            if (feedback == null) return;

            FeedbackHistory.Add(feedback);
            UpdateFeedbackCounts();

            // Notify MainWindowViewModel to log to metrics
            DetectionFeedbackProvided?.Invoke(this, feedback);

            UpdateCommandStates();
        }

        /// <summary>
        /// Returns the history collection for export or display
        /// Fixes CS1061 error
        /// </summary>
        public ObservableCollection<DetectionFeedback> GetFeedbackHistory()
        {
            return FeedbackHistory;
        }

        /// <summary>
        /// External trigger to force button state re-evaluation
        /// </summary>
        public void UpdateCommandStates()
        {
            (ConfirmDetectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RejectDetectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportFeedbackCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        public void SelectDetection(DetectionResult detection)
        {
            SelectedDetection = detection;
        }

        public void ClearSelection()
        {
            SelectedDetection = null;
        }

        public void ClearFeedback()
        {
            FeedbackHistory.Clear();
            ResetCounts();
        }

        #endregion

        #region Private Helpers

        private void UpdateSelectedDetectionDisplay()
        {
            if (_selectedDetectionResult == null)
            {
                HasSelectedDetection = false;
                SelectedDetectionLabel = string.Empty;
                SelectedDetectionConfidence = 0.0;
            }
            else
            {
                HasSelectedDetection = true;
                SelectedDetectionLabel = _selectedDetectionResult.Label;
                SelectedDetectionConfidence = _selectedDetectionResult.Confidence;
            }

            UpdateCommandStates();
        }

        private void ExecuteConfirm()
        {
            var feedback = _selectedDetectionResult?.ToFeedback(FeedbackType.Confirmation);
            if (feedback != null)
            {
                AddFeedback(feedback);
                ClearSelection();
            }
        }

        private void ExecuteReject()
        {
            var feedback = _selectedDetectionResult?.ToFeedback(FeedbackType.Rejection);
            if (feedback != null)
            {
                AddFeedback(feedback);
                ClearSelection();
            }
        }

        private void UpdateFeedbackCounts()
        {
            ConfirmedCount = FeedbackHistory.Count(f => f.FeedbackType == FeedbackType.Confirmation);
            RejectedCount = FeedbackHistory.Count(f => f.FeedbackType == FeedbackType.Rejection);
            CorrectionCount = FeedbackHistory.Count(f => f.FeedbackType == FeedbackType.Correction);
        }

        private void ResetCounts()
        {
            ConfirmedCount = 0;
            RejectedCount = 0;
            CorrectionCount = 0;
            _sessionStartTime = DateTime.Now;
            UpdateCommandStates();
        }

        private void ToggleCorrectionMode()
        {
            IsCorrectionModeActive = !IsCorrectionModeActive;
            CorrectionModeToggled?.Invoke(this, EventArgs.Empty);
        }

        private void ViewFeedbackHistory() => FeedbackHistoryViewRequested?.Invoke(this, EventArgs.Empty);
        private void ExportFeedback() => FeedbackExportRequested?.Invoke(this, EventArgs.Empty);
        private void ResetSession() => SessionResetRequested?.Invoke(this, EventArgs.Empty);

        #endregion
    }
}