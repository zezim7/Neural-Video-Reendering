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
var layer = new NrLayer { Index = 2, ResolutionIndex = 1, Intensity = 0.75, Tone = 0.5, Structure = 0.25 };
AssertEqual("Camada NR 2", layer.Title, "NrLayer title");
AssertEqual("5 0.75 0.50 0.25", layer.Encode(), "NrLayer Encode invariant format");
AssertEqual("▸", layer.Chevron, "NrLayer collapsed chevron");
layer.Expanded = true;
AssertEqual("▾", layer.Chevron, "NrLayer expanded chevron");

var cloned = layer.CloneCollapsed();
AssertTrue(!cloned.Expanded, "NrLayer clone starts collapsed");
AssertEqual(layer.Encode(), cloned.Encode(), "NrLayer clone preserves encoding");

// --- ClipItem ---
var clipItem = new ClipItem("/videos/demo.mp4");
AssertEqual("Na fila", clipItem.Subtitle, "ClipItem subtitle without duration");
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
