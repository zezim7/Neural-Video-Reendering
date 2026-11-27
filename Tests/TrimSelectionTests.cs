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
AssertNear(5.0, PrintProcessor.SafeVideoCaptureTime(5.0, 0.0), "Unknown duration preserves requested time");
AssertNear(0.0, PrintProcessor.SafeVideoCaptureTime(-2.0, 10.0), "SafeVideoCaptureTime negative clamps to 0");
AssertNear(10.0, PrintProcessor.SafeVideoCaptureTime(10.0, 10.0), "End position retained for timestamp lookup");
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
var vfr = new FrameTimes([0, 166667, 416667, 583333, 583333]);
AssertNear(.0416667, vfr.Adjacent(.0166667, 1)!.Value, "VFR next timestamp");
AssertNear(.0166667, vfr.Adjacent(.0416667, -1)!.Value, "VFR previous timestamp");
AssertNear(.0583333, vfr.AtOrBefore(.1), "EOF uses last actual frame");
AssertNear(.0166667, vfr.AtOrBefore(.04), "Display frame at intermediate VFR position");

AssertEqual(3, NrLayer.InitialModelIndex("original", ["AMD"]), "AMD-only ZIP selects AMD on first launch");
AssertEqual(4, NrLayer.InitialModelIndex("amd-original", ["AMD", "NVIDIA"]), "Installed preference retained");
AssertEqual(0, SettingsStore.ToNrLayers(new UserSettings(Layers: []), 4).Count, "Empty NR profile survives reload");

var cuts = new TimelineSequence();
var baseClip = cuts.AddClipAt("first.mp4", 10, 0, 0, new TrimSelection(2, 6))!;
var upperClip = cuts.AddClipAt("upper.mp4", 1, 1.0 / 60, 1)!;
var export = cuts.SnapshotComposition();
AssertEqual(3, export.Count, "Both sides of overlap survive export");
AssertNear(1.0 / 60, export[0].Duration, "One visible frame at 60 fps survives");
AssertNear(2, export[0].SourceStart, "First slice uses source trim");
AssertNear(6, cuts.EditedSegments().First().SourceEnd, "Individual edited export preserves full trimmed segment");
cuts.Tracks[1].Visible = false;
AssertEqual(1, cuts.EditedSegments().Count(), "Hidden track excluded from edited batch");
baseClip.Enabled = false;
AssertEqual(0, cuts.EditedSegments().Count(), "Disabled segment excluded from batch");
cuts.Tracks[0].Locked = true;
AssertTrue(cuts.AddClipAt("other.mp4", 2, 0, 0) is null, "Lock blocks insertion");
AssertTrue(cuts.AppendClip("other.mp4", 2) is null, "Lock blocks append");
AssertEqual(0, cuts.RemoveBySourcePath("first.mp4"), "Lock protects library removal");
AssertEqual(2, cuts.Count, "Locked edits leave all segments intact");

var tempDir = Directory.CreateTempSubdirectory("VideoNR-Test-").FullName;
try
{
    var dest = Path.Combine(tempDir, "shared.mp4");
    using (var first = new OutputTransaction(dest))
    using (var second = new OutputTransaction(dest))
    {
        File.WriteAllText(first.StagedPath, "finished");
        File.WriteAllText(second.StagedPath, "other");
        first.Commit(false, CancellationToken.None);
        try { second.Commit(false, CancellationToken.None); AssertTrue(false, "Collision should fail"); }
        catch (IOException) { }
        try { second.Commit(true, new CancellationToken(true)); AssertTrue(false, "Canceled replace should fail"); }
        catch (OperationCanceledException) { }
    }
    AssertEqual("finished", File.ReadAllText(dest), "Collision and cancellation preserve completed output");
    var rawPath = Path.Combine(tempDir, "sample.y4m");
    var tinyFrame = new RawImage([0,0,0,255, 255,255,255,255, 0,0,0,0, 255,255,255,255], 2, 2);
    StillFrameInput.Write(tinyFrame, rawPath, CancellationToken.None);
    AssertTrue(File.ReadAllBytes(rawPath).TakeLast(6).SequenceEqual(new byte[] {16,235,16,235,128,128}), "Raw YUV planes preserve black, white and neutral chroma");
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
        VideoFixture.EncodeStillVideo(quad320, mp4A, frameCount: 2, fps: 30);
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
        VideoFixture.EncodeStillVideo(quadPadded, mp4B, frameCount: 2, fps: 30);
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

        var detectedDur = MediaFoundationFrameCodec.TryGetDuration(mp4A);
        AssertTrue(detectedDur is > 0.03, "TryGetDuration returns positive duration");
        AssertEqual<double?>(null, MediaFoundationFrameCodec.TryGetDuration(missingMp4), "TryGetDuration returns null on missing file");

        // --- VideoConcatenator Live Roundtrip (Mixed Resolutions) ---
        var redImg = MakeSolid(160, 120, 0, 0, 230);
        var blueImg = MakeSolid(240, 120, 230, 0, 0);
        var seg1Mp4 = Path.Combine(tempDir, "seg1.mp4");
        var seg2Mp4 = Path.Combine(tempDir, "seg2.mp4");
        var mergedMp4 = Path.Combine(tempDir, "merged.mp4");
        VideoFixture.EncodeStillVideo(redImg, seg1Mp4, frameCount: 15, fps: 30);
        VideoFixture.EncodeStillVideo(blueImg, seg2Mp4, frameCount: 15, fps: 30);

        var lastConcatProgress = 0;
        VideoConcatenator.Concatenate([seg1Mp4, seg2Mp4], mergedMp4, preferHevc: false, p => lastConcatProgress = p, CancellationToken.None);
        AssertEqual(10000, lastConcatProgress, "VideoConcatenator reaches 100%");
        AssertTrue(File.Exists(mergedMp4) && new FileInfo(mergedMp4).Length > 0, "VideoConcatenator creates non-empty output");

        var mergedProbe = MediaFoundationFrameCodec.TryProbe(mergedMp4);
        AssertTrue(mergedProbe is not null, "VideoConcatenator output probe succeeds");
        AssertEqual(160, mergedProbe!.Width, "VideoConcatenator preserves target width");
        AssertEqual(120, mergedProbe.Height, "VideoConcatenator preserves target height");
        AssertTrue(mergedProbe.DurationSeconds >= 0.85, "VideoConcatenator combined duration >= 0.85s");

        var mergedStartFrame = MediaFoundationFrameCodec.DecodeFrame(mergedMp4, 0.05);
        var mergedEndFrame = MediaFoundationFrameCodec.DecodeFrame(mergedMp4, 0.75);
        AssertPixelNear(mergedStartFrame, 80, 60, 0, 0, 230, "VideoConcatenator first half red");
        AssertPixelNear(mergedEndFrame, 80, 60, 230, 0, 0, "VideoConcatenator second half blue (letterboxed)");

        var variableRate = Path.Combine(tempDir, "variable-rate.mp4");
        long[] numberedPts = [0, 166667, 583333, 750000, 1250000];
        var numbered = Enumerable.Range(0, numberedPts.Length)
            .Select(i => MakeSolid(160, 120, (byte)(i * 50), (byte)(i * 50), (byte)(i * 50))).ToArray();
        VideoFixture.EncodeFrames(numbered[0], variableRate, numbered.Length, 60, i => numbered[i], numberedPts);
        var actualFrames = MediaFoundationFrameCodec.ReadFrameTimes(variableRate, CancellationToken.None);
        var lastPts = actualFrames.AtOrBefore(1);
        AssertNear(0.125, lastPts, "Variable-rate final timestamp", .001);
        var lastImage = MediaFoundationFrameCodec.DecodeFrame(variableRate, lastPts);
        AssertPixelNear(lastImage, 80, 60, 200, 200, 200, "Variable-rate capture is the final numbered frame");
        AssertNear(.0583333, actualFrames.Adjacent(.030, 1)!.Value, "Variable-rate step uses encoded PTS", .001);

        var singleFrame = Path.Combine(tempDir, "one-frame.mp4");
        VideoFixture.EncodeStillVideo(redImg, singleFrame, frameCount: 1, fps: 60);
        var cutOutput = Path.Combine(tempDir, "one-frame-cut.mp4");
        VideoConcatenator.Concatenate([singleFrame, seg2Mp4], cutOutput, false, null, CancellationToken.None);
        AssertPixelNear(MediaFoundationFrameCodec.DecodeFrame(cutOutput, 0), 80, 60, 0, 0, 230, "First visible 60 fps frame retained");
        AssertPixelNear(MediaFoundationFrameCodec.DecodeFrame(cutOutput, 0.04), 80, 60, 230, 0, 0, "Next clip follows the short slice");
        var timestampIndex = MediaFoundationFrameCodec.ReadFrameTimes(seg2Mp4, CancellationToken.None);
        AssertNear(14.0 / 30, timestampIndex.AtOrBefore(1), "Last encoded presentation timestamp", .003);

        // --- VideoConcatenator Live Roundtrip (Interleaved Audio+Video + Silent Video) ---
        void EncodeAudioVideoMp4(RawImage img, string outPath, int frameCount, uint fps)
        {
            const uint ch = 2, rate = 48_000, bits = 16;
            const uint align = ch * (bits / 8);
            const uint pcmBps = rate * align;
            MediaFoundationInterop.CheckHr(MediaFoundationInterop.MFStartup(MediaFoundationInterop.MfApiVersion, MediaFoundationInterop.MfStartupNoSocket), "Test.MFStartup");
            MediaFoundationInterop.IMFAttributes? attrs = null;
            MediaFoundationInterop.IMFSinkWriter? writer = null;
            try
            {
                MediaFoundationInterop.CheckHr(MediaFoundationInterop.MFCreateAttributes(out attrs, 1), "Test.MFCreateAttributes");
                _ = attrs.SetUINT32(in MediaFoundationInterop.MfReadWriteEnableHardwareTransforms, 0);
                MediaFoundationInterop.CheckHr(MediaFoundationInterop.MFCreateSinkWriterFromURL(outPath, IntPtr.Zero, attrs, out writer), "Test.SinkWriter");

                MediaFoundationInterop.IMFMediaType? vOut = null;
                MediaFoundationInterop.IMFMediaType? vIn = null;
                MediaFoundationInterop.IMFMediaType? aOut = null;
                MediaFoundationInterop.IMFMediaType? aIn = null;
                uint vIdx, aIdx;
                try
                {
                    var packedSize = MediaFoundationInterop.Pack2UInt32((uint)img.Width, (uint)img.Height);
                    var packedRate = MediaFoundationInterop.Pack2UInt32(fps, 1);
                    var packedPar = MediaFoundationInterop.Pack2UInt32(1, 1);

                    MediaFoundationInterop.CheckHr(MediaFoundationInterop.MFCreateMediaType(out vOut), "vOut");
                    MediaFoundationInterop.CheckHr(vOut.SetGUID(in MediaFoundationInterop.MfMtMajorType, in MediaFoundationInterop.MfMediaTypeVideo), "vOut.major");
                    MediaFoundationInterop.CheckHr(vOut.SetGUID(in MediaFoundationInterop.MfMtSubtype, in MediaFoundationInterop.MfVideoFormatH264), "vOut.sub");
                    MediaFoundationInterop.CheckHr(vOut.SetUINT32(in MediaFoundationInterop.MfMtAvgBitrate, 2_000_000), "vOut.br");
                    MediaFoundationInterop.CheckHr(vOut.SetUINT32(in MediaFoundationInterop.MfMtInterlaceMode, MediaFoundationInterop.MfVideoInterlaceProgressive), "vOut.int");
                    MediaFoundationInterop.CheckHr(vOut.SetUINT64(in MediaFoundationInterop.MfMtFrameSize, packedSize), "vOut.sz");
                    MediaFoundationInterop.CheckHr(vOut.SetUINT64(in MediaFoundationInterop.MfMtFrameRate, packedRate), "vOut.fps");
                    MediaFoundationInterop.CheckHr(vOut.SetUINT64(in MediaFoundationInterop.MfMtPixelAspectRatio, packedPar), "vOut.par");
                    MediaFoundationInterop.CheckHr(writer.AddStream(vOut, out vIdx), "writer.AddStream(v)");

                    MediaFoundationInterop.CheckHr(MediaFoundationInterop.MFCreateMediaType(out vIn), "vIn");
                    MediaFoundationInterop.CheckHr(vIn.SetGUID(in MediaFoundationInterop.MfMtMajorType, in MediaFoundationInterop.MfMediaTypeVideo), "vIn.major");
                    MediaFoundationInterop.CheckHr(vIn.SetGUID(in MediaFoundationInterop.MfMtSubtype, in MediaFoundationInterop.MfVideoFormatRgb32), "vIn.sub");
                    MediaFoundationInterop.CheckHr(vIn.SetUINT32(in MediaFoundationInterop.MfMtInterlaceMode, MediaFoundationInterop.MfVideoInterlaceProgressive), "vIn.int");
                    MediaFoundationInterop.CheckHr(vIn.SetUINT64(in MediaFoundationInterop.MfMtFrameSize, packedSize), "vIn.sz");
                    MediaFoundationInterop.CheckHr(vIn.SetUINT64(in MediaFoundationInterop.MfMtFrameRate, packedRate), "vIn.fps");
                    MediaFoundationInterop.CheckHr(vIn.SetUINT64(in MediaFoundationInterop.MfMtPixelAspectRatio, packedPar), "vIn.par");
                    MediaFoundationInterop.CheckHr(vIn.SetUINT32(in MediaFoundationInterop.MfMtDefaultStride, (uint)img.Stride), "vIn.stride");
                    MediaFoundationInterop.CheckHr(writer.SetInputMediaType(vIdx, vIn, null), "writer.SetInput(v)");

                    MediaFoundationInterop.CheckHr(MediaFoundationInterop.MFCreateMediaType(out aOut), "aOut");
                    MediaFoundationInterop.CheckHr(aOut.SetGUID(in MediaFoundationInterop.MfMtMajorType, in MediaFoundationInterop.MfMediaTypeAudio), "aOut.major");
                    MediaFoundationInterop.CheckHr(aOut.SetGUID(in MediaFoundationInterop.MfMtSubtype, in MediaFoundationInterop.MfAudioFormatAac), "aOut.sub");
                    MediaFoundationInterop.CheckHr(aOut.SetUINT32(in MediaFoundationInterop.MfMtAudioNumChannels, ch), "aOut.ch");
                    MediaFoundationInterop.CheckHr(aOut.SetUINT32(in MediaFoundationInterop.MfMtAudioSamplesPerSecond, rate), "aOut.rate");
                    MediaFoundationInterop.CheckHr(aOut.SetUINT32(in MediaFoundationInterop.MfMtAudioBitsPerSample, bits), "aOut.bits");
                    MediaFoundationInterop.CheckHr(aOut.SetUINT32(in MediaFoundationInterop.MfMtAudioAvgBytesPerSecond, 16_000), "aOut.bps");
                    MediaFoundationInterop.CheckHr(writer.AddStream(aOut, out aIdx), "writer.AddStream(a)");

                    MediaFoundationInterop.CheckHr(MediaFoundationInterop.MFCreateMediaType(out aIn), "aIn");
                    MediaFoundationInterop.CheckHr(aIn.SetGUID(in MediaFoundationInterop.MfMtMajorType, in MediaFoundationInterop.MfMediaTypeAudio), "aIn.major");
                    MediaFoundationInterop.CheckHr(aIn.SetGUID(in MediaFoundationInterop.MfMtSubtype, in MediaFoundationInterop.MfAudioFormatPcm), "aIn.sub");
                    MediaFoundationInterop.CheckHr(aIn.SetUINT32(in MediaFoundationInterop.MfMtAudioNumChannels, ch), "aIn.ch");
                    MediaFoundationInterop.CheckHr(aIn.SetUINT32(in MediaFoundationInterop.MfMtAudioSamplesPerSecond, rate), "aIn.rate");
                    MediaFoundationInterop.CheckHr(aIn.SetUINT32(in MediaFoundationInterop.MfMtAudioBitsPerSample, bits), "aIn.bits");
                    MediaFoundationInterop.CheckHr(aIn.SetUINT32(in MediaFoundationInterop.MfMtAudioBlockAlignment, align), "aIn.align");
                    MediaFoundationInterop.CheckHr(aIn.SetUINT32(in MediaFoundationInterop.MfMtAudioAvgBytesPerSecond, pcmBps), "aIn.bps");
                    MediaFoundationInterop.CheckHr(writer.SetInputMediaType(aIdx, aIn, null), "writer.SetInput(a)");
                }
                finally
                {
                    MediaFoundationInterop.ReleaseCom(vOut);
                    MediaFoundationInterop.ReleaseCom(vIn);
                    MediaFoundationInterop.ReleaseCom(aOut);
                    MediaFoundationInterop.ReleaseCom(aIn);
                }

                MediaFoundationInterop.CheckHr(writer.BeginWriting(), "Test.BeginWriting");
                var frameDur = 10_000_000L / fps;
                for (var f = 0; f < frameCount; f++)
                {
                    MediaFoundationInterop.IMFMediaBuffer? buf = null;
                    MediaFoundationInterop.IMFSample? smp = null;
                    var locked = false;
                    try
                    {
                        var bytes = (uint)img.Bgra.Length;
                        MediaFoundationInterop.CheckHr(MediaFoundationInterop.MFCreateMemoryBuffer(bytes, out buf), "buf");
                        MediaFoundationInterop.CheckHr(buf.Lock(out var ptr, out _, out _), "lock");
                        locked = true;
                        System.Runtime.InteropServices.Marshal.Copy(img.Bgra, 0, ptr, img.Bgra.Length);
                        MediaFoundationInterop.CheckHr(buf.Unlock(), "unlock");
                        locked = false;
                        MediaFoundationInterop.CheckHr(buf.SetCurrentLength(bytes), "len");
                        MediaFoundationInterop.CheckHr(MediaFoundationInterop.MFCreateSample(out smp), "smp");
                        MediaFoundationInterop.CheckHr(smp.AddBuffer(buf), "addbuf");
                        MediaFoundationInterop.CheckHr(smp.SetSampleTime(f * frameDur), "pts");
                        MediaFoundationInterop.CheckHr(smp.SetSampleDuration(frameDur), "dur");
                        MediaFoundationInterop.CheckHr(writer.WriteSample(vIdx, smp), "writeV");
                    }
                    finally
                    {
                        if (locked && buf is not null) _ = buf.Unlock();
                        MediaFoundationInterop.ReleaseCom(smp);
                        MediaFoundationInterop.ReleaseCom(buf);
                    }
                }

                var totalAudioHns = frameCount * frameDur;
                var totalSamples = (uint)(totalAudioHns * rate / 10_000_000L);
                var audioBytes = totalSamples * align;
                MediaFoundationInterop.IMFMediaBuffer? abuf = null;
                MediaFoundationInterop.IMFSample? asmp = null;
                var aLocked = false;
                try
                {
                    MediaFoundationInterop.CheckHr(MediaFoundationInterop.MFCreateMemoryBuffer(audioBytes, out abuf), "abuf");
                    MediaFoundationInterop.CheckHr(abuf.Lock(out var aptr, out _, out _), "alock");
                    aLocked = true;
                    var pcm = new short[totalSamples * ch];
                    for (var i = 0; i < totalSamples; i++)
                    {
                        // The tone begins 150 ms into the second clip.
                        var value = i < rate * 0.15 ? (short)0 : (short)(12000 * Math.Sin(2 * Math.PI * 440 * i / rate));
                        pcm[2 * i] = pcm[2 * i + 1] = value;
                    }
                    System.Runtime.InteropServices.Marshal.Copy(pcm, 0, aptr, pcm.Length);
                    MediaFoundationInterop.CheckHr(abuf.Unlock(), "aunlock");
                    aLocked = false;
                    MediaFoundationInterop.CheckHr(abuf.SetCurrentLength(audioBytes), "alen");
                    MediaFoundationInterop.CheckHr(MediaFoundationInterop.MFCreateSample(out asmp), "asmp");
                    MediaFoundationInterop.CheckHr(asmp.AddBuffer(abuf), "addabuf");
                    MediaFoundationInterop.CheckHr(asmp.SetSampleTime(0), "apts");
                    MediaFoundationInterop.CheckHr(asmp.SetSampleDuration(totalAudioHns), "adur");
                    MediaFoundationInterop.CheckHr(writer.WriteSample(aIdx, asmp), "writeA");
                }
                finally
                {
                    if (aLocked && abuf is not null) _ = abuf.Unlock();
                    MediaFoundationInterop.ReleaseCom(asmp);
                    MediaFoundationInterop.ReleaseCom(abuf);
                }

                MediaFoundationInterop.CheckHr(writer.Finalize(), "Test.Finalize");
            }
            finally
            {
                MediaFoundationInterop.ReleaseCom(writer);
                MediaFoundationInterop.ReleaseCom(attrs);
                _ = MediaFoundationInterop.MFShutdown();
            }
        }

        var greenImg = MakeSolid(160, 120, 0, 220, 0);
        var segAvMp4 = Path.Combine(tempDir, "seg-av.mp4");
        var mergedMixedMp4 = Path.Combine(tempDir, "merged-mixed-av.mp4");
        EncodeAudioVideoMp4(greenImg, segAvMp4, frameCount: 15, fps: 30);
        VideoConcatenator.Concatenate([seg1Mp4, segAvMp4], mergedMixedMp4, preferHevc: false, null, CancellationToken.None);
        var mixedProbe = MediaFoundationFrameCodec.TryProbe(mergedMixedMp4);
        var onset = AudioFixture.FindToneOnset(mergedMixedMp4);
        AssertNear(0.65, onset, "Tone begins 150 ms after silent half-second clip", .065);
        AssertTrue(mixedProbe is { HasAudio: true }, "VideoConcatenator interleaved pass preserves audio across silent+A/V segments");
        AssertTrue(mixedProbe!.DurationSeconds >= 0.85, "VideoConcatenator mixed A/V duration >= 0.85s");
        AssertPixelNear(MediaFoundationFrameCodec.DecodeFrame(mergedMixedMp4, 0.05), 80, 60, 0, 0, 230, "VideoConcatenator mixed A/V first half red");
        AssertPixelNear(MediaFoundationFrameCodec.DecodeFrame(mergedMixedMp4, 0.75), 80, 60, 0, 220, 0, "VideoConcatenator mixed A/V second half green");
    }

    // --- Multi-Track TimelineSequence & TimelineHistory ---
    var seq = new TimelineSequence();
    var hist = new TimelineHistory();
    AssertTrue(seq.IsEmpty, "TimelineSequence starts empty");
    AssertEqual(2, seq.TrackCount, "TimelineSequence starts with 2 tracks (V1, V2)");
    AssertEqual("V1", seq.Tracks[0].Name, "Track 0 is V1");
    AssertEqual("V2", seq.Tracks[1].Name, "Track 1 is V2");
    AssertNear(0, seq.TotalDuration, "TimelineSequence empty duration");

    AssertTrue(seq.AddTrack() is { Name: "V3" }, "AddTrack adds V3");
    AssertEqual(3, seq.TrackCount, "TrackCount is 3 after AddTrack");
    AssertTrue(seq.RemoveTopEmptyTrack(), "RemoveTopEmptyTrack removes empty V3");
    AssertEqual(2, seq.TrackCount, "TrackCount restored to 2");

    var segA = seq.AppendClip("/videos/clipA.mp4", 10.0, colorIndex: 0, trackIndex: 0)!;
    var segB = seq.AppendClip("/videos/clipB.mp4", 6.0, colorIndex: 1, trackIndex: 0)!;
    AssertEqual(2, seq.Count, "TimelineSequence count after append");
    AssertNear(0.0, segA.TimelineStart, "segA starts at 0");
    AssertNear(10.0, segB.TimelineStart, "segB starts at 10");
    AssertNear(16.0, seq.TotalDuration, "TimelineSequence TotalDuration");
    AssertNear(16.0, seq.ActiveDuration, "TimelineSequence ActiveDuration");

    segA.UseCustomNr = true;
    segA.CustomNr = new SegmentNrProfile(true, [new SavedLayer(true, 0, 0.75, 0.80, 0.75)]);
    AssertEqual("NR ×1", segA.NrBadge, "TimelineSegment custom NrBadge");

    hist.PushUndo(seq, segA.Id);
    var splitRes = seq.SplitSegmentAt(segA.Id, 4.0);
    AssertTrue(splitRes.HasValue, "SplitSegmentAt middle of segA succeeds");
    AssertEqual(3, seq.Count, "SplitSegmentAt increases count to 3");
    AssertNear(0.0, splitRes!.Value.Left.TimelineStart, "Split left TimelineStart");
    AssertNear(4.0, splitRes.Value.Left.Duration, "Split left duration");
    AssertNear(4.0, splitRes.Value.Right.TimelineStart, "Split right TimelineStart");
    AssertNear(6.0, splitRes.Value.Right.Duration, "Split right duration");
    AssertTrue(splitRes.Value.Right.UseCustomNr && splitRes.Value.Right.CustomNr?.ActiveLayerCount == 1, "Split clones custom NR profile");
    AssertTrue(seq.SplitSegmentAt(segA.Id, 0.01) is null, "SplitSegmentAt rejects cut too close to edge");

    // Multi-track composition: add overlay clip on V2 from 2.0s to 5.0s covering split seam!
    var segOverlay = seq.AddClipAt("/videos/overlay.mp4", 3.0, timelineStart: 2.0, trackIndex: 1, colorIndex: 2)!;
    AssertEqual(1, segOverlay.TrackIndex, "Overlay clip lands on V2");
    AssertNear(2.0, segOverlay.TimelineStart, "Overlay clip starts at 2.0");

    var comp = seq.ResolveComposition();
    AssertEqual(4, comp.Count, "ResolveComposition produces 4 slices (segA-L 0..2, V2 2..5, segA-R 5..10, segB 10..16)");
    AssertEqual(segA.Id, comp[0].Segment.Id, "Slice 0 is segA left");
    AssertNear(2.0, comp[0].TimelineEnd, "Slice 0 ends at 2.0");
    AssertEqual(segOverlay.Id, comp[1].Segment.Id, "Slice 1 is V2 overlay");
    AssertNear(2.0, comp[1].TimelineStart, "Slice 1 starts at 2.0");
    AssertNear(5.0, comp[1].TimelineEnd, "Slice 1 ends at 5.0");
    AssertEqual(splitRes.Value.Right.Id, comp[2].Segment.Id, "Slice 2 is segA right after overlay");
    AssertNear(5.0, comp[2].SourceStart, "Slice 2 source start offset correctly past overlay end");
    AssertEqual(segB.Id, comp[3].Segment.Id, "Slice 3 is segB");
    AssertNear(16.0, seq.ActiveDuration, "Total ActiveDuration remains 16s");

    // Track visibility hiding V2
    seq.Tracks[1].Visible = false;
    var compHiddenV2 = seq.ResolveComposition();
    AssertEqual(3, compHiddenV2.Count, "Hiding V2 reveals 3 V1 slices only");
    AssertNear(4.0, compHiddenV2[0].TimelineEnd, "V1 slice 0 restores full 0..4 when V2 hidden");
    seq.Tracks[1].Visible = true;

    // True NLE TrimEdge left & right
    // Move overlay from 2..5 to 17..20 so we test TrimEdge freely
    AssertTrue(seq.MoveSegmentTo(segOverlay.Id, 17.0, 1), "MoveSegmentTo moves overlay on V2");
    AssertNear(17.0, segOverlay.TimelineStart, "MoveSegmentTo updated TimelineStart");
    AssertNear(20.0, seq.TotalDuration, "TotalDuration reflects moved overlay");

    // Trim segOverlay left by +1.0 -> TimelineStart becomes 18.0, SourceStart becomes 1.0, TimelineEnd stays 20.0
    AssertTrue(seq.TrimEdge(segOverlay.Id, isStart: true, deltaSeconds: 1.0), "TrimEdge left +1s");
    AssertNear(18.0, segOverlay.TimelineStart, "TrimEdge left shifted TimelineStart");
    AssertNear(1.0, segOverlay.SourceStart, "TrimEdge left shifted SourceStart");
    AssertNear(20.0, segOverlay.TimelineEnd, "TrimEdge left kept TimelineEnd fixed");
    AssertTrue(seq.TrimEdge(segOverlay.Id, isStart: true, deltaSeconds: -1.0), "TrimEdge left pull back -1s");
    AssertNear(17.0, segOverlay.TimelineStart, "TrimEdge left pull back restored TimelineStart");
    AssertNear(0.0, segOverlay.SourceStart, "TrimEdge left pull back restored SourceStart");

    // Trim segOverlay right by -0.5 -> TimelineStart stays 17.0, SourceEnd becomes 2.5
    AssertTrue(seq.TrimEdge(segOverlay.Id, isStart: false, deltaSeconds: -0.5), "TrimEdge right -0.5s");
    AssertNear(17.0, segOverlay.TimelineStart, "TrimEdge right kept TimelineStart fixed");
    AssertNear(2.5, segOverlay.SourceEnd, "TrimEdge right reduced SourceEnd");

    // Locked track protection
    seq.Tracks[1].Locked = true;
    AssertTrue(!seq.TrimEdge(segOverlay.Id, isStart: true, deltaSeconds: 0.5), "Locked track rejects TrimEdge");
    AssertTrue(!seq.MoveSegmentTo(segOverlay.Id, 5.0, 0), "Locked track rejects MoveSegmentTo");
    AssertTrue(seq.SplitSegmentAt(segOverlay.Id, 18.0) is null, "Locked track rejects SplitSegmentAt");
    AssertTrue(!seq.RemoveSegment(segOverlay.Id), "Locked track rejects RemoveSegment");
    seq.Tracks[1].Locked = false;

    // Ripple delete & CloseAllGaps
    AssertTrue(seq.RemoveSegment(splitRes.Value.Right.Id, ripple: true), "Ripple remove segA-right succeeds");
    AssertNear(4.0, segB.TimelineStart, "Ripple remove shifted segB left from 10 to 4");
    AssertTrue(seq.CloseAllGaps() >= 1, "CloseAllGaps shifts overlay on V2 from 17 to 0");
    AssertNear(0.0, segOverlay.TimelineStart, "CloseAllGaps moved V2 clip to 0");

    // Snap & Duplicate
    var dup = seq.DuplicateSegment(segOverlay.Id);
    AssertTrue(dup is not null && Math.Abs(dup.TimelineStart - segOverlay.TimelineEnd) < 1e-4, "DuplicateSegment places copy immediately after");
    AssertNear(4.0, seq.SnapTime(4.08, 0.15), "SnapTime snaps to V1 cut boundary");
    AssertNear(2.0, seq.SnapTime(2.0, 0.15, playheadSeconds: 1.5), "SnapTime leaves far time unchanged");

    // Undo / Redo across track count/state + segments
    AssertTrue(hist.CanUndo, "TimelineHistory CanUndo true");
    AssertTrue(hist.TryUndo(seq, dup!.Id, out var undoSel), "TimelineHistory TryUndo succeeds");
    AssertEqual(2, seq.Count, "TimelineHistory Undo restores 2 segments");
    AssertEqual(segA.Id, undoSel, "TimelineHistory Undo restores selected segment id");
    AssertTrue(hist.CanRedo, "TimelineHistory CanRedo true");
    AssertTrue(hist.TryRedo(seq, undoSel, out _), "TimelineHistory TryRedo succeeds");
    AssertEqual(4, seq.Count, "TimelineHistory Redo restores 4 segments");

    // --- Regression: overlapping V2 ends before V1 finishes (transition must advance to V1 remainder) ---
    var overlapSeq = new TimelineSequence();
    var ovV2 = overlapSeq.AddClipAt("/videos/top.mp4", 17.049, timelineStart: 0.0, trackIndex: 1, colorIndex: 0)!;
    var ovV1 = overlapSeq.AddClipAt("/videos/bottom.mp4", 73.0, timelineStart: 8.365, trackIndex: 0, colorIndex: 1)!;
    var ovSlices = overlapSeq.ResolveComposition();
    AssertEqual(2, ovSlices.Count, "Overlapping V2+V1 resolves into 2 playback slices");
    AssertEqual(ovV2.Id, ovSlices[0].Segment.Id, "Slice 0 is V2");
    AssertNear(17.049, ovSlices[0].TimelineEnd, "Slice 0 ends at 17.049");
    AssertEqual(ovV1.Id, ovSlices[1].Segment.Id, "Slice 1 is V1 continuation");
    AssertNear(17.049, ovSlices[1].TimelineStart, "Slice 1 starts at 17.049");
    AssertNear(8.684, ovSlices[1].SourceStart, "Slice 1 resumes V1 at 8.684s", eps: 1e-4);

    var nextFromSlice = overlapSeq.FindNextCompositionSlice(ovSlices[0]);
    AssertTrue(nextFromSlice.HasValue, "FindNextCompositionSlice advances from V2 to V1 remainder");
    AssertEqual(ovV1.Id, nextFromSlice!.Value.Segment.Id, "Next slice is V1 remainder");
    AssertNear(17.049, nextFromSlice.Value.TimelineStart, "Next slice starts at 17.049");

    var nextWithClockOvershoot = overlapSeq.FindNextCompositionSliceAfter(17.049 + 0.002);
    AssertTrue(nextWithClockOvershoot.HasValue, "FindNextCompositionSliceAfter handles +0.002 clock overshoot");
    AssertEqual(ovV1.Id, nextWithClockOvershoot!.Value.Segment.Id, "Clock overshoot returns V1 remainder");

    var nextWithEofOvershoot = overlapSeq.FindNextCompositionSliceAfter(17.049 + 0.03);
    AssertTrue(nextWithEofOvershoot.HasValue, "FindNextCompositionSliceAfter handles +0.03 EOF overshoot");
    AssertEqual(ovV1.Id, nextWithEofOvershoot!.Value.Segment.Id, "EOF overshoot returns V1 remainder");

    AssertTrue(overlapSeq.FindNextCompositionSlice(ovSlices[1]) is null, "FindNextCompositionSlice returns null at true timeline end");

    // --- Regression: failed/no-op mutations must not pollute Undo or wipe Redo ---
    var noOpSeq = new TimelineSequence();
    var noOpHist = new TimelineHistory();
    var baseSeg = noOpSeq.AppendClip("/videos/base.mp4", 10.0, colorIndex: 0, trackIndex: 0)!;
    var beforeDup = noOpSeq.CaptureSnapshot();
    var dupSeg = noOpSeq.DuplicateSegment(baseSeg.Id);
    AssertTrue(dupSeg is not null, "No-op setup duplicate succeeds");
    noOpHist.PushSnapshot(beforeDup, baseSeg.Id);
    AssertTrue(noOpHist.TryUndo(noOpSeq, dupSeg!.Id, out _), "No-op setup Undo populates Redo stack");
    AssertTrue(!noOpHist.CanUndo && noOpHist.CanRedo, "Undo empty, Redo populated");

    var noOpSnap = noOpSeq.CaptureSnapshot();
    AssertTrue(noOpSeq.SplitAllAt(0.01).Count == 0, "No-op split near start fails cleanly");
    AssertEqual(0, noOpSeq.CloseAllGaps(), "No-op CloseAllGaps finds nothing");
    AssertTrue(!noOpSeq.RemoveTopEmptyTrack(), "No-op RemoveTopEmptyTrack respects minimum 2 tracks");
    _ = noOpSnap; // not pushed on failed ops
    AssertTrue(!noOpHist.CanUndo, "Failed no-ops do not push fake Undo entries");
    AssertTrue(noOpHist.CanRedo, "Failed no-ops do not wipe existing Redo stack");
    AssertTrue(noOpHist.TryRedo(noOpSeq, baseSeg.Id, out _), "Existing Redo still succeeds after failed no-ops");
    AssertEqual(2, noOpSeq.Count, "Redo restores 2 segments cleanly");

    // --- SettingsStore ---
    var settingsFile = Path.Combine(tempDir, "prefs", "settings.json");
    var toSave = new UserSettings(
        NrEnabled: false,
        ModelTag: "sf-performance",
        CodecIndex: 1,
        Layers: SettingsStore.FromNrLayers([layer]),
        CustomEngines: new Dictionary<string, string> { ["NVIDIA"] = tempDir },
        ExportModeIndex: 1,
        QueueWidth: 280,
        InspectorWidth: 340,
        TimelineHeight: 210,
        QueueOpen: false,
        CustomPresets: [new SavedCustomPreset("Cinema", 0.65, 0.70, 0.80)]);
    SettingsStore.Save(toSave, settingsFile);
    var loaded = SettingsStore.Load(settingsFile);
    AssertEqual(false, loaded.NrEnabled, "SettingsStore NrEnabled");
    AssertEqual("sf-performance", loaded.ModelTag, "SettingsStore ModelTag");
    AssertEqual(1, loaded.CodecIndex, "SettingsStore CodecIndex");
    AssertEqual(1, loaded.ExportModeIndex, "SettingsStore ExportModeIndex");
    AssertNear(280, loaded.QueueWidth, "SettingsStore QueueWidth");
    AssertNear(340, loaded.InspectorWidth, "SettingsStore InspectorWidth");
    AssertNear(210, loaded.TimelineHeight, "SettingsStore TimelineHeight");
    AssertEqual(false, loaded.QueueOpen, "SettingsStore QueueOpen");
    AssertEqual(1, loaded.CustomPresets?.Count ?? 0, "SettingsStore CustomPresets count");
    AssertEqual("Cinema", loaded.CustomPresets![0].Name, "SettingsStore CustomPresets name");
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
