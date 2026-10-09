# Revisão de UX — LanCast

Data: 9 de outubro de 2026.

As correções foram aplicadas no arquivo [LanCast · Redesign no Figma](https://www.figma.com/design/IsiYrYOsESYhyDqiPaTujh?node-id=0-1), preservando as 13 telas originais e acrescentando estados e diálogos necessários aos fluxos. A página contém 30 frames, incluindo a biblioteca de componentes.

## Inconsistências corrigidas

| Área | Problema encontrado | Comportamento aplicado |
| --- | --- | --- |
| Transmissão | Navegação e telas independentes podiam apresentar estados diferentes. Não havia interações conectadas. | Variáveis compartilhadas mantêm transmissão, fonte, microfone e preferências ao navegar. Abrir uma aba ou escolher uma fonte não inicia o envio. |
| Microfone | Controle desligado nas telas de compartilhamento e ligado na tela de microfone. | Estado inicial desligado e controles sincronizados; rótulos distinguem preparação e transmissão. Medidor identificado como local e barra de 100% preenchida corretamente. |
| Áudio dos apps | Steam mostrava “Sem áudio” junto de “Na transmissão”. Permissão e atividade eram confundidas. | Rótulos “Permitido” e “Silenciado” indicam a escolha do usuário. Restrição de áudio disponível apenas para janela e independente do microfone. |
| Fonte indisponível | “SEM VÍDEO” coexistia com indicador verde “Transmitindo”; fonte ainda selecionada e ícone de monitor para janela. | Estado âmbar “Sem imagem”, prévia indisponível, variante `Unavailable`, ícone correto e aviso sobre o áudio que continua. Recuperação abre o seletor de fontes. |
| Troca de fonte | Faltavam caminhos para selecionar fontes em estados parado e ao vivo. | Tela 1, Tela 2 e Spotify possuem destinos coerentes com o estado atual. Discord minimizado exibe orientação para restaurar a janela. Cancelar preserva a fonte anterior. |
| Moderação | Parar, expulsar e banir não tinham confirmação nem consequências verificáveis. | Diálogos com nome/IP quando necessário, confirmação, cancelamento e Escape. Expulsão não cria banimento; banimentos são independentes; desbanir não reconecta a pessoa. |
| Listas | Ausência de tratamento completo para remoção do último espectador e crescimento da lista de banidos. | Contagem sincronizada, estado vazio, ajuda contextual e áreas com rolagem para listas maiores. |
| Configurações | Consequências de aplicar durante a transmissão e estado de salvamento pouco claros. | Confirmação durante o envio, feedback de aplicação e botão de configurações salvas sem ação até haver alteração. Textos esclarecem bitrate por espectador, início automático e indicador local. |
| Ajuda e feedback | Tela vazia sem rodapé; cópia com estilos diferentes; ações de sistema sem explicação. | Ajuda contextual, botões de cópia consistentes, feedback e orientações de conexão, Firewall e log sem alegar execução real de operações do sistema. |
| Espectador | Estatísticas em inglês, ausência de tela cheia, reconexão e erro de senha. | “Estatísticas”, tela cheia com saída/Escape, conexão perdida com tentativa novamente e estado de senha incorreta com caminho de correção. Volume de 80% corresponde à barra; silenciar afeta apenas o player. |
| Componentes | Estados, ícones e dimensões dos controles variavam entre telas. | Componentes e propriedades reutilizados, variantes da fonte e áreas de clique de 48 × 44 px nos interruptores. Tipografia Inter preservada. |

## Pontos de entrada do protótipo

| Fluxo | Tela |
| --- | --- |
| Anfitrião · iniciar | [01 · Compartilhar / pronto](https://www.figma.com/design/IsiYrYOsESYhyDqiPaTujh?node-id=4-56) |
| Anfitrião · ao vivo | [02 · Compartilhar / ao vivo](https://www.figma.com/design/IsiYrYOsESYhyDqiPaTujh?node-id=4-57) |
| Espectador · entrar | [08 · Espectador / entrar](https://www.figma.com/design/IsiYrYOsESYhyDqiPaTujh?node-id=4-63) |
| Anfitrião · recuperar fonte | [12 · Fonte indisponível / recuperação](https://www.figma.com/design/IsiYrYOsESYhyDqiPaTujh?node-id=16-496) |
| Espectador · reconectar | [27 · Espectador / conexão perdida](https://www.figma.com/design/IsiYrYOsESYhyDqiPaTujh?node-id=33-937) |
| Espectador · corrigir senha | [28 · Espectador / senha incorreta](https://www.figma.com/design/IsiYrYOsESYhyDqiPaTujh?node-id=33-989) |

## Validação

Foram verificados 42 cenários de lógica, todos aprovados, por simulação das reações efetivamente gravadas no Figma e das variáveis relacionadas. Os cenários abrangem navegação parado/ao vivo, microfone, seleção e recuperação de fonte, confirmações, moderação, estado vazio, salvamento, prévia local, restrição de áudio, volume, tela cheia, reconexão e ajuda.

A inspeção estrutural final encontrou 228 reações de protótipo, 139 vínculos de estado e seis pontos de entrada. Não foram detectados transbordamentos na verificação geométrica executada. As telas foram inspecionadas por capturas; os últimos ajustes de componentes, ícones, cabeçalho de desconexão e recorte da tela cheia foram conferidos novamente. As interfaces continuam editáveis, com texto, vetores e componentes; não foram substituídas por imagens achatadas.

Esta validação cobre o design e a lógica do protótipo, sem equivaler a testes de uso com participantes ou execução do aplicativo. Formulários, cópia, conexão, captura, autenticação e ações do Windows precisam ser conectados aos serviços reais na implementação. Nenhum arquivo de código do aplicativo foi alterado por esta revisão de Figma; alterações de código já presentes no workspace foram preservadas.
