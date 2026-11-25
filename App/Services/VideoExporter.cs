using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace VideoNR.Services;

internal sealed record ExportRequest(
    string AdapterPath,
    string EnginePath,
    string InputPath,
    string OutputPath,
    double TrimStartSeconds,
    double TrimEndSeconds,
    string Codec,
    string ModelTag,
    string[] EncodedLayers,
    int MaxFrames = 0);

internal sealed class VideoExporter
{
    internal static IReadOnlyList<string> BuildArguments(ExportRequest request, string targetOutputPath)
    {
        var prefixCount = request.MaxFrames > 0 ? 2 : 0;
        var args = new List<string>(prefixCount + 7 + request.EncodedLayers.Length);
        if (request.MaxFrames > 0)
        {
            args.Add("--max-frames");
            args.Add(request.MaxFrames.ToString(CultureInfo.InvariantCulture));
        }
        args.Add(request.EnginePath);
        args.Add(request.InputPath);
        args.Add(targetOutputPath);
        args.Add(request.Codec);
        args.Add(request.TrimStartSeconds.ToString("R", CultureInfo.InvariantCulture));
        args.Add(request.TrimEndSeconds.ToString("R", CultureInfo.InvariantCulture));
        args.Add(request.ModelTag);
        args.AddRange(request.EncodedLayers);
        return args;
    }

    internal static string UniqueOutputPath(string directory, string stem)
    {
        var path = Path.Combine(directory, stem + ".mp4");
        for (var i = 2; File.Exists(path); i++)
            path = Path.Combine(directory, $"{stem}-{i}.mp4");
        return path;
    }

    internal async Task ExportAsync(
        ExportRequest request,
        Action<int> onProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var replaceExisting = File.Exists(request.OutputPath);
        var folder = Path.GetDirectoryName(request.OutputPath)
            ?? throw new IOException("Pasta de destino inválida.");
        var exportTarget = replaceExisting
            ? UniqueOutputPath(folder, Path.GetFileNameWithoutExtension(request.OutputPath) + "-tmp")
            : request.OutputPath;

        var info = new ProcessStartInfo(request.AdapterPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8
        };
        foreach (var argument in BuildArguments(request, exportTarget))
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info)
            ?? throw new IOException("Não foi possível abrir o adaptador do motor de exportação.");

        using var cancelRegistration = cancellationToken.Register(() =>
        {
            try
            {
                process.StandardInput.WriteLine("CANCEL");
                process.StandardInput.Flush();
                process.StandardInput.Close();
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        });

        try
        {
            string? detail = null;
            string? worker = null;
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line.StartsWith("PROGRESS ", StringComparison.Ordinal) && int.TryParse(line.AsSpan(9), out var value))
                {
                    onProgress(Math.Clamp(value, 0, 10000));
                }
                else if (line.StartsWith("WORKER ", StringComparison.Ordinal))
                {
                    worker = line[7..];
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    detail = line;
                }
            }

            await process.WaitForExitAsync();
            if (process.ExitCode == 3 || cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);

            if (process.ExitCode == 2 && request.MaxFrames > 0)
                throw new IOException("Engine.exe desatualizado (não suporta captura de 1 frame). Recompile com Build.ps1.");

            if (process.ExitCode != 0 || new FileInfo(exportTarget) is not { Exists: true, Length: > 0 })
            {
                var reason = detail ?? "Confira o codec de entrada e a compatibilidade da GPU.";
                throw new IOException($"O motor não concluiu o processamento. {reason}" + (worker is null ? "" : $" (tarefa {worker})"));
            }

            if (replaceExisting)
                File.Move(exportTarget, request.OutputPath, overwrite: true);

            onProgress(10000);
        }
        catch
        {
            TryDeleteFile(exportTarget);
            throw;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
