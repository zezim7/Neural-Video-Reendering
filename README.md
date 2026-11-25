<div align="center">

# 🎬 Vídeo NR Studio

**Editor e renderizador neural de vídeo 100% offline para Windows.**  
Corte com precisão, empilhe até 4 camadas de melhoria neural na GPU e exporte em lote para MP4 (H.264 / HEVC) — sem nuvem, sem assinatura e sem marca d'água.

[![Release](https://img.shields.io/github/v/release/zezim7/Neural-Video-Reendering?style=for-the-badge&color=3B82F6&label=Download)](https://github.com/zezim7/Neural-Video-Reendering/releases/latest)
[![Build](https://img.shields.io/github/actions/workflow/status/zezim7/Neural-Video-Reendering/windows.yml?branch=main&style=for-the-badge&label=Windows%20CI)](https://github.com/zezim7/Neural-Video-Reendering/actions/workflows/windows.yml)
[![Plataforma](https://img.shields.io/badge/Windows%2011-x64-171B24?style=for-the-badge&logo=windows&logoColor=3B82F6)](#requisitos-e-gpus-compatíveis)
[![Licença](https://img.shields.io/badge/Licença-GPL--3.0-22C55E?style=for-the-badge)](LICENSE.txt)

<br/>

[**⬇️ Baixar última versão**](https://github.com/zezim7/Neural-Video-Reendering/releases/latest) · [**✨ Recursos**](#-recursos) · [**⌨️ Atalhos**](#️-atalhos-de-teclado) · [**🛠️ Compilar**](#️-compilar-e-testar)

</div>

---

## ✨ Recursos

- **🧠 Até 4 camadas de Renderização Neural (NR) em sequência** — ajuste individualmente a resolução interna (`480p` a `1440p` ou `Nativa`), **Intensidade**, **Tom** e **Estrutura**, duplique camadas e ative/desative cada uma com um clique.
- **✂️ Linha do tempo com corte preciso** — arraste as alças, digite timecodes em `HH:MM:SS.mmm` ou marque início e fim direto durante a reprodução. Cada vídeo da fila guarda o seu próprio corte.
- **📦 Fila de exportação em lote** — arraste vídeos ou pastas inteiras para a janela e exporte tudo de uma vez com as mesmas configurações de NR e codec.
- **🎞️ Saída MP4 em H.264 ou HEVC** — preserva a resolução, taxa de quadros e faixas de áudio compatíveis da origem, com sobrescrita segura via arquivo temporário e progresso na barra de tarefas do Windows.
- **🌑 Interface de estúdio nativa (.NET 8 WPF)** — tema escuro em grafite neutro + azul elétrico, barra de título escura no Windows 10/11 e persistência automática das suas preferências entre sessões.
- **🔒 100% local e portátil** — nenhum vídeo sai do seu computador. Basta extrair o ZIP e abrir `VideoNR.exe` (runtime .NET e motor já incluídos).

---

## ⬇️ Download e Instalação

1. Acesse a [**release mais recente**](https://github.com/zezim7/Neural-Video-Reendering/releases/latest).
2. Baixe o pacote correspondente à sua placa de vídeo:
   - **`VideoNR-NVIDIA-win64.zip`** — para placas NVIDIA RTX.
   - **`VideoNR-AMD-win64.zip`** — para placas AMD Radeon RX 7000 / 9000.
3. Extraia o conteúdo completo do ZIP para uma pasta gravável.
4. Execute **`VideoNR.exe`**.

---

## 🎮 Requisitos e GPUs compatíveis

- **Sistema operacional:** Windows 11 x64 (drivers de GPU atualizados).
- **GPU compatível:**

| Modo NR | GPUs compatíveis | Pacote |
| :--- | :--- | :--- |
| **NVIDIA original** | RTX série 50 | `VideoNR-NVIDIA-win64.zip` |
| **SF310.9.16 (desempenho / legado)** | RTX séries 20 a 50 | `VideoNR-NVIDIA-win64.zip` |
| **AMD Small / Original** *(experimental)* | Radeon RX 7000 e RX 9000 | `VideoNR-AMD-win64.zip` |

> [!NOTE]
> Os modos AMD exigem o runtime HIP instalado e ainda estão em fase experimental neste editor.  
> O player de preview utiliza os codecs disponíveis no Windows; mesmo se um formato específico (ex: MKV/HEVC sem codec instalado) não abrir no preview, ele continua podendo ser exportado normalmente pela fila.

---

## 🚀 Como usar

1. **Adicione vídeos** clicando em **Selecionar vídeos** ou arrastando arquivos e pastas diretamente para a janela.
2. **Defina o corte** (opcional) usando as alças na linha do tempo, os botões **`[ Início`** / **`Fim ]`** ou digitando `HH:MM:SS.mmm`.
3. **Configure o NR** escolhendo a versão do motor e ajustando até 4 camadas (resolução interna, intensidade, tom e estrutura).
4. **Escolha o codec (`H.264` ou `HEVC`)** e clique em **Iniciar exportação**.

---

## ⌨️ Atalhos de teclado

| Atalho | Ação |
| :---: | :--- |
| `Espaço` ou clique no vídeo | Reproduzir / Pausar |
| `←` / `→` | Voltar / Avançar 1 segundo |
| `Shift` + `←` / `→` | Voltar / Avançar 5 segundos |
| `I` | Marcar início do corte no ponto atual |
| `O` | Marcar fim do corte no ponto atual |
| `M` | Silenciar / Ativar áudio do preview |
| `Delete` | Remover o vídeo selecionado da fila |

---

## 🛠️ Compilar e testar

### Pré-requisitos
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **MSVC x64** (`cl.exe`, recomendado via *Developer PowerShell for VS*) ou **MinGW-w64 `g++`** com suporte a C++20
- **Python 3.11+** *(apenas para montar os ZIPs portáteis completos)*

### Comandos

```powershell
# Executar testes unitários
dotnet run --project Tests/VideoNR.Tests.csproj

# Compilar o editor (build/VideoNR.exe) e o adaptador nativo (build/Engine.exe)
./Build.ps1

# Compilar self-contained e gerar os ZIPs portáteis completos em dist/
./Build.ps1 -SelfContained
python scripts/package-windows.py
```

### Estrutura do repositório

```text
├── App/                  # Interface WPF (.NET 8)
│   ├── Models/           # ClipItem, NrLayer, TrimSelection
│   ├── Services/         # EngineLocator, VideoExporter, SettingsStore
│   ├── Editor.xaml       # Layout estúdio (grafite + azul elétrico)
│   └── Editor.xaml.cs    # Controlador do player, corte e fila
├── Engine.cpp            # Adaptador nativo C++20 (MSVC) para o motor de renderização
├── Tests/                # Testes unitários (.NET 8)
├── scripts/              # Empacotamento automatizado dos ZIPs Windows
└── Build.ps1             # Script de build Windows
```

---

## 📄 Licença e Avisos

Este projeto é distribuído sob a licença **[GPL-3.0](LICENSE.txt)**.  
Consulte [DEPENDENCIAS.md](DEPENDENCIAS.md) para detalhes sobre componentes e avisos de terceiros incluídos nos pacotes portáteis.
