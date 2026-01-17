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

        private double _sensitivity = 1.5;  // Default
        private double _bias = 0.0;         // Default
        private bool _autoUpdate;

        private bool _isUpdating;

        public MICAControlViewModel(IPythonService pythonService, Action runModelsAction)
        {
            _pythonService = pythonService ?? throw new ArgumentNullException(nameof(pythonService));
            _runModelsAction = runModelsAction; // can be null if you want

            RunModelsCommand = new RelayCommand(
                execute: () => _runModelsAction?.Invoke(),
                canExecute: () => _runModelsAction != null && !_isUpdating
            );
        }

        public ICommand RunModelsCommand { get; }

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

        public void ResetAll()
        {
            Sensitivity = 1.5;
            Bias = 0.0;
        }

        private Task UpdateDetectionParametersAsync()
        {
            if (_isUpdating)
                return Task.CompletedTask;
            try
            {
                _isUpdating = true;
                (RunModelsCommand as RelayCommand)?.RaiseCanExecuteChanged();

                // Push params into your PythonModelService (writes mica_params.json and updates fields)
                _pythonService.SetDetectionParameters(_sensitivity, _bias);

                if (_autoUpdate)
                {
                    _runModelsAction?.Invoke();
                }
            }
            finally
            {
                _isUpdating = false;
                (RunModelsCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }

            return Task.CompletedTask;
        }
    }
}
