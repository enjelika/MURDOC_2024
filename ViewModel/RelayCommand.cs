using System;
using System.Windows.Input;

namespace MURDOC_2024.ViewModel
{
    /// <summary>
    /// Parameterless <see cref="ICommand"/> implementation that delegates execution and CanExecute
    /// evaluation to caller-supplied delegates. Re-evaluates CanExecute via WPF's CommandManager.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        /// <summary>Initializes the command with required execute and optional canExecute delegates.</summary>
        /// <param name="execute">Action to run when the command is invoked. Must not be null.</param>
        /// <param name="canExecute">Optional predicate; returns true when not provided.</param>
        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>Evaluates the canExecute predicate; returns true if none was provided.</summary>
        public bool CanExecute(object parameter)
        {
            return _canExecute?.Invoke() ?? true;
        }

        /// <summary>Invokes the execute action.</summary>
        public void Execute(object parameter)
        {
            _execute();
        }

        /// <summary>Forces WPF to re-query CanExecute for all commands on the UI thread.</summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>
    /// Generic <see cref="ICommand"/> implementation that passes a typed parameter to execute
    /// and canExecute delegates. Re-evaluates CanExecute via WPF's CommandManager.
    /// </summary>
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T> _canExecute;

        /// <summary>Initializes the generic command with required execute and optional canExecute delegates.</summary>
        public RelayCommand(Action<T> execute, Predicate<T> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        /// <summary>Evaluates the typed canExecute predicate; returns true if none was provided.</summary>
        public bool CanExecute(object parameter)
        {
            return _canExecute?.Invoke((T)parameter) ?? true;
        }

        /// <summary>Invokes the execute action with the typed parameter.</summary>
        public void Execute(object parameter)
        {
            _execute((T)parameter);
        }

        /// <summary>Forces WPF to re-query CanExecute for all commands on the UI thread.</summary>
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}