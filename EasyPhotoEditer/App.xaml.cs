using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace EasyPhotoEditer
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static bool IsCrashing { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // 未処理例外ハンドラの登録
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            base.OnStartup(e);

            string? initialFilePath = e.Args
                .FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal) && File.Exists(arg));

#if DEBUG
            if (e.Args.Any(arg => string.Equals(arg, "--debug-crash", StringComparison.OrdinalIgnoreCase)))
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
                {
                    throw new InvalidOperationException("デバッグ用テスト例外 (--debug-crash)");
                });
            }
#endif

            // 復元確認ダイアログを閉じた瞬間にアプリが自動終了(OnLastWindowClose)するのを防ぐ
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // 前回異常終了時の自動バックアップがあるか確認
            var pendingBackups = AutoSaveManager.GetPendingBackups();
            if (pendingBackups.Count > 0)
            {
                var projects = pendingBackups
                    .Select(AutoSaveManager.Load)
                    .Where(p => p != null && !string.IsNullOrEmpty(p.BaseImagePath))
                    .Cast<AutoSaveProject>()
                    .ToList();

                if (projects.Count > 0)
                {
                    var restoreDialog = new RestoreConfirmDialog(projects);
                    bool shouldRestore = restoreDialog.ShowDialog() == true;

                    if (shouldRestore)
                    {
                        Window? firstWindow = null;
                        foreach (var proj in projects)
                        {
                            var restoredWin = new MainWindow(proj);
                            firstWindow ??= restoredWin;
                            restoredWin.Show();
                        }

                        if (!string.IsNullOrEmpty(initialFilePath))
                        {
                            var argWin = new MainWindow(initialFilePath);
                            firstWindow ??= argWin;
                            argWin.Show();
                        }

                        if (firstWindow != null)
                        {
                            this.MainWindow = firstWindow;
                        }
                        ShutdownMode = ShutdownMode.OnLastWindowClose;
                        return;
                    }
                    else
                    {
                        AutoSaveManager.ClearAll();
                    }
                }
                else
                {
                    AutoSaveManager.ClearAll();
                }
            }

            var mainWindow = new MainWindow(initialFilePath);
            this.MainWindow = mainWindow;
            mainWindow.Show();
            ShutdownMode = ShutdownMode.OnLastWindowClose;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            HandleCrash(e.Exception);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                HandleCrash(ex);
            }
            else
            {
                HandleCrash(new Exception($"不明な致命的エラーが発生しました: {e.ExceptionObject}"));
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            e.SetObserved();
            HandleCrash(e.Exception);
        }

        private void HandleCrash(Exception ex)
        {
            if (IsCrashing) return;
            IsCrashing = true;

            void ShowCrashUiAndExit()
            {
                bool hasBackup = false;
                try
                {
                    foreach (Window win in Windows)
                    {
                        if (win is MainWindow mw && mw.EmergencySave())
                        {
                            hasBackup = true;
                        }
                    }
                }
                catch
                {
                    // 緊急保存時のエラーは無視
                }

                string logPath = WriteCrashLog(ex, hasBackup);

                try
                {
                    var dialog = new CrashDialog(logPath, hasBackup);
                    dialog.ShowDialog();
                }
                catch
                {
                    MessageBox.Show(
                        $"予期せぬエラーが発生したため、アプリを終了します。\nクラッシュログ: {logPath}\n\n詳細: {ex.Message}",
                        "エラー - EasyPhotoEditer",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                finally
                {
                    Environment.Exit(1);
                }
            }

            if (Dispatcher.CheckAccess())
            {
                ShowCrashUiAndExit();
            }
            else
            {
                try
                {
                    Dispatcher.Invoke(ShowCrashUiAndExit);
                }
                catch
                {
                    string logPath = WriteCrashLog(ex, false);
                    MessageBox.Show(
                        $"予期せぬエラーが発生したため、アプリを終了します。\nクラッシュログ: {logPath}",
                        "エラー - EasyPhotoEditer",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Environment.Exit(1);
                }
            }
        }

        private static string WriteCrashLog(Exception ex, bool hasBackup)
        {
            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_logs");
            string fileName = $"crash_{DateTime.Now:yyyyMMdd_HHmmss}.log";
            string logPath = Path.Combine(logDir, fileName);

            try
            {
                Directory.CreateDirectory(logDir);

                var sb = new StringBuilder();
                sb.AppendLine("============================================================");
                sb.AppendLine("EasyPhotoEditer Crash Report");
                sb.AppendLine("============================================================");
                sb.AppendLine($"Timestamp   : {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine($"OS Version  : {Environment.OSVersion}");
                sb.AppendLine($".NET Version: {Environment.Version}");
                sb.AppendLine($"AutoBackup  : {(hasBackup ? "Saved" : "None")}");
                sb.AppendLine("------------------------------------------------------------");
                sb.AppendLine("Exception Details:");
                sb.AppendLine(ex.ToString());
                sb.AppendLine("============================================================");

                File.WriteAllText(logPath, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // ファイル書き込み失敗時
                logPath = Path.Combine(Path.GetTempPath(), fileName);
                try
                {
                    File.WriteAllText(logPath, ex.ToString(), Encoding.UTF8);
                }
                catch { }
            }

            return logPath;
        }
    }
}
