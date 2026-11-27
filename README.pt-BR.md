# Video NR

[English](README.md) · **Português (BR)** · [简体中文](README.zh-CN.md)

Editor de vídeo para Windows 11 x64. Monte a sequência em até seis faixas, aplique NR na GPU e exporte para MP4. Cada pacote portátil inclui o motor Veyra do fabricante escolhido.

![Editor com duas faixas e exportação por trechos](docs/screenshots/editor-main.png)

## Montagem e exportação

Arraste vídeos da biblioteca para as faixas. O trecho ativo da faixa visível mais alta aparece na sequência. Os espaços entre trechos são omitidos na reprodução e na exportação. O cadeado protege o conteúdo da faixa. Use a tesoura na agulha para dividir, arraste as bordas para aparar ou informe os tempos de origem em `HH:MM:SS.mmm`.

| Modo | Resultado |
| --- | --- |
| Sequência | Um MP4 com a montagem visível. Na junção, o primeiro trecho define o tamanho da imagem. Trechos de outro formato recebem barras para preservar a proporção. A primeira faixa de áudio de cada parte é combinada em AAC estéreo. |
| Trechos | Um MP4 por segmento ativo de faixa visível, inclusive quando outro segmento o cobre. Preserva os cortes e o NR do segmento. |
| Originais | Um MP4 por arquivo inteiro da biblioteca com o perfil NR global. |

O perfil global aceita até quatro camadas, cada uma com resolução interna, intensidade, tom e estrutura. Selecione um trecho e ative seu perfil próprio para ajustar apenas aquele segmento. No pacote AMD, a resolução interna chega a 1080p, inclusive nas opções Nativa e 1440p. Esse limite se refere ao processamento neural.

## Print de vídeo ou imagem

Use `P` para o quadro atual, `Ctrl+Shift+P` para uma região da tela ou `Ctrl+V` para a área de transferência. Também é possível abrir uma imagem. A navegação quadro a quadro usa os timestamps de apresentação disponíveis no Windows, inclusive em vídeos de taxa variável.

Na comparação, **Sem NR** e **NR** passam pelo mesmo motor e pela mesma codificação de saída. A entrada estática usa um quadro YUV sem compressão, limitado a 4096 × 2304 e com transparência composta sobre preto. O comparador permite inspeção a 100%, cópia e salvamento em PNG ou JPEG.

![Comparador a exibir o mesmo padrão de teste nos dois lados para ilustrar os controles](docs/screenshots/print-preview.png)

## Download e uso rápido

1. Baixe o pacote portátil para a sua GPU em [Releases](https://github.com/zezim7/Neural-Video-Reendering/releases/latest):
   - `VideoNR-NVIDIA-win64.zip` — placas NVIDIA RTX
   - `VideoNR-AMD-win64.zip` — placas AMD Radeon RX 7000 / 9000
2. Extraia todo o conteúdo do ZIP para uma pasta gravável.
3. Abra o `VideoNR.exe`.

## Requisitos e GPUs compatíveis

- **Sistema operacional:** Windows 11 x64 com drivers de GPU atualizados.

| Modo NR | GPUs compatíveis | Pacote |
| :--- | :--- | :--- |
| **NVIDIA original** | RTX série 50 | `VideoNR-NVIDIA-win64.zip` |
| **SF310.9.16 (desempenho / legado)** | RTX séries 20 a 50 | `VideoNR-NVIDIA-win64.zip` |
| **AMD Small / Original** *(experimental)* | Radeon RX 7000 e RX 9000 | `VideoNR-AMD-win64.zip` |

> [!NOTE]
> Os modos AMD exigem o runtime HIP instalado e estão em fase experimental neste editor.
> A prévia e a leitura dos tempos dependem dos codecs instalados no Windows. O motor usa seu próprio decodificador na exportação.

## Atalhos de teclado

### Editor principal

| Atalho | Ação |
| :--- | :--- |
| `Espaço` ou clique no vídeo | Reproduzir / Pausar |
| `,` / `.` | Quadro anterior / seguinte pelo timestamp de apresentação |
| `←` / `→` | Voltar / Avançar 1 segundo |
| `Shift` + `←` / `→` | Voltar / Avançar 5 segundos |
| `I` / `O` | Marcar início / fim do corte no ponto atual |
| `M` | Silenciar / Ativar áudio do preview |
| `Q` | Mostrar / Ocultar fila lateral |
| `P` | Renderizar frame atual do vídeo com NR |
| `Ctrl` + `Shift` + `P` | Capturar tela e aplicar NR |
| `Ctrl` + `V` | Colar imagem da área de transferência para NR |
| `Ctrl` + `O` | Abrir vídeos |
| `C` / `S` | Cortar na agulha |
| `Ctrl+D` / `E` | Duplicar / ativar ou desativar o trecho selecionado |
| `G` / `F` | Fechar espaços nas faixas desbloqueadas / ajustar o zoom da timeline |
| `Delete` | Remover o trecho selecionado. Com foco na biblioteca, remover o arquivo da biblioteca. |
| `Shift+Delete` | Remover o trecho selecionado e fechar o espaço na faixa |
| `Ctrl+Z` / `Ctrl+Y` ou `Ctrl+Shift+Z` | Desfazer / refazer |

### Visualização de Print (`NR`)

| Atalho / Gesto | Ação |
| :--- | :--- |
| `1` / `2` | Alternar entre Ajustar e `100%` (1:1) |
| Duplo-clique ou `Ctrl` + Roda | Alternar Ajustar / `100%` focado no cursor |
| Botão do meio, ou arrastar (`NR` / `Sem NR` / segurando `Espaço`) | Mover imagem (pan) no zoom `100%` |
| `←` / `→` (`Shift` + `←` / `→`) | Ajustar divisória ±2% (±10%) no modo Comparar |
| `Ctrl` + `C` | Copiar imagem renderizada para a área de transferência |
| `Ctrl` + `S` | Salvar imagem renderizada em PNG |
| `Esc` | Fechar visualização |

## Compilar e testar

### Pré-requisitos
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **MSVC x64** (`cl.exe`, via *Developer PowerShell for VS*) ou **MinGW-w64 `g++`** com C++20
- **Python 3.11+** *(apenas para montar os ZIPs portáteis completos)*

### Comandos

```powershell
# Executar testes unitários
dotnet run --project Tests/VideoNR.Tests.csproj
dotnet run --project Tests.Windows/VideoNR.WindowsTests.csproj

# Em uma GPU compatível, verificar um pacote extraído e salvar o padrão de teste, as duas imagens, o MP4 cortado e os parâmetros
dotnet run --project Tests.Windows/VideoNR.WindowsTests.csproj -- --verify-package C:\VideoNR .\results

# Compilar o editor (build/VideoNR.exe) e o adaptador nativo (build/Engine.exe)
./Build.ps1

# Publicar self-contained e gerar os ZIPs portáteis em dist/
./Build.ps1 -SelfContained
python scripts/package-windows.py
```

### Estrutura do repositório

```text
├── App/                        # Interface WPF (.NET 8)
│   ├── Assets/                 # Ícone multi-resolução da aplicação
│   ├── Helpers/                # Integração DWM dark mode e WindowChrome
│   ├── Imaging/                # Captura de tela GDI, leitura/escrita WIC, miniaturas e leitura de FPS
│   ├── Models/                 # ClipItem, NrLayer, TimelineSequence, TimelineSegment, TimelineHistory
│   ├── Services/               # EngineLocator, VideoExporter, PrintProcessor, MediaFoundationFrameCodec, MediaFoundationInterop, RawImage, SettingsStore
│   ├── Themes/                 # Sistema visual compartilhado (Theme.xaml)
│   ├── Editor.xaml             # Layout principal
│   ├── Editor.xaml.cs          # Barra de título, fila, predefinições e atalhos
│   ├── Editor.Playback.cs      # Player, régua de tempo, corte, scrub e passo por timestamp
│   ├── Editor.Export.cs        # Exportação em lote e fluxo de Print NR
│   ├── PrintPreviewWindow.*    # Comparador antes/depois e inspeção 1:1 com pan/zoom
│   └── ScreenCaptureOverlay.*  # Overlay multi-monitor para captura de tela
├── Engine.cpp                  # Adaptador nativo C++20 (suporta --max-frames)
├── Tests/                      # Testes unitários (.NET 8)
├── docs/screenshots/           # Capturas de tela da aplicação
├── scripts/                    # Empacotamento automatizado dos ZIPs Windows
└── Build.ps1                   # Script de build Windows
```

## Licença

Distribuído sob a licença [GPL-3.0](LICENSE.txt). Consulte [DEPENDENCIAS.md](DEPENDENCIAS.md) para detalhes sobre os componentes e avisos de terceiros incluídos nos pacotes portáteis.
