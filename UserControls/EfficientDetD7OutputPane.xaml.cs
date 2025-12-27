using MURDOC_2024.ViewModel;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace MURDOC_2024.UserControls
{
    /// <summary>
    /// Interaction logic for EfficientDetD7OutputPane.xaml
    /// </summary>
    public partial class EfficientDetD7OutputPane : UserControl
    {
        public EfficientDetD7OutputPane()
        {
            InitializeComponent();
        }

        private void EfficientDetImage_MouseEnter(object sender, MouseEventArgs e)
        {
            if (DataContext is EfficientDetD7ViewModel vm &&
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
