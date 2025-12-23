using ImageProcessor.Processors;
using MURDOC_2024.ViewModel;
using System;
using System.Windows.Input;

namespace MURDOC_2024.ViewModel
{
    public class ImageControlViewModel : ViewModelBase
    {
        private readonly Action _runModelsAction;
        private readonly Action _resetAction;
        private readonly Action<string> _imageSelectedAction;
        private readonly Action<int, int, int> _slidersChangedAction;

        private bool _isRunButtonEnabled;
        private bool _isBrowseEnabled;
        private bool _isResetEnabled;
        private bool _isSlidersEnabled;

        private int _sliderBrightness;
        private int _sliderContrast;
        private int _sliderSaturation;

        // Commands
        private readonly RelayCommand _browseCommand;
        private readonly RelayCommand _runCommand;
        private readonly RelayCommand _resetCommand;
        private readonly RelayCommand _slidersCommand;

        public ICommand BrowseCommand => _browseCommand;
        public ICommand RunCommand => _runCommand;
        public ICommand ResetCommand => _resetCommand;
        public ICommand SlidersCommand => _slidersCommand;

        public bool IsRunButtonEnabled
        {
            get => _isRunButtonEnabled;
            set
            {
                if (SetProperty(ref _isRunButtonEnabled, value))
                {
                    // Notify commands that their CanExecute may have changed
                    _runCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsBrowseEnabled
        {
            get => _isBrowseEnabled;
            set
            {
                if (SetProperty(ref _isBrowseEnabled, value))
                {
                    _browseCommand?.RaiseCanExecuteChanged();
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

        public bool IsSlidersEnabled
        {
            get => _isSlidersEnabled;
            set
            {
                if (SetProperty(ref _isSlidersEnabled, value))
                {
                    _slidersCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public int SliderBrightness
        {
            get => _sliderBrightness;
            set
            {
                if (SetProperty(ref _sliderBrightness, value))
                {
                    _sliderBrightness = value;
                    _slidersChangedAction?.Invoke(_sliderBrightness, _sliderContrast, _sliderSaturation);
                }
            }
        }

        public int SliderContrast
        {
            get => _sliderContrast;
            set
            {
                if (SetProperty(ref _sliderContrast, value))
                {
                    _sliderContrast = value;
                    _slidersChangedAction?.Invoke(_sliderBrightness, _sliderContrast, _sliderSaturation);
                }
            }
        }

        public int SliderSaturation
        {
            get => _sliderSaturation;
            set
            {
                if (SetProperty(ref _sliderSaturation, value))
                {
                    _sliderSaturation = value;
                    _slidersChangedAction?.Invoke(_sliderBrightness, _sliderContrast, _sliderSaturation);
                }
            }
        }

        public ImageControlViewModel(
            Action runModelsAction,
            Action resetAction,
            Action<string> imageSelectedAction,
            Action<int, int, int> slidersChangedAction)
        {
            _runModelsAction = runModelsAction ?? throw new ArgumentNullException(nameof(runModelsAction));
            _resetAction = resetAction ?? throw new ArgumentNullException(nameof(resetAction));
            _imageSelectedAction = imageSelectedAction ?? throw new ArgumentNullException(nameof(imageSelectedAction));
            _slidersChangedAction = slidersChangedAction ?? throw new ArgumentNullException(nameof(slidersChangedAction));

            // FIXED: Use parameterless RelayCommand constructor since we're not using command parameters
            _browseCommand = new RelayCommand(
                execute: () => ExecuteBrowseCommand(),
                canExecute: () => CanBrowse());

            _runCommand = new RelayCommand(
                execute: () => ExecuteRunCommand(),
                canExecute: () => CanRun());

            _resetCommand = new RelayCommand(
                execute: () => ExecuteResetCommand(),
                canExecute: () => CanReset());

            _slidersCommand = new RelayCommand(
                execute: () => ExecuteSlidersCommand(),
                canExecute: () => CanSliders());

            // Initialize states
            IsRunButtonEnabled = false;
            IsBrowseEnabled = true;
            IsResetEnabled = false; // Typically starts disabled until there's something to reset
            IsSlidersEnabled = false;

            SelectedImagePath = string.Empty;
        }

        // Execute methods
        private void ExecuteBrowseCommand()
        {
            // Your browse logic here
            // For example:
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image files (*.jpg, *.jpeg, *.png)|*.jpg;*.jpeg;*.png|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                _imageSelectedAction?.Invoke(dialog.FileName);
                IsRunButtonEnabled = true;
                IsResetEnabled = true;
                SelectedImagePath = dialog.SafeFileName;
            }
        }

        private void ExecuteRunCommand()
        {
            _runModelsAction?.Invoke();
        }

        private void ExecuteResetCommand()
        {
            _resetAction?.Invoke();
            _sliderBrightness = 0;
            _sliderContrast = 0;
            _sliderSaturation = 0;
            IsRunButtonEnabled = false;
            IsResetEnabled = false;
            IsBrowseEnabled = true;
            SelectedImagePath = string.Empty;
        }

        private void ExecuteSlidersCommand()
        {
            _slidersChangedAction?.Invoke(_sliderBrightness, _sliderContrast, _sliderSaturation);
        }

        // CanExecute methods - return bool, not use properties directly
        private bool CanBrowse()
        {
            return IsBrowseEnabled;
        }

        private bool CanRun()
        {
            return IsRunButtonEnabled;
        }

        private bool CanReset()
        {
            return IsResetEnabled;
        }

        private bool CanSliders()
        {
            return IsSlidersEnabled;
        }

        // Method to update command states when parent VM changes running state
        public void UpdateCommandStates()
        {
            _browseCommand?.RaiseCanExecuteChanged();
            _runCommand?.RaiseCanExecuteChanged();
            _resetCommand?.RaiseCanExecuteChanged();
        }

        // Call this method when the parent's IsRunningModels changes
        public void SetButtonStates(bool isRunning)
        {
            if (isRunning)
            {
                // Disable all buttons during model execution
                IsBrowseEnabled = false;
                IsRunButtonEnabled = false;
                IsResetEnabled = false;
                IsSlidersEnabled = false;
            }
            else
            {
                // Re-enable appropriate buttons after execution
                IsBrowseEnabled = true;
                IsRunButtonEnabled = !string.IsNullOrEmpty(SelectedImagePath);
                IsResetEnabled = !string.IsNullOrEmpty(SelectedImagePath);
                IsSlidersEnabled = true;
            }

            UpdateCommandStates();
        }

        // Add property for selected image path if needed
        private string _selectedImagePath;
        public string SelectedImagePath
        {
            get => _selectedImagePath;
            set
            {
                if (SetProperty(ref _selectedImagePath, value))
                {
                    IsRunButtonEnabled = !string.IsNullOrEmpty(value);
                    IsResetEnabled = !string.IsNullOrEmpty(value);
                    IsSlidersEnabled = !string.IsNullOrEmpty(value);
                }
            }
        }
    }
}