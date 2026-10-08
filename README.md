# VideoStreaming

Compartilhamento de tela local com baixa latência (H.264 NVENC + WebRTC) para usar com VPN (Radmin).

## Duas formas de usar (arquivos em pastas separadas)

| Modo | Arquivo | Para quê |
|---|---|---|
| **Portátil** | `dist\portable\VideoStreaming.exe` | Abre direto, sem instalar. Pode ficar em um pendrive. |
| **Instalador** | `dist\installer\VideoStreaming-Setup-1.0.0.exe` | Instala em Arquivos de Programas, cria atalhos (Menu Iniciar / Área de Trabalho), libera o Firewall e adiciona desinstalador. |

Os dois compartilham as mesmas configurações em `%AppData%\VideoStreaming`.

## Usar
1. Abra o app e clique em **Iniciar transmissão**. Copie o link da Radmin e mande para os amigos.
2. Quem assiste abre o link no Chrome/Edge, digita um nome e clica em **Assistir**.
3. Na primeira vez (modo portátil), aceite o aviso do Firewall ou use Configurações > Liberar no Firewall.

Fechar a janela só esconde o app na bandeja; para sair use o ícone da bandeja > Sair.

## Recursos
- Contador de conectados e lista com nome/IP de cada espectador, com **Expulsar** e **Banir**.
- Aba **Compartilhar**: pré-visualização ao vivo e escolha do que transmitir: uma **tela** específica ou **só uma janela/aplicativo**.
  Dá para trocar durante a transmissão sem derrubar os espectadores. Se a janela fechar, o app espera e volta sozinho quando ela reaparecer.
  Com uma janela escolhida, a opção "Enviar só o áudio do app compartilhado" deixa os outros apps fora da transmissão.
- Aba **Banidos**: lista de IPs banidos, **Desbanir** e banimento manual por IP. Banido não consegue nem abrir a página.
- Nome escolhido pelo espectador (sem login). Senha opcional.
- Aba **Áudio dos apps**: silencia apps específicos na transmissão (você continua ouvindo).
- Aba **Microfone**: liga/desliga o seu mic na transmissão, dispositivo e volume.
- Configurações: porta, monitor, FPS, qualidade, resolução, iniciar ao abrir.

## Se o vídeo ficar preto
O app tenta sozinho, em ordem: NVIDIA (NVENC) > AMD (AMF) > Intel (Quick Sync) > software (x264), e captura por GPU > CPU > GDI,
e fica no primeiro que realmente produzir vídeo. O método em uso aparece no cabeçalho ("Sem vídeo" em amarelo enquanto procura).
Se ainda falhar, abra **Configurações > Abrir log** (arquivo `%AppData%\VideoStreaming\log.txt`, lista GPUs/drivers e erros do ffmpeg)
e envie o arquivo. Dá para forçar um codificador em Configurações > Codificador de vídeo.

## Gerar os arquivos
`publicar.bat` gera o portátil e o instalador (usa `tools\InnoSetup`, instalado localmente no projeto).

## Versionamento
- A versão fica em `src\Host\VideoStreaming.csproj` (`<Version>`); o `publicar.bat` repassa para o instalador.
- Releases são tags git `vMAJOR.MINOR.PATCH` (ver `CHANGELOG.md`).
- Os `.exe` gerados **não** vão no repositório: baixe na página **Releases** do GitHub.
- `tools\ffmpeg.exe` também não está no repositório; coloque-o nessa pasta antes de gerar os arquivos.

## Código (`src\Host`)
`ScreenEncoder` (ffmpeg ddagrab + NVENC -> RTP local), `AudioMixer`/`ProcessLoopbackCapture`/`MicCapture` (áudio por app + mic, Opus 10 ms),
`StreamHub` (WebRTC/SIPSorcery), `StreamService` (servidor web), `MainWindow` + `App.xaml` (WPF, tema escuro).

## Limitações
- Perda de pacotes só se recupera no próximo keyframe (padrão 1 s).
- Requer GPU NVIDIA (AMD/Intel: trocar `h264_nvenc` em `ScreenEncoder.BuildArgs`).
- Windows 10 2004+ / 11. Ban é por IP (na Radmin cada pessoa tem IP fixo, então funciona bem).
