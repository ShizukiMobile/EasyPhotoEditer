using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace EasyPhotoEditer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static readonly RoutedUICommand OpenCommand = new("開く", "OpenCommand", typeof(MainWindow));
        public static readonly RoutedUICommand ZoomInCommand = new("ズームイン", "ZoomInCommand", typeof(MainWindow));
        public static readonly RoutedUICommand ZoomOutCommand = new("ズームアウト", "ZoomOutCommand", typeof(MainWindow));
        public static readonly RoutedUICommand ResetZoomCommand = new("100% に戻す", "ResetZoomCommand", typeof(MainWindow));
        public static readonly RoutedUICommand FitToWindowCommand = new("ウィンドウに合わせる", "FitToWindowCommand", typeof(MainWindow));
        public static readonly RoutedUICommand FitToWidthCommand = new("ウィンドウの横幅に合わせる", "FitToWidthCommand", typeof(MainWindow));

        private const double MinZoom = 0.1;    // 10%
        private const double MaxZoom = 32.0;   // 3200%
        private const double ZoomStepFactor = 1.25;

        private double _currentZoom = 1.0;
        private readonly string? _initialFilePath;

        public MainWindow() : this(null)
        {
        }

        public MainWindow(string? initialFilePath)
        {
            InitializeComponent();
            _initialFilePath = initialFilePath;

            RegisterCommands();
        }

        private void RegisterCommands()
        {
            CommandBindings.Add(new CommandBinding(OpenCommand, ExecuteOpenCommand));
            CommandBindings.Add(new CommandBinding(ZoomInCommand, (s, e) => ZoomRelative(ZoomStepFactor)));
            CommandBindings.Add(new CommandBinding(ZoomOutCommand, (s, e) => ZoomRelative(1.0 / ZoomStepFactor)));
            CommandBindings.Add(new CommandBinding(ResetZoomCommand, (s, e) => SetZoom(1.0)));
            CommandBindings.Add(new CommandBinding(FitToWindowCommand, (s, e) => ExecuteFitToWindow()));
            CommandBindings.Add(new CommandBinding(FitToWidthCommand, (s, e) => ExecuteFitToWidth()));
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

        private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // 要求仕様: 「Ctrl + ホイール」でズーム
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;

                if (CanvasContainer.Visibility != Visibility.Visible)
                {
                    return;
                }

                double factor = e.Delta > 0 ? ZoomStepFactor : (1.0 / ZoomStepFactor);
                Point mousePos = e.GetPosition(MainScrollViewer);
                ZoomAt(mousePos, _currentZoom * factor);
            }
        }

        private void ZoomRelative(double factor)
        {
            if (CanvasContainer.Visibility != Visibility.Visible)
            {
                return;
            }

            // メニュー等からの操作はScrollViewerの中心を基準にズーム
            Point center = new(MainScrollViewer.ViewportWidth / 2, MainScrollViewer.ViewportHeight / 2);
            ZoomAt(center, _currentZoom * factor);
        }

        private void ZoomAt(Point centerInViewer, double targetZoom)
        {
            targetZoom = Math.Clamp(targetZoom, MinZoom, MaxZoom);
            if (Math.Abs(_currentZoom - targetZoom) < 0.0001)
            {
                return;
            }

            // マウス位置が指すコンテンツ上の絶対座標（ズーム1.0換算）
            double contentX = (centerInViewer.X + MainScrollViewer.HorizontalOffset) / _currentZoom;
            double contentY = (centerInViewer.Y + MainScrollViewer.VerticalOffset) / _currentZoom;

            SetZoom(targetZoom);
            MainScrollViewer.UpdateLayout();

            // ズーム後も同じコンテンツ位置がマウス位置に来るようスクロール位置を調整
            double newOffsetX = contentX * _currentZoom - centerInViewer.X;
            double newOffsetY = contentY * _currentZoom - centerInViewer.Y;
            MainScrollViewer.ScrollToHorizontalOffset(newOffsetX);
            MainScrollViewer.ScrollToVerticalOffset(newOffsetY);
        }

        private void SetZoom(double zoom)
        {
            _currentZoom = Math.Clamp(zoom, MinZoom, MaxZoom);

            CanvasScaleTransform.ScaleX = _currentZoom;
            CanvasScaleTransform.ScaleY = _currentZoom;

            // 等倍以上はドットをくっきり表示（NearestNeighbor）、縮小時は滑らかに表示（HighQuality）
            RenderOptions.SetBitmapScalingMode(CanvasImage, _currentZoom >= 1.0 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);

            ZoomPercentText.Text = $"ズーム: {Math.Round(_currentZoom * 100):0}%";
        }

        private void ExecuteFitToWindow()
        {
            if (CanvasContainer.Visibility != Visibility.Visible || ImageCanvas.Width <= 0 || ImageCanvas.Height <= 0)
            {
                return;
            }

            // スクロールバー余白などを考慮した表示可能サイズ
            double availableWidth = MainScrollViewer.ViewportWidth > 40 ? MainScrollViewer.ViewportWidth - 40 : MainScrollViewer.ActualWidth - 40;
            double availableHeight = MainScrollViewer.ViewportHeight > 40 ? MainScrollViewer.ViewportHeight - 40 : MainScrollViewer.ActualHeight - 40;

            if (availableWidth > 0 && availableHeight > 0)
            {
                double scaleX = availableWidth / ImageCanvas.Width;
                double scaleY = availableHeight / ImageCanvas.Height;
                SetZoom(Math.Min(scaleX, scaleY));
            }
        }

        private void ExecuteFitToWidth()
        {
            if (CanvasContainer.Visibility != Visibility.Visible || ImageCanvas.Width <= 0)
            {
                return;
            }

            double availableWidth = MainScrollViewer.ViewportWidth > 40 ? MainScrollViewer.ViewportWidth - 40 : MainScrollViewer.ActualWidth - 40;
            if (availableWidth > 0)
            {
                SetZoom(availableWidth / ImageCanvas.Width);
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

                ImageCanvas.Width = pixelWidth;
                ImageCanvas.Height = pixelHeight;

                CanvasImage.Width = pixelWidth;
                CanvasImage.Height = pixelHeight;
                CanvasImage.Source = bitmap;

                CanvasContainer.Visibility = Visibility.Visible;
                EmptyGuidePanel.Visibility = Visibility.Collapsed;

                // 画像ロード時は 100% ズームにリセット
                SetZoom(1.0);

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
