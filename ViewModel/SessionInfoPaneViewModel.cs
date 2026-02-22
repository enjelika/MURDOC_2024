using System;
using System.Windows.Input;
using System.Windows.Threading;

namespace MURDOC_2024.ViewModel
{
    public class SessionInfoPaneViewModel : ViewModelBase
    {
        private string _currentImageName;
        private DateTime _sessionStartTime;
        private string _sessionDuration;
        private bool _hasBinaryMaskEdits;
        private bool _hasRankMapEdits;
        private bool _hasActiveSession;
        private DispatcherTimer _durationTimer;

        public string CurrentImageName
        {
            get => _currentImageName;
            set => SetProperty(ref _currentImageName, value);
        }

        public string SessionDuration
        {
            get => _sessionDuration;
            set => SetProperty(ref _sessionDuration, value);
        }

        public bool HasBinaryMaskEdits
        {
            get => _hasBinaryMaskEdits;
            set => SetProperty(ref _hasBinaryMaskEdits, value);
        }

        public bool HasRankMapEdits
        {
            get => _hasRankMapEdits;
            set => SetProperty(ref _hasRankMapEdits, value);
        }

        public bool HasActiveSession
        {
            get => _hasActiveSession;
            set => SetProperty(ref _hasActiveSession, value);
        }

        public bool HasAnyModifications => HasBinaryMaskEdits || HasRankMapEdits;

        public ICommand EndSessionCommand { get; }

        public event EventHandler EndSessionRequested;

        public SessionInfoPaneViewModel()
        {
            EndSessionCommand = new RelayCommand(EndSession, () => HasActiveSession && HasAnyModifications);

            // Timer to update duration display every second
            _durationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _durationTimer.Tick += UpdateDuration;
        }

        public void StartSession(string imageName)
        {
            CurrentImageName = imageName;
            _sessionStartTime = DateTime.Now;
            HasActiveSession = true;
            HasBinaryMaskEdits = false;
            HasRankMapEdits = false;

            _durationTimer.Start();
            UpdateDuration(null, null);

            System.Diagnostics.Debug.WriteLine($"Session started for: {imageName}");
        }

        public void EndSessionInternal()
        {
            _durationTimer.Stop();
            HasActiveSession = false;
            CurrentImageName = string.Empty;
            SessionDuration = "00:00:00";
            HasBinaryMaskEdits = false;
            HasRankMapEdits = false;

            UpdateCommandStates();

            System.Diagnostics.Debug.WriteLine("Session ended");
        }

        public void UpdateModificationStatus(bool hasBinaryEdits, bool hasRankEdits)
        {
            HasBinaryMaskEdits = hasBinaryEdits;
            HasRankMapEdits = hasRankEdits;
            OnPropertyChanged(nameof(HasAnyModifications));
            UpdateCommandStates();
        }

        private void UpdateDuration(object sender, EventArgs e)
        {
            if (HasActiveSession)
            {
                var elapsed = DateTime.Now - _sessionStartTime;
                SessionDuration = $"{elapsed:hh\\:mm\\:ss}";
            }
        }

        private void EndSession()
        {
            EndSessionRequested?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateCommandStates()
        {
            (EndSessionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }
}