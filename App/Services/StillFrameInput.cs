using System.IO;
using System.Text;

namespace VideoNR.Services;

internal static class StillFrameInput
{
    // Supply one uncompressed YUV frame to the worker instead of encoding a temporary H.264 video.
    internal static void Write(RawImage image, string path, CancellationToken token)
    {
        image.Validate();
        if ((image.Width & 1) != 0 || (image.Height & 1) != 0)
            throw new ArgumentException("YUV 4:2:0 exige dimensões pares.");

        var size = checked(image.Width * image.Height);
        var planes = new byte[checked(size + size / 2)];
        var cb = size;
        var cr = size + size / 4;
        for (var y = 0; y < image.Height; y += 2)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < image.Width; x += 2)
            {
                double sumCb = 0, sumCr = 0;
                for (var dy = 0; dy < 2; dy++)
                for (var dx = 0; dx < 2; dx++)
                {
                    var pixel = (y + dy) * image.Width + x + dx;
                    var index = pixel * 4;
                    var opacity = image.Bgra[index + 3] / 255.0;
                    var b = image.Bgra[index] * opacity;
                    var g = image.Bgra[index + 1] * opacity;
                    var r = image.Bgra[index + 2] * opacity;
                    var luma = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    planes[pixel] = (byte)Math.Round(16 + luma * (219.0 / 255));
                    sumCb += (b - luma) / (2 * (1 - 0.0722));
                    sumCr += (r - luma) / (2 * (1 - 0.2126));
                }
                planes[cb++] = (byte)Math.Round(128 + sumCb * (224.0 / (4 * 255)));
                planes[cr++] = (byte)Math.Round(128 + sumCr * (224.0 / (4 * 255)));
            }
        }

        using var stream = File.Create(path);
        var header = FormattableString.Invariant($"YUV4MPEG2 W{image.Width} H{image.Height} F30:1 Ip A1:1 C420jpeg XCOLORRANGE=LIMITED\nFRAME\n");
        stream.Write(Encoding.ASCII.GetBytes(header));
        stream.Write(planes);
    }
}
