using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace EasyTagger.Core;

public static class Encoder
{
    static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v",
    };

    public static string FindFfmpeg(TagConfig config) =>
        ResolveTool(config.FfmpegPath, "ffmpeg", AppContext.BaseDirectory, [
            @"C:\ffmpeg\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
        ]);

    public static string FindFfprobe(TagConfig config) =>
        ResolveTool(config.FfprobePath, "ffprobe", AppContext.BaseDirectory, [
            @"C:\ffmpeg\ffprobe.exe",
            @"C:\Program Files\ffmpeg\bin\ffprobe.exe",
        ]);

    public static bool ToolsAvailable(TagConfig config, out string message)
    {
        var ffmpeg = FindFfmpeg(config);
        var ffprobe = FindFfprobe(config);
        if (ffmpeg.Length > 0 && ffprobe.Length > 0)
        {
            message = $"ffmpeg: {ffmpeg} | ffprobe: {ffprobe}";
            return true;
        }

        var missing = new List<string>();
        if (ffmpeg.Length == 0)
            missing.Add("ffmpeg");
        if (ffprobe.Length == 0)
            missing.Add("ffprobe");
        message = string.Join(", ", missing);
        return false;
    }

    public static string MaybeReencode(string path, TagConfig config, Action<string>? log = null,
        Action<double>? progress = null, CancellationToken cancel = default)
    {
        if (!config.ReencodeEnabled)
            return path;
        return ReencodeH264(path, config, log, progress, cancel);
    }

    // progress gets 0 to 1 while ffmpeg runs. Cancelling stops ffmpeg, removes the half-written
    // file and leaves the source as it was, then throws OperationCanceledException.
    public static string ReencodeH264(string srcPath, TagConfig config, Action<string>? log = null,
        Action<double>? progress = null, CancellationToken cancel = default)
    {
        log ??= static _ => { };
        var ext = Path.GetExtension(srcPath);
        if (!VideoExtensions.Contains(ext))
        {
            log($"Re-Encode übersprungen (kein Video): {Path.GetFileName(srcPath)}");
            return srcPath;
        }

        var ffmpeg = FindFfmpeg(config);
        if (ffmpeg.Length == 0)
            throw new InvalidOperationException("ffmpeg nicht gefunden");

        cancel.ThrowIfCancellationRequested();
        var info = Probe(srcPath, config);
        log($"Re-Encode Start: {Path.GetFileName(srcPath)} ({info.Width}x{info.Height} @ {info.Fps.ToString("0.###", CultureInfo.InvariantCulture)} fps)");

        var folder = Path.GetDirectoryName(srcPath)!;
        var stem = Path.GetFileNameWithoutExtension(srcPath);
        var tmpPath = Path.Combine(folder, $"{stem}.__reenc__.mp4");
        var finalPath = OutputPath(srcPath);
        var replacesSource = string.Equals(Path.GetFullPath(finalPath), Path.GetFullPath(srcPath), StringComparison.OrdinalIgnoreCase);
        if (File.Exists(tmpPath))
            File.Delete(tmpPath);

        var args = new List<string>
        {
            "-y", "-nostdin", "-hide_banner", "-loglevel", "error", "-nostats", "-progress", "pipe:1",
            "-i", srcPath,
            "-map", "0:v:0", "-map", "0:a?",
            "-c:v", "libx264",
            "-preset", string.IsNullOrWhiteSpace(config.ReencodePreset) ? "medium" : config.ReencodePreset,
            "-pix_fmt", "yuv420p",
            "-r", info.FpsArgument,
            "-c:a", "aac",
            "-b:a", $"{config.ReencodeAudioBitrate}k",
            "-movflags", "+faststart",
        };
        if (config.ReencodeMode == "bitrate")
        {
            var video = Math.Max(100, config.ReencodeVideoBitrate);
            args.AddRange(["-b:v", $"{video}k", "-maxrate", $"{(int)(video * 1.2)}k", "-bufsize", $"{video * 2}k"]);
        }
        else
        {
            args.AddRange(["-crf", config.ReencodeCrf.ToString(CultureInfo.InvariantCulture)]);
        }
        args.Add(tmpPath);

        var (code, error) = RunEncode(ffmpeg, args, info.Duration, progress, cancel);
        if (cancel.IsCancellationRequested)
        {
            TryDelete(tmpPath);
            cancel.ThrowIfCancellationRequested();
        }
        if (code != 0 || !File.Exists(tmpPath) || new FileInfo(tmpPath).Length < 1000)
        {
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "ffmpeg fehlgeschlagen" : error[..Math.Min(500, error.Length)]);
        }

        if (config.ReencodeKeepOriginal)
        {
            var kept = Path.Combine(folder, $"{stem}.orig{ext}");
            var n = 2;
            while (File.Exists(kept))
            {
                kept = Path.Combine(folder, $"{stem}.orig ({n}){ext}");
                n++;
            }
            File.Move(srcPath, kept);
            log($"Original behalten: {Path.GetFileName(kept)}");
        }
        else
        {
            TryDelete(srcPath);
        }

        // Only the source itself may be replaced. Any other file under that name belongs to another video.
        File.Move(tmpPath, finalPath, overwrite: replacesSource);

        // Ein gesperrtes Original mit anderer Endung bekommt nach dem
        // Umbenennen einen zweiten Versuch, damit keine Doppelung bleibt.
        if (!config.ReencodeKeepOriginal &&
            !string.Equals(srcPath, finalPath, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(srcPath);
        }

        log($"Re-Encode fertig: {Path.GetFileName(finalPath)}");
        return finalPath;
    }

    // Where the mp4 goes. An mp4 of a different video can already sit under the same stem,
    // for example after two long titles were cut to the same 20 characters.
    internal static string OutputPath(string srcPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(srcPath))!;
        var stem = Path.GetFileNameWithoutExtension(srcPath);
        var path = Path.Combine(folder, stem + ".mp4");
        if (!File.Exists(path) || string.Equals(path, Path.GetFullPath(srcPath), StringComparison.OrdinalIgnoreCase))
            return path;
        return Renamer.ResolveCollision(folder, stem, ".mp4");
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file is still locked; the caller keeps the encoded result.
        }
    }

    static ProbeInfo Probe(string path, TagConfig config)
    {
        var ffprobe = FindFfprobe(config);
        if (ffprobe.Length == 0)
            throw new InvalidOperationException("ffprobe nicht gefunden");
        var (code, output, error) = RunCapture(ffprobe, [
            "-v", "error", "-show_streams", "-show_format", "-print_format", "json", path,
        ]);
        if (code != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "ffprobe fehlgeschlagen" : error);
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(output) ? "{}" : output);
        var root = doc.RootElement;
        JsonElement? video = null;
        if (root.TryGetProperty("streams", out var streams))
        {
            foreach (var stream in streams.EnumerateArray())
            {
                if (stream.TryGetProperty("codec_type", out var kind) && kind.GetString() == "video")
                {
                    video = stream;
                    break;
                }
            }
        }
        if (video == null)
            throw new InvalidOperationException("Kein Videostream gefunden");

        var (fps, fpsArg) = Fps(video.Value.TryGetProperty("avg_frame_rate", out var avg) ? avg.GetString() : "");
        if (fps <= 0)
            (fps, fpsArg) = Fps(video.Value.TryGetProperty("r_frame_rate", out var raw) ? raw.GetString() : "");
        if (fps <= 0)
            (fps, fpsArg) = (25, "25/1");
        var width = video.Value.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
        var height = video.Value.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
        var duration = 0.0;
        if (root.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var length) &&
            double.TryParse(length.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            duration = seconds;
        return new ProbeInfo(fps, fpsArg, width, height, duration);
    }

    // Reads the seconds done from one line of "-progress" output. ffmpeg writes out_time_us,
    // and older builds the same microseconds as out_time_ms. "N/A" or a negative start is no value.
    internal static double? ProgressSeconds(string? line)
    {
        var cut = (line ?? "").IndexOf('=');
        if (cut <= 0)
            return null;
        var key = line![..cut].Trim();
        var value = line[(cut + 1)..].Trim();
        if (key is not ("out_time_us" or "out_time_ms") ||
            !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var micro) || micro < 0)
            return null;
        return micro / 1_000_000.0;
    }

    static (double Fps, string Argument) Fps(string? rate)
    {
        rate = (rate ?? "").Trim();
        if (rate.Length == 0 || rate is "0/0" or "N/A" or "0")
            return (0, "");
        var parts = rate.Split('/');
        if (parts.Length == 2 &&
            double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var num) &&
            double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var den) &&
            den > 0 && num > 0)
            return (num / den, $"{parts[0]}/{parts[1]}");
        if (double.TryParse(rate, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0)
            return (value, value.ToString(CultureInfo.InvariantCulture));
        return (0, "");
    }

    internal static string ResolveTool(string? configured, string name, string? appDirectory, IEnumerable<string> fallbacks)
    {
        configured = (configured ?? "").Trim().Trim('"');
        if (configured.Length > 0 && File.Exists(configured))
            return configured;
        var beside = BesideApp(appDirectory, name);
        if (beside.Length > 0)
            return beside;
        var found = FindOnPath(name);
        if (found.Length > 0)
            return found;
        foreach (var path in fallbacks)
        {
            if (File.Exists(path))
                return path;
        }
        return "";
    }

    static string BesideApp(string? appDirectory, string name)
    {
        if (string.IsNullOrWhiteSpace(appDirectory))
            return "";
        var fileName = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var candidate = Path.Combine(appDirectory, fileName);
        return File.Exists(candidate) ? Path.GetFullPath(candidate) : "";
    }

    static string FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), OperatingSystem.IsWindows() ? name + ".exe" : name);
            if (File.Exists(candidate))
                return candidate;
        }
        return "";
    }

    static (int Code, string Error) RunEncode(string file, IReadOnlyList<string> args, double duration,
        Action<double>? progress, CancellationToken cancel)
    {
        using var process = Process.Start(Start(file, args)) ?? throw new InvalidOperationException("Prozessstart fehlgeschlagen");
        using var stop = cancel.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // ffmpeg had already finished.
            }
        });
        var error = process.StandardError.ReadToEndAsync();
        while (process.StandardOutput.ReadLine() is { } line)
        {
            if (duration > 0 && progress != null && ProgressSeconds(line) is double done)
                progress(Math.Clamp(done / duration, 0, 1));
        }
        process.WaitForExit();
        return (process.ExitCode, error.GetAwaiter().GetResult());
    }

    static ProcessStartInfo Start(string file, IReadOnlyList<string> args)
    {
        var start = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        return start;
    }

    static (int Code, string Output, string Error) RunCapture(string file, IReadOnlyList<string> args)
    {
        using var process = Process.Start(Start(file, args)) ?? throw new InvalidOperationException("Prozessstart fehlgeschlagen");
        // Both pipes drain at once. Reading them one after the other hangs as soon as
        // a broken file fills the error pipe while stdout still waits for its end.
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error.GetAwaiter().GetResult());
    }

    sealed record ProbeInfo(double Fps, string FpsArgument, int Width, int Height, double Duration);
}
