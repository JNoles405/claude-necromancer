# Claude Necromancer

A small Windows tray app that keeps Claude Code session transcripts from being deleted.

Claude Code sweeps `~/.claude` at startup and deletes session files older than `cleanupPeriodDays`
— **30 days by default**. Claude Necromancer refreshes the timestamps on the sessions you care
about so the sweep keeps passing them over, on whatever schedule you set.

![version](https://img.shields.io/badge/version-v1.01.00-informational)

## What it does

- **Touches sessions on a schedule.** Zero bytes written — only the modified time moves.
- **Shows what is at risk.** Every session, its size, what it was about, and how many days it has
  left before the sweep is entitled to delete it.
- **Protects everything, or just what you tick.**
- **Raises the retention window.** One click sets `cleanupPeriodDays` to 10 years, which is the
  root-cause fix rather than the symptom.
- **Archives copies** outside `~/.claude`, where the sweep never looks.
- **Backs up claude.ai conversations** to local JSON and Markdown.
- **Updates itself** from GitHub releases, verifying a published SHA-256 before installing.

## Why touching works

Per [Anthropic's documentation](https://code.claude.com/docs/en/claude-directory), the sweep
deletes these once they are older than `cleanupPeriodDays`:

| Path under `~/.claude/` | Contents |
| --- | --- |
| `projects/<project>/<session>.jsonl` | The full conversation transcript |
| `projects/<project>/<session>/subagents/` | Subagent transcripts — *"removed with the parent session transcript when it ages out"* |
| `projects/<project>/<session>/tool-results/` | Large tool outputs spilled to separate files |
| `file-history/<session>/` | Pre-edit snapshots backing checkpoint restore |

Because the sidecars age out *with their parent*, refreshing `<session>.jsonl` shelters the whole
session tree. `file-history/` sits on its own swept path, so it is touched separately.

`projects/<project>/memory/` is explicitly excluded from the sweep, so the app leaves it alone.

## About Claude Chat

**claude.ai conversations are not deleted for being idle.** They are stored server-side and stay
until you delete them, so there is nothing to keep alive and no local file to touch — "touching" a
web chat would mean posting real messages into it.

What is worth doing is keeping your own copy, so the **Chat Backup** tab downloads every
conversation as JSON and readable Markdown. It reads only; it never writes to your account.

It authenticates with your own `sessionKey` cookie against the same private endpoints the web app
uses. There is no public conversations API, so these are undocumented and Anthropic can change them
at any time. The key is stored encrypted with Windows DPAPI, readable only by your Windows account
on this machine.

## Install

Download the latest `.exe` from [Releases](https://github.com/JNoles405/claude-necromancer/releases)
and run it. It is self-contained — no .NET install required.

## Build from source

Requires the .NET 9 SDK.

```bash
dotnet build src/ClaudeNecromancer/ClaudeNecromancer.csproj -c Release
```

## Command line

The app is a tray app, but it will also do a single run and exit — useful with Task Scheduler.

```bash
ClaudeNecromancer.exe --list
```

```bash
ClaudeNecromancer.exe --touch-now
```

Unattended update — checks, downloads, verifies the published SHA-256 and swaps the executable.
Add `--check-only` to report without installing.

```bash
ClaudeNecromancer.exe --update
```

`--version` prints the version. `--minimized` starts straight to the tray.

## Safety

A touch changes timestamps and nothing else. It never appends to a transcript: these are JSONL
files parsed one JSON object per line, and writing filler into them would corrupt the session.

Raising `cleanupPeriodDays` is a real trade, and the app says so before doing it — transcripts hold
whatever passed through a tool, including any secrets that were printed, so keeping them for ten
years is a decision worth making deliberately.

## Licence

MIT
