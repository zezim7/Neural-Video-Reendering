# Vídeo NR Editor

Editor de vídeos para Windows com corte, fila de exportação e melhoria neural em GPUs compatíveis com Vulkan, incluindo NVIDIA e AMD.

## Baixar e usar

Extraia todo o conteúdo de `VideoNR-win64.zip` e abra `VideoNR.exe`. O pacote traz o runtime do .NET e as ferramentas de processamento. [Releases](../../releases)

1. Adicione um ou mais vídeos à fila e selecione o trecho de cada um.
2. Escolha **Vídeo e animação** ou **Imagem real**. Ajuste a resolução interna e a intensidade de cada camada. Para exportar sem melhoria neural, desative **Melhoria NR**.
3. Selecione H.264 ou HEVC e clique em **Iniciar exportação**.

O motor neural trabalha com vídeo SDR, usa espaço temporário para armazenar quadros e converte clipes com taxa variável para taxa constante. Para vídeo HDR, a melhoria neural fica indisponível. A exportação atual usa vídeo de 8 bits. As configurações dos filtros são aplicadas na exportação.

## Compilar

Com o SDK .NET 8 para Windows, execute `./Build.ps1 -SelfContained`. Com Python 3.11 ou superior, execute `python scripts/package-windows.py` para montar o ZIP em `dist/`.

[Dependências e avisos](DEPENDENCIAS.md) · [GPL-3.0](LICENSE.txt)
