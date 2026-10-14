const menu = document.querySelector('.mobile-menu');

// The navigation and FAQ use native disclosures, so they also work without JavaScript.
menu.addEventListener('click', (event) => {
  if (event.target.closest('a')) menu.open = false;
});

document.addEventListener('click', (event) => {
  if (menu.open && !menu.contains(event.target)) menu.open = false;
});

document.addEventListener('keydown', (event) => {
  if (event.key === 'Escape' && menu.open) {
    menu.open = false;
    menu.querySelector('summary').focus();
  }
});

const desktop = window.matchMedia('(min-width: 768px)');
desktop.addEventListener('change', (event) => {
  if (event.matches) menu.open = false;
});

document.querySelector('[data-year]').textContent = new Date().getFullYear();

(() => {
// ---------- Janela "Baixar para Windows": instalador ou portátil, sempre a versão mais recente ----------
const REPO = 'Guma325/LanCast';
const RELEASES_LATEST = `https://github.com/${REPO}/releases/latest`;
let latestPromise = null;

// Busca a release mais recente (uma vez); em falha devolve null e tenta de novo na próxima vez.
function loadLatest() {
  latestPromise ??= fetch(`https://api.github.com/repos/${REPO}/releases/latest`, { headers: { Accept: 'application/vnd.github+json' } })
    .then((response) => { if (!response.ok) throw new Error(String(response.status)); return response.json(); })
    .then((release) => {
      const find = (pattern) => (release.assets || []).find((a) => pattern.test(a.name) && String(a.browser_download_url).startsWith('https://github.com/'));
      return { version: release.tag_name, setup: find(/setup.*\.exe$/i), portable: find(/portable.*\.exe$/i) };
    })
    .catch(() => { latestPromise = null; return null; });
  return latestPromise;
}

const dialog = document.createElement('dialog');
dialog.className = 'download-dialog';
dialog.setAttribute('aria-labelledby', 'download-dialog-title');
dialog.innerHTML = `
  <div class="download-card">
    <button class="download-close" type="button" aria-label="Fechar">×</button>
    <p class="eyebrow">LanCast para Windows</p>
    <h2 id="download-dialog-title">Como você quer usar?</h2>
    <p class="download-lead muted">Os dois baixam a versão mais recente.</p>
    <div class="download-options">
      <a class="download-option" data-kind="setup" rel="noopener">
        <strong>Instalador</strong>
        <span>Instala no Windows, cria atalhos e atualiza pelo próprio app.</span>
        <small class="download-meta muted">Buscando a versão mais recente…</small>
      </a>
      <a class="download-option" data-kind="portable" rel="noopener">
        <strong>Portátil</strong>
        <span>Abre direto, sem instalar. Pode ficar em um pendrive.</span>
        <small class="download-meta muted">Buscando a versão mais recente…</small>
      </a>
    </div>
    <a class="download-all" href="versoes.html">Ver todas as versões</a>
  </div>`;
document.body.append(dialog);

const options = {
  setup: dialog.querySelector('[data-kind="setup"]'),
  portable: dialog.querySelector('[data-kind="portable"]'),
};

function fillOption(kind, latest) {
  const link = options[kind];
  const asset = latest?.[kind];
  const meta = link.querySelector('.download-meta');
  link.href = asset ? asset.browser_download_url : RELEASES_LATEST;     // sem a API, abre a página da versão mais recente
  meta.textContent = asset ? `${latest.version} · ${Math.round(asset.size / 1048576)} MB` : 'Abrir a página de downloads';
}

function openDownloads() {
  dialog.showModal();
  for (const kind of Object.keys(options)) {
    options[kind].href = RELEASES_LATEST;
    options[kind].querySelector('.download-meta').textContent = 'Buscando a versão mais recente…';
  }
  loadLatest().then((latest) => { for (const kind of Object.keys(options)) fillOption(kind, latest); });
}

if (typeof dialog.showModal === 'function') {
  for (const trigger of document.querySelectorAll('[data-download]')) {
    trigger.addEventListener('click', (event) => { event.preventDefault(); openDownloads(); });
    trigger.addEventListener('pointerenter', loadLatest, { once: true });
    trigger.addEventListener('focus', loadLatest, { once: true });
  }
  dialog.querySelector('.download-close').addEventListener('click', () => dialog.close());
  dialog.addEventListener('click', (event) => { if (event.target === dialog) dialog.close(); });       // clique no fundo escuro
  for (const link of Object.values(options)) link.addEventListener('click', () => setTimeout(() => dialog.close(), 300));
}
})();
