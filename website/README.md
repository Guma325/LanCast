# Landing page do LanCast

Site estático em HTML, CSS e JavaScript, implementado a partir da página **LanCast · Landing page** do [Figma](https://www.figma.com/design/IsiYrYOsESYhyDqiPaTujh?node-id=58-1118). Não precisa de instalação de pacotes ou etapa de build.

## Executar localmente

Na raiz do repositório:

```powershell
python -m http.server 4173 --bind 127.0.0.1 --directory website
```

Abra `http://127.0.0.1:4173`. Também é possível abrir `website/index.html` diretamente no navegador.

## Hospedar na Hostinger

O repositório já traz a configuração para a Hostinger achar o site sozinha: o `.htaccess` da **raiz** serve `website/` na raiz do domínio (o endereço não mostra `/website`), redireciona para HTTPS e deixa o resto do repositório (código do app, ferramentas, instalador) inacessível pelo navegador. O `website/.htaccess` cuida de cache, compressão e cabeçalhos de segurança.

Deploy automático pelo Git (nomes dos menus do hPanel podem variar um pouco):

1. No hPanel, abra o site e vá em **Avançado > Git**.
2. Em **Criar um novo repositório**, informe:
   - Repositório: `https://github.com/Guma325/LanCast.git`
   - Branch: `master`
   - Diretório de instalação: deixe em branco, para usar `public_html`. A pasta precisa estar vazia; apague o `default.php` que a Hostinger cria.
3. Clique em **Criar** e depois em **Implantar** (deploy). O site já abre no domínio.
4. Para atualizar sozinho a cada `push`, copie a **URL do webhook** que aparece nessa tela e cadastre no GitHub em **Settings > Webhooks > Add webhook** (Content type `application/json`, evento `push`).
5. Ative o certificado gratuito em **Segurança > SSL**. Faça isso antes de depender do redirecionamento para HTTPS.

Só vai ao ar o que estiver na branch escolhida: trabalhe numa branch própria, abra o PR e mescle na `master`; o webhook publica a atualização.

Se preferir enviar só a pasta do site (por FTP ou Gerenciador de Arquivos), copie o **conteúdo** de `website/` para `public_html`. Nesse caso o `.htaccess` da raiz não é necessário; mantenha o `website/.htaccess`, que passa a ficar na raiz do site.

Outras hospedagens estáticas também servem: mantenha `index.html`, `styles.css`, `script.js` e `assets/` juntos. Os caminhos relativos permitem servir o site em uma subpasta. A publicação não faz parte do build do aplicativo Windows.

Os botões de download levam à release mais recente do projeto no GitHub, onde estão o instalador e a versão portátil. Não há consulta à API do GitHub nem número de versão fixo no site.

## Assets e comportamento

- A fonte Inter vem dos assets locais do projeto; sua licença acompanha o site em `assets/Inter-LICENSE.txt`.
- Os ícones, a grade com partículas, as divisórias e a imagem do aplicativo foram exportados do Figma. O PNG em `assets/lancast-preview.png` é apenas a imagem do produto no hero; todo o conteúdo da landing page é HTML.
- O FAQ e o menu mobile usam `details`/`summary` e funcionam sem JavaScript. O script fecha o menu após a navegação, ao clicar fora ou pressionar Escape, e atualiza o ano no rodapé.
- A página respeita a preferência por movimento reduzido e usa navegação por teclado, link para pular ao conteúdo e texto alternativo na imagem do produto.

O player dos espectadores continua em `src/Host/wwwroot/`, servido pelo aplicativo durante a transmissão.
