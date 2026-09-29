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
        ResolveTool(config.FfmpegPath, "ffmpeg", [
            @"C:\ffmpeg\ffmpeg.exe",
            @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
        ]);

    public static string FindFfprobe(TagConfig config) =>
        ResolveTool(config.FfprobePath, "ffprobe", [
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

    public static string MaybeReencode(string path, TagConfig config, Action<string>? log = null)
    {
        if (!config.ReencodeEnabled)
            return path;
        return ReencodeH264(path, config, log);
    }

    public static string ReencodeH264(string srcPath, TagConfig config, Action<string>? log = null)
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

        var info = Probe(srcPath, config);
        log($"Re-Encode Start: {Path.GetFileName(srcPath)} ({info.Width}x{info.Height} @ {info.Fps.ToString("0.###", CultureInfo.InvariantCulture)} fps)");

        var folder = Path.GetDirectoryName(srcPath)!;
        var stem = Path.GetFileNameWithoutExtension(srcPath);
        var tmpPath = Path.Combine(folder, $"{stem}.__reenc__.mp4");
        var finalPath = Path.Combine(folder, $"{stem}.mp4");
        if (File.Exists(tmpPath))
            File.Delete(tmpPath);

        var args = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
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

        var (code, error) = Run(ffmpeg, args);
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

        if (!string.Equals(Path.GetFullPath(tmpPath), Path.GetFullPath(finalPath), StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(finalPath))
                File.Delete(finalPath);
            File.Move(tmpPath, finalPath);
        }
        else
        {
            finalPath = tmpPath;
        }

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
        return new ProbeInfo(fps, fpsArg, width, height);
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

    static string ResolveTool(string? configured, string name, IEnumerable<string> fallbacks)
    {
        configured = (configured ?? "").Trim().Trim('"');
        if (configured.Length > 0 && File.Exists(configured))
            return configured;
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

    static (int Code, string Error) Run(string file, IReadOnlyList<string> args)
    {
        var (code, _, error) = RunCapture(file, args);
        return (code, error);
    }

    static (int Code, string Output, string Error) RunCapture(string file, IReadOnlyList<string> args)
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
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Prozessstart fehlgeschlagen");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }

    sealed record ProbeInfo(double Fps, string FpsArgument, int Width, int Height);
}
