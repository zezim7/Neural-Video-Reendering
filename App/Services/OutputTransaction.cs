using System.IO;

namespace VideoNR.Services;

internal sealed class OutputTransaction : IDisposable
{
    private readonly string destination;
    private readonly string directory;
    internal string StagedPath { get; }

    internal OutputTransaction(string path)
    {
        destination = Path.GetFullPath(path);
        directory = Path.Combine(Path.GetDirectoryName(destination)!, $".VideoNR-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        StagedPath = Path.Combine(directory, "render.mp4");
    }

    internal void Commit(bool overwrite, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(StagedPath, destination, overwrite);
    }

    public void Dispose()
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
