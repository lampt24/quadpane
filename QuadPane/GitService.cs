using System.Diagnostics;

namespace QuadPane;

internal sealed record GitInfo(string Branch, int Staged, int Unstaged);

internal static class GitService
{
    public static async Task<GitInfo> StatusAsync(string folder, CancellationToken token)
    {
        var output = await RunAsync(folder, ["status", "--porcelain=v1", "--branch"], token);
        return ParseStatus(output);
    }

    internal static GitInfo ParseStatus(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0 || !lines[0].StartsWith("## ", StringComparison.Ordinal))
            throw new InvalidOperationException("Không đọc được trạng thái Git.");
        string branch = lines[0][3..].Split("...", 2)[0].Trim();
        if (branch.StartsWith("No commits yet on ", StringComparison.Ordinal)) branch = branch[18..];
        if (branch.StartsWith("HEAD (no branch)", StringComparison.Ordinal)) branch = "detached HEAD";
        int staged = 0, unstaged = 0;
        foreach (var line in lines.Skip(1))
        {
            if (line.Length < 2) continue;
            if (line[0] == '?' && line[1] == '?') { unstaged++; continue; }
            if (line[0] != ' ') staged++;
            if (line[1] != ' ') unstaged++;
        }
        return new GitInfo(branch, staged, unstaged);
    }

    internal static async Task SelfCheckAsync()
    {
        var info = ParseStatus("## main...origin/main\nM  staged.txt\n M edited.txt\n?? new.txt\n");
        if (info != new GitInfo("main", 1, 2)) throw new Exception("Git status parser failed.");
        if (ParseStatus("## No commits yet on trunk\n") != new GitInfo("trunk", 0, 0))
            throw new Exception("Git initial branch parser failed.");
        if (ParseStatus("## HEAD (no branch)\n") != new GitInfo("detached HEAD", 0, 0))
            throw new Exception("Git detached HEAD parser failed.");
        if (string.IsNullOrWhiteSpace((await StatusAsync(Directory.GetCurrentDirectory(), CancellationToken.None)).Branch))
            throw new Exception("Git status command failed.");
    }

    public static async Task<string> RunAsync(string folder, IReadOnlyList<string> args, CancellationToken token)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("git")
            {
                WorkingDirectory = folder,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (!process.Start()) throw new InvalidOperationException("Không khởi chạy được Git.");
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Lệnh Git thất bại." : error.Trim());
        return output;
    }
}
