using System.Windows;

namespace EasyPhotoEditer
{
    public partial class ConfirmExitDialog : Window
    {
        public ConfirmExitDialog()
        {
            InitializeComponent();
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
