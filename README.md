# AgentBattle

Record and replay battles between AI agents in turn-based games. MVP plays Texas
Hold'em 6-max between any OpenAI-compatible endpoints (cloud or local).

## Quick start

### Prerequisites

- .NET 10 SDK
- (Optional, for running real-LLM battles) [Ollama](https://ollama.com) installed locally with a model pulled, e.g. `ollama pull llama3.2:3b`

### Build

```pwsh
dotnet build
```

### Run a battle against Ollama

Make sure Ollama is running, then:

```pwsh
dotnet run --project src/AgentBattle.BattleRunner -- battle run --config configs/poker-3p-ollama.yaml --agents-dir agents
```

This plays 3 hands of 6-max Texas Hold'em with three llama3.2:3b agents and writes a JSONL battle record to `battles/`. The first hand takes ~30s; subsequent hands are faster.

### Watch the replay

```pwsh
dotnet run --project src/AgentBattle.Web
```

Open the URL the CLI prints (typically `http://localhost:5xxx`) in a browser. The home page lists recorded battles. Click "Watch" on any battle to replay it turn-by-turn with each agent's reasoning visible.

### Adding a new agent

Drop a YAML profile into `agents/`. Example (`agents/my-agent.yaml`):

```yaml
id: my-agent
display_name: My Agent
base_url: https://api.openai.com/v1
model: gpt-4o-mini
api_key_env: OPENAI_API_KEY
temperature: 0.7
max_tokens: 1500
timeout_seconds: 60
persona_prompt: |
  You play loose and aggressive — bet often, fold rarely.
```

Reference it from a battle config by `id` (see `configs/poker-3p-ollama.yaml` for the format).

### Running the test suite

```pwsh
dotnet test
```

## Layout

See `docs/superpowers/specs/2026-05-13-agentbattle-poker-mvp-design.md` section 4.1 for the planned solution layout.

## Docs

- Design spec: `docs/superpowers/specs/2026-05-13-agentbattle-poker-mvp-design.md`
- Implementation plan: `docs/superpowers/plans/2026-05-13-agentbattle-poker-mvp.md`
