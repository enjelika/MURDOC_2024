using System;
using System.Threading.Tasks;
using System.Windows.Input;
using MURDOC_2024.Services;

namespace MURDOC_2024.ViewModel
{
    /// <summary>
    /// ViewModel for MICA (Mixed-Initiative Camouflage Analysis) control panel.
    /// Manages detection parameters (sensitivity/response bias) and model execution controls.
    /// </summary>
    public class MICAControlViewModel : ViewModelBase
    {
        private readonly IPythonService _pythonService;
        private readonly Action _runModelsAction;
        private readonly Action _resetAction;

        // Sensitivity/Bias parameters
        private double _sensitivity = 1.5;  // Default d' value (sensitivity)
        private double _bias = 0.0;         // Default β value (response bias)
        private bool _autoUpdate;
        private bool _isUpdating;

        // Button states
        private bool _isRunButtonEnabled;
        private bool _isResetEnabled;
        private string _selectedImagePath;

        // Commands
        private readonly RelayCommand _runModelsCommand;
        private readonly RelayCommand _resetCommand;

        public ICommand RunCommand => _runModelsCommand;
        public ICommand ResetCommand => _resetCommand;

        /// <summary>
        /// Initializes a new instance of MICAControlViewModel.
        /// </summary>
        /// <param name="pythonService">Service for communicating with Python models</param>
        /// <param name="runModelsAction">Action to execute when running models</param>
        /// <param name="resetAction">Action to execute when resetting</param>
        public MICAControlViewModel(
            IPythonService pythonService,
            Action runModelsAction,
            Action resetAction)
        {
            _pythonService = pythonService ?? throw new ArgumentNullException(nameof(pythonService));
            _runModelsAction = runModelsAction;
            _resetAction = resetAction;

            // Initialize run models command with execute and can-execute delegates
            _runModelsCommand = new RelayCommand(
                execute: () => ExecuteRunModels(),
                canExecute: () => CanRunModels()
            );

            // Initialize reset command
            _resetCommand = new RelayCommand(
                execute: () => ExecuteReset(),
                canExecute: () => CanReset()
            );

            // Initialize states - buttons start disabled until image is selected
            IsRunButtonEnabled = false;
            IsResetEnabled = false;
        }

        #region Properties

        /// <summary>
        /// Gets or sets the sensitivity (d') parameter for detection.
        /// Higher values increase detection sensitivity.
        /// Automatically updates Python model when changed.
        /// </summary>
        public double Sensitivity
        {
            get => _sensitivity;
            set
            {
                if (SetProperty(ref _sensitivity, value))
                    _ = UpdateDetectionParametersAsync();
            }
        }

        /// <summary>
        /// Gets or sets the response bias (β) parameter for detection.
        /// Positive values favor detection, negative values favor rejection.
        /// Automatically updates Python model when changed.
        /// </summary>
        public double Bias
        {
            get => _bias;
            set
            {
                if (SetProperty(ref _bias, value))
                {
                    OnPropertyChanged(nameof(ResponseBias)); // Notify ResponseBias alias
                    _ = UpdateDetectionParametersAsync();
                }
            }
        }

        /// <summary>
        /// Alias for Bias property. Used for clarity in session tracking.
        /// </summary>
        public double ResponseBias => _bias;

        /// <summary>
        /// Gets or sets whether the model should automatically re-run when parameters change.
        /// </summary>
        public bool AutoUpdate
        {
            get => _autoUpdate;
            set => SetProperty(ref _autoUpdate, value);
        }

        /// <summary>
        /// Gets or sets whether the Run button is enabled.
        /// Automatically updates command CanExecute state.
        /// </summary>
        public bool IsRunButtonEnabled
        {
            get => _isRunButtonEnabled;
            set
            {
                if (SetProperty(ref _isRunButtonEnabled, value))
                {
                    _runModelsCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets whether the Reset button is enabled.
        /// Automatically updates command CanExecute state.
        /// </summary>
        public bool IsResetEnabled
        {
            get => _isResetEnabled;
            set
            {
                if (SetProperty(ref _isResetEnabled, value))
                {
                    _resetCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// Gets or sets the currently selected image path.
        /// Automatically enables/disables buttons based on whether path is empty.
        /// </summary>
        public string SelectedImagePath
        {
            get => _selectedImagePath;
            set
            {
                if (SetProperty(ref _selectedImagePath, value))
                {
                    // Enable buttons only when an image is selected
                    IsRunButtonEnabled = !string.IsNullOrEmpty(value);
                    IsResetEnabled = !string.IsNullOrEmpty(value);
                }
            }
        }

        #endregion

        #region Command Execute Methods

        /// <summary>
        /// Executes the run models action (delegates to parent ViewModel).
        /// </summary>
        private void ExecuteRunModels()
        {
            _runModelsAction?.Invoke();
        }

        /// <summary>
        /// Executes the reset action and resets all MICA parameters to defaults.
        /// </summary>
        private void ExecuteReset()
        {
            _resetAction?.Invoke();
            ResetAll();
        }

        #endregion

        #region Command CanExecute Methods

        /// <summary>
        /// Determines whether the Run command can execute.
        /// Disabled during parameter updates or when no image is selected.
        /// </summary>
        /// <returns>True if command can execute, false otherwise</returns>
        private bool CanRunModels()
        {
            return _runModelsAction != null
                   && !_isUpdating
                   && IsRunButtonEnabled;
        }

        /// <summary>
        /// Determines whether the Reset command can execute.
        /// Enabled only when an image is selected.
        /// </summary>
        /// <returns>True if command can execute, false otherwise</returns>
        private bool CanReset()
        {
            return IsResetEnabled;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Resets all MICA parameters to default values and clears image selection.
        /// Default values: Sensitivity = 1.5, Bias = 0.0
        /// </summary>
        public void ResetAll()
        {
            Sensitivity = 1.5;  // Reset to default d' value
            Bias = 0.0;         // Reset to default β value
            IsRunButtonEnabled = false;
            IsResetEnabled = false;
            SelectedImagePath = string.Empty;
        }

        /// <summary>
        /// Updates button states based on whether models are currently running.
        /// Called by parent ViewModel when IsRunningModels state changes.
        /// </summary>
        /// <param name="isRunning">True if models are currently executing, false otherwise</param>
        public void SetButtonStates(bool isRunning)
        {
            if (isRunning)
            {
                // Disable all buttons during model execution to prevent interference
                IsRunButtonEnabled = false;
                IsResetEnabled = false;
            }
            else
            {
                // Re-enable buttons after execution (only if image is selected)
                IsRunButtonEnabled = !string.IsNullOrEmpty(SelectedImagePath);
                IsResetEnabled = !string.IsNullOrEmpty(SelectedImagePath);
            }

            UpdateCommandStates();
        }

        /// <summary>
        /// Forces all commands to re-evaluate their CanExecute state.
        /// Call this when external state changes that might affect command availability.
        /// </summary>
        public void UpdateCommandStates()
        {
            _runModelsCommand?.RaiseCanExecuteChanged();
            _resetCommand?.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Called when an image is selected. Enables MICA controls.
        /// </summary>
        /// <param name="imagePath">Path to the selected image file</param>
        public void OnImageSelected(string imagePath)
        {
            SelectedImagePath = imagePath;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Updates detection parameters in the Python model service asynchronously.
        /// If AutoUpdate is enabled, automatically re-runs the models.
        /// Prevents concurrent updates by checking _isUpdating flag.
        /// </summary>
        /// <returns>Completed task</returns>
        private Task UpdateDetectionParametersAsync()
        {
            // Prevent concurrent parameter updates
            if (_isUpdating)
                return Task.CompletedTask;

            try
            {
                _isUpdating = true;
                _runModelsCommand?.RaiseCanExecuteChanged();

                // Push updated sensitivity (d') and bias (β) parameters to Python service
                _pythonService.SetDetectionParameters(_sensitivity, _bias);

                // If auto-update is enabled, automatically re-run models with new parameters
                if (_autoUpdate)
                {
                    _runModelsAction?.Invoke();
                }
            }
            finally
            {
                // Always reset the updating flag, even if an exception occurs
                _isUpdating = false;
                _runModelsCommand?.RaiseCanExecuteChanged();
            }

            return Task.CompletedTask;
        }

        #endregion
    }
}