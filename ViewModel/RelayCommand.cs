using System;
using System.Windows.Input;

namespace MURDOC_2024.ViewModel
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public event EventHandler CanExecuteChanged;

        // Constructor for parameterless execute + parameterless canExecute
        public RelayCommand(Action execute, Func<bool> canExecute = null)
            : this(
                execute != null ? new Action<object>(_ => execute()) : null,
                canExecute != null ? new Func<object, bool>(_ => canExecute()) : null)
        { }

        // Constructor for execute with parameter and optional canExecute with parameter
        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute?.Invoke(parameter) ?? true;
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }

        /// <summary>
        /// Call this manually from ViewModel when command availability changes
        /// Example: OnPropertyChanged(nameof(IsRunButtonEnabled));
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
