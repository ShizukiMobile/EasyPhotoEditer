using System.Diagnostics;
using System.IO;
using System.Windows;

namespace EasyPhotoEditer
{
    public partial class CrashDialog : Window
    {
        private readonly string _logFilePath;

        public CrashDialog(string logFilePath, bool hasBackup)
        {
            InitializeComponent();
            WindowThemeHelper.ApplyDarkTitleBar(this);

            _logFilePath = logFilePath;
            TxtLogPath.Text = logFilePath;
            TxtBackupNotice.Visibility = hasBackup ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnOpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{_logFilePath}\"",
                        UseShellExecute = true
                    });
                }
                else
                {
                    string? dir = Path.GetDirectoryName(_logFilePath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = dir,
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch
            {
                // フォルダオープン失敗時は無視
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
