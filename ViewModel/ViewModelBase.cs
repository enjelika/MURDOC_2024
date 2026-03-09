using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MURDOC_2024.ViewModel
{
    /// <summary>
    /// Base class for all ViewModels. Implements <see cref="INotifyPropertyChanged"/> and provides
    /// <see cref="SetProperty{T}"/> for concise, change-notifying property setters.
    /// </summary>
    public class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Sets <paramref name="storage"/> to <paramref name="value"/> and raises
        /// <see cref="PropertyChanged"/> if the value changed. Returns true if changed.
        /// </summary>
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(storage, value))
                return false;

            storage = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>Raises the <see cref="PropertyChanged"/> event for the specified property name.</summary>
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
