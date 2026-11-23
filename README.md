# Vídeo NR Editor

Editor de vídeos gravados para Windows. Permite selecionar um trecho, aplicar até quatro camadas de renderização neural e salvar um MP4.

## Uso

1. Adicione um ou mais vídeos à fila.
2. Selecione um vídeo e arraste as alças turquesa ou digite os tempos de início e fim em HH:MM:SS.mmm. Cada vídeo guarda seu próprio corte.
3. Escolha a versão do NR e ajuste a intensidade, a resolução interna, o tom e a estrutura de cada camada.
4. Selecione H.264 ou HEVC e clique em **Iniciar exportação**. A fila usa as mesmas configurações de NR e codec e respeita o corte de cada item.

O player usa os codecs disponíveis no Windows. A exportação mantém a resolução e a taxa de quadros da origem e solicita as faixas de áudio compatíveis com MP4.

## Requisitos

Windows x64, .NET Desktop Runtime 8 e um pacote de processamento 2.2.0 compatível com a GPU. Se o aplicativo não localizar o pacote, clique em **Localizar motor**. A origem do motor e os requisitos por GPU estão em [Dependências](DEPENDENCIAS.md).

## Compilar

Use o SDK .NET 8 e um compilador g++ MinGW-w64 x64 com C++20. Execute `./Build.ps1 -Compiler caminho/para/g++.exe`. O executável fica em `build/VideoNR.exe`.

O código é distribuído sob [GPL-3.0](LICENSE.txt).
