# Redesign "On Air"

Proposta de redesign do app host (WPF). Mockups: https://claude.ai/artifact/7QMYDS5rm9Mp3L6YSNXLGi

O código-fonte dos boards do canvas fica em `mockups/` (um `.dc.html` por tela + `canvas.json`), para retomar a partir daqui.

Direção gerada com a skill `ui-ux-pro-max` (`"game streaming desktop tool dark modern" --design-system`):
estilo **Dark Mode (OLED)**, tipografia **Inter**, paleta de "Gaming" (roxo neon + ação vermelha), stack `wpf`.

## Ideias principais

1. **Barra "No ar" fixa no topo** de todas as telas: ponto AO VIVO pulsando, tempo de transmissão,
   resolução/FPS/bitrate/encoder em fonte mono, avatares de quem assiste, atalho global e o botão
   Iniciar/Parar. Hoje o status é um chip pequeno e o botão Parar é um rosa pastel que parece desabilitado.
2. **Menu lateral agrupado**: Transmissão (Compartilhar, Áudio dos apps, Microfone), Público
   (Espectadores, Banidos), Assistir (Assistindo amigos, Streams salvas); Configurações e o cartão de rede no rodapé.
   Largura padrão de 264 px (rótulos em uma linha só) e ajustável arrastando a borda direita, de 216 a 360 px
   (setas do teclado ajustam de 16 em 16, duplo clique volta ao padrão; a largura escolhida fica salva).
   Mantém o botão de recolher do app atual (ao lado do logo): recolhida, vira um trilho de 72 px só com ícones,
   com um ponto verde em Streams salvas quando alguém está no ar.
3. **Compartilhar**: prévia grande em 16:9 com selos sobrepostos (AO VIVO, resolução, "só você vê");
   fonte da transmissão com seletor Telas | Janelas e cartões com miniatura; convites com IP mascarado em mono
   e o link principal (Radmin) com botão primário.
4. **Espectadores**: contadores grandes em mono por cor de estado, lista em tabela com avatar de iniciais,
   estado (Conectado / Conectando), tempo e ações Expulsar / Banir.
5. **Assistindo amigos**: layout Grade | Destaque em controle segmentado; cada tile tem selo com nome e
   quantos assistem, latência em mono e barra de controles que aparece no hover/foco (volume, estatísticas,
   destacar, tela cheia, fechar). Senha pedida dentro do próprio tile. "Adicionar transmissão" abre a lista
   das Streams salvas com status (Ativa / Offline) sem sair da tela; vaga tracejada completa a grade.
6. **Streams salvas**: lista em cartões, quem está no ar primeiro (ícone de transmissão animado no avatar,
   "No ar · N assistindo", selo de senha), filtro Todas | No ar. Ação principal muda com o estado: Assistir →
   Assistindo (leva para a aba); offline fica sem ação principal. Abrir no navegador, Renomear (inline,
   Enter/Esc) e Remover (com Desfazer) viram ícones. Formulário "Salvar uma stream" ao lado, com rótulos visíveis,
   validação do endereço junto ao campo e porta 8080 por padrão.

## Tokens (tema Escuro)

Mapeados nas chaves que `Themes/*.xaml` já usam, para que o `ThemeManager` continue funcionando:

| Chave XAML | Novo valor | Atual |
|---|---|---|
| `Bg` | `#0A0A12` | `#0B0E14` |
| `Sidebar` | `#0E0E18` | `#10141C` |
| `Surface` | `#12121C` | `#151B26` |
| `Surface2` / `Surface3` | `#1A1A28` | `#1D2635` |
| `Line` | `#1E1E2E` (controles: `#2A2A3E`) | `#2A3445` |
| `Text` | `#F2F2F8` | `#F3F6FC` |
| `Muted` | `#A0A0B8` | `#9AA9BF` |
| `Accent` | `#6D4AFF` | `#3568D4` |
| `AccentSoft` / `OnAccentSoft` | `#6D4AFF` a 16% / `#C2B5FF` | `#1B2E50` / `#A8C4FF` |
| `Danger` / `DangerSoft` / `DangerLine` / `DangerText` | `#FF4D6D` / `#2A1520` / `#5A2433` / `#FF8FA3` | |
| `Ok` / `Warn` | `#3DDC97` / `#F5CA7B` | `#69DEAE` / `#F5CA7B` |
| novo `Live` | `#FF4D6D` (selo sólido: `#E5294B` com texto branco) | — |

Contraste: texto branco sobre `#6D4AFF` ≈ 5,1:1; `#A0A0B8` sobre `#12121C` ≈ 7:1.

## Tipografia

- Interface: **Inter** (embutir como recurso, `pack://application:,,,/Assets/Fonts/#Inter`).
- Dados (tempo, IP, bitrate, contadores, atalhos): **JetBrains Mono**, para os números não "pularem".
- Escala: título 26/700, seção 15/600, corpo 14/400, rótulo 11/600 com espaçamento 0.08em.

## Forma e movimento

- Raios: card 16, controle 10, chip 6, pílula 999. Espaço em múltiplos de 4.
- Profundidade por cor de superfície; sombra/brilho só no botão primário.
- Transições de 150–220 ms ease-out. Pulso só no ponto AO VIVO; desligar quando
  `SystemParameters.ClientAreaAnimation` for falso.
- Alvos de clique ≥ 44 px; foco visível com anel de 2 px em `#A99BFF`.

## Implementação sugerida (WPF)

1. Atualizar `Themes/Dark.xaml` com os tokens acima (e derivar os outros cinco temas).
2. `App.xaml`: raios 10/16, novo estilo `LiveChip`, `kbd`, segmented control, switch maior.
3. `MainWindow.xaml`: extrair a barra "No ar" para o topo do conteúdo; agrupar o menu.
4. Embutir Inter e JetBrains Mono (ambas OFL).
