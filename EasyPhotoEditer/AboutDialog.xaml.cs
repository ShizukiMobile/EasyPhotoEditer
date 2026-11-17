using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Navigation;

namespace EasyPhotoEditer
{
    public partial class AboutDialog : Window
    {
        public AboutDialog()
        {
            InitializeComponent();
            WindowThemeHelper.ApplyDarkTitleBar(this);

            var assembly = Assembly.GetExecutingAssembly();
            string? infoVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            string versionText;
            if (!string.IsNullOrWhiteSpace(infoVersion))
            {
                int plusIndex = infoVersion.IndexOf('+');
                versionText = plusIndex >= 0 ? infoVersion[..plusIndex] : infoVersion;
            }
            else
            {
                var ver = assembly.GetName().Version;
                versionText = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.1";
            }

            TxtVersion.Text = $"Version {versionText}";

            string? copyright = assembly
                .GetCustomAttribute<AssemblyCopyrightAttribute>()?
                .Copyright;
            if (!string.IsNullOrWhiteSpace(copyright))
            {
                TxtCopyright.Text = copyright;
            }

            RunRuntime.Text = $"{RuntimeInformation.FrameworkDescription} ({RuntimeInformation.OSArchitecture})";
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                // ブラウザ起動失敗時は無視
            }
            e.Handled = true;
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
