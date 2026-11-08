using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using Path = System.IO.Path;
using WpfPath = System.Windows.Shapes.Path;

namespace EasyPhotoEditer
{
    public enum ToolType
    {
        Select,
        TextBox,
        Circle,
        Rectangle,
        RoundedRectangle,
        Checkmark
    }

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

        // ツールとオブジェクト編集状態
        private ToolType _currentTool = ToolType.Select;
        private EditableItem? _selectedItem;
        private bool _isDrawing;
        private Point _drawStartPoint;

        public MainWindow() : this(null)
        {
        }

        public MainWindow(string? initialFilePath)
        {
            InitializeComponent();
            _initialFilePath = initialFilePath;

            RegisterCommands();
            UpdateToolMenuState();
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

        #region 画像ロード・D&D処理

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

                // 既存のオブジェクト（EditableItem）をクリア
                var itemsToRemove = ImageCanvas.Children.OfType<EditableItem>().ToList();
                foreach (var item in itemsToRemove)
                {
                    ImageCanvas.Children.Remove(item);
                }
                _selectedItem = null;

                ImageCanvas.Width = pixelWidth;
                ImageCanvas.Height = pixelHeight;

                CanvasImage.Width = pixelWidth;
                CanvasImage.Height = pixelHeight;
                CanvasImage.Source = bitmap;

                CanvasContainer.Visibility = Visibility.Visible;
                EmptyGuidePanel.Visibility = Visibility.Collapsed;

                SetZoom(1.0);
                SetCurrentTool(ToolType.Select);

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

        #endregion

        #region ズーム処理

        private void MainScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
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

            double contentX = (centerInViewer.X + MainScrollViewer.HorizontalOffset) / _currentZoom;
            double contentY = (centerInViewer.Y + MainScrollViewer.VerticalOffset) / _currentZoom;

            SetZoom(targetZoom);
            MainScrollViewer.UpdateLayout();

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

            RenderOptions.SetBitmapScalingMode(CanvasImage, _currentZoom >= 1.0 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);

            ZoomPercentText.Text = $"ズーム: {Math.Round(_currentZoom * 100):0}%";
        }

        private void ExecuteFitToWindow()
        {
            if (CanvasContainer.Visibility != Visibility.Visible || ImageCanvas.Width <= 0 || ImageCanvas.Height <= 0)
            {
                return;
            }

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

        #endregion

        #region ツール選択・図形挿入・編集

        private void MenuTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string tag)
            {
                if (Enum.TryParse<ToolType>(tag, out var tool))
                {
                    SetCurrentTool(tool);
                }
            }
        }

        private void SetCurrentTool(ToolType tool)
        {
            _currentTool = tool;
            UpdateToolMenuState();

            // ツール変更時のカーソル設定
            ImageCanvas.Cursor = tool == ToolType.Select ? Cursors.Arrow : Cursors.Cross;

            // 挿入ツールに切り替えた際は既存オブジェクトの選択を解除
            if (tool != ToolType.Select)
            {
                DeselectAll();
            }
        }

        private void UpdateToolMenuState()
        {
            MenuToolSelect.IsChecked = _currentTool == ToolType.Select;
            MenuToolTextBox.IsChecked = _currentTool == ToolType.TextBox;
            MenuToolCircle.IsChecked = _currentTool == ToolType.Circle;
            MenuToolRectangle.IsChecked = _currentTool == ToolType.Rectangle;
            MenuToolRoundedRectangle.IsChecked = _currentTool == ToolType.RoundedRectangle;
            MenuToolCheckmark.IsChecked = _currentTool == ToolType.Checkmark;

            string toolName = _currentTool switch
            {
                ToolType.Select => "選択",
                ToolType.TextBox => "テキストボックス",
                ToolType.Circle => "丸（円）",
                ToolType.Rectangle => "四角形",
                ToolType.RoundedRectangle => "角丸四角形",
                ToolType.Checkmark => "チェックマーク",
                _ => "選択"
            };
            CurrentToolText.Text = $"ツール: {toolName}";
        }

        private void ImageCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (CanvasContainer.Visibility != Visibility.Visible)
            {
                return;
            }

            Point clickPoint = e.GetPosition(ImageCanvas);

            if (_currentTool == ToolType.Checkmark)
            {
                // チェックマーク: クリックで位置を指定して挿入（デフォルトサイズ 48x48）
                const double checkSize = 48.0;
                double left = Math.Clamp(clickPoint.X - checkSize / 2, 0, Math.Max(0, ImageCanvas.Width - checkSize));
                double top = Math.Clamp(clickPoint.Y - checkSize / 2, 0, Math.Max(0, ImageCanvas.Height - checkSize));

                CreateAndInsertObject(ToolType.Checkmark, left, top, checkSize, checkSize);
                SetCurrentTool(ToolType.Select);
                e.Handled = true;
                return;
            }

            if (_currentTool != ToolType.Select)
            {
                // 前4つ（テキストボックス、丸、四角形、角丸四角形）: ドラッグ開始
                _isDrawing = true;
                _drawStartPoint = clickPoint;

                Canvas.SetLeft(RubberBand, _drawStartPoint.X);
                Canvas.SetTop(RubberBand, _drawStartPoint.Y);
                RubberBand.Width = 0;
                RubberBand.Height = 0;
                RubberBand.Visibility = Visibility.Visible;

                ImageCanvas.CaptureMouse();
                e.Handled = true;
                return;
            }

            // 選択モードでキャンバス背景（画像またはキャンバス余白）をクリックした場合のみ選択解除
            if (e.OriginalSource == ImageCanvas || e.OriginalSource == CanvasImage)
            {
                DeselectAll();
            }
        }

        private void ImageCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDrawing)
            {
                Point currentPoint = e.GetPosition(ImageCanvas);
                double x = Math.Min(_drawStartPoint.X, currentPoint.X);
                double y = Math.Min(_drawStartPoint.Y, currentPoint.Y);
                double width = Math.Abs(currentPoint.X - _drawStartPoint.X);
                double height = Math.Abs(currentPoint.Y - _drawStartPoint.Y);

                Canvas.SetLeft(RubberBand, x);
                Canvas.SetTop(RubberBand, y);
                RubberBand.Width = width;
                RubberBand.Height = height;
            }
        }

        private void ImageCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDrawing)
            {
                _isDrawing = false;
                ImageCanvas.ReleaseMouseCapture();
                RubberBand.Visibility = Visibility.Collapsed;

                Point currentPoint = e.GetPosition(ImageCanvas);
                double x = Math.Min(_drawStartPoint.X, currentPoint.X);
                double y = Math.Min(_drawStartPoint.Y, currentPoint.Y);
                double width = Math.Abs(currentPoint.X - _drawStartPoint.X);
                double height = Math.Abs(currentPoint.Y - _drawStartPoint.Y);

                // 意図しない微小クリックを防ぎ、十分な大きさがあれば作成
                if (width >= 10 && height >= 10)
                {
                    CreateAndInsertObject(_currentTool, x, y, width, height);
                }

                // 挿入後は選択ツールに戻す
                SetCurrentTool(ToolType.Select);
                e.Handled = true;
            }
        }

        private void CreateAndInsertObject(ToolType tool, double x, double y, double width, double height)
        {
            var item = new EditableItem
            {
                Width = width,
                Height = height
            };

            Canvas.SetLeft(item, x);
            Canvas.SetTop(item, y);

            TextBox? textControl = null;

            switch (tool)
            {
                case ToolType.TextBox:
                    item.IsTextBox = true;
                    textControl = new TextBox
                    {
                        Text = "テキストを入力",
                        Foreground = Brushes.Red,
                        Background = Brushes.Transparent,
                        BorderBrush = Brushes.Red,
                        BorderThickness = new Thickness(1.5),
                        FontSize = 18,
                        FontFamily = new FontFamily("Yu Gothic UI, Segoe UI, Meiryo, sans-serif"),
                        AcceptsReturn = true,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    };
                    item.ItemContent = textControl;
                    break;

                case ToolType.Circle:
                    item.ItemContent = new Ellipse
                    {
                        Stroke = Brushes.Red,
                        StrokeThickness = 3,
                        Fill = Brushes.Transparent
                    };
                    break;

                case ToolType.Rectangle:
                    item.ItemContent = new System.Windows.Shapes.Rectangle
                    {
                        Stroke = Brushes.Red,
                        StrokeThickness = 3,
                        Fill = Brushes.Transparent
                    };
                    break;

                case ToolType.RoundedRectangle:
                    item.ItemContent = new System.Windows.Shapes.Rectangle
                    {
                        Stroke = Brushes.Red,
                        StrokeThickness = 3,
                        Fill = Brushes.Transparent,
                        RadiusX = 16,
                        RadiusY = 16
                    };
                    break;

                case ToolType.Checkmark:
                    var path = new WpfPath
                    {
                        Data = Geometry.Parse("M 4,22 L 15,33 L 38,8"),
                        Stroke = Brushes.Red,
                        StrokeThickness = 4.5,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        StrokeLineJoin = PenLineJoin.Round
                    };
                    item.ItemContent = new Viewbox
                    {
                        Stretch = Stretch.Uniform,
                        Child = path
                    };
                    break;
            }

            item.Selected += (s, e) => SelectItem(item);
            ImageCanvas.Children.Add(item);

            // 挿入直後に選択状態にする
            SelectItem(item);

            // テキストボックスの場合は即座に入力可能にする
            if (textControl != null)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    textControl.Focus();
                    textControl.SelectAll();
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void SelectItem(EditableItem item)
        {
            if (_selectedItem == item)
            {
                return;
            }

            if (_selectedItem != null)
            {
                _selectedItem.IsSelected = false;
            }

            _selectedItem = item;
            _selectedItem.IsSelected = true;
        }

        private void DeselectAll()
        {
            if (_selectedItem != null)
            {
                _selectedItem.IsSelected = false;
                _selectedItem = null;
            }
        }

        private void DeleteSelectedItem()
        {
            if (_selectedItem != null)
            {
                ImageCanvas.Children.Remove(_selectedItem);
                _selectedItem = null;
            }
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedItem();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                // テキストボックス入力中は文字削除を優先する
                if (Keyboard.FocusedElement is TextBox)
                {
                    return;
                }

                if (_selectedItem != null)
                {
                    DeleteSelectedItem();
                    e.Handled = true;
                }
            }
        }

        #endregion
    }
}
