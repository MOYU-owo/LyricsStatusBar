using System.IO;
using Microsoft.Win32;

namespace LyricsStatusBar.App;

internal sealed record BetterNcmDeploymentResult(string Status, string? DataDirectory, bool Changed);

internal static class BetterNcmPluginDeployment
{
    private const string RegistryPath = @"Software\LyricsStatusBar";
    private const string RegistryValueName = "BetterNCMDataDir";
    private const string PluginSlug = "lyrics_statusbar_bridge";
    private static readonly string[] RuntimeFiles = ["manifest.json", "plugin.js", "native.dll"];

    public static BetterNcmDeploymentResult TryInstall()
    {
        var payloadDirectory = Path.Combine(AppContext.BaseDirectory, "BetterNCM-Plugin");
        if (RuntimeFiles.Any(file => !File.Exists(Path.Combine(payloadDirectory, file))))
        {
            return new BetterNcmDeploymentResult("安装包中缺少 BetterNCM 桥接文件", null, false);
        }

        var dataDirectory = CandidateDataDirectories().FirstOrDefault(IsBetterNcmDataDirectory);
        if (dataDirectory is null)
        {
            return new BetterNcmDeploymentResult("未检测到 BetterNCM；安装后会自动补装桥接插件", null, false);
        }

        try
        {
            var changed = false;
            var runtimeDirectory = Path.Combine(dataDirectory, "plugins_runtime", PluginSlug);
            Directory.CreateDirectory(runtimeDirectory);
            foreach (var fileName in RuntimeFiles)
            {
                changed |= CopyIfDifferent(
                    Path.Combine(payloadDirectory, fileName),
                    Path.Combine(runtimeDirectory, fileName));
            }

            var packageSource = Path.Combine(payloadDirectory, "LyricsStatusBarBridge.plugin");
            if (File.Exists(packageSource))
            {
                var packageDirectory = Path.Combine(dataDirectory, "plugins");
                Directory.CreateDirectory(packageDirectory);
                changed |= CopyIfDifferent(packageSource, Path.Combine(packageDirectory, "LyricsStatusBarBridge.plugin"));
            }

            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key?.SetValue(RegistryValueName, dataDirectory, RegistryValueKind.String);
            var status = changed
                ? $"BetterNCM 桥接已安装/更新：{dataDirectory}（请重启网易云音乐）"
                : $"BetterNCM 桥接已就绪：{dataDirectory}";
            return new BetterNcmDeploymentResult(status, dataDirectory, changed);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new BetterNcmDeploymentResult($"BetterNCM 桥接部署失败：{exception.Message}", dataDirectory, false);
        }
    }

    public static IEnumerable<string> CandidateDataDirectories()
    {
        var candidates = new List<string>();
        using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
        {
            if (key?.GetValue(RegistryValueName) is string savedPath)
            {
                candidates.Add(savedPath);
            }
        }

        var environmentPath = Environment.GetEnvironmentVariable("BETTERNCM_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            candidates.Add(environmentPath);
        }

        candidates.Add(@"C:\betterncm");
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            candidates.Add(Path.Combine(appData, "BetterNCM"));
        }
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, "BetterNCM"));
        }

        try
        {
            candidates.AddRange(
                DriveInfo.GetDrives()
                    .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)
                    .Select(drive => Path.Combine(drive.RootDirectory.FullName, "betterncm")));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsBetterNcmDataDirectory(string path) =>
        Directory.Exists(path) &&
        (File.Exists(Path.Combine(path, "betterncm.dll")) ||
         Directory.Exists(Path.Combine(path, "plugins")) ||
         Directory.Exists(Path.Combine(path, "plugins_runtime")));

    private static bool CopyIfDifferent(string source, string destination)
    {
        if (FilesEqual(source, destination))
        {
            return false;
        }

        File.Copy(source, destination, overwrite: true);
        return true;
    }

    private static bool FilesEqual(string first, string second)
    {
        var firstInfo = new FileInfo(first);
        var secondInfo = new FileInfo(second);
        if (!secondInfo.Exists || firstInfo.Length != secondInfo.Length)
        {
            return false;
        }

        const int bufferSize = 81920;
        using var firstStream = File.OpenRead(first);
        using var secondStream = File.OpenRead(second);
        var firstBuffer = new byte[bufferSize];
        var secondBuffer = new byte[bufferSize];
        while (true)
        {
            var firstRead = firstStream.Read(firstBuffer, 0, firstBuffer.Length);
            var secondRead = secondStream.Read(secondBuffer, 0, secondBuffer.Length);
            if (firstRead != secondRead)
            {
                return false;
            }
            if (firstRead == 0)
            {
                return true;
            }
            if (!firstBuffer.AsSpan(0, firstRead).SequenceEqual(secondBuffer.AsSpan(0, secondRead)))
            {
                return false;
            }
        }
    }
}