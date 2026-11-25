# Motor de renderização do Vídeo NR

Esta branch contém um executável de exportação experimental chamado `VideoNR-RenderWorker.exe`. Ele usa o protocolo 15 que o editor já usa para enviar trabalhos a um processo separado. O executável liga o núcleo de exportação sem compilar a interface QML.

O arquivo em `origem/` contém o código-fonte correspondente ao Veyra-NRVideo 2.2.0. A origem, a licença e os avisos são preservados. [Publicação do código-fonte de origem](https://github.com/Likely7/Veyra-NRVideo/releases/tag/v2.2.0-sources)

## Compilar

Use Windows x64, Visual Studio 2022 com MSVC, Windows SDK com DXC e CMake 3.24 ou mais recente. Informe as pastas de desenvolvimento correspondentes ao FFmpeg, DLSS SDK 310.7, Optical Flow SDK 5.0.7 e cabeçalhos NVENC n13.0.19.0. O libdovi pode ser informado com `-LibDoviRoot`.

```powershell
./motor/Build.ps1 -FfmpegRoot C:/SDK/ffmpeg -DlssSdkRoot C:/SDK/dlss -OpticalFlowSdkRoot C:/SDK/nvof -NvencHeadersRoot C:/SDK/nvenc
```

Para testar, copie `motor/build/worker/Release/VideoNR-RenderWorker.exe` para a mesma pasta que contém `package-manifest.json` dentro de um pacote da GPU. Nessa pasta o editor dá preferência ao executável experimental. Os shaders, DLLs e modelos correspondentes continuam necessários. Os modos NVIDIA e AMD dependem de runtimes que o arquivo de código-fonte não inclui.

O build completo e a exportação precisam ser verificados em Windows com os SDKs e as GPUs correspondentes antes de promover o executável a uma release.
