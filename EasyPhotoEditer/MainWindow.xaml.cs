using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
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

    public class FontItem
    {
        public string DisplayName { get; set; } = "";
        public FontFamily FontFamily { get; set; } = null!;
        public override string ToString() => DisplayName;
    }

    public class ColorItem
    {
        public string Name { get; set; } = "";
        public SolidColorBrush Brush { get; set; } = null!;
        public override string ToString() => Name;
    }

    public class ThicknessItem
    {
        public double Value { get; set; }
        public string DisplayText => Value == 0 ? "0 px (なし)" : $"{Value} px";
        public double PreviewWidth => Value > 0 ? 20 : 0;
        public Thickness PreviewMargin => Value > 0 ? new Thickness(0, 0, 6, 0) : new Thickness(0);
        public override string ToString() => DisplayText;
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static readonly RoutedUICommand OpenCommand = new("開く", "OpenCommand", typeof(MainWindow));
        public static readonly RoutedUICommand SaveCommand = new("編集した画像を保存", "SaveCommand", typeof(MainWindow));
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
        private string? _currentLoadedFilePath;

        // 設定管理
        private AppSettings _settings;

        // Undo/Redo マネージャー
        private readonly UndoManager _undoManager = new();

        // ツールとオブジェクト編集状態
        private ToolType _currentTool = ToolType.Select;
        private EditableItem? _selectedItem;
        private bool _isDrawing;
        private Point _drawStartPoint;

        // ツールバー選択連動フラグ
        private bool _isUpdatingToolbarFromSelection;

        public MainWindow() : this(null)
        {
        }

        public MainWindow(string? initialFilePath)
        {
            InitializeComponent();
            _initialFilePath = initialFilePath;

            _settings = AppSettings.Load();

            RegisterCommands();
            InitializePropertyControls();
            ApplySettingsToState();
            UpdateToolMenuState();
            UpdateToolbarFromSelectedObject(null);
        }

        private void RegisterCommands()
        {
            CommandBindings.Add(new CommandBinding(OpenCommand, ExecuteOpenCommand));
            CommandBindings.Add(new CommandBinding(SaveCommand, ExecuteSaveCommand, CanExecuteSaveCommand));
            CommandBindings.Add(new CommandBinding(ZoomInCommand, (s, e) => ZoomRelative(ZoomStepFactor)));
            CommandBindings.Add(new CommandBinding(ZoomOutCommand, (s, e) => ZoomRelative(1.0 / ZoomStepFactor)));
            CommandBindings.Add(new CommandBinding(ResetZoomCommand, (s, e) => SetZoom(1.0)));
            CommandBindings.Add(new CommandBinding(FitToWindowCommand, (s, e) => ExecuteFitToWindow()));
            CommandBindings.Add(new CommandBinding(FitToWidthCommand, (s, e) => ExecuteFitToWidth()));

            // 元に戻す (Ctrl+Z) / やり直す (Ctrl+Y)
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (s, e) => _undoManager.Undo(), (s, e) => e.CanExecute = _undoManager.CanUndo));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (s, e) => _undoManager.Redo(), (s, e) => e.CanExecute = _undoManager.CanRedo));
            _undoManager.StateChanged += (s, e) => CommandManager.InvalidateRequerySuggested();
        }

        private void InitializePropertyControls()
        {
            _isUpdatingToolbarFromSelection = true;
            try
            {
                // 1. システムフォント一覧の取得（日本語名対応）
                var jaLang = XmlLanguage.GetLanguage("ja-jp");
                var fontItems = new List<FontItem>();
                foreach (var family in Fonts.SystemFontFamilies)
                {
                    string displayName = family.FamilyNames.TryGetValue(jaLang, out string? jaName) ? jaName : family.Source;
                    fontItems.Add(new FontItem { DisplayName = displayName, FontFamily = family });
                }
                fontItems = fontItems.OrderBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
                CbFontFamily.ItemsSource = fontItems;

                // 2. 文字サイズ一覧 (36以降も4区切りで72まで)
                double[] sizes = [9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 68, 72];
                CbFontSize.ItemsSource = sizes;

                // 3. 線の色 / 文字色一覧
                var colors = new List<ColorItem>
                {
                    new() { Name = "赤", Brush = Brushes.Red },
                    new() { Name = "青", Brush = (SolidColorBrush)new BrushConverter().ConvertFromString("#0078D7")! },
                    new() { Name = "緑", Brush = (SolidColorBrush)new BrushConverter().ConvertFromString("#107C41")! },
                    new() { Name = "黄", Brush = (SolidColorBrush)new BrushConverter().ConvertFromString("#FFB900")! },
                    new() { Name = "黒", Brush = Brushes.Black },
                    new() { Name = "白", Brush = Brushes.White },
                    new() { Name = "橙", Brush = (SolidColorBrush)new BrushConverter().ConvertFromString("#D83B01")! },
                    new() { Name = "紫", Brush = (SolidColorBrush)new BrushConverter().ConvertFromString("#881798")! },
                    new() { Name = "水色", Brush = (SolidColorBrush)new BrushConverter().ConvertFromString("#00B7C3")! }
                };
                CbStrokeColor.ItemsSource = colors;

                // 4. 線の太さ一覧 (0px, 0.5px, 1.5pxを含む)
                var thicknesses = new List<ThicknessItem>
                {
                    new() { Value = 0 },
                    new() { Value = 0.5 },
                    new() { Value = 1 },
                    new() { Value = 1.5 },
                    new() { Value = 2 },
                    new() { Value = 3 },
                    new() { Value = 4 },
                    new() { Value = 5 },
                    new() { Value = 6 },
                    new() { Value = 8 },
                    new() { Value = 10 },
                    new() { Value = 12 }
                };
                CbStrokeThickness.ItemsSource = thicknesses;
            }
            finally
            {
                _isUpdatingToolbarFromSelection = false;
            }
        }

        private void ApplySettingsToState()
        {
            UpdateToolbarFromSelectedObject(_selectedItem);
        }

        private FontFamily GetDefaultFontFamily()
        {
            if (CbFontFamily.ItemsSource is List<FontItem> fontItems)
            {
                var match = fontItems.FirstOrDefault(f => f.DisplayName == _settings.DefaultFontFamily || f.FontFamily.Source == _settings.DefaultFontFamily)
                            ?? fontItems.FirstOrDefault(f => f.DisplayName is "メイリオ" or "Yu Gothic UI")
                            ?? fontItems.FirstOrDefault();
                if (match != null)
                {
                    return match.FontFamily;
                }
            }
            return new FontFamily("Yu Gothic UI");
        }

        private SolidColorBrush GetDefaultTextColorBrush()
        {
            return FindColorBrushByName(_settings.DefaultTextColor) ?? Brushes.Red;
        }

        private SolidColorBrush GetDefaultShapeColorBrush()
        {
            return FindColorBrushByName(_settings.DefaultShapeColor) ?? Brushes.Red;
        }

        private SolidColorBrush? FindColorBrushByName(string name)
        {
            if (CbStrokeColor.ItemsSource is List<ColorItem> colors)
            {
                return colors.FirstOrDefault(c => c.Name == name)?.Brush;
            }
            return null;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_initialFilePath) && File.Exists(_initialFilePath))
            {
                LoadImageFromFile(_initialFilePath);
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (_settings.ConfirmOnExit)
            {
                var dialog = new ConfirmExitDialog { Owner = this };
                if (dialog.ShowDialog() != true)
                {
                    e.Cancel = true;
                }
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
                if (CanvasContainer != null && CanvasContainer.Visibility == Visibility.Visible)
                {
                    // 既存の編集内容を保持するため、新しいウィンドウで開く
                    var newWindow = new MainWindow(dialog.FileName);
                    newWindow.Show();
                }
                else
                {
                    LoadImageFromFile(dialog.FileName);
                }
            }
        }

        private void CanExecuteSaveCommand(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = CanvasContainer != null && CanvasContainer.Visibility == Visibility.Visible;
        }

        private void ExecuteSaveCommand(object sender, ExecutedRoutedEventArgs e)
        {
            string initialDir = !string.IsNullOrEmpty(_currentLoadedFilePath)
                ? Path.GetDirectoryName(_currentLoadedFilePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

            string initialFileName = !string.IsNullOrEmpty(_currentLoadedFilePath)
                ? Path.GetFileNameWithoutExtension(_currentLoadedFilePath) + "_edited.png"
                : "edited_image.png";

            var dialog = new SaveFileDialog
            {
                Title = "編集した画像を保存",
                Filter = "PNG画像 (*.png)|*.png",
                DefaultExt = "png",
                InitialDirectory = initialDir,
                FileName = initialFileName
            };

            if (dialog.ShowDialog(this) == true)
            {
                SaveCanvasToPng(dialog.FileName);
            }
        }

        private void SaveCanvasToPng(string filePath)
        {
            try
            {
                int pixelWidth = (int)Math.Round(ImageCanvas.Width);
                int pixelHeight = (int)Math.Round(ImageCanvas.Height);

                if (pixelWidth <= 0 || pixelHeight <= 0)
                {
                    MessageBox.Show(this, "保存対象の画像サイズが無効です。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 1. レンダリング時の映り込みを防ぐため、選択枠とラバーバンドを一時退避
                var previouslySelectedItem = _selectedItem;
                if (_selectedItem != null)
                {
                    _selectedItem.IsSelected = false;
                }
                var previousRubberBandVisibility = RubberBand.Visibility;
                RubberBand.Visibility = Visibility.Collapsed;

                ImageCanvas.UpdateLayout();

                RenderTargetBitmap rtb;
                try
                {
                    // 2. 96 DPI・元画像ピクセル解像度固定で DrawingVisual を使用してレンダリング
                    // VisualBrush を使用することで、ImageCanvas の親の LayoutTransform (ズーム) や
                    // システムの DPI スケーリング設定に一切左右されず、(0, 0, pixelWidth, pixelHeight) の等倍矩形として描画される
                    var drawingVisual = new DrawingVisual();
                    using (var dc = drawingVisual.RenderOpen())
                    {
                        var visualBrush = new VisualBrush(ImageCanvas)
                        {
                            Stretch = Stretch.None,
                            Viewbox = new Rect(0, 0, pixelWidth, pixelHeight),
                            ViewboxUnits = BrushMappingMode.Absolute,
                            Viewport = new Rect(0, 0, pixelWidth, pixelHeight),
                            ViewportUnits = BrushMappingMode.Absolute
                        };
                        dc.DrawRectangle(visualBrush, null, new Rect(0, 0, pixelWidth, pixelHeight));
                    }

                    rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(drawingVisual);
                }
                finally
                {
                    // 3. 選択状態およびラバーバンドの表示を復元
                    if (previouslySelectedItem != null)
                    {
                        previouslySelectedItem.IsSelected = true;
                    }
                    RubberBand.Visibility = previousRubberBandVisibility;
                }

                // 4. PNGエンコーダーで書き出し
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));

                using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    encoder.Save(stream);
                }

                _currentLoadedFilePath = filePath;
                CommandManager.InvalidateRequerySuggested();

                string fileName = Path.GetFileName(filePath);
                StatusInfoText.Text = $"画像を保存しました: {filePath}";
                Title = $"{fileName} ({pixelWidth} × {pixelHeight} px) - EasyPhotoEditer";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"画像の保存に失敗しました。\n\n詳細: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
                if (files != null && files.Length > 0 && IsSupportedImageFile(files[0]))
                {
                    string filePath = files[0];
                    if (CanvasContainer != null && CanvasContainer.Visibility == Visibility.Visible)
                    {
                        if (_settings.DragDropBehavior == DragDropBehavior.OpenNew)
                        {
                            var newWindow = new MainWindow(filePath);
                            newWindow.Show();
                        }
                        else
                        {
                            // TODO: 画像を編集中の画像に合成する機能の実装
                        }
                    }
                    else
                    {
                        LoadImageFromFile(filePath);
                    }
                }
            }
        }

        private void Options_Click(object sender, RoutedEventArgs e)
        {
            var optionsWindow = new OptionsWindow(_settings) { Owner = this };
            if (optionsWindow.ShowDialog() == true)
            {
                _settings = optionsWindow.CurrentSettings;
                _settings.Save();
                ApplySettingsToState();
                UpdateToolbarFromSelectedObject(_selectedItem);
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

                // 既存のオブジェクト（EditableItem）とUndo履歴をクリア
                var itemsToRemove = ImageCanvas.Children.OfType<EditableItem>().ToList();
                foreach (var item in itemsToRemove)
                {
                    ImageCanvas.Children.Remove(item);
                }
                _selectedItem = null;
                _undoManager.Clear();

                ImageCanvas.Width = pixelWidth;
                ImageCanvas.Height = pixelHeight;

                CanvasImage.Width = pixelWidth;
                CanvasImage.Height = pixelHeight;
                CanvasImage.Source = bitmap;

                CanvasContainer.Visibility = Visibility.Visible;
                EmptyGuidePanel.Visibility = Visibility.Collapsed;

                SetZoom(1.0);
                SetCurrentTool(ToolType.Select);
                UpdateToolbarFromSelectedObject(null);

                _currentLoadedFilePath = filePath;
                CommandManager.InvalidateRequerySuggested();

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

        private void ToolItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tag)
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
            // メニューバー項目の更新
            MenuToolSelect.IsChecked = _currentTool == ToolType.Select;
            MenuToolTextBox.IsChecked = _currentTool == ToolType.TextBox;
            MenuToolCircle.IsChecked = _currentTool == ToolType.Circle;
            MenuToolRectangle.IsChecked = _currentTool == ToolType.Rectangle;
            MenuToolRoundedRectangle.IsChecked = _currentTool == ToolType.RoundedRectangle;
            MenuToolCheckmark.IsChecked = _currentTool == ToolType.Checkmark;

            // ツールバーボタン項目の更新
            if (TbToolSelect != null) TbToolSelect.IsChecked = _currentTool == ToolType.Select;
            if (TbToolTextBox != null) TbToolTextBox.IsChecked = _currentTool == ToolType.TextBox;
            if (TbToolCircle != null) TbToolCircle.IsChecked = _currentTool == ToolType.Circle;
            if (TbToolRectangle != null) TbToolRectangle.IsChecked = _currentTool == ToolType.Rectangle;
            if (TbToolRoundedRectangle != null) TbToolRoundedRectangle.IsChecked = _currentTool == ToolType.RoundedRectangle;
            if (TbToolCheckmark != null) TbToolCheckmark.IsChecked = _currentTool == ToolType.Checkmark;

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

                // 意図しない微小ドラッグを防ぎ、十分な大きさがあれば作成
                if (width >= 10 && height >= 10)
                {
                    CreateAndInsertObject(_currentTool, x, y, width, height);
                }
                else if (_currentTool == ToolType.Circle)
                {
                    // 円ツールでクリック操作（または微小移動）された場合: 規定サイズ（直径80px）の真円を生成
                    const double circleSize = 80.0;
                    double circleLeft = Math.Clamp(_drawStartPoint.X - circleSize / 2, 0, Math.Max(0, ImageCanvas.Width - circleSize));
                    double circleTop = Math.Clamp(_drawStartPoint.Y - circleSize / 2, 0, Math.Max(0, ImageCanvas.Height - circleSize));
                    CreateAndInsertObject(ToolType.Circle, circleLeft, circleTop, circleSize, circleSize);
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
                    var textFont = GetDefaultFontFamily();
                    var textColor = GetDefaultTextColorBrush();
                    textControl = new TextBox
                    {
                        Text = "テキストを入力",
                        Foreground = textColor,
                        Background = Brushes.Transparent,
                        BorderBrush = textColor,
                        BorderThickness = new Thickness(_settings.DefaultStrokeThickness),
                        FontSize = _settings.DefaultFontSize,
                        FontFamily = textFont,
                        AcceptsReturn = true,
                        TextWrapping = TextWrapping.Wrap,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                    };
                    item.ItemContent = textControl;

                    // テキスト変更時の Undo/Redo 追跡
                    string textBefore = textControl.Text;
                    textControl.GotFocus += (s, e) => textBefore = textControl.Text;
                    textControl.LostFocus += (s, e) =>
                    {
                        if (textControl.Text != textBefore)
                        {
                            _undoManager.Record(new TextChangedAction(textControl, textBefore, textControl.Text));
                            textBefore = textControl.Text;
                        }
                    };
                    break;

                case ToolType.Circle:
                    item.ItemContent = new Ellipse
                    {
                        Stroke = GetDefaultShapeColorBrush(),
                        StrokeThickness = _settings.DefaultStrokeThickness,
                        Fill = Brushes.Transparent
                    };
                    break;

                case ToolType.Rectangle:
                    item.ItemContent = new System.Windows.Shapes.Rectangle
                    {
                        Stroke = GetDefaultShapeColorBrush(),
                        StrokeThickness = _settings.DefaultStrokeThickness,
                        Fill = Brushes.Transparent
                    };
                    break;

                case ToolType.RoundedRectangle:
                    item.ItemContent = new System.Windows.Shapes.Rectangle
                    {
                        Stroke = GetDefaultShapeColorBrush(),
                        StrokeThickness = _settings.DefaultStrokeThickness,
                        Fill = Brushes.Transparent,
                        RadiusX = 16,
                        RadiusY = 16
                    };
                    break;

                case ToolType.Checkmark:
                    var path = new WpfPath
                    {
                        Data = Geometry.Parse("M 4,22 L 15,33 L 38,8"),
                        Stroke = GetDefaultShapeColorBrush(),
                        StrokeThickness = _settings.DefaultStrokeThickness,
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
            item.TransformCompleted += (s, e) => _undoManager.Record(new TransformObjectAction(item, e.Before, e.After, SelectItem));

            ImageCanvas.Children.Add(item);

            // Undo にオブジェクト追加アクションを記録
            _undoManager.Record(new AddObjectAction(ImageCanvas, item, SelectItem));

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

        private void SelectItem(EditableItem? item)
        {
            if (item == null)
            {
                DeselectAll();
                return;
            }

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

            UpdateToolbarFromSelectedObject(_selectedItem);
        }

        private void DeselectAll()
        {
            if (_selectedItem != null)
            {
                _selectedItem.IsSelected = false;
                _selectedItem = null;
                UpdateToolbarFromSelectedObject(null);
            }
        }

        private void DeleteSelectedItem()
        {
            if (_selectedItem != null)
            {
                var itemToDelete = _selectedItem;
                _undoManager.Record(new DeleteObjectAction(ImageCanvas, itemToDelete, SelectItem));
                ImageCanvas.Children.Remove(itemToDelete);
                _selectedItem = null;
                UpdateToolbarFromSelectedObject(null);
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

        #region ツールバープロパティ連動処理

        private void UpdateToolbarFromSelectedObject(EditableItem? item)
        {
            _isUpdatingToolbarFromSelection = true;
            try
            {
                if (item == null)
                {
                    // 図形未選択時: すべて無効化（操作不可）
                    CbFontFamily.IsEnabled = false;
                    CbFontSize.IsEnabled = false;
                    CbStrokeColor.IsEnabled = false;
                    CbStrokeThickness.IsEnabled = false;

                    // 表示はオプション設定の既定値
                    SelectFontInComboBox(GetDefaultFontFamily());
                    CbFontSize.Text = _settings.DefaultFontSize.ToString("0");
                    SelectColorInComboBox(GetDefaultShapeColorBrush());
                    SelectThicknessInComboBox(_settings.DefaultStrokeThickness);
                    return;
                }

                if (item.ItemContent is TextBox tb)
                {
                    // テキストボックス選択時: すべて有効
                    CbFontFamily.IsEnabled = true;
                    CbFontSize.IsEnabled = true;
                    CbStrokeColor.IsEnabled = true;
                    CbStrokeThickness.IsEnabled = true;

                    SelectFontInComboBox(tb.FontFamily);
                    CbFontSize.Text = tb.FontSize.ToString("0");
                    if (tb.Foreground is SolidColorBrush sb)
                    {
                        SelectColorInComboBox(sb);
                    }
                    SelectThicknessInComboBox(tb.BorderThickness.Top);
                }
                else if (item.ItemContent is Shape shape)
                {
                    // 通常図形選択時: 色と太さのみ有効
                    CbFontFamily.IsEnabled = false;
                    CbFontSize.IsEnabled = false;
                    CbStrokeColor.IsEnabled = true;
                    CbStrokeThickness.IsEnabled = true;

                    if (shape.Stroke is SolidColorBrush sb)
                    {
                        SelectColorInComboBox(sb);
                    }
                    SelectThicknessInComboBox(shape.StrokeThickness);
                }
                else if (item.ItemContent is Viewbox vb && vb.Child is WpfPath path)
                {
                    // チェックマーク選択時: 色と太さのみ有効
                    CbFontFamily.IsEnabled = false;
                    CbFontSize.IsEnabled = false;
                    CbStrokeColor.IsEnabled = true;
                    CbStrokeThickness.IsEnabled = true;

                    if (path.Stroke is SolidColorBrush sb)
                    {
                        SelectColorInComboBox(sb);
                    }
                    SelectThicknessInComboBox(path.StrokeThickness);
                }
            }
            finally
            {
                _isUpdatingToolbarFromSelection = false;
            }
        }

        private void SelectFontInComboBox(FontFamily fontFamily)
        {
            if (CbFontFamily.ItemsSource is List<FontItem> items)
            {
                var match = items.FirstOrDefault(i => i.FontFamily.Source == fontFamily.Source);
                if (match != null)
                {
                    CbFontFamily.SelectedItem = match;
                }
            }
        }

        private void SelectColorInComboBox(SolidColorBrush brush)
        {
            if (CbStrokeColor.ItemsSource is List<ColorItem> items)
            {
                var match = items.FirstOrDefault(c => c.Brush.Color == brush.Color);
                if (match != null)
                {
                    CbStrokeColor.SelectedItem = match;
                }
            }
        }

        private void SelectThicknessInComboBox(double thickness)
        {
            if (CbStrokeThickness.ItemsSource is List<ThicknessItem> items)
            {
                var match = items.FirstOrDefault(t => Math.Abs(t.Value - thickness) < 0.05);
                if (match != null)
                {
                    CbStrokeThickness.SelectedItem = match;
                }
            }
        }

        private void CbFontFamily_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingToolbarFromSelection) return;

            if (_selectedItem?.ItemContent is TextBox tb && CbFontFamily.SelectedItem is FontItem fi)
            {
                if (tb.FontFamily.Source != fi.FontFamily.Source)
                {
                    var oldFont = tb.FontFamily;
                    var newFont = fi.FontFamily;
                    _undoManager.Record(new StyleChangeAction(
                        () => { tb.FontFamily = oldFont; if (_selectedItem?.ItemContent == tb) SelectFontInComboBox(oldFont); },
                        () => { tb.FontFamily = newFont; if (_selectedItem?.ItemContent == tb) SelectFontInComboBox(newFont); }
                    ));
                    tb.FontFamily = newFont;
                }
            }
        }

        private void CbFontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingToolbarFromSelection) return;

            if (CbFontSize.SelectedItem is double size)
            {
                ApplyFontSize(size);
            }
        }

        private void CbFontSize_LostFocus(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingToolbarFromSelection) return;

            if (double.TryParse(CbFontSize.Text, out double size))
            {
                size = Math.Clamp(size, 6, 200);
                ApplyFontSize(size);
            }
        }

        private void CbFontSize_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CbFontSize_LostFocus(sender, e);
            }
        }

        private void ApplyFontSize(double size)
        {
            if (_selectedItem?.ItemContent is TextBox tb && Math.Abs(tb.FontSize - size) > 0.1)
            {
                var oldSize = tb.FontSize;
                var newSize = size;
                _undoManager.Record(new StyleChangeAction(
                    () => { tb.FontSize = oldSize; if (_selectedItem?.ItemContent == tb) CbFontSize.Text = oldSize.ToString("0"); },
                    () => { tb.FontSize = newSize; if (_selectedItem?.ItemContent == tb) CbFontSize.Text = newSize.ToString("0"); }
                ));
                tb.FontSize = newSize;
            }
        }

        private void CbStrokeColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingToolbarFromSelection) return;

            if (_selectedItem != null && CbStrokeColor.SelectedItem is ColorItem ci)
            {
                if (_selectedItem.ItemContent is TextBox tb)
                {
                    var oldFg = tb.Foreground;
                    var oldBc = tb.BorderBrush;
                    var newBrush = ci.Brush;
                    _undoManager.Record(new StyleChangeAction(
                        () => { tb.Foreground = oldFg; tb.BorderBrush = oldBc; if (_selectedItem?.ItemContent == tb && oldFg is SolidColorBrush sb) SelectColorInComboBox(sb); },
                        () => { tb.Foreground = newBrush; tb.BorderBrush = newBrush; if (_selectedItem?.ItemContent == tb) SelectColorInComboBox(newBrush); }
                    ));
                    tb.Foreground = newBrush;
                    tb.BorderBrush = newBrush;
                }
                else if (_selectedItem.ItemContent is Shape shape)
                {
                    var oldStroke = shape.Stroke;
                    var newBrush = ci.Brush;
                    _undoManager.Record(new StyleChangeAction(
                        () => { shape.Stroke = oldStroke; if (_selectedItem?.ItemContent == shape && oldStroke is SolidColorBrush sb) SelectColorInComboBox(sb); },
                        () => { shape.Stroke = newBrush; if (_selectedItem?.ItemContent == shape) SelectColorInComboBox(newBrush); }
                    ));
                    shape.Stroke = newBrush;
                }
                else if (_selectedItem.ItemContent is Viewbox vb && vb.Child is WpfPath path)
                {
                    var oldStroke = path.Stroke;
                    var newBrush = ci.Brush;
                    _undoManager.Record(new StyleChangeAction(
                        () => { path.Stroke = oldStroke; if (_selectedItem?.ItemContent == vb && oldStroke is SolidColorBrush sb) SelectColorInComboBox(sb); },
                        () => { path.Stroke = newBrush; if (_selectedItem?.ItemContent == vb) SelectColorInComboBox(newBrush); }
                    ));
                    path.Stroke = newBrush;
                }
            }
        }

        private void CbStrokeThickness_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingToolbarFromSelection) return;

            if (_selectedItem != null && CbStrokeThickness.SelectedItem is ThicknessItem ti)
            {
                if (_selectedItem.ItemContent is TextBox tb)
                {
                    var oldThick = tb.BorderThickness;
                    var newThick = new Thickness(ti.Value);
                    _undoManager.Record(new StyleChangeAction(
                        () => { tb.BorderThickness = oldThick; if (_selectedItem?.ItemContent == tb) SelectThicknessInComboBox(oldThick.Top); },
                        () => { tb.BorderThickness = newThick; if (_selectedItem?.ItemContent == tb) SelectThicknessInComboBox(newThick.Top); }
                    ));
                    tb.BorderThickness = newThick;
                }
                else if (_selectedItem.ItemContent is Shape shape)
                {
                    var oldThick = shape.StrokeThickness;
                    var newThick = ti.Value;
                    _undoManager.Record(new StyleChangeAction(
                        () => { shape.StrokeThickness = oldThick; if (_selectedItem?.ItemContent == shape) SelectThicknessInComboBox(oldThick); },
                        () => { shape.StrokeThickness = newThick; if (_selectedItem?.ItemContent == shape) SelectThicknessInComboBox(newThick); }
                    ));
                    shape.StrokeThickness = newThick;
                }
                else if (_selectedItem.ItemContent is Viewbox vb && vb.Child is WpfPath path)
                {
                    var oldThick = path.StrokeThickness;
                    var newThick = ti.Value;
                    _undoManager.Record(new StyleChangeAction(
                        () => { path.StrokeThickness = oldThick; if (_selectedItem?.ItemContent == vb) SelectThicknessInComboBox(oldThick); },
                        () => { path.StrokeThickness = newThick; if (_selectedItem?.ItemContent == vb) SelectThicknessInComboBox(newThick); }
                    ));
                    path.StrokeThickness = newThick;
                }
            }
        }

        #endregion
    }
}
