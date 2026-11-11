using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;

namespace EasyPhotoEditer
{
    public partial class OptionsWindow : Window
    {
        public AppSettings CurrentSettings { get; private set; }

        public OptionsWindow(AppSettings settings)
        {
            InitializeComponent();
            CurrentSettings = settings.Clone();

            InitializeControls();
            LoadSettingsToUI();
        }

        private void InitializeControls()
        {
            // 1. システムフォント一覧（日本語名対応）
            var jaLang = XmlLanguage.GetLanguage("ja-jp");
            var fontItems = new List<FontItem>();
            foreach (var family in Fonts.SystemFontFamilies)
            {
                string displayName = family.FamilyNames.TryGetValue(jaLang, out string? jaName) ? jaName : family.Source;
                fontItems.Add(new FontItem { DisplayName = displayName, FontFamily = family });
            }
            fontItems = fontItems.OrderBy(f => f.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
            CbDefaultFont.ItemsSource = fontItems;

            // 2. 文字サイズ一覧 (36以降も4区切りで72まで)
            double[] sizes = [9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 32, 36, 40, 44, 48, 52, 56, 60, 64, 68, 72];
            CbDefaultFontSize.ItemsSource = sizes;

            // 3. 色一覧
            var textColors = CreateColorList();
            CbDefaultTextColor.ItemsSource = textColors;

            var shapeColors = CreateColorList();
            CbDefaultShapeColor.ItemsSource = shapeColors;

            // 4. 線の太さ一覧
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
            CbDefaultStrokeThickness.ItemsSource = thicknesses;
        }

        private static List<ColorItem> CreateColorList()
        {
            return
            [
                new() { Name = "赤", Brush = Brushes.Red },
                new() { Name = "青", Brush = (SolidColorBrush)new BrushConverter().ConvertFromString("#0078D7")! },
                new() { Name = "緑", Brush = (SolidColorBrush)new BrushConverter().ConvertFromString("#107C41")! },
                new() { Name = "黄", Brush = (SolidColorBrush)new BrushConverter().ConvertFromString("#FFB900")! },
                new() { Name = "黒", Brush = Brushes.Black },
                new() { Name = "白", Brush = Brushes.White },
                new() { Name = "橙", Brush = (SolidColorBrush)new BrushConverter().ConvertFromString("#D83B01")! },
                new() { Name = "紫", Brush = (SolidColorBrush)new BrushConverter().ConvertFromString("#881798")! },
                new() { Name = "水色", Brush = (SolidColorBrush)new BrushConverter().ConvertFromString("#00B7C3")! }
            ];
        }

        private void LoadSettingsToUI()
        {
            // 一般
            ChkConfirmOnExit.IsChecked = CurrentSettings.ConfirmOnExit;

            // 編集: フォント
            if (CbDefaultFont.ItemsSource is List<FontItem> fonts)
            {
                var match = fonts.FirstOrDefault(f => f.DisplayName == CurrentSettings.DefaultFontFamily || f.FontFamily.Source == CurrentSettings.DefaultFontFamily)
                            ?? fonts.FirstOrDefault();
                CbDefaultFont.SelectedItem = match;
            }

            // 編集: 文字サイズ
            CbDefaultFontSize.Text = CurrentSettings.DefaultFontSize.ToString("0");

            // 編集: 文字色
            if (CbDefaultTextColor.ItemsSource is List<ColorItem> textColors)
            {
                var match = textColors.FirstOrDefault(c => c.Name == CurrentSettings.DefaultTextColor) ?? textColors.FirstOrDefault();
                CbDefaultTextColor.SelectedItem = match;
            }

            // 編集: 図形色
            if (CbDefaultShapeColor.ItemsSource is List<ColorItem> shapeColors)
            {
                var match = shapeColors.FirstOrDefault(c => c.Name == CurrentSettings.DefaultShapeColor) ?? shapeColors.FirstOrDefault();
                CbDefaultShapeColor.SelectedItem = match;
            }

            // 編集: 線の太さ
            if (CbDefaultStrokeThickness.ItemsSource is List<ThicknessItem> thicknesses)
            {
                var match = thicknesses.FirstOrDefault(t => Math.Abs(t.Value - CurrentSettings.DefaultStrokeThickness) < 0.05)
                            ?? thicknesses.FirstOrDefault(t => Math.Abs(t.Value - 3.0) < 0.05);
                CbDefaultStrokeThickness.SelectedItem = match;
            }

            // 編集: 右クリック挿入動作
            if (CurrentSettings.RightClickBehavior == RightClickInsertBehavior.ClickToInsert)
            {
                RbRightClickClickToInsert.IsChecked = true;
            }
            else
            {
                RbRightClickDragToSpecify.IsChecked = true;
            }
        }

        private void Category_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelGeneral == null || PanelEdit == null) return;

            if (RbCategoryGeneral.IsChecked == true)
            {
                PanelGeneral.Visibility = Visibility.Visible;
                PanelEdit.Visibility = Visibility.Collapsed;
            }
            else
            {
                PanelGeneral.Visibility = Visibility.Collapsed;
                PanelEdit.Visibility = Visibility.Visible;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // 一般
            CurrentSettings.ConfirmOnExit = ChkConfirmOnExit.IsChecked == true;

            // 編集: フォント
            if (CbDefaultFont.SelectedItem is FontItem fi)
            {
                CurrentSettings.DefaultFontFamily = fi.DisplayName;
            }

            // 編集: 文字サイズ
            if (double.TryParse(CbDefaultFontSize.Text, out double size))
            {
                CurrentSettings.DefaultFontSize = Math.Clamp(size, 6, 200);
            }

            // 編集: 文字色
            if (CbDefaultTextColor.SelectedItem is ColorItem tc)
            {
                CurrentSettings.DefaultTextColor = tc.Name;
            }

            // 編集: 図形色
            if (CbDefaultShapeColor.SelectedItem is ColorItem sc)
            {
                CurrentSettings.DefaultShapeColor = sc.Name;
            }

            // 編集: 線の太さ
            if (CbDefaultStrokeThickness.SelectedItem is ThicknessItem ti)
            {
                CurrentSettings.DefaultStrokeThickness = ti.Value;
            }

            // 編集: 右クリック動作
            CurrentSettings.RightClickBehavior = RbRightClickClickToInsert.IsChecked == true
                ? RightClickInsertBehavior.ClickToInsert
                : RightClickInsertBehavior.DragToSpecify;

            DialogResult = true;
            Close();
        }
    }
}
