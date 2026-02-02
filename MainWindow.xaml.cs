using MURDOC_2024.Services;
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
        private MainWindowViewModel ViewModel { get; set; }

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

                    // Subscribe to ViewModel events for drawing mode
                    ViewModel.PolygonModeRequested += OnPolygonModeRequested;
                    ViewModel.FreehandModeRequested += OnFreehandModeRequested;
                    ViewModel.ClearROIsRequested += OnClearROIsRequested;
                    ViewModel.ROIMaskExportRequested += OnROIMaskExportRequested;
                    Console.WriteLine("Subscribed to ViewModel drawing events");
                }
                catch (Exception viewModelEx)
                {
                    Console.WriteLine($"Error creating MainWindowViewModel: {viewModelEx.Message}");
                    Console.WriteLine($"StackTrace: {viewModelEx.StackTrace}");
                    throw; // Re-throw to be caught by the outer try-catch
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

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("MainWindow Loaded event fired");
            // You can add any initialization code that needs to run after the window is loaded
        }

        private void MainWindow_Closed(object sender, EventArgs e)
        {
            Console.WriteLine("MainWindow Closed event fired");
            // ------------------------------------------------------------------
            // **CRITICAL CLEANUP FIX**
            // ------------------------------------------------------------------
            if (DataContext is IDisposable disposableViewModel)
            {
                Console.WriteLine("Calling Dispose() on MainWindowViewModel.");
                disposableViewModel.Dispose(); // This calls PythonEngine.Shutdown()
            }
            // ------------------------------------------------------------------
        }

        #region Drawing Mode Event Handlers

        private void OnPolygonModeRequested(object sender, EventArgs e)
        {
            try
            {
                // Enable polygon drawing on FinalPredictionPane
                FinalPredictionPaneControl.EnablePolygonDrawing();
                System.Diagnostics.Debug.WriteLine("MainWindow: Enabled polygon drawing on FinalPredictionPane");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error enabling polygon mode: {ex.Message}");
                MessageBox.Show($"Could not enable polygon drawing: {ex.Message}",
                    "Drawing Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnFreehandModeRequested(object sender, EventArgs e)
        {
            try
            {
                // Enable freehand drawing on FinalPredictionPane
                FinalPredictionPaneControl.EnableFreehandDrawing();
                System.Diagnostics.Debug.WriteLine("MainWindow: Enabled freehand drawing on FinalPredictionPane");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error enabling freehand mode: {ex.Message}");
                MessageBox.Show($"Could not enable freehand drawing: {ex.Message}",
                    "Drawing Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnClearROIsRequested(object sender, EventArgs e)
        {
            try
            {
                // Clear all drawings on FinalPredictionPane
                FinalPredictionPaneControl.CancelDrawing();
                System.Diagnostics.Debug.WriteLine("MainWindow: Cleared ROIs on FinalPredictionPane");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing ROIs: {ex.Message}");
            }
        }

        private void OnROIMaskExportRequested(object sender, string filename)
        {
            try
            {
                // Export the current mask from FinalPredictionPane
                FinalPredictionPaneControl.ExportCurrentMask(filename);
                System.Diagnostics.Debug.WriteLine($"MainWindow: Exported mask to {filename}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error exporting mask: {ex.Message}");
                throw; // Let MainWindowViewModel handle the error display
            }
        }

        #endregion
    }
}