using MURDOC_2024.ViewModel;
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
            InitializeComponent();

            // Create an instance of MainWindowViewModel and set it as the DataContext
            DataContext = new MainWindowViewModel();
        }

        private void LocalizationImage_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            var imageItem = sender as System.Windows.Controls.Image;
            if (imageItem != null)
            {
                var viewModel = DataContext as MainWindowViewModel;
                if (viewModel != null)
                {
                    viewModel.HandlePreviewImageChanged(imageItem.Source.ToString());
                }
            }
        }
    }
}
