# Vídeo NR Editor

Editor de vídeos gravados para Windows. Permite selecionar um trecho, aplicar até quatro camadas de renderização neural e salvar um MP4.

## Baixar

Escolha `VideoNR-NVIDIA-win64.zip` ou `VideoNR-AMD-win64.zip` na [release mais recente](../../releases/latest). Extraia o ZIP inteiro para uma pasta gravável e abra `VideoNR.exe`. Cada ZIP inclui o motor na pasta `motor` e o runtime do .NET.

## Uso

1. Adicione um ou mais vídeos à fila.
2. Selecione um vídeo e arraste as alças turquesa ou digite os tempos de início e fim em HH:MM:SS.mmm. Cada vídeo guarda seu próprio corte.
3. Escolha a versão do NR e ajuste a intensidade, a resolução interna, o tom e a estrutura de cada camada.
4. Selecione H.264 ou HEVC e clique em **Iniciar exportação**. A fila usa as mesmas configurações de NR e codec e respeita o corte de cada item.

O player usa os codecs disponíveis no Windows. A exportação mantém a resolução e a taxa de quadros da origem e solicita as faixas de áudio compatíveis com MP4.

## Requisitos

Windows 11 x64 e uma GPU compatível com o pacote escolhido.

| Modo | GPUs compatíveis | Pacote |
| --- | --- | --- |
| NVIDIA original | RTX 50 | NVIDIA |
| SF310.9.16, desempenho ou legado | RTX 20 a 50 | NVIDIA |
| AMD Small ou Original (experimental) | RX 7000 e RX 9000 | AMD |

Os modos AMD exigem o runtime HIP e ainda precisam de teste em uma Radeon neste editor.

## Compilar e testar

Use o SDK .NET 8 e MSVC x64 (`cl.exe`, recomendado via Developer PowerShell do Visual Studio) ou um compilador `g++` MinGW-w64 x64 com C++20.

- Testes unitários: `dotnet run --project Tests/VideoNR.Tests.csproj`
- Compilar: `./Build.ps1` (ou `./Build.ps1 -Compiler caminho/para/g++.exe`). Os executáveis ficam em `build/VideoNR.exe` e `build/Engine.exe`.
- Empacotar os ZIPs completos: `./Build.ps1 -SelfContained` no Windows e depois `python scripts/package-windows.py` com Python 3.11 ou superior. Os ZIPs ficam em `dist/`.

O código é distribuído sob [GPL-3.0](LICENSE.txt).
