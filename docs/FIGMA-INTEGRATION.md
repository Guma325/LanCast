# Integração do redesign do Figma

Referência: [LanCast · Redesign](https://www.figma.com/design/IsiYrYOsESYhyDqiPaTujh?node-id=0-1). Implementação em 9 de outubro de 2026.

## Aplicativo Windows

- Interface WPF com tipografia Inter, paleta, ícones exportados do Figma, cards, navegação e cabeçalhos por seção.
- Menu de 200 px ou 72 px, com tooltips e preferência persistida.
- Configurações divididas em Transmissão e Aparência. Temas Escuro, Claro, Dracula, Nord, Solarized Dark e Monokai, com prévias, seleção, aplicação imediata, persistência e restauração do padrão.
- Configurações com senha protegida visualmente, detecção de alterações e confirmação antes de reiniciar uma transmissão ativa.
- Confirmações para parar, desconectar e banir; cancelar e Escape preservam o estado. Expulsar não bane; desbanir não reconecta automaticamente.
- Microfone sincronizado entre as telas, volume de 0 a 100% e medidor local enquanto a seção estiver aberta. O medidor local não inicia a transmissão.
- Preferências de áudio dos aplicativos também podem ser preparadas com a transmissão parada. Permissão e atividade são indicadas separadamente.
- Fonte fechada ou minimizada apresenta aviso de indisponibilidade, mantendo a escolha. Uma janela minimizada pede restauração antes de ser selecionada.
- Estados vazios, ajuda de conexão, explicações de Firewall/log e feedback de cópia e salvamento.

## Espectador web

Página de entrada conforme o Figma, formulário acessível de nome/senha, player responsivo, volume inicial de 80%, silêncio local, estatísticas em português, tela cheia, saída, erro de senha e reconexão. A troca de conexão fecha o peer anterior e descarta callbacks antigos. Os SVGs e a fonte são servidos pelos recursos embutidos do executável, sem dependência dos links temporários do Figma.

## Verificação executada

- Compilação Release e inspeção de renderizações WPF das seis seções, menu recolhido, temas e diálogo.
- Exercício das preferências persistidas, navegação, restauração de tema, configurações alteradas/salvas, cancelamento de diálogo e seleção de fonte sem iniciar transmissão.
- Servidor real: página atualizada, SVG e fonte embutidos, senha incorreta com HTTP 401, recurso inexistente com HTTP 404 e início/parada do serviço.
- Edge: formulário, senha incorreta, volume, silêncio, estatísticas, tela cheia/saída e viewport mobile sem transbordamento horizontal. Reconexão e estatísticas também verificadas com peer controlado para reproduzir os estados de forma determinística.

Esses checks não medem qualidade/latência de vídeo e áudio entre dois computadores, nem executam elevação de administrador para alterar o Firewall. As preferências de teste foram gravadas em uma pasta temporária separada dos dados do usuário.

O executável portátil é gerado em `dist/portable/LanCast.exe`. O FFmpeg comprimido continua fora do versionamento, conforme o fluxo de build existente. A licença da fonte acompanha a publicação em `Assets/Fonts/LICENSE.txt`.
