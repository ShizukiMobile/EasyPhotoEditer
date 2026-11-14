using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace EasyPhotoEditer
{
    public class AutoSaveProject
    {
        public string WindowId { get; set; } = "";
        public string? BaseImagePath { get; set; }
        public DateTime SavedAt { get; set; }
        public List<AutoSaveItem> Items { get; set; } = [];
    }

    public class AutoSaveItem
    {
        public string ItemType { get; set; } = "";
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string? Text { get; set; }
        public string? FontFamily { get; set; }
        public double FontSize { get; set; }
        public string? ColorHex { get; set; }
        public double StrokeThickness { get; set; }
        public string? AssetImagePath { get; set; }
        public string? ClipType { get; set; }
    }

    public static class AutoSaveManager
    {
        private static readonly string AutoSaveDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "autosave");
        private static readonly string AssetsDir = Path.Combine(AutoSaveDir, "assets");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static string GetBackupFilePath(string windowId)
        {
            return Path.Combine(AutoSaveDir, $"autosave_{windowId}.json");
        }

        public static void Save(string windowId, AutoSaveProject project)
        {
            try
            {
                Directory.CreateDirectory(AutoSaveDir);
                string targetPath = GetBackupFilePath(windowId);
                string tempPath = targetPath + ".tmp";

                string json = JsonSerializer.Serialize(project, JsonOptions);
                File.WriteAllText(tempPath, json);
                File.Move(tempPath, targetPath, overwrite: true);
            }
            catch
            {
                // オートセーブ失敗時はアプリ動作を妨げない
            }
        }

        public static string? EnsureImageAssetSaved(BitmapSource bitmap, string assetKey)
        {
            try
            {
                Directory.CreateDirectory(AssetsDir);
                string assetPath = Path.Combine(AssetsDir, $"{assetKey}.png");
                if (!File.Exists(assetPath))
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = new FileStream(assetPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    encoder.Save(stream);
                }
                return assetPath;
            }
            catch
            {
                return null;
            }
        }

        public static void Delete(string windowId)
        {
            try
            {
                string path = GetBackupFilePath(windowId);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                // すべてのバックアップが消えた場合はアセットフォルダもクリーンアップ
                if (Directory.Exists(AutoSaveDir) && GetPendingBackups().Count == 0 && Directory.Exists(AssetsDir))
                {
                    Directory.Delete(AssetsDir, recursive: true);
                }
            }
            catch
            {
                // 削除エラーは無視
            }
        }

        public static List<string> GetPendingBackups()
        {
            try
            {
                if (!Directory.Exists(AutoSaveDir)) return [];
                return Directory.GetFiles(AutoSaveDir, "autosave_*.json").OrderByDescending(File.GetLastWriteTime).ToList();
            }
            catch
            {
                return [];
            }
        }

        public static AutoSaveProject? Load(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;
                string json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<AutoSaveProject>(json);
            }
            catch
            {
                return null;
            }
        }

        public static void ClearAll()
        {
            try
            {
                if (Directory.Exists(AutoSaveDir))
                {
                    Directory.Delete(AutoSaveDir, recursive: true);
                }
            }
            catch
            {
                // クリアエラーは無視
            }
        }
    }
}
