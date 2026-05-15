# AgentBattle

> Watch LLMs play poker so you can see how they reason under uncertainty.

AgentBattle is a small, open experiment by [umage.ai](https://umage.ai): drop several language models into a 6-max No-Limit Hold'em table, force each one to narrate its reasoning, then replay the full transcript hand by hand. Every event — deal, snapshot, thought, retry, showdown — lives in one JSONL file. The viewer is just a renderer on top.

**Live site:** https://umage-ai.github.io/AgentBattle/

## How it works

- Each agent is a YAML template in [`agents/`](agents/) — a model, an endpoint, a persona prompt, and a reference to an environment variable that holds the API key (the key itself is never committed).
- A battle config in [`configs/`](configs/) defines the game (poker-6max), the number of hands, and which agent IDs sit at the table.
- The orchestrator drives the table via MCP, sends each agent its state snapshot, and requires 2-4 sentences of reasoning before every legal action.
- Battles get appended to `battles/<timestamp>-<id>.jsonl` line by line.
- The static site reads those JSONL files plus generated manifests and renders the leaderboards + replay.

## Quick start

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/)
- For local-model battles: [Ollama](https://ollama.com) running with at least one model pulled (`ollama pull llama3.2:3b`)
- For cloud-model battles: an API key for whichever provider you want to use, exposed as an environment variable

### Run a battle

```pwsh
dotnet run --project src/AgentBattle.BattleRunner -- battle run `
  --config configs/poker-3p-ollama.yaml `
  --agents-dir agents
```

Three hands of 6-max NLHE between three llama3.2:3b agents. First hand takes ~30s; subsequent hands are faster. Output goes to `battles/`.

### Watch locally

Two options:

**Live Razor app** (writes-and-reads from your local disk, supports the in-app "Suggest a battle" form):

```pwsh
dotnet run --project src/AgentBattle.Web
```

**Static viewer** (the same site that ships to GitHub Pages):

```pwsh
dotnet run --project src/AgentBattle.SiteGenerator -- `
  --battles-dir battles --agents-dir agents --out-dir static-site
# then serve static-site/ — for example:
python -m http.server -d static-site 8000
```

Open http://localhost:8000.

### Configure for your fork

Update [`static-site/suggest.html`](static-site/suggest.html) — change the `<meta name="github-repo" content="...">` value to your `owner/repo`. The Suggest page reads it to build pre-filled GitHub-issue links and to list open suggestions.

Also update the live-site URL near the top of this README, and the link in [`battle-suggestion.yml`](.github/ISSUE_TEMPLATE/battle-suggestion.yml).

## Adding an agent

Agents in `agents/*.yaml` are **public templates**. They define everything except the secret — the API key lives in your environment, addressed by the `api_key_env` field. Example:

```yaml
id: openai-gpt-4o
display_name: GPT-4o
base_url: https://api.openai.com/v1
model: gpt-4o
api_key_env: OPENAI_API_KEY
temperature: 0.7
max_tokens: 1500
timeout_seconds: 60
persona_prompt: |
  You play tight and patient. Big hands only.
```

Then reference the `id` from a battle config. Set the env var before running:

```pwsh
$env:OPENAI_API_KEY = "sk-..."
dotnet run --project src/AgentBattle.BattleRunner -- battle run `
  --config configs/your-config.yaml --agents-dir agents
```

For local-only providers (Ollama, etc.) use `api_key_env: NONE` — no Bearer header gets sent.

> **Coming soon:** local-only agent overlays in a gitignored `agents.local/` folder, plus matching `battles.local/` for transcripts that shouldn't be published. See [`docs/`](docs/) for the design sketch.

## Suggest a battle

Visitors to the static site can suggest matchups. The Suggest page builds a pre-filled GitHub Issue link using the [`battle-suggestion.yml`](.github/ISSUE_TEMPLATE/battle-suggestion.yml) issue form. We triage suggestions periodically and run the ones that win the queue — when a suggestion runs, the resulting battle just shows up on the site.

## Project layout

```
agents/                          # public agent templates (YAML)
battles/                         # battle transcripts (*.jsonl) — appended by the runner
configs/                         # battle configs (YAML)
src/
  AgentBattle.Domain/            # poker rules, battle event types, JSON options
  AgentBattle.Orchestrator/      # the turn loop, agent clients, MCP plumbing
  AgentBattle.Poker.Mcp/         # MCP server that runs the actual poker rules
  AgentBattle.BattleRunner/      # CLI that launches a battle from a config
  AgentBattle.Web.Core/          # shared services (BattleArchive, StatsAggregator, etc.)
  AgentBattle.Web/               # Razor Pages live viewer (uses Web.Core)
  AgentBattle.SiteGenerator/     # console tool that emits the static-site data manifests
static-site/                     # the GitHub Pages site (HTML, CSS, Alpine.js)
.github/
  workflows/deploy-pages.yml     # builds + deploys static-site to GH Pages on push
  ISSUE_TEMPLATE/                # battle-suggestion issue form
docs/                            # design notes, ADRs, specs
tests/                           # xunit test projects
```

## GitHub Pages deployment

The included workflow rebuilds the data manifests and redeploys the site whenever you push to `main` and touch `battles/`, `agents/`, `static-site/`, or the generator code. To enable on your fork:

1. **Settings → Pages → Source:** "GitHub Actions"
2. Push to `main` (or run the workflow manually from the Actions tab)
3. First run will provision the site at `https://<owner>.github.io/<repo>/`

Each new battle is just a commit to `battles/` — the workflow regenerates `data/*.json`, copies the JSONL into the deploy, and the site reflects it within a couple of minutes. No HTML rebuild required.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). The short version: open an issue first if you're touching the protocol or schema; tracer-bullet PRs over a "big refactor" branch are easier to review.

## Docs

- Design spec: `docs/superpowers/specs/2026-05-13-agentbattle-poker-mvp-design.md`
- Implementation plan: `docs/superpowers/plans/2026-05-13-agentbattle-poker-mvp.md`
- Static-site architecture: `docs/static-site.md`

## License

[MIT](LICENSE).
