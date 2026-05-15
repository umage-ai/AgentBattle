// Alpine.data() registrations for every static page in the site.
// Components fetch their data via ./data.js and stay declarative on the HTML side.
import {
  fetchBattles, fetchAgents, fetchStats,
  getQueryParam, formatDate, formatDay, formatNumber, slugify
} from './data.js';

function isWinnerFor(battle, displayName) {
  if (!battle.isComplete || !battle.winnerAgentId) return false;
  const w = battle.winnerAgentId;
  return displayName.toLowerCase() === w.toLowerCase()
      || displayName.toLowerCase().startsWith(w.toLowerCase());
}

const battleHref = b => `battle.html?id=${encodeURIComponent(b.battleId)}`;
const agentHref  = slug => `stats/agents/detail.html?slug=${encodeURIComponent(slug)}`;
const modelHref  = slug => `stats/models/detail.html?slug=${encodeURIComponent(slug)}`;

document.addEventListener('alpine:init', () => {

  Alpine.data('battlesList', () => ({
    battles: [],
    loaded: false,
    async init() {
      this.battles = await fetchBattles();
      this.loaded = true;
    },
    href: battleHref,
    agentHref(displayName) { return agentHref(slugify(displayName)); },
    isWinner: isWinnerFor,
    fmtDate: formatDate,
  }));

  Alpine.data('agentsList', () => ({
    agents: [],
    loaded: false,
    async init() {
      this.agents = await fetchAgents();
      this.loaded = true;
    },
  }));

  Alpine.data('statsHub', () => ({
    snapshot: null,
    loaded: false,
    async init() {
      this.snapshot = await fetchStats();
      this.loaded = true;
    },
    topModels() { return (this.snapshot?.models ?? []).slice(0, 10); },
    topAgents() { return (this.snapshot?.agents ?? []).slice(0, 10); },
    pct(wins, battles) { return battles ? Math.round((wins / battles) * 100) + '%' : '0%'; },
    fmtNum: formatNumber,
    agentHref,
    modelHref,
  }));

  Alpine.data('agentsLeaderboard', () => ({
    agents: [],
    loaded: false,
    async init() {
      const snap = await fetchStats();
      this.agents = snap.agents;
      this.loaded = true;
    },
    pct(wins, battles) { return battles ? Math.round((wins / battles) * 100) + '%' : '0%'; },
    fmtNum: formatNumber,
    fmtDay: formatDay,
    agentHref,
  }));

  Alpine.data('agentDetail', () => ({
    slug: '',
    isMatchup: false,
    agent: null,
    matchup: null,
    matchups: [],
    battles: [],
    loaded: false,
    notFound: false,
    async init() {
      this.slug = getQueryParam('slug') ?? '';
      const [snap, allBattles] = await Promise.all([fetchStats(), fetchBattles()]);

      const vsIdx = this.slug.lastIndexOf('-vs-');
      if (vsIdx > 0 && vsIdx + 4 < this.slug.length) {
        const leftSlug = this.slug.slice(0, vsIdx);
        const rightSlug = this.slug.slice(vsIdx + 4);
        const known = (s) => snap.agents.some(a => a.slug === s);
        if (known(leftSlug) && known(rightSlug)) {
          const [aSlug, bSlug] = [leftSlug, rightSlug].sort();
          if (aSlug !== leftSlug) {
            window.location.replace(`stats/agents/detail.html?slug=${aSlug}-vs-${bSlug}`);
            return;
          }
          const m = snap.agentMatchups.find(x => x.aSlug === aSlug && x.bSlug === bSlug);
          if (!m) { this.notFound = true; this.loaded = true; return; }
          this.isMatchup = true;
          this.matchup = m;
          this.battles = allBattles.filter(b => m.battleIds.includes(b.battleId));
          this.loaded = true;
          document.title = `${m.aDisplayName} vs ${m.bDisplayName} — agent battles · AgentBattle`;
          return;
        }
      }

      const single = snap.agents.find(a => a.slug === this.slug);
      if (!single) { this.notFound = true; this.loaded = true; return; }
      this.agent = single;
      this.matchups = snap.agentMatchups
        .filter(m => m.aSlug === this.slug || m.bSlug === this.slug)
        .sort((a, b) => b.battleCount - a.battleCount);
      this.battles = allBattles
        .filter(b => b.agentDisplayNames.some(n => slugify(n) === this.slug))
        .slice(0, 20);
      this.loaded = true;
      document.title = `${single.displayName} — agent record · AgentBattle`;
    },
    opponent(m) {
      const isA = m.aSlug === this.slug;
      return {
        slug: isA ? m.bSlug : m.aSlug,
        display: isA ? m.bDisplayName : m.aDisplayName,
        myWins: isA ? m.aWins : m.bWins,
        theirWins: isA ? m.bWins : m.aWins,
      };
    },
    matchupHref(m) { return `stats/agents/detail.html?slug=${m.aSlug}-vs-${m.bSlug}`; },
    href: battleHref,
    agentHref,
    fmtNum: formatNumber,
    fmtDate: formatDate,
    pct(wins, battles) { return battles ? Math.round((wins / battles) * 100) + '%' : '0%'; },
  }));

  Alpine.data('modelsLeaderboard', () => ({
    models: [],
    loaded: false,
    async init() {
      const snap = await fetchStats();
      this.models = snap.models;
      this.loaded = true;
    },
    pct(wins, battles) { return battles ? Math.round((wins / battles) * 100) + '%' : '0%'; },
    fmtPct(v) { return Math.round(v * 100) + '%'; },
    fmtNum: formatNumber,
    fmtDay: formatDay,
    modelHref,
  }));

  Alpine.data('modelDetail', () => ({
    slug: '',
    isMatchup: false,
    model: null,
    matchup: null,
    matchups: [],
    battles: [],
    loaded: false,
    notFound: false,
    async init() {
      this.slug = getQueryParam('slug') ?? '';
      const [snap, allBattles, agents] = await Promise.all([fetchStats(), fetchBattles(), fetchAgents()]);

      const vsIdx = this.slug.lastIndexOf('-vs-');
      if (vsIdx > 0 && vsIdx + 4 < this.slug.length) {
        const leftSlug = this.slug.slice(0, vsIdx);
        const rightSlug = this.slug.slice(vsIdx + 4);
        const known = (s) => snap.models.some(m => m.slug === s);
        if (known(leftSlug) && known(rightSlug)) {
          const [aSlug, bSlug] = [leftSlug, rightSlug].sort();
          if (aSlug !== leftSlug) {
            window.location.replace(`stats/models/detail.html?slug=${aSlug}-vs-${bSlug}`);
            return;
          }
          const m = snap.modelMatchups.find(x => x.aSlug === aSlug && x.bSlug === bSlug);
          if (!m) { this.notFound = true; this.loaded = true; return; }
          this.isMatchup = true;
          this.matchup = m;
          this.battles = allBattles.filter(b => m.battleIds.includes(b.battleId));
          this.loaded = true;
          document.title = `${m.aDisplayName} vs ${m.bDisplayName} — poker battles · AgentBattle`;
          return;
        }
      }

      const single = snap.models.find(m => m.slug === this.slug);
      if (!single) { this.notFound = true; this.loaded = true; return; }
      this.model = single;
      this.matchups = snap.modelMatchups
        .filter(m => m.aSlug === this.slug || m.bSlug === this.slug)
        .sort((a, b) => b.battleCount - a.battleCount);
      const agentsById = Object.fromEntries(agents.map(a => [a.id, a]));
      this.battles = allBattles
        .filter(b => b.seatedAgents.some(sa => {
          const a = agentsById[sa.id];
          return a && slugify(a.model) === this.slug;
        }))
        .slice(0, 20);
      this.loaded = true;
      document.title = `${single.displayName} — battle record · AgentBattle`;
    },
    opponent(m) {
      const isA = m.aSlug === this.slug;
      return {
        slug: isA ? m.bSlug : m.aSlug,
        display: isA ? m.bDisplayName : m.aDisplayName,
        myWins: isA ? m.aWins : m.bWins,
        theirWins: isA ? m.bWins : m.aWins,
      };
    },
    matchupHref(m) { return `stats/models/detail.html?slug=${m.aSlug}-vs-${m.bSlug}`; },
    href: battleHref,
    agentHref(name) { return agentHref(slugify(name)); },
    fmtNum: formatNumber,
    fmtDate: formatDate,
    pct(wins, battles) { return battles ? Math.round((wins / battles) * 100) + '%' : '0%'; },
  }));

  Alpine.data('suggest', () => ({
    repoOwner: '',
    repoName: '',
    name: '',
    game: 'poker-6max',
    note: '',
    agents: ['', ''],
    recent: [],
    loaded: false,
    error: '',
    async init() {
      const meta = document.querySelector('meta[name="github-repo"]')?.content ?? '';
      const [owner, name] = meta.split('/');
      this.repoOwner = owner ?? '';
      this.repoName = name ?? '';
      await this.loadRecent();
      this.loaded = true;
    },
    addAgent() { if (this.agents.length < 8) this.agents.push(''); },
    removeAgent(i) { if (this.agents.length > 2) this.agents.splice(i, 1); },
    async loadRecent() {
      if (!this.repoOwner || !this.repoName) return;
      const url = `https://api.github.com/repos/${this.repoOwner}/${this.repoName}/issues?labels=battle-suggestion&state=open&per_page=20`;
      try {
        const res = await fetch(url);
        if (!res.ok) return;
        this.recent = await res.json();
      } catch { /* best-effort */ }
    },
    submit() {
      const trimmed = this.agents.map(a => a.trim()).filter(Boolean);
      if (trimmed.length < 2) {
        this.error = 'Please name at least two agents.';
        return;
      }
      this.error = '';
      if (!this.repoOwner || !this.repoName) {
        this.error = 'GitHub repo not configured — set <meta name="github-repo" content="owner/name">.';
        return;
      }
      const params = new URLSearchParams({
        template: 'battle-suggestion.yml',
        labels: 'battle-suggestion',
        title: `Battle: ${trimmed.join(' vs ')}`,
      });
      params.append('game', this.game);
      params.append('agents', trimmed.join('\n'));
      if (this.name) params.append('suggested_by', this.name);
      if (this.note) params.append('note', this.note);
      const url = `https://github.com/${this.repoOwner}/${this.repoName}/issues/new?${params}`;
      window.open(url, '_blank', 'noopener');
    },
    fmtDate: formatDate,
  }));
});
