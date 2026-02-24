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

        // Selection state
        private bool _hasSelectedDetection;
        private string _selectedDetectionLabel;
        private double _selectedDetectionConfidence;
        private object _selectedDetection;
        private DetectionResult _selectedDetectionResult;

        // Edit mode state
        private bool _isEditModeActive;
        private bool _isAddPointsMode;
        private bool _isRemovePointsMode;

        // Rank brush state
        private bool _isIncreaseBrushActive;
        private bool _isDecreaseBrushActive;
        private double _brushSize = 20;
        private double _brushStrength = 0.5;

        #region Properties

        // Edit mode
        public bool IsEditModeActive
        {
            get => _isEditModeActive;
            set => SetProperty(ref _isEditModeActive, value);
        }

        public bool IsAddPointsMode
        {
            get => _isAddPointsMode;
            set => SetProperty(ref _isAddPointsMode, value);
        }

        public bool IsRemovePointsMode
        {
            get => _isRemovePointsMode;
            set => SetProperty(ref _isRemovePointsMode, value);
        }

        // Rank brush
        public bool IsIncreaseBrushActive
        {
            get => _isIncreaseBrushActive;
            set => SetProperty(ref _isIncreaseBrushActive, value);
        }

        public bool IsDecreaseBrushActive
        {
            get => _isDecreaseBrushActive;
            set => SetProperty(ref _isDecreaseBrushActive, value);
        }

        public double BrushSize
        {
            get => _brushSize;
            set => SetProperty(ref _brushSize, value);
        }

        public double BrushStrength
        {
            get => _brushStrength;
            set => SetProperty(ref _brushStrength, value);
        }

        // Feedback
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

        // Selection
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

        // Counts
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

        #region Commands

        // Edit mode commands
        public ICommand EnterEditModeCommand { get; }
        public ICommand SetAddPointsModeCommand { get; }
        public ICommand SetRemovePointsModeCommand { get; }
        public ICommand SaveChangesCommand { get; }

        // Rank brush commands
        public ICommand SetIncreaseBrushCommand { get; }
        public ICommand SetDecreaseBrushCommand { get; }
        public ICommand IncreaseBrushSizeCommand { get; }
        public ICommand DecreaseBrushSizeCommand { get; }

        // Detection feedback commands
        public ICommand ConfirmDetectionCommand { get; }
        public ICommand RejectDetectionCommand { get; }
        public ICommand EnableCorrectionModeCommand { get; }
        public ICommand ViewFeedbackHistoryCommand { get; }
        public ICommand ExportFeedbackCommand { get; }
        public ICommand ResetSessionCommand { get; }

        #endregion

        #region Events

        // Edit mode events
        public event EventHandler EnterEditModeRequested;
        public event EventHandler ExitEditModeRequested;
        public event EventHandler<PointEditMode> PointEditModeChanged;
        public event EventHandler SaveChangesRequested;

        // Rank brush events
        public event EventHandler<RankBrushEventArgs> RankBrushChanged;

        // Feedback events
        public event EventHandler CorrectionModeToggled;
        public event EventHandler FeedbackHistoryViewRequested;
        public event EventHandler FeedbackExportRequested;
        public event EventHandler SessionResetRequested;
        public event EventHandler<DetectionFeedback> DetectionFeedbackProvided;

        #endregion

        public EditorControlsPaneViewModel()
        {
            FeedbackHistory = new ObservableCollection<DetectionFeedback>();
            CurrentDetections = new ObservableCollection<DetectionResult>();
            _sessionStartTime = DateTime.Now;

            // Edit mode commands
            EnterEditModeCommand = new RelayCommand(EnterEditMode);
            SetAddPointsModeCommand = new RelayCommand(SetAddPointsMode);
            SetRemovePointsModeCommand = new RelayCommand(SetRemovePointsMode);
            SaveChangesCommand = new RelayCommand(SaveChanges);

            // Rank brush commands
            SetIncreaseBrushCommand = new RelayCommand(SetIncreaseBrush);
            SetDecreaseBrushCommand = new RelayCommand(SetDecreaseBrush);
            IncreaseBrushSizeCommand = new RelayCommand(() => BrushSize = Math.Min(100, BrushSize + 5));
            DecreaseBrushSizeCommand = new RelayCommand(() => BrushSize = Math.Max(5, BrushSize - 5));

            // Detection feedback commands
            ConfirmDetectionCommand = new RelayCommand(ExecuteConfirm, () => HasSelectedDetection);
            RejectDetectionCommand = new RelayCommand(ExecuteReject, () => HasSelectedDetection);
            EnableCorrectionModeCommand = new RelayCommand(ToggleCorrectionMode);
            ViewFeedbackHistoryCommand = new RelayCommand(ViewFeedbackHistory);
            ExportFeedbackCommand = new RelayCommand(ExportFeedback, () => FeedbackHistory.Count > 0);
            ResetSessionCommand = new RelayCommand(ResetSession);
        }

        #region Edit Mode Methods

        private void EnterEditMode()
        {
            IsEditModeActive = true;
            IsAddPointsMode = true; // Default to add points
            IsRemovePointsMode = false;
            IsIncreaseBrushActive = true; // Default to increase brush
            IsDecreaseBrushActive = false;

            EnterEditModeRequested?.Invoke(this, EventArgs.Empty);
            System.Diagnostics.Debug.WriteLine("Entered unified edit mode");
        }

        public void ExitEditMode()
        {
            IsEditModeActive = false;
            IsAddPointsMode = false;
            IsRemovePointsMode = false;

            ExitEditModeRequested?.Invoke(this, EventArgs.Empty);
            System.Diagnostics.Debug.WriteLine("Exited edit mode");
        }

        private void SetAddPointsMode()
        {
            IsAddPointsMode = true;
            IsRemovePointsMode = false;
            PointEditModeChanged?.Invoke(this, PointEditMode.Add);
            System.Diagnostics.Debug.WriteLine("Set to Add Points mode");
        }

        private void SetRemovePointsMode()
        {
            IsAddPointsMode = false;
            IsRemovePointsMode = true;
            PointEditModeChanged?.Invoke(this, PointEditMode.Remove);
            System.Diagnostics.Debug.WriteLine("Set to Remove Points mode");
        }

        private void SaveChanges()
        {
            SaveChangesRequested?.Invoke(this, EventArgs.Empty);
            System.Diagnostics.Debug.WriteLine("Save changes requested");
        }

        #endregion

        #region Rank Brush Methods

        private void SetIncreaseBrush()
        {
            IsIncreaseBrushActive = true;
            IsDecreaseBrushActive = false;
            RankBrushChanged?.Invoke(this, new RankBrushEventArgs(RankBrushMode.Increase, BrushSize, BrushStrength));
            System.Diagnostics.Debug.WriteLine("Set to Increase brush");
        }

        private void SetDecreaseBrush()
        {
            IsIncreaseBrushActive = false;
            IsDecreaseBrushActive = true;
            RankBrushChanged?.Invoke(this, new RankBrushEventArgs(RankBrushMode.Decrease, BrushSize, BrushStrength));
            System.Diagnostics.Debug.WriteLine("Set to Decrease brush");
        }

        #endregion

        #region Detection Feedback Methods

        public void AddFeedback(DetectionFeedback feedback)
        {
            if (feedback == null) return;

            FeedbackHistory.Add(feedback);
            UpdateFeedbackCounts();

            DetectionFeedbackProvided?.Invoke(this, feedback);
            UpdateCommandStates();
        }

        public ObservableCollection<DetectionFeedback> GetFeedbackHistory()
        {
            return FeedbackHistory;
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

        public void UpdateCommandStates()
        {
            (ConfirmDetectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RejectDetectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportFeedbackCommand as RelayCommand)?.RaiseCanExecuteChanged();
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

        private void ViewFeedbackHistory() => FeedbackHistoryViewRequested?.Invoke(this, EventArgs.Empty);
        private void ExportFeedback() => FeedbackExportRequested?.Invoke(this, EventArgs.Empty);
        private void ResetSession() => SessionResetRequested?.Invoke(this, EventArgs.Empty);

        #endregion
    }
}