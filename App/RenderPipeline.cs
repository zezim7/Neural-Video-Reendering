using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace VideoNR;

internal sealed record RenderLayer(int Height, double Intensity, double Tone, double Structure);

internal sealed class RenderPipeline(string directory)
{
    private readonly string ffmpeg = Path.Combine(directory, "ffmpeg.exe");
    private readonly string ffprobe = Path.Combine(directory, "ffprobe.exe");
    private readonly string inference = Path.Combine(directory, "realesrgan-ncnn-vulkan.exe");

    internal bool Ready(bool neural) => File.Exists(ffmpeg) && File.Exists(ffprobe)
        && (!neural || (File.Exists(inference) && Directory.Exists(Path.Combine(directory, "models"))));

    internal async Task ExportAsync(string input, string output, double start, double end, string codec,
        string model, RenderLayer[] settings, Action<int, string> report, CancellationToken cancellation)
    {
        using var files = new FrameWorkspace(output);
        var trim = end > start ? new[] { "-t", Number(end - start) } : [];
        var seek = new[] { "-ss", Number(start) };
        var format = new[] { "-c:v", codec == "hevc" ? "libx265" : "libx264", "-preset", "fast",
            "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", files.Partial };
        var layers = settings.Where(layer => layer.Intensity > 0).ToArray();
        if (layers.Length == 0)
        {
            report(25, "Codificando vídeo");
            await Ffmpeg([.. seek, "-i", input, .. trim, "-map", "0:v:0", "-map", "0:a?",
                "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", .. format], cancellation);
        }
        else
        {
            var metadata = await Run(ffprobe,
                ["-v", "error", "-select_streams", "v:0", "-show_entries",
                 "stream=width,height,avg_frame_rate,r_frame_rate,color_transfer:stream_side_data=rotation", "-of", "json", input], cancellation);
            using var probe = JsonDocument.Parse(metadata);
            var stream = probe.RootElement.GetProperty("streams").EnumerateArray().FirstOrDefault();
            if (stream.ValueKind != JsonValueKind.Object) throw new IOException("O arquivo não tem uma faixa de vídeo.");
            if (stream.TryGetProperty("color_transfer", out var transfer) && transfer.GetString() is "smpte2084" or "arib-std-b67")
                throw new IOException("Desative Melhoria NR para exportar este vídeo HDR.");
            var width = (stream.GetProperty("width").GetInt32() + 1) / 2 * 2;
            var height = (stream.GetProperty("height").GetInt32() + 1) / 2 * 2;
            if (stream.TryGetProperty("side_data_list", out var sideData)
                && sideData.EnumerateArray().Any(data => data.TryGetProperty("rotation", out var rotation)
                    && rotation.TryGetInt32(out var degrees) && Math.Abs(degrees % 180) == 90))
                (width, height) = (height, width);
            var rate = stream.GetProperty("avg_frame_rate").GetString();
            if (rate is null or "0/0" or "0/1") rate = stream.GetProperty("r_frame_rate").GetString();
            if (rate is null or "0/0" or "0/1") throw new IOException("Não foi possível determinar a taxa de quadros.");
            var size = $"{width}:{height}";
            var current = files.Directory("original");
            report(5, "Extraindo quadros");
            await Ffmpeg([.. seek, "-i", input, .. trim, "-an",
                "-vf", $"fps={rate},scale={size}", "-start_number", "1", Frames(current)], cancellation);

            for (var i = 0; i < layers.Length; i++)
            {
                var layer = layers[i];
                report(15 + 65 * i / layers.Length, $"Melhoria neural · camada {i + 1}/{layers.Length}");
                var small = files.Directory($"entrada-{i}");
                var enhanced = files.Directory($"melhorado-{i}");
                var mixed = files.Directory($"camada-{i}");
                var internalHeight = layer.Height == 0 ? height : Math.Min(height, layer.Height);
                await Ffmpeg([.. Sequence(current, rate), "-vf", $"scale=-2:{internalHeight}", Frames(small)], cancellation);
                await Run(inference, ["-i", small, "-o", enhanced, "-m", Path.Combine(directory, "models"),
                    "-n", model, "-s", model == "realesr-animevideov3" ? "2" : "4", "-f", "png"], cancellation);

                var adjustments = $"scale={size},setsar=1,format=yuv444p,eq=saturation={Number(0.9 + 0.2 * layer.Tone)}"
                    + $",unsharp=5:5:{Number(0.35 * layer.Structure)}";
                var blend = $"[0:v]setsar=1,format=yuv444p[base];[1:v]{adjustments}[nr];"
                    + $"[nr][base]blend=all_mode=normal:all_opacity={Number(layer.Intensity)}:shortest=1[v]";
                if (Directory.GetFiles(current, "*.png").Length != Directory.GetFiles(enhanced, "*.png").Length)
                    throw new IOException("O motor neural não devolveu todos os quadros.");
                await Ffmpeg([.. Sequence(current, rate), .. Sequence(enhanced, rate),
                    "-filter_complex_threads", "1", "-filter_complex", blend, "-map", "[v]", Frames(mixed)], cancellation);
                Directory.Delete(current, true);
                Directory.Delete(small, true);
                Directory.Delete(enhanced, true);
                current = mixed;
            }

            report(85, "Codificando vídeo e áudio");
            await Ffmpeg([.. Sequence(current, rate), .. seek, "-i", input, .. trim, "-map", "0:v:0",
                "-map", "1:a?", "-af", "apad", "-shortest", .. format], cancellation);
        }

        cancellation.ThrowIfCancellationRequested();
        if (new FileInfo(files.Partial) is not { Exists: true, Length: > 0 })
            throw new IOException("O motor não produziu um arquivo de saída.");
        File.Move(files.Partial, output);
        report(100, "Concluído");
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    private static string Frames(string folder) => Path.Combine(folder, "%09d.png");
    private static string[] Sequence(string folder, string rate) =>
        ["-framerate", rate, "-start_number", "1", "-i", Frames(folder)];

    private Task<string> Ffmpeg(string[] arguments, CancellationToken cancellation) =>
        Run(ffmpeg, ["-hide_banner", "-loglevel", "error", "-nostdin", "-n", .. arguments], cancellation);

    private static async Task<string> Run(string tool, string[] arguments, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(tool)
        {
            CreateNoWindow = true, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new IOException($"Falha ao iniciar {Path.GetFileName(tool)}.");
        using var registration = cancellation.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        });
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var result = await output;
        var detail = await errors;
        cancellation.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
        {
            var last = detail.Trim();
            if (last.Length > 600) last = last[^600..];
            throw new IOException($"{Path.GetFileName(tool)} retornou código {process.ExitCode}. {last}");
        }
        return result;
    }

    private sealed class FrameWorkspace(string output) : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), $"VideoNR-{Guid.NewGuid():N}");
        internal string Partial { get; } = Path.Combine(Path.GetDirectoryName(output)!, $".VideoNR-{Guid.NewGuid():N}.mp4");

        internal string Directory(string name) =>
            System.IO.Directory.CreateDirectory(Path.Combine(root, name)).FullName;

        public void Dispose()
        {
            try { if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            try { File.Delete(Partial); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
