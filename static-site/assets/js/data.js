// Central data access for the static AgentBattle site.
// Each fetch is cached as a Promise so concurrent callers share one request.
// All fetches use plain relative paths and depend on each page setting a
// <base href> that points to the site root (e.g. "../" for /stats/index.html).

let _battles, _agents, _stats;

export function fetchBattles() {
  if (!_battles) _battles = fetch('data/battles.json').then(r => r.json());
  return _battles;
}

export function fetchAgents() {
  if (!_agents) _agents = fetch('data/agents.json').then(r => r.json());
  return _agents;
}

export function fetchStats() {
  if (!_stats) _stats = fetch('data/stats.json').then(r => r.json());
  return _stats;
}

export async function fetchBattleEvents(filename) {
  const res = await fetch('battles/' + filename);
  if (!res.ok) throw new Error(`Failed to load ${filename}: ${res.status}`);
  const text = await res.text();
  const out = [];
  for (const line of text.split('\n')) {
    if (!line.trim()) continue;
    try { out.push(JSON.parse(line)); }
    catch { /* skip malformed lines */ }
  }
  return out;
}

export function getQueryParam(name) {
  return new URL(window.location.href).searchParams.get(name);
}

export function formatDate(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return iso;
  const pad = n => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

export function formatDay(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return iso;
  const pad = n => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

export function formatNumber(value, digits = 1) {
  return Number(value).toFixed(digits).replace(/\.0$/, '');
}

// Mirror of AgentBattle.Web.Services.ModelSlug.For, for client-side slug-by-name lookups.
export function slugify(value) {
  if (!value) return '';
  let s = '';
  let lastDash = true;
  for (const ch of value) {
    if (/[A-Za-z0-9]/.test(ch)) { s += ch.toLowerCase(); lastDash = false; }
    else if (!lastDash) { s += '-'; lastDash = true; }
  }
  if (s.endsWith('-')) s = s.slice(0, -1);
  return s;
}
