// Lista as versões do LanCast a partir das Releases públicas do GitHub, sempre atualizada.
// Tudo é montado com textContent (nunca innerHTML) porque os dados vêm de fora do site.
const REPO = 'Guma325/LanCast';
const list = document.getElementById('releases');

const el = (tag, className, text) => {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
};

const safeUrl = (url) => (typeof url === 'string' && url.startsWith('https://github.com/')) ? url : `https://github.com/${REPO}/releases`;

const formatDate = (iso) => new Date(iso).toLocaleDateString('pt-BR', { day: 'numeric', month: 'long', year: 'numeric' });
const formatSize = (bytes) => `${Math.round(bytes / 1048576)} MB`;

// Trechos **negrito**, *itálico* e `código` das notas, sem HTML
function inline(parent, text) {
  for (const part of text.split(/(\*\*[^*]+\*\*|\*[^*]+\*|`[^`]+`)/)) {
    if (!part) continue;
    if (part.startsWith('**')) parent.append(el('strong', '', part.slice(2, -2)));
    else if (part.startsWith('*')) parent.append(el('em', '', part.slice(1, -1)));
    else if (part.startsWith('`')) parent.append(el('code', '', part.slice(1, -1)));
    else parent.append(part);
  }
}

// Notas da release: itens "- ..." viram lista; o resto vira parágrafo.
// Os itens que só descrevem os arquivos (**LanCast-Setup...**) são omitidos: os botões de download já mostram isso.
function notes(body) {
  const box = el('div', 'release-notes');
  let ul = null;
  for (const raw of (body || '').split('\n')) {
    const line = raw.trim();
    if (!line) { ul = null; continue; }
    if (/^[-*] \*\*(LanCast|VideoStreaming)-/.test(line)) continue;
    if (/^[-*] /.test(line)) {
      if (!ul) { ul = el('ul'); box.append(ul); }
      const li = el('li'); inline(li, line.slice(2)); ul.append(li);
    } else {
      ul = null;
      const p = el('p'); inline(p, line); box.append(p);
    }
  }
  return box.childNodes.length ? box : null;
}

function downloads(release, featured) {
  const row = el('div', 'release-downloads');
  const assets = (release.assets || []).filter((a) => /\.exe$/i.test(a.name));
  assets.sort((a, b) => /setup/i.test(b.name) - /setup/i.test(a.name));     // instalador primeiro
  for (const asset of assets) {
    const kind = /setup/i.test(asset.name) ? 'Instalador' : /portable/i.test(asset.name) ? 'Portátil' : asset.name;
    const link = el('a', `button ${featured && /setup/i.test(asset.name) ? 'button-primary' : 'button-secondary'}`);
    link.href = safeUrl(asset.browser_download_url);
    link.append(`${kind} `, el('span', 'release-size', `· ${formatSize(asset.size)}`));
    link.title = asset.name;
    row.append(link);
  }
  const page = el('a', 'release-github', 'Notas completas no GitHub ↗');
  page.href = safeUrl(release.html_url);
  row.append(page);
  return row;
}

function render(releases) {
  const visible = releases.filter((r) => !r.draft);
  const latest = visible.find((r) => !r.prerelease);
  list.replaceChildren();
  if (!visible.length) { list.append(el('p', 'versions-status muted', 'Nenhuma versão publicada ainda.')); return; }

  for (const release of visible) {
    const isLatest = release === latest;
    const card = el('article', `release${isLatest ? ' release-latest' : ''}`);
    const head = el('header', 'release-head');
    head.append(el('h2', '', release.name || release.tag_name));
    if (isLatest) head.append(el('span', 'release-badge', 'Mais recente'));
    if (release.prerelease) head.append(el('span', 'release-badge release-badge-pre', 'Pré-lançamento'));
    if (release.published_at) {
      const time = el('time', 'release-date muted', formatDate(release.published_at));
      time.dateTime = release.published_at;
      head.append(time);
    }
    card.append(head);
    const body = notes(release.body);
    if (body) card.append(body);
    card.append(downloads(release, isLatest));
    list.append(card);
  }
}

function fail() {
  list.replaceChildren();
  const msg = el('p', 'versions-status', 'Não foi possível carregar a lista agora. Veja todas as versões ');
  const link = el('a', 'inline-link', 'no GitHub');
  link.href = `https://github.com/${REPO}/releases`;
  msg.append(link, '.');
  list.append(msg);
}

fetch(`https://api.github.com/repos/${REPO}/releases?per_page=100`, { headers: { Accept: 'application/vnd.github+json' } })
  .then((response) => { if (!response.ok) throw new Error(String(response.status)); return response.json(); })
  .then(render)
  .catch(fail)
  .finally(() => list.setAttribute('aria-busy', 'false'));
