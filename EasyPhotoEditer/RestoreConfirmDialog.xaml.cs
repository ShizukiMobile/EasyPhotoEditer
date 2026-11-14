using System.IO;
using System.Windows;

namespace EasyPhotoEditer
{
    public partial class RestoreConfirmDialog : Window
    {
        public RestoreConfirmDialog(IReadOnlyList<AutoSaveProject> projects)
        {
            InitializeComponent();
            WindowThemeHelper.ApplyDarkTitleBar(this);

            if (projects.Count > 1)
            {
                TxtDescription.Text = $"自動保存された {projects.Count} 件の作業内容が見つかりました。前回の編集状態を復元しますか？";
            }

            if (projects.Count > 0)
            {
                var latest = projects.OrderByDescending(p => p.SavedAt).First();
                string fileName = Path.GetFileName(latest.BaseImagePath) ?? latest.BaseImagePath ?? string.Empty;
                string savedTime = latest.SavedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss");
                TxtBackupInfo.Text = projects.Count == 1
                    ? $"対象: {fileName} （最終保存: {savedTime}）"
                    : $"最新の対象: {fileName} ほか {projects.Count - 1} 件 （最終保存: {savedTime}）";
            }
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void BtnDiscard_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
