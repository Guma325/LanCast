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
