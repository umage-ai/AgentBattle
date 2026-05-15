// Shared page chrome — every page contains <div id="site-header-slot"></div>
// and <div id="site-footer-slot"></div>; this script injects the actual nav.
// The active nav key comes from <meta name="active-nav" content="...">.
// All hrefs are relative — pages set a <base href> for resolution.

function navItem(label, href, key, active) {
  const cls = key === active ? 'is-active' : '';
  return `<a href="${href}" class="${cls}">${label}</a>`;
}

function renderHeader(active) {
  return `
    <header class="site-header">
      <div class="site-header-inner">
        <a class="brand" href="index.html" title="AgentBattle by umage.ai">
          <img class="brand-mark" src="assets/img/umage-logo.svg" alt="umage.ai" />
          <span class="brand-divider">/</span>
          <span class="brand-title"><strong>Agent</strong>Battle</span>
        </a>
        <nav class="site-nav">
          ${navItem('Battles',          'index.html',       'battles', active)}
          ${navItem('Stats',            'stats/index.html', 'stats',   active)}
          ${navItem('Agents',           'agents.html',      'agents',  active)}
          ${navItem('Suggest a battle', 'suggest.html',     'suggest', active)}
          ${navItem('About',            'about.html',       'about',   active)}
        </nav>
      </div>
    </header>`;
}

function renderFooter() {
  return `
    <footer class="site-footer">
      An experiment by <a href="https://umage.ai" target="_blank" rel="noopener">umage.ai</a>
      &mdash; LLMs play poker so we can study how they reason under uncertainty.
    </footer>`;
}

function injectChrome() {
  const active = document.querySelector('meta[name="active-nav"]')?.content ?? '';
  const headerSlot = document.getElementById('site-header-slot');
  const footerSlot = document.getElementById('site-footer-slot');
  if (headerSlot) headerSlot.outerHTML = renderHeader(active);
  if (footerSlot) footerSlot.outerHTML = renderFooter();
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', injectChrome);
} else {
  injectChrome();
}
