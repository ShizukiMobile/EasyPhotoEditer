using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
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
        public static readonly RoutedUICommand InsertImageCommand = new("画像の挿入", "InsertImageCommand", typeof(MainWindow));
        public static readonly RoutedUICommand DuplicateCommand = new("複製", "DuplicateCommand", typeof(MainWindow));
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

        // 方向キー移動のUndo管理用
        private EditableItem? _arrowMovingItem;
        private Rect _arrowMoveStartRect;

        // ツールバー選択連動フラグ
        private bool _isUpdatingToolbarFromSelection;

        // 自動バックアップ（オートセーブ）管理
        private readonly string _autoSaveId = Guid.NewGuid().ToString("N");
        private readonly DispatcherTimer _autoSaveDebounceTimer;
        private readonly DispatcherTimer _autoSavePeriodicTimer;
        private readonly Dictionary<Image, string> _imageAssetKeys = new();
        private readonly AutoSaveProject? _restoreProject;

        public MainWindow() : this((string?)null)
        {
        }

        public MainWindow(AutoSaveProject restoreProject) : this((string?)null)
        {
            _restoreProject = restoreProject;
        }

        public MainWindow(string? initialFilePath)
        {
            InitializeComponent();
            WindowThemeHelper.ApplyDarkTitleBar(this);
            _initialFilePath = initialFilePath;

            _settings = AppSettings.Load();

            // オートセーブ用タイマーの初期化
            _autoSaveDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _autoSaveDebounceTimer.Tick += (s, e) =>
            {
                _autoSaveDebounceTimer.Stop();
                SaveAutoSaveSnapshot();
            };
            _autoSavePeriodicTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _autoSavePeriodicTimer.Tick += (s, e) => SaveAutoSaveSnapshot();
            Closed += Window_Closed;

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
            CommandBindings.Add(new CommandBinding(InsertImageCommand, ExecuteInsertImageCommand, CanExecuteInsertImageCommand));
            CommandBindings.Add(new CommandBinding(DuplicateCommand, (s, e) => DuplicateSelectedItem(), (s, e) => e.CanExecute = _selectedItem != null));
            CommandBindings.Add(new CommandBinding(ZoomInCommand, (s, e) => ZoomRelative(ZoomStepFactor)));
            CommandBindings.Add(new CommandBinding(ZoomOutCommand, (s, e) => ZoomRelative(1.0 / ZoomStepFactor)));
            CommandBindings.Add(new CommandBinding(ResetZoomCommand, (s, e) => SetZoom(1.0)));
            CommandBindings.Add(new CommandBinding(FitToWindowCommand, (s, e) => ExecuteFitToWindow()));
            CommandBindings.Add(new CommandBinding(FitToWidthCommand, (s, e) => ExecuteFitToWidth()));

            // 元に戻す (Ctrl+Z) / やり直す (Ctrl+Y)
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (s, e) => _undoManager.Undo(), (s, e) => e.CanExecute = _undoManager.CanUndo));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (s, e) => _undoManager.Redo(), (s, e) => e.CanExecute = _undoManager.CanRedo));
            _undoManager.StateChanged += (s, e) =>
            {
                CommandManager.InvalidateRequerySuggested();
                ScheduleAutoSave();
            };
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
            if (_restoreProject != null)
            {
                RestoreFromAutoSave(_restoreProject);
            }
            else if (!string.IsNullOrEmpty(_initialFilePath) && File.Exists(_initialFilePath))
            {
                LoadImageFromFile(_initialFilePath);
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!App.IsCrashing && _settings.ConfirmOnExit)
            {
                var dialog = new ConfirmExitDialog { Owner = this };
                if (dialog.ShowDialog() != true)
                {
                    e.Cancel = true;
                }
            }
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            _autoSaveDebounceTimer.Stop();
            _autoSavePeriodicTimer.Stop();

            if (!App.IsCrashing)
            {
                AutoSaveManager.Delete(_autoSaveId);
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

        private void CanExecuteInsertImageCommand(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = CanvasContainer != null && CanvasContainer.Visibility == Visibility.Visible;
        }

        private void ExecuteInsertImageCommand(object sender, ExecutedRoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "挿入する画像を選択",
                Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|すべてのファイル (*.*)|*.*",
                FilterIndex = 1,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) == true)
            {
                InsertImageFromFile(dialog.FileName, null);
            }
        }

        private void InsertImageFromFile(string filePath, Point? dropPosition)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    MessageBox.Show(this, "指定された画像ファイルが見つかりません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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

                double imgWidth = bitmap.PixelWidth;
                double imgHeight = bitmap.PixelHeight;

                // ベース画像の解像度サイズ
                double canvasWidth = ImageCanvas.Width;
                double canvasHeight = ImageCanvas.Height;
                if (double.IsNaN(canvasWidth) || canvasWidth <= 0) canvasWidth = 800;
                if (double.IsNaN(canvasHeight) || canvasHeight <= 0) canvasHeight = 600;

                // ベース画像より大きい場合は縦横比を維持してキャンバスの80%以内に縮小
                double maxAllowedW = canvasWidth * 0.8;
                double maxAllowedH = canvasHeight * 0.8;
                if (imgWidth > maxAllowedW || imgHeight > maxAllowedH)
                {
                    double scale = Math.Min(maxAllowedW / imgWidth, maxAllowedH / imgHeight);
                    imgWidth = Math.Round(imgWidth * scale);
                    imgHeight = Math.Round(imgHeight * scale);
                }

                // 配置位置の計算（ドロップ位置、または中央）
                double left, top;
                if (dropPosition.HasValue)
                {
                    left = dropPosition.Value.X - imgWidth / 2.0;
                    top = dropPosition.Value.Y - imgHeight / 2.0;
                }
                else
                {
                    left = (canvasWidth - imgWidth) / 2.0;
                    top = (canvasHeight - imgHeight) / 2.0;
                }

                // キャンバス内に収まるようクランプ
                left = Math.Clamp(left, 0, Math.Max(0, canvasWidth - imgWidth));
                top = Math.Clamp(top, 0, Math.Max(0, canvasHeight - imgHeight));

                var imageControl = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Fill,
                    Focusable = false
                };
                RenderOptions.SetBitmapScalingMode(imageControl, BitmapScalingMode.HighQuality);

                var item = new EditableItem
                {
                    Width = imgWidth,
                    Height = imgHeight,
                    ItemContent = imageControl
                };

                Canvas.SetLeft(item, left);
                Canvas.SetTop(item, top);

                item.Selected += (s, e) => SelectItem(item);
                item.TransformCompleted += (s, e) => _undoManager.Record(new TransformObjectAction(item, e.Before, e.After, SelectItem));
                item.SizeChanged += (s, e) =>
                {
                    if (item.ItemContent is Image && GetItemClipType(item) != ImageClipType.None)
                    {
                        ApplyImageClip(item, GetItemClipType(item));
                    }
                };

                ImageCanvas.Children.Add(item);
                _undoManager.Record(new AddObjectAction(ImageCanvas, item, SelectItem));

                SelectItem(item);
                SetCurrentTool(ToolType.Select);

                StatusInfoText.Text = $"画像を挿入しました ({bitmap.PixelWidth} × {bitmap.PixelHeight} px)";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"画像の挿入に失敗しました。\n\n詳細: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                            Point dropPoint = e.GetPosition(ImageCanvas);
                            InsertImageFromFile(filePath, dropPoint);
                        }
                    }
                    else
                    {
                        LoadImageFromFile(filePath);
                    }

                    Activate();
                    Focus();
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

                if (!_autoSavePeriodicTimer.IsEnabled)
                {
                    _autoSavePeriodicTimer.Start();
                }
                ScheduleAutoSave();
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
                        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                    };
                    item.ItemContent = textControl;
                    AttachTextBoxBehavior(item, textControl);
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

            // テキストボックスの場合は即座に入力可能にし、高さを自動調整
            if (textControl != null)
            {
                AdjustTextBoxHeight(item, textControl);
                Dispatcher.BeginInvoke(() =>
                {
                    textControl.Focus();
                    textControl.SelectAll();
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        private void AttachTextBoxBehavior(EditableItem item, TextBox textBox)
        {
            textBox.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            textBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

            // テキスト変更時の Undo/Redo 追跡
            string textBefore = textBox.Text;
            textBox.GotFocus += (s, e) => textBefore = textBox.Text;
            textBox.LostFocus += (s, e) =>
            {
                if (textBox.Text != textBefore)
                {
                    _undoManager.Record(new TextChangedAction(textBox, textBefore, textBox.Text));
                    textBefore = textBox.Text;
                }
            };

            // 文字入力や枠幅変更時に見切れないよう高さを自動調整・随時保存予約
            textBox.TextChanged += (s, e) =>
            {
                AdjustTextBoxHeight(item, textBox);
                ScheduleAutoSave();
            };
            item.SizeChanged += (s, e) => AdjustTextBoxHeight(item, textBox);
        }

        private void AdjustTextBoxHeight(EditableItem item, TextBox tb)
        {
            double itemWidth = item.ActualWidth > 0 ? item.ActualWidth : item.Width;
            if (double.IsNaN(itemWidth) || itemWidth <= 8) return;

            double tbAvailableWidth = Math.Max(10, itemWidth - 8);

            string measureText = string.IsNullOrEmpty(tb.Text) ? " " : tb.Text;
            if (measureText.EndsWith('\n') || measureText.EndsWith('\r'))
            {
                measureText += " ";
            }

            var measureBox = new TextBox
            {
                Text = measureText,
                FontFamily = tb.FontFamily,
                FontSize = tb.FontSize,
                FontWeight = tb.FontWeight,
                FontStyle = tb.FontStyle,
                BorderThickness = tb.BorderThickness,
                Padding = tb.Padding,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            measureBox.Measure(new Size(tbAvailableWidth, double.PositiveInfinity));
            double requiredItemHeight = Math.Ceiling(measureBox.DesiredSize.Height) + 8;

            double currentItemHeight = item.ActualHeight > 0 ? item.ActualHeight : item.Height;
            if (double.IsNaN(currentItemHeight) || requiredItemHeight > currentItemHeight + 0.5)
            {
                item.Height = requiredItemHeight;
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
            CommandManager.InvalidateRequerySuggested();
        }

        private void DeselectAll()
        {
            if (_selectedItem != null)
            {
                _selectedItem.IsSelected = false;
                _selectedItem = null;
                UpdateToolbarFromSelectedObject(null);
                CommandManager.InvalidateRequerySuggested();
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

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
            {
                return;
            }

            // 文字入力中やコントロール操作中は方向キー移動を行わない（文字カーソル移動などを優先）
            if (Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem)
            {
                return;
            }

            if (_selectedItem == null)
            {
                return;
            }

            if (_arrowMovingItem != _selectedItem)
            {
                _arrowMovingItem = _selectedItem;
                _arrowMoveStartRect = _selectedItem.GetCurrentRect();
            }

            double dx = e.Key switch
            {
                Key.Left => -1.0,
                Key.Right => 1.0,
                _ => 0.0
            };
            double dy = e.Key switch
            {
                Key.Up => -1.0,
                Key.Down => 1.0,
                _ => 0.0
            };

            double curLeft = Canvas.GetLeft(_selectedItem);
            if (double.IsNaN(curLeft)) curLeft = 0;
            double curTop = Canvas.GetTop(_selectedItem);
            if (double.IsNaN(curTop)) curTop = 0;

            Canvas.SetLeft(_selectedItem, curLeft + dx);
            Canvas.SetTop(_selectedItem, curTop + dy);

            e.Handled = true;
        }

        private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Left or Key.Right or Key.Up or Key.Down))
            {
                return;
            }

            if (_arrowMovingItem != null)
            {
                var endRect = _arrowMovingItem.GetCurrentRect();
                if (Math.Abs(endRect.X - _arrowMoveStartRect.X) > 0.5 ||
                    Math.Abs(endRect.Y - _arrowMoveStartRect.Y) > 0.5)
                {
                    _undoManager.Record(new TransformObjectAction(_arrowMovingItem, _arrowMoveStartRect, endRect, SelectItem));
                }
                _arrowMovingItem = null;
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
                else if (item.ItemContent is Image)
                {
                    // 合成画像選択時: フォントや図形描画設定はすべて無効化
                    CbFontFamily.IsEnabled = false;
                    CbFontSize.IsEnabled = false;
                    CbStrokeColor.IsEnabled = false;
                    CbStrokeThickness.IsEnabled = false;
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
                var item = _selectedItem;
                if (tb.FontFamily.Source != fi.FontFamily.Source)
                {
                    var oldFont = tb.FontFamily;
                    var newFont = fi.FontFamily;
                    _undoManager.Record(new StyleChangeAction(
                        () => { tb.FontFamily = oldFont; AdjustTextBoxHeight(item, tb); if (_selectedItem?.ItemContent == tb) SelectFontInComboBox(oldFont); },
                        () => { tb.FontFamily = newFont; AdjustTextBoxHeight(item, tb); if (_selectedItem?.ItemContent == tb) SelectFontInComboBox(newFont); }
                    ));
                    tb.FontFamily = newFont;
                    AdjustTextBoxHeight(item, tb);
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
                var item = _selectedItem;
                var oldSize = tb.FontSize;
                var newSize = size;
                _undoManager.Record(new StyleChangeAction(
                    () => { tb.FontSize = oldSize; AdjustTextBoxHeight(item, tb); if (_selectedItem?.ItemContent == tb) CbFontSize.Text = oldSize.ToString("0"); },
                    () => { tb.FontSize = newSize; AdjustTextBoxHeight(item, tb); if (_selectedItem?.ItemContent == tb) CbFontSize.Text = newSize.ToString("0"); }
                ));
                tb.FontSize = newSize;
                AdjustTextBoxHeight(item, tb);
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
                    var item = _selectedItem;
                    var oldThick = tb.BorderThickness;
                    var newThick = new Thickness(ti.Value);
                    _undoManager.Record(new StyleChangeAction(
                        () => { tb.BorderThickness = oldThick; AdjustTextBoxHeight(item, tb); if (_selectedItem?.ItemContent == tb) SelectThicknessInComboBox(oldThick.Top); },
                        () => { tb.BorderThickness = newThick; AdjustTextBoxHeight(item, tb); if (_selectedItem?.ItemContent == tb) SelectThicknessInComboBox(newThick.Top); }
                    ));
                    tb.BorderThickness = newThick;
                    AdjustTextBoxHeight(item, tb);
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

        #region 右クリックコンテキストメニュー・トリミング・Zオーダー・複製

        public enum ImageClipType
        {
            None,
            RoundedRectangle,
            Circle
        }

        private readonly Dictionary<EditableItem, ImageClipType> _itemClips = new();
        private Point _lastRightClickCanvasPos;

        private ImageClipType GetItemClipType(EditableItem item)
        {
            return _itemClips.TryGetValue(item, out var type) ? type : ImageClipType.None;
        }

        private void SetItemClipType(EditableItem item, ImageClipType type)
        {
            _itemClips[item] = type;
        }

        private void ApplyImageClip(EditableItem item, ImageClipType clipType)
        {
            if (item.ItemContent is not Image img) return;

            if (clipType == ImageClipType.None)
            {
                img.Clip = null;
                return;
            }

            double totalW = item.ActualWidth > 0 ? item.ActualWidth : item.Width;
            double totalH = item.ActualHeight > 0 ? item.ActualHeight : item.Height;
            double w = img.ActualWidth > 0 ? img.ActualWidth : Math.Max(0, totalW - 8.0);
            double h = img.ActualHeight > 0 ? img.ActualHeight : Math.Max(0, totalH - 8.0);

            if (w <= 0 || h <= 0)
            {
                // レイアウト未完了の場合は完了後に再適用
                Dispatcher.BeginInvoke(() => ApplyImageClip(item, clipType), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }

            switch (clipType)
            {
                case ImageClipType.RoundedRectangle:
                    double radius = Math.Min(24, Math.Min(w, h) / 4.0);
                    img.Clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
                    break;
                case ImageClipType.Circle:
                    img.Clip = new EllipseGeometry(new Point(w / 2.0, h / 2.0), w / 2.0, h / 2.0);
                    break;
            }
        }

        private void ChangeSelectedItemClip(ImageClipType newType)
        {
            if (_selectedItem == null || _selectedItem.ItemContent is not Image) return;

            var item = _selectedItem;
            var oldType = GetItemClipType(item);
            if (oldType == newType) return;

            _undoManager.Record(new StyleChangeAction(
                () =>
                {
                    SetItemClipType(item, oldType);
                    ApplyImageClip(item, oldType);
                },
                () =>
                {
                    SetItemClipType(item, newType);
                    ApplyImageClip(item, newType);
                }
            ));

            SetItemClipType(item, newType);
            ApplyImageClip(item, newType);
        }

        private void ImageCanvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            _lastRightClickCanvasPos = e.GetPosition(ImageCanvas);

            // ヒットテストで EditableItem 上か判定
            HitTestResult hitResult = VisualTreeHelper.HitTest(ImageCanvas, _lastRightClickCanvasPos);
            EditableItem? hitItem = FindParentEditableItem(hitResult?.VisualHit);

            if (hitItem != null)
            {
                // オブジェクトの上で右クリック
                SelectItem(hitItem);
                hitItem.Focus();

                if (TryFindResource("ObjectContextMenu") is ContextMenu objMenu)
                {
                    bool isImage = hitItem.ItemContent is Image;
                    if (objMenu.FindName("CtxCropMenu") is MenuItem cropMenu)
                    {
                        cropMenu.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
                    }
                    if (objMenu.FindName("CtxCropSeparator") is Separator cropSep)
                    {
                        cropSep.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed;
                    }

                    if (isImage)
                    {
                        var cropType = GetItemClipType(hitItem);
                        if (objMenu.FindName("CtxCropRoundedRect") is MenuItem rRect) rRect.IsChecked = cropType == ImageClipType.RoundedRectangle;
                        if (objMenu.FindName("CtxCropCircle") is MenuItem circle) circle.IsChecked = cropType == ImageClipType.Circle;
                        if (objMenu.FindName("CtxCropNone") is MenuItem none) none.IsChecked = cropType == ImageClipType.None;
                    }

                    objMenu.PlacementTarget = ImageCanvas;
                    objMenu.Placement = PlacementMode.MousePoint;
                    objMenu.IsOpen = true;
                }
            }
            else
            {
                // 空白キャンバス上で右クリック
                DeselectAll();

                if (TryFindResource("CanvasContextMenu") is ContextMenu canvasMenu)
                {
                    canvasMenu.PlacementTarget = ImageCanvas;
                    canvasMenu.Placement = PlacementMode.MousePoint;
                    canvasMenu.IsOpen = true;
                }
            }

            e.Handled = true;
        }

        private static EditableItem? FindParentEditableItem(DependencyObject? element)
        {
            while (element != null && element is not EditableItem && element is not Canvas)
            {
                element = VisualTreeHelper.GetParent(element);
            }
            return element as EditableItem;
        }

        private void CtxCanvasInsert_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string tagStr && Enum.TryParse<ToolType>(tagStr, out var toolType))
            {
                if (_settings.RightClickBehavior == RightClickInsertBehavior.ClickToInsert)
                {
                    double w = 120, h = 80;
                    switch (toolType)
                    {
                        case ToolType.TextBox:
                            w = 160; h = 50;
                            break;
                        case ToolType.Circle:
                            w = 80; h = 80;
                            break;
                        case ToolType.Rectangle:
                            w = 120; h = 80;
                            break;
                        case ToolType.RoundedRectangle:
                            w = 120; h = 80;
                            break;
                        case ToolType.Checkmark:
                            w = 48; h = 48;
                            break;
                    }

                    double left = Math.Clamp(_lastRightClickCanvasPos.X - w / 2.0, 0, Math.Max(0, ImageCanvas.Width - w));
                    double top = Math.Clamp(_lastRightClickCanvasPos.Y - h / 2.0, 0, Math.Max(0, ImageCanvas.Height - h));

                    CreateAndInsertObject(toolType, left, top, w, h);
                    SetCurrentTool(ToolType.Select);
                }
                else
                {
                    SetCurrentTool(toolType);
                }
            }
        }

        private void CtxCanvasInsertImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "挿入する画像を選択",
                Filter = "画像ファイル (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|すべてのファイル (*.*)|*.*",
                FilterIndex = 1,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) == true)
            {
                InsertImageFromFile(dialog.FileName, _lastRightClickCanvasPos);
            }
        }

        private void CtxBringForward_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;
            int idx = ImageCanvas.Children.IndexOf(_selectedItem);
            if (idx < 0 || idx >= ImageCanvas.Children.Count - 1) return;

            int newIdx = idx + 1;
            ImageCanvas.Children.RemoveAt(idx);
            ImageCanvas.Children.Insert(newIdx, _selectedItem);
            _undoManager.Record(new ZOrderAction(ImageCanvas, _selectedItem, idx, newIdx));
        }

        private void CtxSendBackward_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedItem == null) return;
            int idx = ImageCanvas.Children.IndexOf(_selectedItem);
            if (idx <= 2) return; // CanvasImage(0), RubberBand(1) を下回らない

            int newIdx = idx - 1;
            ImageCanvas.Children.RemoveAt(idx);
            ImageCanvas.Children.Insert(newIdx, _selectedItem);
            _undoManager.Record(new ZOrderAction(ImageCanvas, _selectedItem, idx, newIdx));
        }

        private void CtxCrop_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string tagStr && Enum.TryParse<ImageClipType>(tagStr, out var type))
            {
                ChangeSelectedItemClip(type);
            }
        }

        private void CtxDuplicate_Click(object sender, RoutedEventArgs e)
        {
            DuplicateSelectedItem();
        }

        private void DuplicateSelectedItem()
        {
            if (_selectedItem == null) return;

            double curLeft = Canvas.GetLeft(_selectedItem);
            double curTop = Canvas.GetTop(_selectedItem);
            if (double.IsNaN(curLeft)) curLeft = 0;
            if (double.IsNaN(curTop)) curTop = 0;

            double w = _selectedItem.ActualWidth > 0 ? _selectedItem.ActualWidth : _selectedItem.Width;
            double h = _selectedItem.ActualHeight > 0 ? _selectedItem.ActualHeight : _selectedItem.Height;

            double newLeft = Math.Clamp(curLeft + 20, 0, Math.Max(0, ImageCanvas.Width - w));
            double newTop = Math.Clamp(curTop + 20, 0, Math.Max(0, ImageCanvas.Height - h));

            var newItem = new EditableItem
            {
                Width = w,
                Height = h
            };
            Canvas.SetLeft(newItem, newLeft);
            Canvas.SetTop(newItem, newTop);

            TextBox? newTextBox = null;

            if (_selectedItem.ItemContent is TextBox srcTb)
            {
                newItem.IsTextBox = true;
                newTextBox = new TextBox
                {
                    Text = srcTb.Text,
                    Foreground = srcTb.Foreground,
                    Background = srcTb.Background,
                    BorderBrush = srcTb.BorderBrush,
                    BorderThickness = srcTb.BorderThickness,
                    FontSize = srcTb.FontSize,
                    FontFamily = srcTb.FontFamily,
                    AcceptsReturn = srcTb.AcceptsReturn,
                    TextWrapping = srcTb.TextWrapping,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                };

                newItem.ItemContent = newTextBox;
                AttachTextBoxBehavior(newItem, newTextBox);
            }
            else if (_selectedItem.ItemContent is Ellipse srcEllipse)
            {
                newItem.ItemContent = new Ellipse
                {
                    Stroke = srcEllipse.Stroke,
                    StrokeThickness = srcEllipse.StrokeThickness,
                    Fill = srcEllipse.Fill
                };
            }
            else if (_selectedItem.ItemContent is System.Windows.Shapes.Rectangle srcRect)
            {
                newItem.ItemContent = new System.Windows.Shapes.Rectangle
                {
                    Stroke = srcRect.Stroke,
                    StrokeThickness = srcRect.StrokeThickness,
                    Fill = srcRect.Fill,
                    RadiusX = srcRect.RadiusX,
                    RadiusY = srcRect.RadiusY
                };
            }
            else if (_selectedItem.ItemContent is Viewbox srcVb && srcVb.Child is WpfPath srcPath)
            {
                var newPath = new WpfPath
                {
                    Data = srcPath.Data.Clone(),
                    Stroke = srcPath.Stroke,
                    StrokeThickness = srcPath.StrokeThickness,
                    StrokeStartLineCap = srcPath.StrokeStartLineCap,
                    StrokeEndLineCap = srcPath.StrokeEndLineCap,
                    StrokeLineJoin = srcPath.StrokeLineJoin
                };
                newItem.ItemContent = new Viewbox
                {
                    Stretch = Stretch.Uniform,
                    Child = newPath
                };
            }
            else if (_selectedItem.ItemContent is Image srcImg)
            {
                var newImg = new Image
                {
                    Source = srcImg.Source,
                    Stretch = Stretch.Fill,
                    Focusable = false
                };
                RenderOptions.SetBitmapScalingMode(newImg, BitmapScalingMode.HighQuality);
                newItem.ItemContent = newImg;

                var clipType = GetItemClipType(_selectedItem);
                SetItemClipType(newItem, clipType);
                ApplyImageClip(newItem, clipType);
            }

            newItem.Selected += (s, e) => SelectItem(newItem);
            newItem.TransformCompleted += (s, e) => _undoManager.Record(new TransformObjectAction(newItem, e.Before, e.After, SelectItem));
            newItem.SizeChanged += (s, e) =>
            {
                if (newItem.ItemContent is Image && GetItemClipType(newItem) != ImageClipType.None)
                {
                    ApplyImageClip(newItem, GetItemClipType(newItem));
                }
            };

            ImageCanvas.Children.Add(newItem);
            _undoManager.Record(new AddObjectAction(ImageCanvas, newItem, SelectItem));

            SelectItem(newItem);

            if (newTextBox != null)
            {
                AdjustTextBoxHeight(newItem, newTextBox);
                Dispatcher.BeginInvoke(() =>
                {
                    newTextBox.Focus();
                    newTextBox.SelectAll();
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
        }

        #endregion

        #region 自動保存（オートセーブ）および異常終了リカバリ

        private void ScheduleAutoSave()
        {
            if (CanvasContainer.Visibility != Visibility.Visible || string.IsNullOrEmpty(_currentLoadedFilePath))
            {
                return;
            }
            _autoSaveDebounceTimer.Stop();
            _autoSaveDebounceTimer.Start();
        }

        private bool SaveAutoSaveSnapshot()
        {
            if (CanvasContainer.Visibility != Visibility.Visible || string.IsNullOrEmpty(_currentLoadedFilePath))
            {
                return false;
            }

            var project = new AutoSaveProject
            {
                WindowId = _autoSaveId,
                BaseImagePath = _currentLoadedFilePath,
                SavedAt = DateTime.Now
            };

            foreach (var child in ImageCanvas.Children)
            {
                if (child is not EditableItem item) continue;

                var rect = item.GetCurrentRect();
                var saveItem = new AutoSaveItem
                {
                    Left = rect.X,
                    Top = rect.Y,
                    Width = rect.Width,
                    Height = rect.Height
                };

                if (item.ItemContent is TextBox tb)
                {
                    saveItem.ItemType = "TextBox";
                    saveItem.Text = tb.Text;
                    saveItem.FontFamily = tb.FontFamily.Source;
                    saveItem.FontSize = tb.FontSize;
                    saveItem.ColorHex = (tb.Foreground as SolidColorBrush)?.Color.ToString() ?? "#FFFF0000";
                    saveItem.StrokeThickness = tb.BorderThickness.Top;
                }
                else if (item.ItemContent is Ellipse el)
                {
                    saveItem.ItemType = "Circle";
                    saveItem.ColorHex = (el.Stroke as SolidColorBrush)?.Color.ToString() ?? "#FFFF0000";
                    saveItem.StrokeThickness = el.StrokeThickness;
                }
                else if (item.ItemContent is System.Windows.Shapes.Rectangle rc)
                {
                    saveItem.ItemType = rc.RadiusX > 0 ? "RoundedRectangle" : "Rectangle";
                    saveItem.ColorHex = (rc.Stroke as SolidColorBrush)?.Color.ToString() ?? "#FFFF0000";
                    saveItem.StrokeThickness = rc.StrokeThickness;
                }
                else if (item.ItemContent is Viewbox vb && vb.Child is WpfPath path)
                {
                    saveItem.ItemType = "Checkmark";
                    saveItem.ColorHex = (path.Stroke as SolidColorBrush)?.Color.ToString() ?? "#FFFF0000";
                    saveItem.StrokeThickness = path.StrokeThickness;
                }
                else if (item.ItemContent is Image img && img.Source is BitmapSource bmp)
                {
                    saveItem.ItemType = "Image";
                    if (!_imageAssetKeys.TryGetValue(img, out string? assetKey))
                    {
                        assetKey = Guid.NewGuid().ToString("N");
                        _imageAssetKeys[img] = assetKey;
                    }
                    saveItem.AssetImagePath = AutoSaveManager.EnsureImageAssetSaved(bmp, assetKey);
                    saveItem.ClipType = GetItemClipType(item).ToString();
                }
                else
                {
                    continue;
                }

                project.Items.Add(saveItem);
            }

            AutoSaveManager.Save(_autoSaveId, project);
            return true;
        }

        public bool EmergencySave()
        {
            try
            {
                _autoSaveDebounceTimer.Stop();
                _autoSavePeriodicTimer.Stop();
                return SaveAutoSaveSnapshot();
            }
            catch
            {
                return false;
            }
        }

        private void RestoreFromAutoSave(AutoSaveProject project)
        {
            if (string.IsNullOrEmpty(project.BaseImagePath) || !File.Exists(project.BaseImagePath))
            {
                MessageBox.Show(this, "復元対象の元画像ファイルが見つかりませんでした。", "復元エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LoadImageFromFile(project.BaseImagePath);

            foreach (var saved in project.Items)
            {
                var item = new EditableItem
                {
                    Width = saved.Width,
                    Height = saved.Height
                };
                Canvas.SetLeft(item, saved.Left);
                Canvas.SetTop(item, saved.Top);

                SolidColorBrush brush = Brushes.Red;
                if (!string.IsNullOrEmpty(saved.ColorHex))
                {
                    try
                    {
                        var color = (Color)ColorConverter.ConvertFromString(saved.ColorHex);
                        brush = new SolidColorBrush(color);
                        brush.Freeze();
                    }
                    catch { }
                }

                switch (saved.ItemType)
                {
                    case "TextBox":
                        item.IsTextBox = true;
                        var tb = new TextBox
                        {
                            Text = saved.Text ?? "",
                            Foreground = brush,
                            Background = Brushes.Transparent,
                            BorderBrush = brush,
                            BorderThickness = new Thickness(saved.StrokeThickness),
                            FontSize = saved.FontSize > 0 ? saved.FontSize : 18,
                            FontFamily = !string.IsNullOrEmpty(saved.FontFamily) ? new FontFamily(saved.FontFamily) : GetDefaultFontFamily(),
                            AcceptsReturn = true,
                            TextWrapping = TextWrapping.Wrap,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                        };
                        item.ItemContent = tb;
                        AttachTextBoxBehavior(item, tb);
                        break;

                    case "Circle":
                        item.ItemContent = new Ellipse
                        {
                            Stroke = brush,
                            StrokeThickness = saved.StrokeThickness,
                            Fill = Brushes.Transparent
                        };
                        break;

                    case "Rectangle":
                        item.ItemContent = new System.Windows.Shapes.Rectangle
                        {
                            Stroke = brush,
                            StrokeThickness = saved.StrokeThickness,
                            Fill = Brushes.Transparent
                        };
                        break;

                    case "RoundedRectangle":
                        item.ItemContent = new System.Windows.Shapes.Rectangle
                        {
                            Stroke = brush,
                            StrokeThickness = saved.StrokeThickness,
                            Fill = Brushes.Transparent,
                            RadiusX = 16,
                            RadiusY = 16
                        };
                        break;

                    case "Checkmark":
                        var path = new WpfPath
                        {
                            Data = Geometry.Parse("M 4,22 L 15,33 L 38,8"),
                            Stroke = brush,
                            StrokeThickness = saved.StrokeThickness,
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

                    case "Image":
                        if (!string.IsNullOrEmpty(saved.AssetImagePath) && File.Exists(saved.AssetImagePath))
                        {
                            var bitmap = new BitmapImage();
                            using (var stream = new FileStream(saved.AssetImagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                bitmap.BeginInit();
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.StreamSource = stream;
                                bitmap.EndInit();
                            }
                            bitmap.Freeze();

                            var img = new Image
                            {
                                Source = bitmap,
                                Stretch = Stretch.Fill,
                                Focusable = false
                            };
                            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                            item.ItemContent = img;

                            if (Enum.TryParse<ImageClipType>(saved.ClipType, out var clipType))
                            {
                                SetItemClipType(item, clipType);
                                ApplyImageClip(item, clipType);
                            }

                            item.SizeChanged += (s, e) =>
                            {
                                if (item.ItemContent is Image && GetItemClipType(item) != ImageClipType.None)
                                {
                                    ApplyImageClip(item, GetItemClipType(item));
                                }
                            };
                        }
                        else
                        {
                            continue;
                        }
                        break;

                    default:
                        continue;
                }

                item.Selected += (s, e) => SelectItem(item);
                item.TransformCompleted += (s, e) => _undoManager.Record(new TransformObjectAction(item, e.Before, e.After, SelectItem));

                ImageCanvas.Children.Add(item);
            }

            if (!string.IsNullOrEmpty(project.WindowId) && project.WindowId != _autoSaveId)
            {
                AutoSaveManager.Delete(project.WindowId);
            }
            ScheduleAutoSave();
            StatusInfoText.Text = "前回の作業内容を復元しました";
        }

        #endregion
    }
}
