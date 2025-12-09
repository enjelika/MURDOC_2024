using System;
using System.Windows.Input;
using MURDOC_2024.ViewModel;

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

        // Commands
        private readonly RelayCommand _browseCommand;
        private readonly RelayCommand _runCommand;
        private readonly RelayCommand _resetCommand;

        public ICommand BrowseCommand => _browseCommand;
        public ICommand RunCommand => _runCommand;
        public ICommand ResetCommand => _resetCommand;

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

        public ImageControlViewModel(
            Action runModelsAction,
            Action resetAction,
            Action<string> imageSelectedAction,
            Action<int, int, int> slidersChangedAction)
        {
            _runModelsAction = runModelsAction ?? throw new ArgumentNullException(nameof(runModelsAction));
            _resetAction = resetAction ?? throw new ArgumentNullException(nameof(resetAction));
            _imageSelectedAction = imageSelectedAction ?? throw new ArgumentNullException(nameof(imageSelectedAction));
            _slidersChangedAction = slidersChangedAction;

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

            // Initialize states
            IsRunButtonEnabled = false;
            IsBrowseEnabled = true;
            IsResetEnabled = false; // Typically starts disabled until there's something to reset
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
            }
        }

        private void ExecuteRunCommand()
        {
            _runModelsAction?.Invoke();
        }

        private void ExecuteResetCommand()
        {
            _resetAction?.Invoke();
            IsRunButtonEnabled = false;
            IsResetEnabled = false;
            IsBrowseEnabled = true;
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
            }
            else
            {
                // Re-enable appropriate buttons after execution
                IsBrowseEnabled = true;
                IsRunButtonEnabled = !string.IsNullOrEmpty(SelectedImagePath);
                IsResetEnabled = !string.IsNullOrEmpty(SelectedImagePath);
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
                }
            }
        }
    }
}