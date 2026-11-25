using VideoNR.Models;
using VideoNR.Services;

var failures = 0;

void AssertEqual<T>(T expected, T actual, string name)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        Console.Error.WriteLine($"FAIL [{name}]: expected '{expected}', got '{actual}'");
        failures++;
    }
}

void AssertNear(double expected, double actual, string name, double eps = 1e-6)
{
    if (Math.Abs(expected - actual) > eps)
    {
        Console.Error.WriteLine($"FAIL [{name}]: expected {expected}, got {actual}");
        failures++;
    }
}

void AssertTrue(bool condition, string name)
{
    if (!condition)
    {
        Console.Error.WriteLine($"FAIL [{name}]: condition was false");
        failures++;
    }
}

// --- TrimSelection ---
AssertNear(120.0, new TrimSelection(0, 0).EndAt(120.0), "EndAt unbounded returns duration");
AssertNear(45.5, new TrimSelection(10, 45.5).EndAt(120.0), "EndAt bounded returns End");

var trim = new TrimSelection(0, 0).MoveEdge(true, 10.0, 100.0);
AssertNear(10.0, trim.Start, "MoveEdge start");
AssertNear(0.0, trim.End, "MoveEdge start preserves unbounded end");

trim = trim.MoveEdge(false, 99.9999, 100.0);
AssertNear(0.0, trim.End, "MoveEdge end near duration snaps to unbounded 0");

trim = trim.MoveEdge(false, 50.0, 100.0);
AssertNear(50.0, trim.End, "MoveEdge end inside duration");

trim = trim.MoveEdge(true, 80.0, 100.0);
AssertTrue(trim.Start < trim.End, "MoveEdge start clamped before end");

trim = trim.MoveEdge(true, double.NaN, 100.0);
AssertTrue(double.IsFinite(trim.Start), "MoveEdge ignores NaN");

AssertEqual("00:00:00", TrimSelection.Format(0), "Format zero");
AssertEqual("00:00:00.000", TrimSelection.Format(-5, true), "Format negative clamps to zero");
AssertEqual("00:00:00.000", TrimSelection.Format(double.NaN, true), "Format NaN clamps to zero");
AssertEqual("01:02:03.450", TrimSelection.Format(3723.45, true), "Format HMS with ms");
AssertEqual("25:00:00", TrimSelection.Format(25 * 3600), "Format >24h");

AssertTrue(TrimSelection.TryParse("01:02:03.500", out var parsed), "TryParse HH:MM:SS.mmm");
AssertNear(3723.5, parsed, "TryParse HH:MM:SS.mmm value");

AssertTrue(TrimSelection.TryParse("02:15,25", out parsed), "TryParse MM:SS,ms");
AssertNear(135.25, parsed, "TryParse MM:SS,ms value");

AssertTrue(TrimSelection.TryParse("42.5", out parsed), "TryParse SS.s");
AssertNear(42.5, parsed, "TryParse SS.s value");

AssertTrue(!TrimSelection.TryParse("", out _), "TryParse rejects empty");
AssertTrue(!TrimSelection.TryParse("   ", out _), "TryParse rejects whitespace");
AssertTrue(!TrimSelection.TryParse(null, out _), "TryParse rejects null");
AssertTrue(!TrimSelection.TryParse("00:60:00", out _), "TryParse rejects >=60 minutes");
AssertTrue(!TrimSelection.TryParse("00:00:60", out _), "TryParse rejects >=60 seconds");
AssertTrue(!TrimSelection.TryParse("1:2:3:4", out _), "TryParse rejects >3 parts");
AssertTrue(!TrimSelection.TryParse("-5", out _), "TryParse rejects negative");

// --- NrLayer ---
AssertEqual("NVIDIA original · RTX 50", NrLayer.Models[0].ToString(), "NrModelOption ToString returns Label");
var layer = new NrLayer { Index = 2, ResolutionIndex = 1, Intensity = 0.75, Tone = 0.5, Structure = 0.25 };
AssertEqual("Camada NR 2", layer.Title, "NrLayer title");
AssertEqual("5 0.75 0.50 0.25", layer.Encode(), "NrLayer Encode invariant format");
AssertEqual("▸", layer.Chevron, "NrLayer collapsed chevron");
layer.Expanded = true;
AssertEqual("▾", layer.Chevron, "NrLayer expanded chevron");

var cloned = layer.CloneCollapsed();
AssertTrue(!cloned.Expanded, "NrLayer clone starts collapsed");
AssertEqual(layer.Encode(), cloned.Encode(), "NrLayer clone preserves encoding");

AssertEqual(3, NrLayer.Presets.Length, "NrLayer presets count");
layer.ApplyPreset(NrLayer.Presets[0]);
AssertNear(0.45, layer.Intensity, "NrLayer preset Sutil intensity");
AssertNear(0.50, layer.Tone, "NrLayer preset Sutil tone");
AssertNear(0.50, layer.Structure, "NrLayer preset Sutil structure");
layer.ResetDefaults();
AssertNear(1.0, layer.Intensity, "NrLayer ResetDefaults intensity");
AssertNear(1.0, layer.Tone, "NrLayer ResetDefaults tone");
AssertNear(1.0, layer.Structure, "NrLayer ResetDefaults structure");

// --- ClipItem ---
var clipItem = new ClipItem("/videos/demo.mp4");
AssertEqual("Na fila", clipItem.Subtitle, "ClipItem subtitle without duration");
AssertEqual("", clipItem.ResolutionBadge, "ClipItem ResolutionBadge empty when unset");
AssertEqual("", clipItem.FrameRateBadge, "ClipItem FrameRateBadge empty when unset");
AssertEqual("", clipItem.MetadataBadge, "ClipItem MetadataBadge empty when unset");
clipItem.FrameRate = 30.0;
AssertEqual("30 fps", clipItem.FrameRateBadge, "ClipItem FrameRateBadge integer fps");
AssertEqual("30 fps", clipItem.MetadataBadge, "ClipItem MetadataBadge fps only");
clipItem.VideoWidth = 1920;
clipItem.VideoHeight = 1080;
AssertEqual("1920×1080 · 30 fps", clipItem.MetadataBadge, "ClipItem MetadataBadge resolution + integer fps");
clipItem.FrameRate = 29.97;
AssertEqual("29.97 fps", clipItem.FrameRateBadge, "ClipItem FrameRateBadge fractional fps");
AssertEqual("1920×1080 · 29.97 fps", clipItem.MetadataBadge, "ClipItem MetadataBadge resolution + fractional fps");
clipItem.FrameRate = null;
AssertEqual("1920×1080", clipItem.MetadataBadge, "ClipItem MetadataBadge resolution only");
clipItem.FrameRate = -5;
AssertEqual<double?>(null, clipItem.FrameRate, "ClipItem FrameRate normalizes non-positive to null");
clipItem.VideoWidth = 3840;
clipItem.VideoHeight = 2160;
AssertEqual("4K", clipItem.ResolutionBadge, "ClipItem ResolutionBadge 4K");
clipItem.VideoHeight = 1440;
AssertEqual("1440p", clipItem.ResolutionBadge, "ClipItem ResolutionBadge 1440p");
clipItem.VideoHeight = 1080;
AssertEqual("1080p", clipItem.ResolutionBadge, "ClipItem ResolutionBadge 1080p");
clipItem.VideoHeight = 720;
AssertEqual("720p", clipItem.ResolutionBadge, "ClipItem ResolutionBadge 720p");
clipItem.VideoHeight = 480;
AssertEqual("480p", clipItem.ResolutionBadge, "ClipItem ResolutionBadge 480p");
clipItem.Duration = 125;
AssertEqual("Na fila · 00:02:05", clipItem.Subtitle, "ClipItem subtitle with full duration");
clipItem.Trim = new TrimSelection(10, 40);
AssertEqual("Na fila · corte 00:00:30", clipItem.Subtitle, "ClipItem subtitle with trim");

// --- VideoExporter.BuildArguments ---
var request = new ExportRequest(
    "Engine.exe",
    "motor/veyra_qml_ui.exe",
    "in.mp4",
    "out.mp4",
    1.5,
    12.25,
    "hevc",
    "sf-performance",
    ["0 1.00 1.00 1.00"]);
var cliArgs = VideoExporter.BuildArguments(request, "out-tmp.mp4");
AssertEqual(8, cliArgs.Count, "BuildArguments count");
AssertEqual("motor/veyra_qml_ui.exe", cliArgs[0], "BuildArguments engine");
AssertEqual("in.mp4", cliArgs[1], "BuildArguments input");
AssertEqual("out-tmp.mp4", cliArgs[2], "BuildArguments output");
AssertEqual("hevc", cliArgs[3], "BuildArguments codec");
AssertEqual("1.5", cliArgs[4], "BuildArguments start");
AssertEqual("12.25", cliArgs[5], "BuildArguments end");
AssertEqual("sf-performance", cliArgs[6], "BuildArguments model");
AssertEqual("0 1.00 1.00 1.00", cliArgs[7], "BuildArguments layer");

var singleFrameArgs = VideoExporter.BuildArguments(request with { MaxFrames = 1 }, "out-tmp.mp4");
AssertEqual(10, singleFrameArgs.Count, "BuildArguments MaxFrames count");
AssertEqual("--max-frames", singleFrameArgs[0], "BuildArguments --max-frames flag");
AssertEqual("1", singleFrameArgs[1], "BuildArguments MaxFrames value");
AssertEqual("motor/veyra_qml_ui.exe", singleFrameArgs[2], "BuildArguments MaxFrames engine shifted");

// --- PrintProcessor.SafeVideoCaptureTime ---
AssertNear(0.0, PrintProcessor.SafeVideoCaptureTime(double.NaN, 10.0), "SafeVideoCaptureTime NaN returns 0");
AssertNear(0.0, PrintProcessor.SafeVideoCaptureTime(5.0, 0.0), "SafeVideoCaptureTime zero duration returns 0");
AssertNear(0.0, PrintProcessor.SafeVideoCaptureTime(-2.0, 10.0), "SafeVideoCaptureTime negative clamps to 0");
AssertNear(9.95, PrintProcessor.SafeVideoCaptureTime(10.0, 10.0), "SafeVideoCaptureTime clamps before EOF");
AssertNear(4.25, PrintProcessor.SafeVideoCaptureTime(4.25, 10.0), "SafeVideoCaptureTime preserves middle timestamp");

// --- RawImage ---
RawImage MakeSolid(int w, int h, byte b, byte g, byte r, byte a = 255)
{
    var data = new byte[w * h * 4];
    for (var i = 0; i < data.Length; i += 4)
    {
        data[i] = b;
        data[i + 1] = g;
        data[i + 2] = r;
        data[i + 3] = a;
    }
    return new RawImage(data, w, h);
}

var oddImg = MakeSolid(321, 241, 10, 20, 30);
// Distinctive bottom-right edge pixels to verify edge clamping.
oddImg.Bgra[(240 * 321 + 320) * 4 + 0] = 77;
oddImg.Bgra[(240 * 321 + 320) * 4 + 1] = 88;
oddImg.Bgra[(240 * 321 + 320) * 4 + 2] = 99;
var (oddPadded, oddDidPad) = oddImg.PadForVideoEncoding();
AssertTrue(oddDidPad, "PadForVideoEncoding pads odd dimensions");
AssertEqual(322, oddPadded.Width, "PadForVideoEncoding odd width rounded even");
AssertEqual(242, oddPadded.Height, "PadForVideoEncoding odd height rounded even");
AssertEqual<byte>(77, oddPadded.Bgra[(241 * 322 + 321) * 4 + 0], "PadForVideoEncoding clamps corner B");
AssertEqual<byte>(88, oddPadded.Bgra[(241 * 322 + 321) * 4 + 1], "PadForVideoEncoding clamps corner G");
AssertEqual<byte>(99, oddPadded.Bgra[(241 * 322 + 321) * 4 + 2], "PadForVideoEncoding clamps corner R");

var tinyImg = MakeSolid(50, 40, 11, 22, 33);
var (tinyPadded, tinyDidPad) = tinyImg.PadForVideoEncoding();
AssertTrue(tinyDidPad, "PadForVideoEncoding pads tiny dimensions");
AssertEqual(160, tinyPadded.Width, "PadForVideoEncoding tiny min width 160");
AssertEqual(120, tinyPadded.Height, "PadForVideoEncoding tiny min height 120");

var croppedBack = oddPadded.Crop(321, 241);
AssertEqual(321, croppedBack.Width, "Crop restores width");
AssertEqual(241, croppedBack.Height, "Crop restores height");
AssertEqual<byte>(77, croppedBack.Bgra[(240 * 321 + 320) * 4 + 0], "Crop preserves edge pixel");

var subRegion = oddPadded.CropRegion(10, 12, 20, 15);
AssertEqual(20, subRegion.Width, "CropRegion width");
AssertEqual(15, subRegion.Height, "CropRegion height");

var oversized = MakeSolid(5000, 3000, 40, 50, 60);
var (downscaled, didDownscale) = oversized.DownscaleIfExceeds();
AssertTrue(didDownscale, "DownscaleIfExceeds shrinks oversized image");
AssertTrue(downscaled.Width <= 4096 && downscaled.Height <= 2304, "DownscaleIfExceeds fits within 4K bounds");
AssertTrue((downscaled.Width & 1) == 0 && (downscaled.Height & 1) == 0, "DownscaleIfExceeds produces even dims");

// --- EngineLocator ---
var tempDir = Directory.CreateTempSubdirectory("VideoNR-Test-").FullName;
try
{
    AssertEqual<string?>(null, EngineLocator.FindAdapter(tempDir), "FindAdapter returns null when missing");
    var adapterFile = Path.Combine(tempDir, "Engine.exe");
    File.WriteAllText(adapterFile, "");
    AssertEqual(adapterFile, EngineLocator.FindAdapter(tempDir), "FindAdapter finds Engine.exe");

    var engineDir = Path.Combine(tempDir, "motor", "versions", EngineLocator.ExpectedVersion);
    Directory.CreateDirectory(engineDir);
    var veyraExe = Path.Combine(engineDir, "veyra_qml_ui.exe");
    File.WriteAllText(veyraExe, "");
    File.WriteAllText(
        Path.Combine(engineDir, "package-manifest.json"),
        $$"""{"version":"{{EngineLocator.ExpectedVersion}}","gpuPackage":"NVIDIA"}""");

    AssertEqual(veyraExe, EngineLocator.FindBundledEngine("NVIDIA", tempDir), "FindBundledEngine finds NVIDIA");
    AssertEqual<string?>(null, EngineLocator.FindBundledEngine("AMD", tempDir), "FindBundledEngine rejects wrong vendor");

    // --- MediaFoundationFrameCodec Live Roundtrip ---
    if (OperatingSystem.IsWindows())
    {
        RawImage MakeQuadrantImage(int w, int h)
        {
            var buf = new byte[w * h * 4];
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var p = (y * w + x) * 4;
                    var top = y < h / 2;
                    var left = x < w / 2;
                    if (top && left) { buf[p] = 0; buf[p + 1] = 0; buf[p + 2] = 255; }           // Red
                    else if (top && !left) { buf[p] = 0; buf[p + 1] = 255; buf[p + 2] = 0; }     // Green
                    else if (!top && left) { buf[p] = 255; buf[p + 1] = 0; buf[p + 2] = 0; }     // Blue
                    else { buf[p] = 255; buf[p + 1] = 255; buf[p + 2] = 255; }                   // White
                    buf[p + 3] = 255;
                }
            }
            return new RawImage(buf, w, h);
        }

        void AssertPixelNear(RawImage img, int x, int y, byte expB, byte expG, byte expR, string label)
        {
            var p = (y * img.Width + x) * 4;
            var db = Math.Abs(img.Bgra[p] - expB);
            var dg = Math.Abs(img.Bgra[p + 1] - expG);
            var dr = Math.Abs(img.Bgra[p + 2] - expR);
            AssertTrue(db <= 35 && dg <= 35 && dr <= 35,
                $"{label} color ({img.Bgra[p]},{img.Bgra[p + 1]},{img.Bgra[p + 2]}) near ({expB},{expG},{expR})");
            AssertEqual<byte>(255, img.Bgra[p + 3], $"{label} opaque alpha");
        }

        var quad320 = MakeQuadrantImage(320, 240);
        var mp4A = Path.Combine(tempDir, "quad-320.mp4");
        MediaFoundationFrameCodec.EncodeStillVideo(quad320, mp4A, frameCount: 2, fps: 30);
        var decoded320 = MediaFoundationFrameCodec.DecodeFrame(mp4A, 0);
        AssertEqual(320, decoded320.Width, "MF roundtrip 320 width");
        AssertEqual(240, decoded320.Height, "MF roundtrip 240 height");
        AssertPixelNear(decoded320, 80, 60, 0, 0, 255, "MF 320 TL red");
        AssertPixelNear(decoded320, 240, 60, 0, 255, 0, "MF 320 TR green");
        AssertPixelNear(decoded320, 80, 180, 255, 0, 0, "MF 320 BL blue");
        AssertPixelNear(decoded320, 240, 180, 255, 255, 255, "MF 320 BR white");

        var quadOdd = MakeQuadrantImage(321, 241);
        var (quadPadded, _) = quadOdd.PadForVideoEncoding();
        AssertEqual(322, quadPadded.Width, "MF odd padded width");
        AssertEqual(242, quadPadded.Height, "MF odd padded height");
        var mp4B = Path.Combine(tempDir, "quad-odd.mp4");
        MediaFoundationFrameCodec.EncodeStillVideo(quadPadded, mp4B, frameCount: 2, fps: 30);
        var decodedOdd = MediaFoundationFrameCodec.DecodeFrame(mp4B, 0).Crop(321, 241);
        AssertEqual(321, decodedOdd.Width, "MF odd cropped width");
        AssertEqual(241, decodedOdd.Height, "MF odd cropped height");
        AssertPixelNear(decodedOdd, 80, 60, 0, 0, 255, "MF 321 TL red");
        AssertPixelNear(decodedOdd, 240, 60, 0, 255, 0, "MF 321 TR green");
        AssertPixelNear(decodedOdd, 80, 180, 255, 0, 0, "MF 321 BL blue");
        AssertPixelNear(decodedOdd, 240, 180, 255, 255, 255, "MF 321 BR white");

        var detectedFps = MediaFoundationFrameCodec.TryGetFrameRate(mp4A);
        AssertTrue(detectedFps.HasValue, "TryGetFrameRate returns value for 30fps MP4");
        AssertNear(30.0, detectedFps ?? 0.0, "TryGetFrameRate 30fps MP4 value", eps: 0.1);

        var missingMp4 = Path.Combine(tempDir, "does-not-exist.mp4");
        AssertEqual<double?>(null, MediaFoundationFrameCodec.TryGetFrameRate(missingMp4), "TryGetFrameRate returns null on missing file");
        AssertEqual<double?>(null, MediaFoundationFrameCodec.TryGetFrameRate(""), "TryGetFrameRate returns null on empty path");

        var invalidMp4 = Path.Combine(tempDir, "corrupt.mp4");
        File.WriteAllText(invalidMp4, "not a valid mp4 stream");
        AssertEqual<double?>(null, MediaFoundationFrameCodec.TryGetFrameRate(invalidMp4), "TryGetFrameRate returns null on invalid file");
    }

    // --- SettingsStore ---
    var settingsFile = Path.Combine(tempDir, "prefs", "settings.json");
    var toSave = new UserSettings(
        NrEnabled: false,
        ModelTag: "sf-performance",
        CodecIndex: 1,
        Layers: SettingsStore.FromNrLayers([layer]),
        CustomEngines: new Dictionary<string, string> { ["NVIDIA"] = tempDir });
    SettingsStore.Save(toSave, settingsFile);
    var loaded = SettingsStore.Load(settingsFile);
    AssertEqual(false, loaded.NrEnabled, "SettingsStore NrEnabled");
    AssertEqual("sf-performance", loaded.ModelTag, "SettingsStore ModelTag");
    AssertEqual(1, loaded.CodecIndex, "SettingsStore CodecIndex");
    var restoredLayers = SettingsStore.ToNrLayers(loaded, 4);
    AssertEqual(1, restoredLayers.Count, "SettingsStore layers count");
    AssertEqual(layer.Encode(), restoredLayers[0].Encode(), "SettingsStore layer encoding preserved");

    File.WriteAllText(settingsFile, "{ invalid json");
    var fallback = SettingsStore.Load(settingsFile);
    AssertEqual(true, fallback.NrEnabled, "SettingsStore corrupt JSON falls back to defaults");
}
finally
{
    Directory.Delete(tempDir, true);
}

if (failures > 0)
{
    Console.Error.WriteLine($"{failures} test(s) failed.");
    return 1;
}

Console.WriteLine("All VideoNR tests passed.");
return 0;
