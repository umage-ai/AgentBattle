# AgentBattle — Poker MVP Design

**Status:** Approved 2026-05-13
**Scope:** First vertical slice of the AgentBattle project. One game (Texas Hold'em 6-max), one match format, one replay viewer. Subsequent slices (more games, live spectating, leaderboards, agent registry editing) get their own specs.

## 1. Vision and motivation

AgentBattle is a website that shows recorded battles between AI agents in turn-based games, with each agent's per-turn reasoning visible to viewers. The goal is to surface how different LLMs (cloud and local) compare on games involving psychology, hidden information, and strategic reasoning — not just raw IQ benchmarks.

This MVP slice delivers the full pipeline end-to-end for a single game so that adding subsequent games is a content problem rather than an architecture problem.

## 2. Product surface (MVP)

A user visiting the site can:

- See a list of recorded poker battles with date, participating agents, winner, and final chip deltas.
- Click into a battle and watch it replay turn-by-turn. The poker table is rendered visually. A scrub bar lets the user step through events or play continuously at 1x / 2x / 4x speed.
- Read each agent's reasoning prose for the currently-displayed turn in a side panel.
- Toggle between "god view" (see all hole cards from the start) and "spectator view" (see hole cards only at showdown).
- Browse the registered agent profiles (read-only).

A developer can:

- Define an agent profile in a YAML file and reference it from a battle config.
- Run `battle run --config battle.yaml` to play out a 50-hand 6-max match between up to six agents and emit a JSONL battle record.

## 3. Locked decisions

| Decision | Value | Notes |
|---|---|---|
| Game | Texas Hold'em, 6-max | Hidden information, betting psychology |
| Match format | 50 hands, 1000-chip starting stacks, 10/20 blinds, no escalation | Fixed length means both agents always play through |
| Model endpoint | Any OpenAI-compatible chat completions API | OpenAI, OpenRouter, Ollama, LM Studio, vLLM, etc. Single HTTP client, configurable base URL + model |
| Backend | .NET / C# | |
| Frontend | Razor Pages + HTMX + Alpine.js | No npm build, no SPA framework |
| Game engine interface | MCP (Model Context Protocol) | Game engines run as MCP servers; orchestrator is the MCP host |
| Watch mode | Replay-only | Battles run out-of-band, JSONL persisted, web app reads files |
| Thoughts capture | Free-text prose alongside tool call in same agent reply | No separate `record_thoughts` round-trip |
| Illegal action policy | Re-prompt with error, max 3 retries, then auto-fold | All retries logged |
| Hidden info in replay | God-view by default, toggle to hide | Client-side toggle, JSONL always contains full reveal events |
| Storage | JSONL files on disk, one per battle | No database for MVP |
| Agent identity | YAML profile per agent in `agents/*.yaml` | First-class, reusable across battles |

## 4. Architecture

### 4.1 Solution layout

A single .NET solution `AgentBattle.sln` with these projects:

| Project | Type | Purpose |
|---|---|---|
| `AgentBattle.Domain` | classlib | Shared records: `Card`, `Action`, `BattleEvent`, agent profile records, battle config records. Referenced by everything else. No behavior, just types. |
| `AgentBattle.Poker.Mcp` | console (MCP server) | Texas Hold'em engine. Exposed via MCP stdio transport. Stateless protocol, state held in-process per server instance. Spawned fresh by the orchestrator for each battle. |
| `AgentBattle.Orchestrator` | classlib | Drives a battle: spawns one poker MCP server, opens one OpenAI-compatible chat session per agent, runs the turn loop, validates and forwards tool calls, writes JSONL events. |
| `AgentBattle.BattleRunner` | console | CLI entry-point. `battle run --config battle.yaml`. Loads agent profiles, invokes orchestrator, prints summary. |
| `AgentBattle.Web` | ASP.NET Core Razor Pages | Battle list, replay viewer, read-only agent registry. Reads JSONL files from a configured battles directory. |

Battles run **out-of-band via CLI**. The web app is purely a viewer — it never starts a battle. This keeps the web app stateless and trivially deployable, and decouples battle runtime (which can take 30+ minutes wall-clock) from request handling.

### 4.2 Data and control flow

```
┌───────────────────── BattleRunner (CLI) ─────────────────────┐
│                                                              │
│   Load battle.yaml + referenced agents/*.yaml                │
│                  │                                           │
│                  ▼                                           │
│   ┌───────────── Orchestrator ──────────────┐                │
│   │                                         │                │
│   │   Spawn Poker.Mcp (stdio)               │                │
│   │   Open chat sessions for each agent     │                │
│   │   Loop hands × turns:                   │                │
│   │     get_my_state(seat) → MCP            │                │
│   │     POST chat/completions → agent       │                │
│   │     parse thoughts + tool_call          │                │
│   │     forward action → MCP                │                │
│   │     on reject: re-prompt (max 3)        │                │
│   │     append events → JSONL               │                │
│   │                                         │                │
│   └─────────────────────────────────────────┘                │
│                  │                                           │
│                  ▼                                           │
│   battles/2026-05-13T1830-{id}.jsonl                         │
└──────────────────────────────────────────────────────────────┘

                         (offline)

┌──────────────────────── Web (ASP.NET Razor) ─────────────────┐
│                                                              │
│   GET /              → list battles (scan jsonl directory)   │
│   GET /battles/{id}  → render replay shell, ship full JSONL  │
│                       to client; Alpine drives playback      │
│   GET /agents        → list agent profiles                   │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

### 4.3 MCP integration model

The poker engine is a real MCP server. The orchestrator is the sole MCP host. **Agents never see MCP directly** — they use plain OpenAI function calling against any OpenAI-compatible chat completions endpoint. The orchestrator bridges the two protocols.

Why: model endpoints in the wild (OpenAI, OpenRouter, Ollama, etc.) speak OpenAI function calling, not MCP. Keeping the agent integration narrow at "OpenAI-compatible" maximises the pool of usable models. Keeping the game engine as a real MCP server keeps it swappable: tomorrow we add `AgentBattle.Chess.Mcp` and the orchestrator code barely changes — it loads a different MCP server and presents that server's tools to the agents.

**Tools exposed by `AgentBattle.Poker.Mcp`:**

- `get_my_state(seat: int)` → returns scoped state for that seat: hole cards (only for `seat`), community cards, pot, side pots, each seat's stack, each seat's current bet on this street, the action log for the current hand (visible to all), whose turn it is, legal actions for `seat` if it's their turn.
- `fold(seat: int)`
- `check(seat: int)` — only legal when no outstanding bet to call
- `call(seat: int)` — implicit amount = current bet to match, capped at stack (auto-allin if short)
- `raise(seat: int, amount: int)` — `amount` is the new total bet level (not the increment). Must be ≥ current bet + min-raise.
- `all_in(seat: int)` — convenience; equivalent to raising to seat's full stack.

All mutating tools return `{ ok: true, applied_action: {...} }` on success or `{ ok: false, error: "below_min_raise", legal_actions: {...} }` on rejection.

The orchestrator translates each of these MCP tools into an OpenAI function definition the agent sees (minus the `seat` argument, which the orchestrator fills in based on whose turn it is). The agent calls e.g. `raise(amount=60)` — the orchestrator forwards `raise(seat=3, amount=60)` to MCP.

### 4.4 Turn loop (per hand)

Pseudocode:

```
for hand in 1..50:
    mcp.start_hand()
    log: hand_started, hole_cards_dealt
    for street in [preflop, flop, turn, river]:
        if street != preflop: mcp.deal_street(); log community_dealt
        while not betting_round_complete():
            seat = mcp.current_seat()
            state = mcp.get_my_state(seat)
            log: agent_turn_started
            attempt = 0
            while attempt < 3:
                resp = agents[seat].chat(format_prompt(state, error_from_last_attempt))
                log: agent_thoughts (prose portion)
                action = parse_tool_call(resp)
                result = mcp.apply(action, seat=seat)
                log: agent_action (with attempt number)
                if result.ok: break
                log: agent_action_rejected
                attempt += 1
                error_from_last_attempt = result.error
            if attempt == 3:
                # Forced default: check if legal, otherwise fold.
                # Checking preserves the agent's stack when it costs nothing to stay in.
                if "check" in state.legal_actions:
                    mcp.check(seat=seat); forced = "check"
                else:
                    mcp.fold(seat=seat); forced = "fold"
                log: agent_action (action=forced, attempt=4, auto_reason=retries_exhausted)
        if betting_collapsed_to_one_player: break
    showdown_result = mcp.showdown()
    log: showdown, hand_ended
log: battle_ended
```

### 4.5 Per-agent prompting

Each agent has a persistent chat session for the whole battle. The orchestrator maintains the message history per agent.

**System message (set once at session start):**

```
You are {agent.display_name}, playing 6-max No-Limit Texas Hold'em.
Starting stacks: 1000. Blinds: 10/20. 50 hands total. You are seat {seat_id}.
On each turn you will receive your scoped game state. Respond with your reasoning
in natural prose, then call exactly one action tool: fold, check, call, raise, or all_in.

{agent.persona_prompt}
```

**Per-turn user message:**

```
Hand {hand_no}, {street}. Your turn.

Your hole cards: {cards}
Community: {community or "(none yet)"}
Your stack: {stack}
Pot: {pot}
To call: {to_call}
Min raise: {min_raise}
Action so far this hand: {action_log}
Other stacks: {summary}

Legal actions: {list}

Reply with your reasoning then call one action tool.
```

The agent's text response is captured verbatim as the `agent_thoughts` event. The tool call is captured as the `agent_action` event.

## 5. Battle event schema (JSONL)

One file per battle: `battles/{iso_timestamp}-{battle_id}.jsonl`. Each line is a self-contained JSON object with at minimum `t` (event type) and `ts` (ISO 8601 timestamp).

Event types:

| Event | Fields | Notes |
|---|---|---|
| `battle_started` | `battle_id`, `config`, `agents[]` (seat, id, display_name) | Always first event |
| `hand_started` | `hand_no`, `button_seat`, `sb_seat`, `bb_seat`, `inactive_seats[]` | `inactive_seats` lists seats sitting out this hand (out of chips) |
| `hole_cards_dealt` | `hand_no`, `deals[]` (seat, cards[2]) | Full reveal in the log; UI hides per spectator-mode toggle |
| `community_dealt` | `hand_no`, `street` ("flop"\|"turn"\|"river"), `cards[]` | |
| `agent_turn_started` | `hand_no`, `seat`, `state_snapshot` | `state_snapshot` is exactly the object returned by `get_my_state(seat)` (section 4.3) so the UI can show what the agent saw. |
| `agent_thoughts` | `hand_no`, `seat`, `text`, `tokens`, `attempt` | Prose portion of agent reply |
| `agent_action` | `hand_no`, `seat`, `action`, `amount?`, `attempt`, `auto_reason?` | Applied action. `auto_reason` is set when the orchestrator forced the action (e.g. `"retries_exhausted"`, `"endpoint_timeout"`). |
| `agent_action_rejected` | `hand_no`, `seat`, `action`, `amount?`, `reason`, `attempt` | Logged then agent re-prompted |
| `showdown` | `hand_no`, `reveals[]` (seat, cards), `winners[]` (seat, pot, hand_description) | Side pots produce multiple winner entries |
| `hand_ended` | `hand_no`, `stacks` (map of seat → chips) | |
| `battle_ended` | `final_stacks`, `ranking[]` (seat, chips, agent_id) | Always last event |

All amounts are integer chips. Cards use standard 2-character notation: `As`, `Td`, `7h`, `2c`.

The JSONL is the **single source of truth** for replay. The web viewer reads the file once on page load and drives playback purely client-side.

## 6. Agent profile and battle config

**Agent profile** (`agents/{id}.yaml`):

```yaml
id: gpt-5
display_name: GPT-5
base_url: https://api.openai.com/v1
model: gpt-5
api_key_env: OPENAI_API_KEY
temperature: 0.7
max_tokens: 1500
timeout_seconds: 60
persona_prompt: |
  Play tight-aggressive. Don't be afraid to fold marginal hands.
```

`api_key_env` is the name of an environment variable to read at runtime. The key itself is never stored in the profile.

**Battle config** (`configs/battle-poker-001.yaml`):

```yaml
game: poker-6max
hands: 50
starting_stack: 1000
blinds: { small: 10, big: 20 }
seats:
  - { seat: 0, agent: gpt-5 }
  - { seat: 1, agent: claude-opus-4-7 }
  - { seat: 2, agent: gemini-2-5 }
  - { seat: 3, agent: ollama-qwen3-30b }
  - { seat: 4, agent: ollama-gemma3-27b }
  - { seat: 5, agent: openrouter-deepseek }
```

Fewer than 6 seats is allowed (3–6); the engine handles seat count generically.

## 7. Web frontend

**Pages:**

- `/` — Battle list. Table: date, agents (avatars or names), winner, chip deltas, hand count. Click row → replay.
- `/battles/{id}` — Replay viewer. Layout:
  - Top: poker table SVG with up to 6 seats around an oval. Each seat shows agent name, stack, current bet, and hole cards (or backs, per spectator-mode toggle). Center: community cards and pot.
  - Bottom: scrub bar. Two granularities: per-hand markers (large) and per-event ticks (small). Play/pause, speed selector (1x/2x/4x), step-forward, step-back.
  - Side panel: collapsible per-agent panels showing the prose from the most-recent `agent_thoughts` event for that agent.
  - Header: spectator-mode toggle (god / play-as-spectator).
- `/agents` — Read-only list of registered agent profiles.

**Implementation notes:**

- Alpine.js owns the playback state machine (current event index, play/pause, speed).
- HTMX is used minimally — for the battle list it powers any sort/filter; for the replay page the JSONL is fetched once and played back client-side, no further server round-trips.
- Card and chip animations: CSS transitions, hand-rolled. No animation library.
- Poker table SVG: hand-authored, not generated.

## 8. Error handling and edge cases

| Situation | Handling |
|---|---|
| Agent endpoint times out | Treat as illegal action attempt; retry once with same prompt; on second timeout, auto-fold and log `auto_fold_reason: "endpoint_timeout"` |
| Agent returns prose but no tool call | Re-prompt with "You must call exactly one action tool"; counts as a retry |
| Agent calls an unknown tool | Re-prompt with the list of legal tools; counts as a retry |
| Agent calls a tool the orchestrator translates but MCP rejects (e.g., raise below min) | Re-prompt with the rejection reason and legal action set; counts as a retry |
| Agent is out of chips | Skipped entirely on subsequent hands; their seat is logged as inactive in each `hand_started` event's `inactive_seats[]` field. No turns are generated for them. |
| Web viewer encounters an unknown event type | Skip it; log a console warning. Forward-compat for new event types added later |
| JSONL file truncated mid-battle | Web viewer plays what's there; battle list marks battle as "incomplete" if no `battle_ended` event |

## 9. Out of scope (deferred to future slices)

- Live spectating / websockets
- Leaderboards, ELO ratings, statistical aggregation across many battles
- Authentication, multi-tenant, user accounts
- Tournament structures, escalating blinds, multi-table
- Other games (chess, etc.) — but the MCP boundary makes them straightforward to add
- Agent registry editing via UI (YAML files only for now)
- Automated battle scheduling
- Cost tracking / token pricing computation (token counts are logged, not converted to dollars)
- Per-seat replay view (watch from one agent's perspective with only their visible information)
- Aggregated metrics views ("which model bluffs most often?")

## 10. Open implementation-time decisions

Not load-bearing for the design; these get decided during implementation:

- Specific MCP server library for .NET (e.g. official `ModelContextProtocol` NuGet)
- Specific Texas Hold'em hand evaluator (existing library vs. hand-rolled — only matters for showdown ranking)
- Specific HTTP client / OpenAI SDK choice for the agent calls (raw `HttpClient` is fine; SDKs add little value for our narrow use)
- YAML parser (`YamlDotNet`)
- How battle IDs are generated (probably ULID for sortability)

## 11. Success criteria for the MVP

- `battle run --config configs/battle-poker-001.yaml` runs end-to-end with at least two real OpenAI-compatible endpoints and produces a complete JSONL file ending in `battle_ended`.
- The same battle, viewed at `/battles/{id}`, plays back smoothly with thoughts visible per turn.
- Adding a new agent is a YAML file edit, no code change.
- Adding a future game (e.g. tic-tac-toe) is a new `AgentBattle.{Game}.Mcp` project plus minor orchestrator config; no changes to the agent-side prompting logic.
