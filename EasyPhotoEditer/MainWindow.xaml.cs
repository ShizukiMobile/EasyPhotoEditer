using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace EasyPhotoEditer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static readonly RoutedUICommand OpenCommand = new(
            "開く",
            "OpenCommand",
            typeof(MainWindow),
            new InputGestureCollection { new KeyGesture(Key.O, ModifierKeys.Control) }
        );

        private readonly string? _initialFilePath;

        public MainWindow() : this(null)
        {
        }

        public MainWindow(string? initialFilePath)
        {
            InitializeComponent();
            _initialFilePath = initialFilePath;

            CommandBindings.Add(new CommandBinding(OpenCommand, ExecuteOpenCommand));
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_initialFilePath) && File.Exists(_initialFilePath))
            {
                LoadImageFromFile(_initialFilePath);
            }
        }

        private void ExecuteOpenCommand(object sender, ExecutedRoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "画像ファイルを開く",
                Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|すべてのファイル (*.*)|*.*",
                FilterIndex = 1,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) == true)
            {
                LoadImageFromFile(dialog.FileName);
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && IsSupportedImageFile(files[0]))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }

            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    LoadImageFromFile(files[0]);
                }
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void LoadImageFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    MessageBox.Show(this, "指定されたファイルが見つかりません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // ファイルロックを防止するため MemoryStream 経由で BitmapImage を作成
                var bitmap = new BitmapImage();
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                }
                bitmap.Freeze();

                int pixelWidth = bitmap.PixelWidth;
                int pixelHeight = bitmap.PixelHeight;

                // キャンバスのサイズを画像ピクセル数に合わせて設定
                ImageCanvas.Width = pixelWidth;
                ImageCanvas.Height = pixelHeight;

                CanvasImage.Width = pixelWidth;
                CanvasImage.Height = pixelHeight;
                CanvasImage.Source = bitmap;

                // 表示の切り替え
                CanvasContainer.Visibility = Visibility.Visible;
                EmptyGuidePanel.Visibility = Visibility.Collapsed;

                // タイトルとステータスバーの更新
                string fileName = Path.GetFileName(filePath);
                Title = $"{fileName} ({pixelWidth} × {pixelHeight} px) - EasyPhotoEditer";
                StatusInfoText.Text = filePath;
                CanvasSizeText.Text = $"サイズ: {pixelWidth} × {pixelHeight} px";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"画像の読み込みに失敗しました。\n\n詳細: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool IsSupportedImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".ico" or ".tif" or ".tiff" => true,
                _ => false,
            };
        }
    }
}
