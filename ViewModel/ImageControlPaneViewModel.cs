using Microsoft.Win32;
using System;
using System.IO;
using System.Windows.Input;

namespace MURDOC_2024.ViewModel
{
    public class ImageControlViewModel : ViewModelBase
    {
        private readonly Action _runModelsAction;
        private readonly Action _resetAction;
        private readonly Action<string> _imageSelectedAction;
        private readonly Action<int, int, int> _slidersChangedAction;

        private readonly RelayCommand _runCommand;
        private readonly RelayCommand _resetCommand;
        private readonly RelayCommand _browseCommand;

        public ICommand BrowseCommand => _browseCommand;
        public ICommand RunCommand => _runCommand;
        public ICommand ResetCommand => _resetCommand;

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

            _browseCommand = new RelayCommand(_ => ExecuteBrowseCommand(), _ => CanBrowse);
            _runCommand = new RelayCommand(_ => ExecuteRunCommand(), _ => CanRun);
            _resetCommand = new RelayCommand(_ => ExecuteResetCommand(), _ => CanReset);

            IsRunButtonEnabled = false;
            IsBrowseEnabled = true;
        }

        // =======================
        // Properties
        // =======================

        private string _selectedImageFileName;
        public string SelectedImageFileName
        {
            get => _selectedImageFileName;
            set => SetProperty(ref _selectedImageFileName, value);
        }

        private string _selectedImagePath;
        public string SelectedImagePath
        {
            get => _selectedImagePath;
            set
            {
                if (SetProperty(ref _selectedImagePath, value))
                {
                    IsRunButtonEnabled = !string.IsNullOrEmpty(value) && !IsModelRunning;
                    UpdateCommandStates();
                }
            }
        }

        private int _sliderBrightness;
        public int SliderBrightness
        {
            get => _sliderBrightness;
            set
            {
                if (SetProperty(ref _sliderBrightness, value))
                {
                    _slidersChangedAction?.Invoke(
                        SliderBrightness, SliderContrast, SliderSaturation);
                }
            }
        }

        private int _sliderContrast;
        public int SliderContrast
        {
            get => _sliderContrast;
            set
            {
                if (SetProperty(ref _sliderContrast, value))
                {
                    _slidersChangedAction?.Invoke(
                        SliderBrightness, SliderContrast, SliderSaturation);
                }
            }
        }

        private int _sliderSaturation;
        public int SliderSaturation
        {
            get => _sliderSaturation;
            set
            {
                if (SetProperty(ref _sliderSaturation, value))
                {
                    _slidersChangedAction?.Invoke(
                        SliderBrightness, SliderContrast, SliderSaturation);
                }
            }
        }

        private bool _isRunEnabled;
        public bool IsRunButtonEnabled
        {
            get => _isRunEnabled;
            set
            {
                if (SetProperty(ref _isRunEnabled, value))
                {
                    _runCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isBrowseEnabled = true;
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

        private bool _isModelRunning;
        public bool IsModelRunning
        {
            get => _isModelRunning;
            set
            {
                if (SetProperty(ref _isModelRunning, value))
                {
                    UpdateCommandStates();
                    OnPropertyChanged(nameof(IsNotRunning));
                }
            }
        }

        public bool IsNotRunning => !IsModelRunning;

        // =======================
        // Command Execution
        // =======================

        private bool CanBrowse => !IsModelRunning;
        private bool CanRun => !string.IsNullOrEmpty(SelectedImagePath) && !IsModelRunning;
        private bool CanReset => !IsModelRunning;

        /// <summary>
        /// Updates all command states - call this when model running state changes
        /// </summary>
        public void UpdateCommandStates()
        {
            IsRunButtonEnabled = CanRun;
            IsBrowseEnabled = CanBrowse;

            _runCommand?.RaiseCanExecuteChanged();
            _browseCommand?.RaiseCanExecuteChanged();
            _resetCommand?.RaiseCanExecuteChanged();
        }

        private void ExecuteBrowseCommand()
        {
            if (IsModelRunning)
                return;

            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp",
                Title = "Select an Image for Camouflage Detection"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var fullPath = openFileDialog.FileName;

                SelectedImagePath = fullPath;
                SelectedImageFileName = Path.GetFileName(fullPath);

                // Notify the parent viewmodel
                _imageSelectedAction?.Invoke(fullPath);
            }
        }

        private void ExecuteRunCommand()
        {
            if (!CanRun)
                return;

            IsModelRunning = true;

            try
            {
                _runModelsAction();
            }
            finally
            {
                // The parent ViewModel should handle setting this back to false
                // when the async operation completes
            }
        }

        private void ExecuteResetCommand()
        {
            if (IsModelRunning)
                return;

            SelectedImagePath = null;
            SelectedImageFileName = null;
            SliderBrightness = 0;
            SliderContrast = 0;
            SliderSaturation = 0;

            _resetAction();
        }
    }
}