# Video NR

**English** · [Português (BR)](README.pt-BR.md) · [简体中文](README.zh-CN.md)

Offline neural video renderer, trimmer, and still-frame DLSS 5 processor for Windows 11 x64. Powered locally on GPU through the bundled Veyra rendering engine with no cloud dependencies.

![Video NR main workspace](docs/screenshots/editor-main.png)

## Features

- **Stacked Neural Rendering (up to 4 layers)** — configure internal processing resolution (`480p` to `1440p` or `Native`), **Intensity**, **Tone**, and **Structure** per layer, apply presets (`Sutil`, `Equilibrado`, `Máximo`), duplicate layers, or double-click any slider to reset to `1.00`.
- **DLSS 5 / NR Print & Image Processing** — render a single video frame at the current playhead (`P`), capture a screen region/monitor (`Ctrl+Shift+P`), open an image (`PNG`, `JPG`, `BMP`, `WEBP`, `TIFF`), or paste from the clipboard (`Ctrl+V`). Includes an interactive before/after viewer with `100%` (1:1) drag-to-pan inspection, focal-point zoom toggle, clipboard copy, and PNG/JPG export.
- **Frame-Accurate Trimming & Queue** — scrub along the time ruler, step frame-by-frame (`,` / `.`) using the clip's real detected frame rate (falling back to `30 fps` when unknown), drag trim handles, or enter `HH:MM:SS.mmm` bounds. Videos in the collapsible queue keep independent trim ranges and display live extracted thumbnails, resolution, and frame rate metadata.
- **Batch MP4 Export (H.264 / HEVC)** — drag videos or folders onto the window and export sequentially while preserving source resolution, frame rate, and compatible audio tracks.
- **Native Windows 11 Interface** — .NET 8 WPF darkroom UI with integrated `WindowChrome` title bar, `Segoe Fluent Icons`, Per-Monitor DPI v2 support, and automatic preference persistence.

![DLSS 5 / NR before-and-after inspection](docs/screenshots/print-preview.png)

## Download & Quick Start

1. Download the portable package for your GPU from [Releases](https://github.com/zezim7/Neural-Video-Reendering/releases/latest):
   - `VideoNR-NVIDIA-win64.zip` — NVIDIA RTX GPUs
   - `VideoNR-AMD-win64.zip` — AMD Radeon RX 7000 / 9000 GPUs
2. Extract the entire archive into a writable folder.
3. Launch `VideoNR.exe`.

## Requirements & Supported GPUs

- **OS:** Windows 11 x64 with up-to-date GPU drivers.

| NR Mode | Supported GPUs | Package |
| :--- | :--- | :--- |
| **NVIDIA Original** | RTX 50 series | `VideoNR-NVIDIA-win64.zip` |
| **SF310.9.16 (Performance / Legacy)** | RTX 20–50 series | `VideoNR-NVIDIA-win64.zip` |
| **AMD Small / Original** *(experimental)* | Radeon RX 7000 & RX 9000 | `VideoNR-AMD-win64.zip` |

> [!NOTE]
> AMD modes require the HIP runtime and are experimental in this editor.
> The built-in preview uses system codecs installed on Windows; files that cannot preview in the player (e.g., MKV/HEVC without Windows codecs) can still be exported and single-frame printed through the GPU engine.

## Keyboard Shortcuts

### Main Editor

| Shortcut | Action |
| :--- | :--- |
| `Space` or click video | Play / Pause |
| `,` / `.` | Step −1 / +1 frame (uses detected clip FPS) |
| `←` / `→` | Seek −1s / +1s |
| `Shift` + `←` / `→` | Seek −5s / +5s |
| `I` / `O` | Mark trim start / end at current position |
| `M` | Mute / Unmute preview audio |
| `Q` | Toggle queue drawer |
| `P` | Render current video frame with DLSS 5 / NR |
| `Ctrl` + `Shift` + `P` | Screen capture print with DLSS 5 / NR |
| `Ctrl` + `V` | Paste clipboard image for DLSS 5 / NR |
| `Ctrl` + `O` | Open video files |
| `Delete` | Remove selected clip from queue |

### Print Preview (`DLSS 5 / NR`)

| Shortcut / Gesture | Action |
| :--- | :--- |
| `1` / `2` | Switch between Fit and `100%` (1:1) zoom |
| Double-click or `Ctrl` + Wheel | Toggle Fit / `100%` centered on cursor |
| Middle-drag, or Left-drag (`NR` / `Original` / `Space` held) | Pan image while zoomed to `100%` |
| `←` / `→` (`Shift` + `←` / `→`) | Nudge split divider ±2% (±10%) in Compare mode |
| `Ctrl` + `C` | Copy rendered image to clipboard |
| `Ctrl` + `S` | Save rendered image as PNG |
| `Esc` | Close preview |

## Build & Test

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **MSVC x64** (`cl.exe`, via *Developer PowerShell for VS*) or **MinGW-w64 `g++`** with C++20 support
- **Python 3.11+** *(only required to assemble offline portable ZIPs)*

### Commands

```powershell
# Run unit tests
dotnet run --project Tests/VideoNR.Tests.csproj

# Build the editor (build/VideoNR.exe) and native adapter (build/Engine.exe)
./Build.ps1

# Build self-contained and package portable ZIPs into dist/
./Build.ps1 -SelfContained
python scripts/package-windows.py
```

### Repository Structure

```text
├── App/                        # .NET 8 WPF application
│   ├── Assets/                 # Multi-resolution application icon
│   ├── Helpers/                # DWM dark mode and window chrome helpers
│   ├── Imaging/                # GDI screen capture, WIC I/O, video thumbnails & FPS probing
│   ├── Models/                 # ClipItem, NrLayer, TrimSelection
│   ├── Services/               # EngineLocator, VideoExporter, PrintProcessor, MediaFoundationFrameCodec, MediaFoundationInterop, RawImage, SettingsStore
│   ├── Themes/                 # Shared Fluent darkroom styles and tokens
│   ├── Editor.xaml             # Main window layout
│   ├── Editor.xaml.cs          # Window chrome, queue, presets, shortcuts
│   ├── Editor.Playback.cs      # Player, time ruler, trim, scrubbing & real-FPS stepping
│   ├── Editor.Export.cs        # Batch export and DLSS 5 print workflows
│   ├── PrintPreviewWindow.*    # Interactive before/after & 1:1 pan/zoom inspection modal
│   └── ScreenCaptureOverlay.*  # Multi-monitor region capture overlay
├── Engine.cpp                  # Native C++20 shared-memory adapter (supports --max-frames)
├── Tests/                      # Unit tests (.NET 8)
├── docs/screenshots/           # Application screenshots
├── scripts/                    # Automated Windows portable ZIP packager
└── Build.ps1                   # Windows build script
```

## License

Licensed under [GPL-3.0](LICENSE.txt). See [DEPENDENCIAS.md](DEPENDENCIAS.md) for third-party components and notices bundled in the portable packages.
