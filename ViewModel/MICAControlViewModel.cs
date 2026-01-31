using System;
using System.Threading.Tasks;
using System.Windows.Input;
using MURDOC_2024.Services;

namespace MURDOC_2024.ViewModel
{
    public class MICAControlViewModel : ViewModelBase
    {
        private readonly IPythonService _pythonService;
        private readonly Action _runModelsAction;
        private readonly Action _resetAction;

        // Sensitivity/Bias parameters
        private double _sensitivity = 1.5;  // Default
        private double _bias = 0.0;         // Default
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

        public MICAControlViewModel(
            IPythonService pythonService,
            Action runModelsAction,
            Action resetAction)
        {
            _pythonService = pythonService ?? throw new ArgumentNullException(nameof(pythonService));
            _runModelsAction = runModelsAction;
            _resetAction = resetAction;

            _runModelsCommand = new RelayCommand(
                execute: () => ExecuteRunModels(),
                canExecute: () => CanRunModels()
            );

            _resetCommand = new RelayCommand(
                execute: () => ExecuteReset(),
                canExecute: () => CanReset()
            );

            // Initialize states
            IsRunButtonEnabled = false;
            IsResetEnabled = false;
        }

        #region Properties

        public double Sensitivity
        {
            get => _sensitivity;
            set
            {
                if (SetProperty(ref _sensitivity, value))
                    _ = UpdateDetectionParametersAsync();
            }
        }

        public double Bias
        {
            get => _bias;
            set
            {
                if (SetProperty(ref _bias, value))
                    _ = UpdateDetectionParametersAsync();
            }
        }

        public bool AutoUpdate
        {
            get => _autoUpdate;
            set => SetProperty(ref _autoUpdate, value);
        }

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

        public string SelectedImagePath
        {
            get => _selectedImagePath;
            set
            {
                if (SetProperty(ref _selectedImagePath, value))
                {
                    IsRunButtonEnabled = !string.IsNullOrEmpty(value);
                    IsResetEnabled = !string.IsNullOrEmpty(value);
                }
            }
        }

        #endregion

        #region Command Execute Methods

        private void ExecuteRunModels()
        {
            _runModelsAction?.Invoke();
        }

        private void ExecuteReset()
        {
            _resetAction?.Invoke();
            ResetAll();
        }

        #endregion

        #region Command CanExecute Methods

        private bool CanRunModels()
        {
            return _runModelsAction != null
                   && !_isUpdating
                   && IsRunButtonEnabled;
        }

        private bool CanReset()
        {
            return IsResetEnabled;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Resets sensitivity, bias, and button states
        /// </summary>
        public void ResetAll()
        {
            Sensitivity = 1.5;
            Bias = 0.0;
            IsRunButtonEnabled = false;
            IsResetEnabled = false;
            SelectedImagePath = string.Empty;
        }

        /// <summary>
        /// Call this when parent's IsRunningModels state changes
        /// </summary>
        public void SetButtonStates(bool isRunning)
        {
            if (isRunning)
            {
                // Disable buttons during model execution
                IsRunButtonEnabled = false;
                IsResetEnabled = false;
            }
            else
            {
                // Re-enable buttons after execution
                IsRunButtonEnabled = !string.IsNullOrEmpty(SelectedImagePath);
                IsResetEnabled = !string.IsNullOrEmpty(SelectedImagePath);
            }

            UpdateCommandStates();
        }

        /// <summary>
        /// Updates all command CanExecute states
        /// </summary>
        public void UpdateCommandStates()
        {
            _runModelsCommand?.RaiseCanExecuteChanged();
            _resetCommand?.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Call this when an image is selected to enable buttons
        /// </summary>
        public void OnImageSelected(string imagePath)
        {
            SelectedImagePath = imagePath;
        }

        #endregion

        #region Private Methods

        private Task UpdateDetectionParametersAsync()
        {
            if (_isUpdating)
                return Task.CompletedTask;

            try
            {
                _isUpdating = true;
                _runModelsCommand?.RaiseCanExecuteChanged();

                // Push params into your PythonModelService
                _pythonService.SetDetectionParameters(_sensitivity, _bias);

                if (_autoUpdate)
                {
                    _runModelsAction?.Invoke();
                }
            }
            finally
            {
                _isUpdating = false;
                _runModelsCommand?.RaiseCanExecuteChanged();
            }

            return Task.CompletedTask;
        }

        #endregion
    }
}