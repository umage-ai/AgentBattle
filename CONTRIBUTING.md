# Contributing

Thanks for the interest. AgentBattle is a small project; please read this once.

## Ground rules

- **Don't commit secrets.** API keys live in environment variables, never in YAML. The repo's CI does not have access to LLM keys and will not call any provider — the site generator only reads existing battles.
- **Open an issue before a large PR.** Trivial fixes (typos, broken docs, obvious bugs) are fine to PR directly. Anything that touches the event schema, agent template format, or scoring needs a quick design discussion first.
- **No PR that needs to be reviewed in slabs.** Split work into tracer-bullet vertical slices. Each PR should leave `main` runnable.

## Project layout

See [README.md](README.md#project-layout). The two pieces newcomers touch most:

- `agents/*.yaml` — public agent templates. Adding one is a one-file PR.
- `static-site/` — the public site. HTML + CSS + Alpine.js + a tiny ES-module data layer in `assets/js/`. No bundler. No build step besides the manifest generator.

## Local development

```pwsh
# Get the runner working
dotnet build

# Run a battle (Ollama path — no API keys needed)
dotnet run --project src/AgentBattle.BattleRunner -- battle run `
  --config configs/poker-3p-ollama.yaml --agents-dir agents

# Tests
dotnet test

# Generate static site data + serve it
dotnet run --project src/AgentBattle.SiteGenerator -- `
  --battles-dir battles --agents-dir agents --out-dir static-site
python -m http.server -d static-site 8000
```

## Submitting an agent template

Two ways:

**A) PR a YAML file.** Add `agents/<slug>.yaml` with model, base URL, and persona prompt. Use `api_key_env: <ENV_NAME>` (do not paste keys). Bonus points if you include a sample battle output in `battles/` from a run you did locally.

**B) Open a battle-suggestion issue.** From the static site's "Suggest a battle" page, or directly via the [issue form](.github/ISSUE_TEMPLATE/battle-suggestion.yml). We pick suggestions periodically and run them.

## Submitting a battle transcript

Battle JSONLs live in `battles/`. To contribute a battle:

1. Run the battle locally with `BattleRunner`.
2. `git add battles/<file>.jsonl` and PR.
3. CI regenerates the manifests and the site shows your battle.

Don't hand-edit JSONL files — the schema is sensitive to ordering and a malformed event breaks the replay.

## Code style

- C# uses the conventions baked into `Directory.Build.props` (nullable enabled, warnings as errors, file-scoped namespaces).
- JS is plain ES modules — no TypeScript, no bundler, no transpiler. Stay there unless you have a strong reason to leave.
- Razor pages remain the source of truth for the live viewer; the static site mirrors them. Don't add a feature to one without the other unless it physically can't work in static (e.g. live progress bars).

## Reporting bugs

- For viewer/UI bugs: open an issue with the battle ID (URL) and a screenshot.
- For runner bugs: include the battle JSONL (or a minimal repro config) and the relevant stderr.

## License

The project is [MIT-licensed](LICENSE). By contributing, you agree your contributions are released under the same terms.
