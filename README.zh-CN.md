<div align="center">

# 🎬 Video NR Studio

[English](README.md) · [Português (BR)](README.pt-BR.md) · **简体中文**

<br/>

**适用于 Windows 的 100% 离线神经视频渲染与剪辑工具。**  
精准剪辑片段、叠加最多 4 层 GPU 神经渲染增强并批量导出为 MP4（H.264 / HEVC）—— 无需云端、无需订阅、无水印。

[![Release](https://img.shields.io/github/v/release/zezim7/Neural-Video-Reendering?style=for-the-badge&color=3B82F6&label=下载)](https://github.com/zezim7/Neural-Video-Reendering/releases/latest)
[![Build](https://img.shields.io/github/actions/workflow/status/zezim7/Neural-Video-Reendering/windows.yml?branch=main&style=for-the-badge&label=Windows%20CI)](https://github.com/zezim7/Neural-Video-Reendering/actions/workflows/windows.yml)
[![平台](https://img.shields.io/badge/Windows%2011-x64-171B24?style=for-the-badge&logo=windows&logoColor=3B82F6)](#系统要求与支持的显卡)
[![许可证](https://img.shields.io/badge/许可证-GPL--3.0-22C55E?style=for-the-badge)](LICENSE.txt)

<br/>

[**⬇️ 下载最新版本**](https://github.com/zezim7/Neural-Video-Reendering/releases/latest) · [**✨ 核心功能**](#-核心功能) · [**⌨️ 快捷键**](#️-快捷键) · [**🛠️ 从源码构建**](#️-构建与测试)

</div>

---

## ✨ 核心功能

- **🧠 最多叠加 4 层神经渲染（NR）** — 独立调节每层的内部处理分辨率（`480p` 至 `1440p` 或 `原生`）、**强度**、**色调**与**结构**，支持一键复制与单独开关。
- **✂️ 精准时间轴剪辑** — 拖动裁剪手柄、输入 `HH:MM:SS.mmm` 时间码，或在播放时即时标记入点/出点；队列中的每个视频均独立保存各自的剪辑区间。
- **📦 批量导出队列** — 直接将多个视频或整个文件夹拖入窗口，使用统一的 NR 参数与编码格式依次导出。
- **🎞️ H.264 / HEVC MP4 导出** — 保留源视频的分辨率、帧率及兼容的音轨，通过临时文件实现安全覆盖，并在 Windows 任务栏实时显示进度。
- **🌑 原生工作室深色界面（.NET 8 WPF）** — 中性石墨灰 + 电光蓝视觉风格，适配 Windows 10/11 深色标题栏，并自动记忆上次的偏好设置。
- **🔒 100% 本地便携运行** — 视频全程保留在本地，无需联网。解压 ZIP 后直接运行 `VideoNR.exe` 即可（已内置 .NET 运行时与渲染引擎）。

---

## ⬇️ 下载与快速开始

1. 前往 [**最新发布页**](https://github.com/zezim7/Neural-Video-Reendering/releases/latest)。
2. 根据您的显卡下载对应便携包：
   - **`VideoNR-NVIDIA-win64.zip`** — 适用于 NVIDIA RTX 显卡。
   - **`VideoNR-AMD-win64.zip`** — 适用于 AMD Radeon RX 7000 / 9000 显卡。
3. 将整个 ZIP 解压至可写文件夹。
4. 运行 **`VideoNR.exe`**。

---

## 🎮 系统要求与支持的显卡

- **操作系统：** Windows 11 x64（需安装最新显卡驱动）。
- **支持的显卡：**

| NR 模式 | 支持的显卡 | 对应安装包 |
| :--- | :--- | :--- |
| **NVIDIA Original** | RTX 50 系列 | `VideoNR-NVIDIA-win64.zip` |
| **SF310.9.16（性能 / 传统）** | RTX 20–50 系列 | `VideoNR-NVIDIA-win64.zip` |
| **AMD Small / Original** *（实验性）* | Radeon RX 7000 / RX 9000 | `VideoNR-AMD-win64.zip` |

> [!NOTE]
> AMD 模式需要安装 HIP 运行时，目前在本编辑器中仍处于实验阶段。  
> 预览播放器依赖 Windows 系统已安装的解码器；即使部分格式（如缺少解码器的 MKV/HEVC）无法预览，仍可通过导出队列正常处理。

---

## 🚀 使用步骤

1. **添加视频**：点击 **Selecionar vídeos** 或直接将视频文件/文件夹拖入窗口。
2. **设置剪辑区间**（可选）：拖动时间轴手柄、点击 **`[ Início`** / **`Fim ]`**，或输入 `HH:MM:SS.mmm`。
3. **配置 NR 增强**：选择 NR 引擎版本，并调整最多 4 层的内部分辨率、强度、色调与结构。
4. **选择编码格式（`H.264` 或 `HEVC`）** 并点击 **Iniciar exportação**。

---

## ⌨️ 快捷键

| 快捷键 | 功能 |
| :---: | :--- |
| `Space` 或点击画面 | 播放 / 暂停 |
| `←` / `→` | 后退 / 前进 1 秒 |
| `Shift` + `←` / `→` | 后退 / 前进 5 秒 |
| `I` | 在当前位置标记剪辑起点 |
| `O` | 在当前位置标记剪辑终点 |
| `M` | 静音 / 取消静音预览音频 |
| `Delete` | 从队列移除当前选中的视频 |

---

## 🛠️ 构建与测试

### 环境要求
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **MSVC x64**（`cl.exe`，推荐使用 *Developer PowerShell for VS*）或支持 C++20 的 **MinGW-w64 `g++`**
- **Python 3.11+** *（仅在打包完整离线便携 ZIP 时需要）*

### 常用命令

```powershell
# 运行单元测试
dotnet run --project Tests/VideoNR.Tests.csproj

# 编译编辑器 (build/VideoNR.exe) 与原生适配器 (build/Engine.exe)
./Build.ps1

# 发布独立版并在 dist/ 中打包完整离线 ZIP
./Build.ps1 -SelfContained
python scripts/package-windows.py
```

### 项目结构

```text
├── App/                  # .NET 8 WPF 工作室界面
│   ├── Models/           # ClipItem, NrLayer, TrimSelection
│   ├── Services/         # EngineLocator, VideoExporter, SettingsStore
│   ├── Editor.xaml       # 石墨灰 + 电光蓝工作室布局
│   └── Editor.xaml.cs    # 播放器、时间轴、队列与导出控制器
├── Engine.cpp            # 渲染引擎的 C++20 (MSVC) 原生适配器
├── Tests/                # 单元测试 (.NET 8)
├── scripts/              # Windows 离线便携 ZIP 打包脚本
└── Build.ps1             # Windows 构建脚本
```

---

## 📄 许可证与第三方声明

本项目采用 **[GPL-3.0](LICENSE.txt)** 许可证开源。  
便携包中包含的第三方组件与声明详见 [DEPENDENCIAS.md](DEPENDENCIAS.md)。
