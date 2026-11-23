# Dependências e avisos de terceiros

O editor e sua interface são implementados em `App/`. O adaptador em `Engine.cpp` envia trabalhos ao exportador do Veyra-NRVideo 2.2.0 pelo protocolo v15. Ele usa o executável, os shaders e os modelos de uma instalação local compatível. O pacote do motor e os modelos proprietários não são redistribuídos com o editor.

- [Código-fonte do motor, versão 2.2.0](https://github.com/Likely7/Veyra-NRVideo/tree/v2.2.0)
- [Pacotes do motor](https://github.com/Likely7/Veyra-NRVideo/releases/tag/v2.2.0)
- [Avisos de terceiros e componentes do motor](https://github.com/Likely7/Veyra-NRVideo/blob/v2.2.0/THIRD_PARTY_NOTICES.md)

Os arquivos em `upstream/` foram copiados do Veyra-NRVideo, commit `671f9e963db63cbd7f912caa35c4caa3e0e60f76`, sob GPL-3.0. As referências de origem e os avisos incluídos no código foram preservados.

| Versão de NR | GPU | Pacote |
| --- | --- | --- |
| NVIDIA original | RTX 50 | NVIDIA |
| SF310.9.16, desempenho ou legado | RTX 20 a 50 | NVIDIA |
| AMD Small ou Original, experimental | RX 7000 e RX 9000 | AMD |

O motor verifica o hardware e o driver. As três opções NVIDIA foram testadas em uma RTX 5060 Ti. Os modos AMD exigem o runtime HIP e ainda não foram testados em uma Radeon neste editor.
