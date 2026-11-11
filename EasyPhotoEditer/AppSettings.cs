using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyPhotoEditer
{
    public enum RightClickInsertBehavior
    {
        [JsonPropertyName("ClickToInsert")]
        ClickToInsert,

        [JsonPropertyName("DragToSpecify")]
        DragToSpecify
    }

    public class AppSettings
    {
        private static readonly string SettingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        // 一般設定
        public bool ConfirmOnExit { get; set; } = true;

        // 編集設定: デフォルト値
        public string DefaultFontFamily { get; set; } = "メイリオ";
        public double DefaultFontSize { get; set; } = 18.0;
        public string DefaultTextColor { get; set; } = "赤";
        public string DefaultShapeColor { get; set; } = "赤";
        public double DefaultStrokeThickness { get; set; } = 3.0;

        // 編集設定: 右クリック時の図形挿入動作
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RightClickInsertBehavior RightClickBehavior { get; set; } = RightClickInsertBehavior.ClickToInsert;

        /// <summary>
        /// settings.json から設定を読み込む。ファイルが存在しないか破損している場合は既定値を返す。
        /// </summary>
        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch
            {
                // エラー時は安全にコード内の既定値を使用
            }

            return new AppSettings();
        }

        /// <summary>
        /// 設定を settings.json に保存する。
        /// </summary>
        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch
            {
                // 書き込み失敗時の無視またはログ記録
            }
        }

        /// <summary>
        /// 設定の複製を作成
        /// </summary>
        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }
    }
}
