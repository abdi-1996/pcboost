using Microsoft.Win32;
using System.Diagnostics;

namespace PCAIOldRemover;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private readonly string oldRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PCAI Server");

    private readonly Label pathLabel = new();
    private readonly Button removeButton = new();
    private readonly TextBox logBox = new();
    private readonly Label statusLabel = new();

    public MainForm()
    {
        Text = "PCAI Old Version Remover";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 620;
        Height = 430;
        MinimumSize = new Size(560, 380);
        MaximizeBox = false;

        var title = new Label
        {
            Text = "Удаление старой версии PCAI Server",
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            AutoSize = true,
            Left = 24,
            Top = 22
        };
        Controls.Add(title);

        var info = new Label
        {
            Text = "Удаляется только старая версия. PC AI Studio, ComfyUI и модели не затрагиваются.",
            AutoSize = false,
            Left = 26,
            Top = 64,
            Width = 540,
            Height = 42
        };
        Controls.Add(info);

        pathLabel.Left = 26;
        pathLabel.Top = 112;
        pathLabel.Width = 540;
        pathLabel.Height = 38;
        pathLabel.Text = $"Старая папка: {oldRoot}";
        Controls.Add(pathLabel);

        removeButton.Text = "Удалить старую версию полностью";
        removeButton.Left = 26;
        removeButton.Top = 158;
        removeButton.Width = 300;
        removeButton.Height = 42;
        removeButton.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        removeButton.Click += async (_, _) => await RemoveOldVersionAsync();
        Controls.Add(removeButton);

        statusLabel.Left = 340;
        statusLabel.Top = 166;
        statusLabel.Width = 220;
        statusLabel.Height = 30;
        statusLabel.Text = Directory.Exists(oldRoot) ? "Старая версия найдена" : "Старая версия не найдена";
        Controls.Add(statusLabel);

        logBox.Left = 26;
        logBox.Top = 216;
        logBox.Width = 540;
        logBox.Height = 130;
        logBox.Multiline = true;
        logBox.ReadOnly = true;
        logBox.ScrollBars = ScrollBars.Vertical;
        logBox.BackColor = SystemColors.Window;
        Controls.Add(logBox);

        var closeButton = new Button
        {
            Text = "Закрыть",
            Left = 466,
            Top = 356,
            Width = 100,
            Height = 32
        };
        closeButton.Click += (_, _) => Close();
        Controls.Add(closeButton);

        Append("Готово к проверке.");
        Append(Directory.Exists(oldRoot)
            ? "Обнаружена старая папка PCAI Server."
            : "Старая папка PCAI Server не обнаружена.");
    }

    private async Task RemoveOldVersionAsync()
    {
        if (!IsSafeOldPath(oldRoot))
        {
            MessageBox.Show("Защитная проверка пути не пройдена. Удаление отменено.",
                "PCAI Remover", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var answer = MessageBox.Show(
            "Будет удалена только старая версия PCAI Server:\n\n" +
            oldRoot +
            "\n\nНовая PC AI Studio, ComfyUI и модели не будут удалены. Продолжить?",
            "Подтверждение удаления",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes) return;

        removeButton.Enabled = false;
        statusLabel.Text = "Удаление...";
        logBox.Clear();

        try
        {
            Append("1. Останавливаю процессы старой версии...");
            await StopOldProcessesAsync();
            await Task.Delay(700);

            Append("2. Удаляю старый автозапуск...");
            RemoveRunEntries();

            Append("3. Удаляю старые ярлыки...");
            RemoveKnownShortcuts();

            Append("4. Удаляю старую папку программы...");
            DeleteDirectorySafely(oldRoot);

            Append("5. Проверяю результат...");
            var remaining = Directory.Exists(oldRoot);

            if (!remaining)
            {
                statusLabel.Text = "Удалено полностью";
                Append("✓ Старая версия PCAI Server удалена.");
                Append("✓ PC AI Studio и ComfyUI не изменялись.");
                MessageBox.Show(
                    "Старая версия PCAI Server удалена полностью.\n\nТеперь можно запускать PC AI Studio.",
                    "Готово",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                statusLabel.Text = "Нужна перезагрузка";
                Append("Не удалось удалить один или несколько занятых файлов.");
                Append("Перезагрузи Windows и запусти этот Remover ещё раз.");
                MessageBox.Show(
                    "Часть файлов сейчас используется Windows. Перезагрузи ПК и запусти Remover ещё раз.",
                    "Не всё удалено",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        catch (Exception ex)
        {
            statusLabel.Text = "Ошибка";
            Append("Ошибка: " + ex.Message);
            MessageBox.Show(ex.Message, "Ошибка удаления", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            removeButton.Enabled = true;
        }
    }

    private bool IsSafeOldPath(string path)
    {
        var expected = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PCAI Server")).TrimEnd(Path.DirectorySeparatorChar);

        var actual = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);

        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
               && !actual.Contains("PC AI Studio", StringComparison.OrdinalIgnoreCase)
               && actual.EndsWith("PCAI Server", StringComparison.OrdinalIgnoreCase);
    }

    private async Task StopOldProcessesAsync()
    {
        string escaped = oldRoot.Replace("'", "''");
        string script =
            "$p='" + escaped + "';" +
            "Get-CimInstance Win32_Process | " +
            "Where-Object { ($_.CommandLine -and $_.CommandLine -like ('*' + $p + '*')) -or " +
            "($_.ExecutablePath -and $_.ExecutablePath -like ('*' + $p + '*')) } | " +
            "Where-Object { $_.ProcessId -ne $PID } | " +
            "ForEach-Object { try { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop } catch {} }";

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" +
                        script.Replace("\"", "\\\"") + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var p = Process.Start(psi);
        if (p != null)
        {
            await p.WaitForExitAsync();
        }
    }

    private void RemoveRunEntries()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);

            if (run == null) return;

            foreach (var name in run.GetValueNames())
            {
                var value = run.GetValue(name)?.ToString() ?? "";
                bool oldName = name.Contains("PCAI", StringComparison.OrdinalIgnoreCase)
                               || name.Contains("PC AI Server", StringComparison.OrdinalIgnoreCase);
                bool oldTarget = value.Contains(oldRoot, StringComparison.OrdinalIgnoreCase)
                                 || value.Contains("PCAI Server", StringComparison.OrdinalIgnoreCase);

                if (oldName && oldTarget)
                {
                    run.DeleteValue(name, throwOnMissingValue: false);
                    Append($"Удалён автозапуск: {name}");
                }
            }
        }
        catch (Exception ex)
        {
            Append("Автозапуск: " + ex.Message);
        }
    }

    private void RemoveKnownShortcuts()
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs)
        };

        string[] names =
        {
            "PCAI Server.lnk",
            "PC AI Server.lnk",
            "PCAI Server Settings.lnk",
            "PC AI Server Settings.lnk"
        };

        foreach (var folder in folders.Where(Directory.Exists))
        {
            foreach (var name in names)
            {
                var file = Path.Combine(folder, name);
                TryDeleteFile(file);
            }
        }
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            Append("Удалён ярлык: " + path);
        }
        catch (Exception ex)
        {
            Append($"Не удалось удалить ярлык {path}: {ex.Message}");
        }
    }

    private void DeleteDirectorySafely(string path)
    {
        if (!Directory.Exists(path))
        {
            Append("Старая папка уже отсутствует.");
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            Thread.Sleep(900);
            Directory.Delete(path, recursive: true);
        }
    }

    private void Append(string text)
    {
        if (InvokeRequired)
        {
            Invoke(() => Append(text));
            return;
        }

        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
    }
}
