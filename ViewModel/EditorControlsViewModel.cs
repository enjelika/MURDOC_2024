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
        private bool _isEraserMode;

        // Top-level editing tool mode
        private bool _isPolygonEditingMode;
        private bool _isRankPaintingMode;

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

        public bool IsEraserMode
        {
            get => _isEraserMode;
            set => SetProperty(ref _isEraserMode, value);
        }

        // Top-level editing tool mode
        public bool IsPolygonEditingMode
        {
            get => _isPolygonEditingMode;
            set => SetProperty(ref _isPolygonEditingMode, value);
        }

        public bool IsRankPaintingMode
        {
            get => _isRankPaintingMode;
            set => SetProperty(ref _isRankPaintingMode, value);
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
        public ICommand SwitchToPolygonEditingCommand { get; }
        public ICommand SwitchToRankPaintingCommand { get; }
        public ICommand SetAddPointsModeCommand { get; }
        public ICommand SetRemovePointsModeCommand { get; }
        public ICommand SetEraserModeCommand { get; }
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
        public ICommand ViewSessionHistoryCommand { get; }

        #endregion

        #region Events

        // Edit mode events
        public event EventHandler EnterEditModeRequested;
        public event EventHandler ExitEditModeRequested;
        public event EventHandler<PointEditMode> PointEditModeChanged;
        public event EventHandler<string> EditingToolModeChanged;
        public event EventHandler SaveChangesRequested;

        // Rank brush events
        public event EventHandler<RankBrushEventArgs> RankBrushChanged;

        // Feedback events
        public event EventHandler CorrectionModeToggled;
        public event EventHandler FeedbackHistoryViewRequested;
        public event EventHandler FeedbackExportRequested;
        public event EventHandler SessionResetRequested;
        public event EventHandler ViewSessionHistoryRequested;
        public event EventHandler<DetectionFeedback> DetectionFeedbackProvided;

        #endregion

        public EditorControlsPaneViewModel()
        {
            FeedbackHistory = new ObservableCollection<DetectionFeedback>();
            CurrentDetections = new ObservableCollection<DetectionResult>();
            _sessionStartTime = DateTime.Now;

            // Edit mode commands
            EnterEditModeCommand = new RelayCommand(EnterEditMode);
            SwitchToPolygonEditingCommand = new RelayCommand(SwitchToPolygonEditing);
            SwitchToRankPaintingCommand = new RelayCommand(SwitchToRankPainting);
            SetAddPointsModeCommand = new RelayCommand(SetAddPointsMode);
            SetRemovePointsModeCommand = new RelayCommand(SetRemovePointsMode);
            SetEraserModeCommand = new RelayCommand(SetEraserMode);
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
            ViewSessionHistoryCommand = new RelayCommand(ViewSessionHistory);
        }

        #region Edit Mode Methods

        /// <summary>Activates unified edit mode and defaults to polygon editing sub-mode.</summary>
        private void EnterEditMode()
        {
            IsEditModeActive = true;
            IsAddPointsMode = true; // Default to add points
            IsRemovePointsMode = false;
            IsEraserMode = false;
            IsIncreaseBrushActive = true; // Default to increase brush
            IsDecreaseBrushActive = false;

            // Default to polygon editing tool
            IsPolygonEditingMode = true;
            IsRankPaintingMode = false;

            EnterEditModeRequested?.Invoke(this, EventArgs.Empty);
            EditingToolModeChanged?.Invoke(this, "Polygon");
            System.Diagnostics.Debug.WriteLine("Entered unified edit mode (Polygon Editing)");
        }

        /// <summary>Switches the active editing tool to polygon editing mode.</summary>
        private void SwitchToPolygonEditing()
        {
            IsPolygonEditingMode = true;
            IsRankPaintingMode = false;
            EditingToolModeChanged?.Invoke(this, "Polygon");
            System.Diagnostics.Debug.WriteLine("Switched to Polygon Editing mode");
        }

        /// <summary>Switches the active editing tool to rank painting mode.</summary>
        private void SwitchToRankPainting()
        {
            IsPolygonEditingMode = false;
            IsRankPaintingMode = true;
            EditingToolModeChanged?.Invoke(this, "RankPaint");
            System.Diagnostics.Debug.WriteLine("Switched to Rank Painting mode");
        }

        /// <summary>Deactivates edit mode and fires ExitEditModeRequested so the view can clean up overlays.</summary>
        public void ExitEditMode()
        {
            IsEditModeActive = false;
            IsAddPointsMode = false;
            IsRemovePointsMode = false;
            IsEraserMode = false;
            IsPolygonEditingMode = false;
            IsRankPaintingMode = false;

            ExitEditModeRequested?.Invoke(this, EventArgs.Empty);
            System.Diagnostics.Debug.WriteLine("Exited edit mode");
        }

        /// <summary>Switches the polygon editor to add-points sub-mode and fires PointEditModeChanged.</summary>
        private void SetAddPointsMode()
        {
            IsAddPointsMode = true;
            IsRemovePointsMode = false;
            IsEraserMode = false;
            PointEditModeChanged?.Invoke(this, PointEditMode.Add);
            System.Diagnostics.Debug.WriteLine("Set to Add Points mode");
        }

        /// <summary>Switches the polygon editor to remove-points sub-mode and fires PointEditModeChanged.</summary>
        private void SetRemovePointsMode()
        {
            IsAddPointsMode = false;
            IsRemovePointsMode = true;
            IsEraserMode = false;
            PointEditModeChanged?.Invoke(this, PointEditMode.Remove);
            System.Diagnostics.Debug.WriteLine("Set to Remove Points mode");
        }

        /// <summary>Switches the polygon editor to eraser sub-mode for bulk point removal by dragging.</summary>
        private void SetEraserMode()
        {
            IsAddPointsMode = false;
            IsRemovePointsMode = false;
            IsEraserMode = true;
            PointEditModeChanged?.Invoke(this, PointEditMode.Erase);
            System.Diagnostics.Debug.WriteLine("Set to Eraser mode");
        }

        /// <summary>Fires SaveChangesRequested so the parent can persist the current polygon and rank map edits to disk.</summary>
        private void SaveChanges()
        {
            SaveChangesRequested?.Invoke(this, EventArgs.Empty);
            System.Diagnostics.Debug.WriteLine("Save changes requested");
        }

        #endregion

        #region Rank Brush Methods

        /// <summary>Activates the increase brush and fires RankBrushChanged with current size and strength.</summary>
        private void SetIncreaseBrush()
        {
            IsIncreaseBrushActive = true;
            IsDecreaseBrushActive = false;
            RankBrushChanged?.Invoke(this, new RankBrushEventArgs(RankBrushMode.Increase, BrushSize, BrushStrength));
            System.Diagnostics.Debug.WriteLine("Set to Increase brush");
        }

        /// <summary>Activates the decrease brush and fires RankBrushChanged with current size and strength.</summary>
        private void SetDecreaseBrush()
        {
            IsIncreaseBrushActive = false;
            IsDecreaseBrushActive = true;
            RankBrushChanged?.Invoke(this, new RankBrushEventArgs(RankBrushMode.Decrease, BrushSize, BrushStrength));
            System.Diagnostics.Debug.WriteLine("Set to Decrease brush");
        }

        #endregion

        #region Detection Feedback Methods

        /// <summary>Adds a feedback entry to the history, updates counts, and notifies subscribers.</summary>
        public void AddFeedback(DetectionFeedback feedback)
        {
            if (feedback == null) return;

            FeedbackHistory.Add(feedback);
            UpdateFeedbackCounts();

            DetectionFeedbackProvided?.Invoke(this, feedback);
            UpdateCommandStates();
        }

        /// <summary>Returns the full feedback history collection.</summary>
        public ObservableCollection<DetectionFeedback> GetFeedbackHistory()
        {
            return FeedbackHistory;
        }

        /// <summary>Sets the currently selected detection, updating all display properties.</summary>
        public void SelectDetection(DetectionResult detection)
        {
            SelectedDetection = detection;
        }

        /// <summary>Deselects the current detection and resets selection display properties.</summary>
        public void ClearSelection()
        {
            SelectedDetection = null;
        }

        /// <summary>Clears all feedback history and resets Confirmed/Rejected/Correction counts to zero.</summary>
        public void ClearFeedback()
        {
            FeedbackHistory.Clear();
            ResetCounts();
        }

        /// <summary>Forces Confirm, Reject, and Export commands to re-evaluate their CanExecute state.</summary>
        public void UpdateCommandStates()
        {
            (RejectDetectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ExportFeedbackCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        /// <summary>Creates a Confirmation feedback entry for the selected detection and clears the selection.</summary>
        private void ExecuteConfirm()
        {
            var feedback = _selectedDetectionResult?.ToFeedback(FeedbackType.Confirmation);
            if (feedback != null)
            {
                AddFeedback(feedback);
                ClearSelection();
            }
        }

        /// <summary>Creates a Rejection feedback entry for the selected detection and clears the selection.</summary>
        private void ExecuteReject()
        {
            var feedback = _selectedDetectionResult?.ToFeedback(FeedbackType.Rejection);
            if (feedback != null)
            {
                AddFeedback(feedback);
                ClearSelection();
            }
        }

        /// <summary>Recomputes Confirmed/Rejected/Correction counts from the current FeedbackHistory.</summary>
        private void UpdateFeedbackCounts()
        {
            ConfirmedCount = FeedbackHistory.Count(f => f.FeedbackType == FeedbackType.Confirmation);
            RejectedCount = FeedbackHistory.Count(f => f.FeedbackType == FeedbackType.Rejection);
            CorrectionCount = FeedbackHistory.Count(f => f.FeedbackType == FeedbackType.Correction);
        }

        /// <summary>Resets all feedback counts to zero and reinitializes the session start timestamp.</summary>
        private void ResetCounts()
        {
            ConfirmedCount = 0;
            RejectedCount = 0;
            CorrectionCount = 0;
            _sessionStartTime = DateTime.Now;
            UpdateCommandStates();
        }

        /// <summary>Toggles IsCorrectionModeActive and fires CorrectionModeToggled so the view can update the canvas.</summary>
        private void ToggleCorrectionMode()
        {
            IsCorrectionModeActive = !IsCorrectionModeActive;
            CorrectionModeToggled?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Syncs HasSelectedDetection, label, and confidence display properties from the currently selected detection.</summary>
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
        private void ViewSessionHistory() => ViewSessionHistoryRequested?.Invoke(this, EventArgs.Empty);

        #endregion
    }
}