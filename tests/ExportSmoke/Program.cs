using System.Diagnostics;
using System.Globalization;
using VideoNR;

var (motor, input, output) = (Path.GetFullPath(args[0]), Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
var renderer = new RenderPipeline(motor);
if (!renderer.Ready(neural: true)) throw new InvalidOperationException("O ZIP está incompleto.");

foreach (var codec in new[] { "h264", "hevc" })
{
    var destination = Path.Combine(output, codec + ".mp4");
    await renderer.ExportAsync(input, destination, 0.5, 1.5, codec,
        "realesr-animevideov3", [], (_, _) => { }, CancellationToken.None);

    using var probe = Process.Start(new ProcessStartInfo(Path.Combine(motor, "ffprobe.exe"))
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        ArgumentList = { "-v", "error", "-show_entries", "format=duration:stream=codec_name", "-of", "default=nw=1", destination },
    }) ?? throw new InvalidOperationException("Falha ao consultar o vídeo.");
    var lines = (await probe.StandardOutput.ReadToEndAsync()).Split('\n', StringSplitOptions.TrimEntries);
    await probe.WaitForExitAsync();
    if (probe.ExitCode != 0 || !lines.Contains("codec_name=" + codec) || !lines.Contains("codec_name=aac"))
        throw new InvalidOperationException("O vídeo não preservou as faixas e o codec esperados.");
    var duration = double.Parse(lines.Single(line => line.StartsWith("duration="))[9..], CultureInfo.InvariantCulture);
    if (Math.Abs(duration - 1.0) > 0.08) throw new InvalidOperationException("Duração de corte incorreta.");
    Console.WriteLine($"{codec}: corte, vídeo e áudio verificados.");
}
