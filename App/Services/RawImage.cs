namespace VideoNR.Services;

internal sealed record RawImage(byte[] Bgra, int Width, int Height)
{
    internal int Stride => Width * 4;

    internal void Validate()
    {
        if (Width <= 0 || Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(Width), "As dimensões da imagem devem ser positivas.");
        var required = (long)Width * Height * 4;
        if (Bgra is null || Bgra.LongLength < required)
            throw new ArgumentException("O buffer BGRA da imagem é menor do que o esperado.", nameof(Bgra));
    }

    internal (RawImage Padded, bool WasPadded) PadForVideoEncoding(int minWidth = 160, int minHeight = 120)
    {
        Validate();
        var targetW = (Math.Max(minWidth, Width) + 1) & ~1;
        var targetH = (Math.Max(minHeight, Height) + 1) & ~1;
        if (targetW == Width && targetH == Height)
            return (this, false);

        var srcStride = Stride;
        var dstStride = targetW * 4;
        var dst = new byte[checked(dstStride * targetH)];

        for (var y = 0; y < Height; y++)
        {
            var srcRowOffset = y * srcStride;
            var dstRowOffset = y * dstStride;
            Buffer.BlockCopy(Bgra, srcRowOffset, dst, dstRowOffset, srcStride);

            if (targetW > Width)
            {
                var lastPixelOffset = srcRowOffset + (Width - 1) * 4;
                var b = Bgra[lastPixelOffset];
                var g = Bgra[lastPixelOffset + 1];
                var r = Bgra[lastPixelOffset + 2];
                var a = Bgra[lastPixelOffset + 3];
                for (var x = Width; x < targetW; x++)
                {
                    var p = dstRowOffset + x * 4;
                    dst[p] = b;
                    dst[p + 1] = g;
                    dst[p + 2] = r;
                    dst[p + 3] = a;
                }
            }
        }

        if (targetH > Height)
        {
            var lastRowOffset = (Height - 1) * dstStride;
            for (var y = Height; y < targetH; y++)
                Buffer.BlockCopy(dst, lastRowOffset, dst, y * dstStride, dstStride);
        }

        return (new RawImage(dst, targetW, targetH), true);
    }

    internal RawImage Crop(int targetWidth, int targetHeight) =>
        CropRegion(0, 0, targetWidth, targetHeight);

    internal RawImage CropRegion(int x, int y, int width, int height)
    {
        Validate();
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x + width > Width || y + height > Height)
            throw new ArgumentOutOfRangeException(nameof(width), "Região de recorte fora dos limites da imagem.");

        if (x == 0 && y == 0 && width == Width && height == Height && Bgra.Length == Stride * Height)
            return this;

        var srcStride = Stride;
        var dstStride = width * 4;
        var dst = new byte[checked(dstStride * height)];
        for (var row = 0; row < height; row++)
        {
            var srcOffset = (y + row) * srcStride + x * 4;
            Buffer.BlockCopy(Bgra, srcOffset, dst, row * dstStride, dstStride);
        }

        return new RawImage(dst, width, height);
    }

    internal (RawImage Resized, bool WasDownscaled) DownscaleIfExceeds(int maxWidth = 4096, int maxHeight = 2304)
    {
        Validate();
        if (maxWidth < 2 || maxHeight < 2)
            throw new ArgumentOutOfRangeException(nameof(maxWidth));

        if (Width <= maxWidth && Height <= maxHeight)
            return (this, false);

        var scale = Math.Min((double)maxWidth / Width, (double)maxHeight / Height);
        var targetW = Math.Max(2, ((int)Math.Floor(Width * scale)) & ~1);
        var targetH = Math.Max(2, ((int)Math.Floor(Height * scale)) & ~1);

        var srcStride = Stride;
        var dstStride = targetW * 4;
        var dst = new byte[checked(dstStride * targetH)];

        var x0Table = new int[targetW];
        var x1Table = new int[targetW];
        var fxTable = new float[targetW];
        for (var dx = 0; dx < targetW; dx++)
        {
            var sx = Math.Clamp((dx + 0.5) * Width / targetW - 0.5, 0.0, Width - 1.0);
            var x0 = (int)Math.Floor(sx);
            var x1 = Math.Min(x0 + 1, Width - 1);
            x0Table[dx] = x0 * 4;
            x1Table[dx] = x1 * 4;
            fxTable[dx] = (float)(sx - x0);
        }

        for (var dy = 0; dy < targetH; dy++)
        {
            var sy = Math.Clamp((dy + 0.5) * Height / targetH - 0.5, 0.0, Height - 1.0);
            var y0 = (int)Math.Floor(sy);
            var y1 = Math.Min(y0 + 1, Height - 1);
            var fy = (float)(sy - y0);
            var w00Base = y0 * srcStride;
            var w10Base = y1 * srcStride;
            var dstRow = dy * dstStride;

            for (var dx = 0; dx < targetW; dx++)
            {
                var fx = fxTable[dx];
                var p00 = w00Base + x0Table[dx];
                var p01 = w00Base + x1Table[dx];
                var p10 = w10Base + x0Table[dx];
                var p11 = w10Base + x1Table[dx];

                var invFx = 1f - fx;
                var invFy = 1f - fy;
                var w00 = invFx * invFy;
                var w01 = fx * invFy;
                var w10 = invFx * fy;
                var w11 = fx * fy;

                var dstPx = dstRow + dx * 4;
                dst[dstPx] = (byte)Math.Clamp(MathF.Round(Bgra[p00] * w00 + Bgra[p01] * w01 + Bgra[p10] * w10 + Bgra[p11] * w11), 0, 255);
                dst[dstPx + 1] = (byte)Math.Clamp(MathF.Round(Bgra[p00 + 1] * w00 + Bgra[p01 + 1] * w01 + Bgra[p10 + 1] * w10 + Bgra[p11 + 1] * w11), 0, 255);
                dst[dstPx + 2] = (byte)Math.Clamp(MathF.Round(Bgra[p00 + 2] * w00 + Bgra[p01 + 2] * w01 + Bgra[p10 + 2] * w10 + Bgra[p11 + 2] * w11), 0, 255);
                dst[dstPx + 3] = (byte)Math.Clamp(MathF.Round(Bgra[p00 + 3] * w00 + Bgra[p01 + 3] * w01 + Bgra[p10 + 3] * w10 + Bgra[p11 + 3] * w11), 0, 255);
            }
        }

        return (new RawImage(dst, targetW, targetH), true);
    }
}
