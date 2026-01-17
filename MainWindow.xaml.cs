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
        public MainWindow()
        {
            try
            {
                Console.WriteLine("MainWindow constructor started");
                InitializeComponent();
                Console.WriteLine("InitializeComponent completed");            

                try
                {
                    DataContext = new MainWindowViewModel();
                    Console.WriteLine("MainWindowViewModel set as DataContext");
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
                MessageBox.Show($"An error occurred during window initialization: {ex.Message}\n\nStackTrace: {ex.StackTrace}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
    }
}
