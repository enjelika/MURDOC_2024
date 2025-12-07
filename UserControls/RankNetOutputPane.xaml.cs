using MURDOC_2024.ViewModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.UserControls
{
    /// <summary>
    /// Interaction logic for RankNetOutputPane.xaml
    /// </summary>
    public partial class RankNetOutputPane : UserControl
    {
        public RankNetOutputPane()
        {
            InitializeComponent();
        }

        private void RankNetImage_MouseEnter(object sender, MouseEventArgs e)
        {
            if (DataContext is RankNetViewModel vm &&
                sender is Image img &&
                img.Source is BitmapImage bmp &&
                bmp.UriSource != null)
            {
                // Send the *actual* URI string
                vm.OnMouseOverImage(bmp.UriSource.OriginalString);
            }
        }
    }
}
