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

        private readonly RelayCommand _runCommand;
        private readonly RelayCommand _resetCommand;
        private readonly RelayCommand _browseCommand;

        public ICommand BrowseCommand => _browseCommand;
        public ICommand RunCommand => _runCommand;
        public ICommand ResetCommand => _resetCommand;

        private readonly Action<string> _imageSelectedAction;

        public ImageControlViewModel(
            Action runModelsAction,
            Action resetAction,
            Action<string> imageSelectedAction)
        {
            _runModelsAction = runModelsAction;
            _resetAction = resetAction;
            _imageSelectedAction = imageSelectedAction;

            _browseCommand = new RelayCommand(_ => ExecuteBrowseCommand());
            _runCommand = new RelayCommand(_ => _runModelsAction(), _ => IsRunButtonEnabled);
            _resetCommand = new RelayCommand(_ => _resetAction());

            IsRunButtonEnabled = false;
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
                    // Enable/disable Run button based on whether an image is selected
                    IsRunButtonEnabled = !string.IsNullOrEmpty(value);
                }
            }
        }

        private int _sliderBrightness;
        public int SliderBrightness
        {
            get => _sliderBrightness;
            set => SetProperty(ref _sliderBrightness, value);
        }

        private int _sliderContrast;
        public int SliderContrast
        {
            get => _sliderContrast;
            set => SetProperty(ref _sliderContrast, value);
        }

        private int _sliderSaturation;
        public int SliderSaturation
        {
            get => _sliderSaturation;
            set => SetProperty(ref _sliderSaturation, value);
        }

        private bool _isRunEnabled;
        public bool IsRunButtonEnabled
        {
            get => _isRunEnabled;
            set
            {
                if (SetProperty(ref _isRunEnabled, value))
                {
                    // When the flag changes, tell WPF to re-evaluate CanExecute
                    _runCommand.RaiseCanExecuteChanged();
                }
            }
        }

        // =======================
        // Command logic
        // =======================

        private void ExecuteBrowseCommand()
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                var fullPath = openFileDialog.FileName;
                SelectedImagePath = fullPath;
                SelectedImageFileName = Path.GetFileName(fullPath);

                // 👉 Notify the InputImagePaneViewModel
                _imageSelectedAction?.Invoke(fullPath);
            }
        }
    }
}
