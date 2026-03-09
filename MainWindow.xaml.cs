using MURDOC_2024.Model;
using MURDOC_2024.Services;
using MURDOC_2024.UserControls;
using MURDOC_2024.ViewModel;
using System;
using System.Windows;

namespace MURDOC_2024
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindowViewModel ViewModel { get; set; }

        public MainWindow()
        {
            try
            {
                Console.WriteLine("MainWindow constructor started");
                InitializeComponent();
                Console.WriteLine("InitializeComponent completed");

                try
                {
                    ViewModel = new MainWindowViewModel();
                    DataContext = ViewModel;
                    Console.WriteLine("MainWindowViewModel set as DataContext");

                    // Subscribe to ViewModel events for unified edit mode
                    ViewModel.EnterEditModeRequested += OnEnterEditModeRequested;
                    ViewModel.ExitEditModeRequested += OnExitEditModeRequested;
                    ViewModel.PointEditModeChanged += OnPointEditModeChanged;
                    ViewModel.SaveAllModificationsRequested += OnSaveAllModificationsRequested;
                    ViewModel.RankBrushChangedRequested += OnRankBrushChanged;
                    ViewModel.ResetDrawingRequested += OnResetDrawingRequested;

                    Console.WriteLine("Subscribed to ViewModel unified edit mode events");
                }
                catch (Exception viewModelEx)
                {
                    Console.WriteLine($"Error creating MainWindowViewModel: {viewModelEx.Message}");
                    Console.WriteLine($"StackTrace: {viewModelEx.StackTrace}");
                    throw;
                }

                this.Loaded += MainWindow_Loaded;
                this.Closed += MainWindow_Closed;
                Console.WriteLine("MainWindow constructor completed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in MainWindow constructor: {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                MessageBox.Show($"An error occurred during window initialization: {ex.Message}\n\nStackTrace: {ex.StackTrace}",
                    "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        #region Unified Edit Mode Event Handlers

        private void OnEnterEditModeRequested(object sender, EventArgs e)
        {
            try
            {
                var mode = ViewModel.EditorControlsVM.IsIncreaseBrushActive ? RankBrushMode.Increase : RankBrushMode.Decrease;
                var brushSize = ViewModel.EditorControlsVM.BrushSize;
                var brushStrength = ViewModel.EditorControlsVM.BrushStrength;

                FinalPredictionPaneControl.EnterUnifiedEditMode(mode, brushSize, brushStrength);
                System.Diagnostics.Debug.WriteLine("MainWindow: Entered unified edit mode");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error entering edit mode: {ex.Message}");
                MessageBox.Show($"Could not enter edit mode: {ex.Message}",
                    "Edit Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnExitEditModeRequested(object sender, EventArgs e)
        {
            try
            {
                FinalPredictionPaneControl.ExitUnifiedEditMode();
                System.Diagnostics.Debug.WriteLine("MainWindow: Exited unified edit mode");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error exiting edit mode: {ex.Message}");
            }
        }

        private void OnPointEditModeChanged(object sender, PointEditMode mode)
        {
            try
            {
                FinalPredictionPaneControl.SetPointEditMode(mode);
                System.Diagnostics.Debug.WriteLine($"MainWindow: Set point edit mode to {mode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error changing point edit mode: {ex.Message}");
            }
        }

        private void OnSaveAllModificationsRequested(object sender, EventArgs e)
        {
            try
            {
                FinalPredictionPaneControl.SaveAllChanges();
                FinalPredictionPaneControl.ExitUnifiedEditMode();
                System.Diagnostics.Debug.WriteLine("MainWindow: Saved all modifications and exited edit mode");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving modifications: {ex.Message}");
                throw;
            }
        }

        private void OnRankBrushChanged(object sender, RankBrushEventArgs e)
        {
            try
            {
                FinalPredictionPaneControl.UpdateRankBrush(e.Mode, e.BrushSize, e.BrushStrength);
                System.Diagnostics.Debug.WriteLine($"MainWindow: Updated brush - Mode: {e.Mode}, Size: {e.BrushSize}, Strength: {e.BrushStrength}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating rank brush: {ex.Message}");
            }
        }

        private void OnResetDrawingRequested(object sender, EventArgs e)
        {
            try
            {
                FinalPredictionPaneControl.ExitUnifiedEditMode();
                System.Diagnostics.Debug.WriteLine("MainWindow: Reset drawing on FinalPredictionPane");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error resetting drawing: {ex.Message}");
            }
        }

        #endregion

        #region Window Lifecycle

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("MainWindow Loaded event fired");
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            Console.WriteLine("MainWindow Closed event fired");

            // Critical cleanup
            if (DataContext is IDisposable disposableViewModel)
            {
                Console.WriteLine("Calling Dispose() on MainWindowViewModel.");
                disposableViewModel.Dispose();
            }
        }

        #endregion
    }
}