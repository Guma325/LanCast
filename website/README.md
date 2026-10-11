# Landing page do LanCast

Site estático em HTML, CSS e JavaScript, implementado a partir da página **LanCast · Landing page** do [Figma](https://www.figma.com/design/IsiYrYOsESYhyDqiPaTujh?node-id=58-1118). Não precisa de instalação de pacotes ou etapa de build.

## Executar localmente

Na raiz do repositório:

```powershell
python -m http.server 4173 --bind 127.0.0.1 --directory website
```

Abra `http://127.0.0.1:4173`. Também é possível abrir `website/index.html` diretamente no navegador.

## Hospedar

Publique o conteúdo desta pasta em qualquer hospedagem de arquivos estáticos. Mantenha `index.html`, `styles.css`, `script.js` e `assets/` juntos. Os caminhos relativos permitem servir o site em uma subpasta, inclusive no GitHub Pages. A publicação não faz parte do build do aplicativo Windows.

Os botões de download levam à release mais recente do projeto no GitHub, onde estão o instalador e a versão portátil. Não há consulta à API do GitHub nem número de versão fixo no site.

## Assets e comportamento

- A fonte Inter vem dos assets locais do projeto; sua licença acompanha o site em `assets/Inter-LICENSE.txt`.
- Os ícones, a grade com partículas, as divisórias e a imagem do aplicativo foram exportados do Figma. O PNG em `assets/lancast-preview.png` é apenas a imagem do produto no hero; todo o conteúdo da landing page é HTML.
- O FAQ e o menu mobile usam `details`/`summary` e funcionam sem JavaScript. O script fecha o menu após a navegação, ao clicar fora ou pressionar Escape, e atualiza o ano no rodapé.
- A página respeita a preferência por movimento reduzido e usa navegação por teclado, link para pular ao conteúdo e texto alternativo na imagem do produto.

O player dos espectadores continua em `src/Host/wwwroot/`, servido pelo aplicativo durante a transmissão.
