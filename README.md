<div align="center">

# 🎬 Video NR Studio

**English** · [Português (BR)](README.pt-BR.md) · [简体中文](README.zh-CN.md)

<br/>

**100% offline neural video renderer & trimmer for Windows.**  
Trim with frame-level accuracy, stack up to 4 GPU neural rendering layers, and batch-export to MP4 (H.264 / HEVC) — no cloud, no subscriptions, no watermarks.

[![Release](https://img.shields.io/github/v/release/zezim7/Neural-Video-Reendering?style=for-the-badge&color=3B82F6&label=Download)](https://github.com/zezim7/Neural-Video-Reendering/releases/latest)
[![Build](https://img.shields.io/github/actions/workflow/status/zezim7/Neural-Video-Reendering/windows.yml?branch=main&style=for-the-badge&label=Windows%20CI)](https://github.com/zezim7/Neural-Video-Reendering/actions/workflows/windows.yml)
[![Platform](https://img.shields.io/badge/Windows%2011-x64-171B24?style=for-the-badge&logo=windows&logoColor=3B82F6)](#requirements--supported-gpus)
[![License](https://img.shields.io/badge/License-GPL--3.0-22C55E?style=for-the-badge)](LICENSE.txt)

<br/>

[**⬇️ Download Latest Release**](https://github.com/zezim7/Neural-Video-Reendering/releases/latest) · [**✨ Features**](#-features) · [**⌨️ Shortcuts**](#️-keyboard-shortcuts) · [**🛠️ Build from Source**](#️-build--test)

</div>

---

## ✨ Features

- **🧠 Up to 4 stacked Neural Rendering (NR) layers** — individually configure internal processing resolution (`480p` to `1440p` or `Native`), **Intensity**, **Tone**, and **Structure**, duplicate layers, and toggle each pass on/off with one click.
- **✂️ Precision trimming timeline** — drag handles, type exact `HH:MM:SS.mmm` timecodes, or mark in/out points while playing. Every video in the queue keeps its own trim range.
- **📦 Batch export queue** — drag and drop multiple videos or entire folders into the window and export everything in sequence with shared NR and codec settings.
- **🎞️ MP4 output in H.264 or HEVC** — preserves source resolution, frame rate, and MP4-compatible audio tracks, with safe temp-file overwrite protection and native Windows taskbar progress.
- **🌑 Native studio UI (.NET 8 WPF)** — neutral graphite & electric-blue dark theme, native dark title bar on Windows 10/11, and automatic preference persistence between sessions.
- **🔒 100% local & portable** — your videos never leave your machine. Extract the ZIP and launch `VideoNR.exe` (both the .NET runtime and rendering engine are bundled).

---

## ⬇️ Download & Quick Start

1. Go to the [**latest release**](https://github.com/zezim7/Neural-Video-Reendering/releases/latest).
2. Download the portable package for your GPU:
   - **`VideoNR-NVIDIA-win64.zip`** — for NVIDIA RTX GPUs.
   - **`VideoNR-AMD-win64.zip`** — for AMD Radeon RX 7000 / 9000 GPUs.
3. Extract the entire ZIP into a writable folder.
4. Launch **`VideoNR.exe`**.

---

## 🎮 Requirements & Supported GPUs

- **OS:** Windows 11 x64 (with up-to-date GPU drivers).
- **GPU:**

| NR Mode | Supported GPUs | Package |
| :--- | :--- | :--- |
| **NVIDIA Original** | RTX 50 series | `VideoNR-NVIDIA-win64.zip` |
| **SF310.9.16 (Performance / Legacy)** | RTX 20–50 series | `VideoNR-NVIDIA-win64.zip` |
| **AMD Small / Original** *(experimental)* | Radeon RX 7000 & RX 9000 | `VideoNR-AMD-win64.zip` |

> [!NOTE]
> AMD modes require the HIP runtime and are currently experimental in this editor.  
> The built-in preview uses codecs installed on Windows; even if an uncommon container/codec (e.g., MKV/HEVC without Windows codecs) cannot preview, it can still be exported normally through the queue.

---

## 🚀 Usage

1. **Add videos** by clicking **Selecionar vídeos** or dragging files/folders directly onto the window.
2. **Set a trim range** (optional) with the timeline handles, **`[ Início`** / **`Fim ]`** buttons, or `HH:MM:SS.mmm` inputs.
3. **Tune Neural Rendering** by choosing your NR mode and configuring up to 4 layers (internal resolution, intensity, tone, and structure).
4. **Pick H.264 or HEVC** and click **Iniciar exportação**.

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
| :---: | :--- |
| `Space` or click video | Play / Pause |
| `←` / `→` | Seek −1s / +1s |
| `Shift` + `←` / `→` | Seek −5s / +5s |
| `I` | Mark trim start at current position |
| `O` | Mark trim end at current position |
| `M` | Mute / Unmute preview audio |
| `Delete` | Remove selected clip from queue |

---

## 🛠️ Build & Test

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **MSVC x64** (`cl.exe`, recommended via *Developer PowerShell for VS*) or **MinGW-w64 `g++`** with C++20 support
- **Python 3.11+** *(only required to assemble full offline portable ZIPs)*

### Commands

```powershell
# Run unit tests
dotnet run --project Tests/VideoNR.Tests.csproj

# Build the editor (build/VideoNR.exe) and native adapter (build/Engine.exe)
./Build.ps1

# Build self-contained and package full offline ZIPs into dist/
./Build.ps1 -SelfContained
python scripts/package-windows.py
```

### Repository Layout

```text
├── App/                  # .NET 8 WPF Studio UI
│   ├── Models/           # ClipItem, NrLayer, TrimSelection
│   ├── Services/         # EngineLocator, VideoExporter, SettingsStore
│   ├── Editor.xaml       # Neutral graphite + electric blue studio layout
│   └── Editor.xaml.cs    # Player, timeline, queue, and export controller
├── Engine.cpp            # Native C++20 (MSVC) adapter for the rendering engine
├── Tests/                # Unit tests (.NET 8)
├── scripts/              # Automated Windows portable ZIP packager
└── Build.ps1             # Windows build script
```

---

## 📄 License & Third-Party Notices

This project is licensed under **[GPL-3.0](LICENSE.txt)**.  
See [DEPENDENCIAS.md](DEPENDENCIAS.md) for third-party components and notices bundled in the portable packages.
