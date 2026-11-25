# Video NR

[English](README.md) · [Português (BR)](README.pt-BR.md) · **简体中文**

适用于 Windows 11 x64 的全离线神经视频渲染、剪辑与 DLSS 5 单帧/图像处理工具。通过内置的 Veyra 引擎在本地 GPU 运行，无需联网。

![Video NR 主界面](docs/screenshots/editor-main.png)

## 主要功能

- **多层神经渲染（最多 4 层）** — 独立设置每层的内部处理分辨率（`480p` 至 `1440p` 或 `原生`）、**强度**、**色调**与**结构**，支持一键预设（`Sutil` / `Equilibrado` / `Máximo`）、复制图层及双击滑块重置为 `1.00`。
- **DLSS 5 / NR 截图与图像处理** — 一键渲染当前视频帧（`P`）、区域/显示器截图（`Ctrl+Shift+P`）、打开静态图片（`PNG` / `JPG` / `BMP` / `WEBP` / `TIFF`）或直接粘贴剪贴板图片（`Ctrl+V`）。内置交互式对比窗口，支持前后拖动对比、`100%`（1:1）像素级平移查看、光标焦点缩放切换、复制及保存 PNG/JPG。
- **精准剪辑与缩略图队列** — 支持时间刻度拖动、按真实检测帧率逐帧微调（`,` / `.`，未知时回退至 `30 fps`）、剪辑手柄与 `HH:MM:SS.mmm` 时间码输入。可折叠队列自动提取视频缩略图、分辨率与帧率信息，并独立保存每个片段的剪辑区间。
- **批量导出 MP4（H.264 / HEVC）** — 拖入多个视频或整个文件夹即可按队列依次导出，保留源分辨率、帧率与兼容音轨。
- **原生 Windows 11 界面** — .NET 8 WPF 暗室风格界面，采用一体化 `WindowChrome` 标题栏、`Segoe Fluent Icons` 图标、Per-Monitor DPI v2 适配与自动配置持久化。

![DLSS 5 / NR 前后对比预览窗口](docs/screenshots/print-preview.png)

## 下载与使用

1. 前往 [Releases](https://github.com/zezim7/Neural-Video-Reendering/releases/latest) 下载对应显卡的便携压缩包：
   - `VideoNR-NVIDIA-win64.zip` — NVIDIA RTX 显卡
   - `VideoNR-AMD-win64.zip` — AMD Radeon RX 7000 / 9000 显卡
2. 将压缩包完整解压到可写目录。
3. 运行 `VideoNR.exe`。

## 系统要求与支持的显卡

- **操作系统：** Windows 11 x64（需安装最新显卡驱动）。

| NR 模式 | 支持的显卡 | 安装包 |
| :--- | :--- | :--- |
| **NVIDIA Original** | RTX 50 系列 | `VideoNR-NVIDIA-win64.zip` |
| **SF310.9.16（性能 / 传统）** | RTX 20–50 系列 | `VideoNR-NVIDIA-win64.zip` |
| **AMD Small / Original** *（实验性）* | Radeon RX 7000 / RX 9000 | `VideoNR-AMD-win64.zip` |

> [!NOTE]
> AMD 模式需要安装 HIP 运行时，目前处于实验阶段。
> 预览播放器依赖 Windows 系统解码器；即使某些格式无法直接预览，仍可通过导出队列或单帧渲染正常处理。

## 快捷键

### 主编辑器

| 快捷键 | 功能 |
| :--- | :--- |
| `Space` 或点击画面 | 播放 / 暂停 |
| `,` / `.` | 按片段真实帧率后退 / 前进 1 帧 |
| `←` / `→` | 后退 / 前进 1 秒 |
| `Shift` + `←` / `→` | 后退 / 前进 5 秒 |
| `I` / `O` | 在当前位置标记剪辑入点 / 出点 |
| `M` | 静音 / 取消静音预览音频 |
| `Q` | 显示 / 隐藏队列侧栏 |
| `P` | 使用 DLSS 5 / NR 渲染当前视频帧 |
| `Ctrl` + `Shift` + `P` | 屏幕截图并应用 DLSS 5 / NR |
| `Ctrl` + `V` | 粘贴剪贴板图片应用 DLSS 5 / NR |
| `Ctrl` + `O` | 打开视频文件 |
| `Delete` | 从队列移除当前选中的视频 |

### 对比预览窗口（`DLSS 5 / NR`）

| 快捷键 / 操作 | 功能 |
| :--- | :--- |
| `1` / `2` | 切换「适应窗口」与 `100%`（1:1）缩放 |
| 双击画面或 `Ctrl` + 滚轮 | 以光标为中心切换适应 / `100%` |
| 中键拖动，或在 `NR` / `Original` 模式及按住 `Space` 时左键拖动 | `100%` 缩放下平移画面 |
| `←` / `→`（`Shift` + `←` / `→`） | 对比模式下微调分割线 ±2%（±10%） |
| `Ctrl` + `C` | 复制渲染结果到剪贴板 |
| `Ctrl` + `S` | 将渲染结果保存为 PNG |
| `Esc` | 关闭预览窗口 |

## 构建与测试

### 环境要求
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **MSVC x64**（`cl.exe`，推荐使用 *Developer PowerShell for VS*）或支持 C++20 的 **MinGW-w64 `g++`**
- **Python 3.11+** *（仅在打包完整离线便携 ZIP 时需要）*

### 命令

```powershell
# 运行单元测试
dotnet run --project Tests/VideoNR.Tests.csproj

# 编译编辑器 (build/VideoNR.exe) 与原生适配器 (build/Engine.exe)
./Build.ps1

# 发布独立版并在 dist/ 中打包便携 ZIP
./Build.ps1 -SelfContained
python scripts/package-windows.py
```

### 项目结构

```text
├── App/                        # .NET 8 WPF 应用
│   ├── Assets/                 # 多分辨率应用图标
│   ├── Helpers/                # DWM 深色模式与 WindowChrome 辅助类
│   ├── Imaging/                # GDI 截图、WIC 图片读写、视频缩略图与帧率探测
│   ├── Models/                 # ClipItem, NrLayer, TrimSelection
│   ├── Services/               # EngineLocator, VideoExporter, PrintProcessor, MediaFoundationFrameCodec, MediaFoundationInterop, RawImage, SettingsStore
│   ├── Themes/                 # 共享暗室样式主题 (Theme.xaml)
│   ├── Editor.xaml             # 主界面布局
│   ├── Editor.xaml.cs          # 标题栏、队列、预设与快捷键
│   ├── Editor.Playback.cs      # 播放器、时间标尺、剪辑、拖动与真实帧率逐帧步进
│   ├── Editor.Export.cs        # 批量导出与 DLSS 5 截图处理流程
│   ├── PrintPreviewWindow.*    # 交互式前后对比与 1:1 平移缩放预览窗口
│   └── ScreenCaptureOverlay.*  # 多显示器区域截图遮罩窗口
├── Engine.cpp                  # 原生 C++20 共享内存适配器（支持 --max-frames）
├── Tests/                      # 单元测试 (.NET 8)
├── docs/screenshots/           # 应用界面截图
├── scripts/                    # Windows 便携 ZIP 打包脚本
└── Build.ps1                   # Windows 构建脚本
```

## 许可证

本项目基于 [GPL-3.0](LICENSE.txt) 开源。便携包中包含的第三方组件与声明详见 [DEPENDENCIAS.md](DEPENDENCIAS.md)。
