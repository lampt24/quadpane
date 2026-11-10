using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia.Controls;

namespace QuadPane;

internal static class UpdateService
{
    internal const string Repo = "lampt24/quadpane";
    private static readonly HttpClient Client = new(new HttpClientHandler
    {
        UseProxy = true,
        DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        AllowAutoRedirect = true,
    }) { Timeout = TimeSpan.FromMinutes(10) };

    internal sealed record Asset(string Name, string Url);
    internal sealed record Release(string Tag, Asset[] Assets);

    static UpdateService() => Client.DefaultRequestHeaders.UserAgent.ParseAdd("QuadPane-updater");

    internal static async Task<Release?> GetNewReleaseAsync()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://github.com/{Repo}/releases/latest");
        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        var url = response.RequestMessage?.RequestUri;
        var prefix = $"/{Repo}/releases/tag/";
        if (url is null || url.Scheme != Uri.UriSchemeHttps || url.Host != "github.com" ||
            !url.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("GitHub không trả về tag release hợp lệ.");
        var tag = url.AbsolutePath[prefix.Length..];
        if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var latest) ||
            tag != $"v{latest.ToString(3)}")
            throw new InvalidDataException("Tag release không hợp lệ.");
        var current = typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0);
        if (latest <= current) return null;
        var baseUrl = $"https://github.com/{Repo}/releases/download/{tag}/";
        var assets = new[] { "win-x64", "win-x86", "win-arm64" }
            .Select(rid => new Asset($"QuadPane-{tag}-{rid}.zip", $"{baseUrl}QuadPane-{tag}-{rid}.zip"))
            .Append(new Asset("SHA256SUMS.txt", $"{baseUrl}SHA256SUMS.txt"))
            .ToArray();
        return new Release(tag, assets);
    }

    internal static bool Confirm(Window owner, string message, string title = "Cập nhật QuadPane") =>
        MessageBoxW(owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, message, title, 0x24) == 6;

    internal static void ShowError(Window owner, string message, string title = "Cập nhật QuadPane") =>
        MessageBoxW(owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, message, title, 0x10);

    internal static void ShowInfo(Window owner, string message, string title = "QuadPane") =>
        MessageBoxW(owner.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero, message, title, 0x40);

    internal static async Task InstallAsync(Release release)
    {
        var executable = Environment.ProcessPath;
        if (executable is null || !Path.GetFileName(executable).Equals("QuadPane.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Chỉ cập nhật được bản ứng dụng đã đóng gói.");

        var rid = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => throw new PlatformNotSupportedException("Kiến trúc máy chưa được hỗ trợ."),
        };
        var name = $"QuadPane-{release.Tag}-{rid}.zip";
        var archive = release.Assets.SingleOrDefault(a => a.Name == name)
            ?? throw new InvalidDataException($"Release thiếu {name}.");
        var checksums = release.Assets.SingleOrDefault(a => a.Name == "SHA256SUMS.txt")
            ?? throw new InvalidDataException("Release thiếu SHA256SUMS.txt.");
        ValidateUrl(archive, release.Tag);
        ValidateUrl(checksums, release.Tag);

        var install = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var parent = Directory.GetParent(install)?.FullName ?? throw new InvalidOperationException("Không tìm thấy thư mục ứng dụng.");
        var stage = Path.Combine(parent, $".QuadPane-update-{Guid.NewGuid():N}");
        var backup = Path.Combine(parent, $".QuadPane-backup-{Guid.NewGuid():N}");
        var download = Path.Combine(Path.GetTempPath(), $"QuadPane-{Guid.NewGuid():N}.zip");
        var script = Path.Combine(Path.GetTempPath(), $"QuadPane-updater-{Guid.NewGuid():N}.ps1");
        try
        {
            var sums = await Client.GetStringAsync(checksums.Url);
            var expected = sums.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .SingleOrDefault(parts => parts.Length == 2 && parts[1] == name)?[0];
            if (expected is null || expected.Length != 64 || !expected.All(Uri.IsHexDigit))
                throw new InvalidDataException($"SHA256SUMS.txt không có mã hợp lệ cho {name}.");

            await using (var source = await Client.GetStreamAsync(archive.Url))
            await using (var target = File.Create(download))
                await source.CopyToAsync(target);
            await using (var file = File.OpenRead(download))
            {
                var actual = Convert.ToHexString(await SHA256.HashDataAsync(file));
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Mã SHA-256 của gói cập nhật không khớp.");
            }

            ZipFile.ExtractToDirectory(download, stage);
            if (!File.Exists(Path.Combine(stage, "QuadPane.exe")) ||
                !File.Exists(Path.Combine(stage, "Assets", "EverythingSdk", "LICENSE.txt")))
                throw new InvalidDataException("Gói cập nhật thiếu file ứng dụng.");
            // Giữ trình gỡ cài đặt khi bản cài Inno Setup tự cập nhật bằng gói ZIP.
            foreach (var file in Directory.EnumerateFiles(install, "unins*.*"))
                File.Copy(file, Path.Combine(stage, Path.GetFileName(file)));

            File.Copy(Path.Combine(install, "Updater.ps1"), script);
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetTempPath(),
            };
            foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                         "-AppProcessId", Environment.ProcessId.ToString(), "-InstallPath", install,
                         "-StagePath", stage, "-BackupPath", backup, "-ScriptPath", script })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Không chạy được trình cập nhật.");
        }
        catch
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            if (File.Exists(script)) File.Delete(script);
            throw;
        }
        finally
        {
            try { if (File.Exists(download)) File.Delete(download); }
            catch (IOException) { /* File tạm không được cản bước khởi động bản mới. */ }
        }
    }

    private static void ValidateUrl(Asset asset, string tag)
    {
        if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps
            || url.Host != "github.com" || url.AbsolutePath != $"/{Repo}/releases/download/{tag}/{asset.Name}")
            throw new InvalidDataException("Địa chỉ tải release không hợp lệ.");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint options);
}
