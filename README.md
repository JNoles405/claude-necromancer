# Claude Necromancer

A small Windows tray app that keeps Claude Code session transcripts from being deleted.

Claude Code sweeps `~/.claude` at startup and deletes session files older than `cleanupPeriodDays`
— **30 days by default**. Claude Necromancer refreshes the timestamps on the sessions you care
about so the sweep keeps passing them over, on whatever schedule you set.

![version](https://img.shields.io/badge/version-v1.02.00-informational)

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
- **Starts with Windows** by default, so the schedule actually runs. One checkbox to turn off.

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

Set `CLAUDE_NECROMANCER_HOME` to keep config, logs and backups somewhere other than
`%APPDATA%\ClaudeNecromancer` — useful for a portable install, or for trying settings without
disturbing your real ones.

## Starting with Windows

**On by default.** This is a tray utility whose entire job happens on a schedule, and a scheduler
that only runs when you remember to launch it is not a scheduler. On its first run the app adds
itself to `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` — per-user, so it needs no
administrator rights — and logs that it did.

Turn it off any time with **Schedule & Settings → Start with Windows**. That sticks: the default is
applied once, on the first run only, and is never re-applied afterwards.

## How do I know it's working?

Three checks, in increasing order of independence from this app.

**1. Ask the app.** The Sessions tab shows "Last touched" and "Days left" per session, and the
Activity tab logs every run. From a terminal:

```
ClaudeNecromancer.exe --list
```

**2. Check the filesystem yourself**, trusting nothing this app says. Any session whose modified
time is recent is one the sweep will pass over:

```
Get-ChildItem "$env:USERPROFILE\.claude\projects" -Recurse -Filter *.jsonl |
  Sort-Object LastWriteTime |
  Select-Object -First 10 LastWriteTime, @{n='MB';e={[math]::Round($_.Length/1MB,2)}}, Name
```

**3. Check the retention window**, which is the protection that does not depend on this app running
at all:

```
type %USERPROFILE%\.claude\settings.json
```

### The proof that touching works

The claim underneath all of this — that the sweep judges files by modified time — was verified by
experiment, not by reading the documentation:

An isolated `CLAUDE_CONFIG_DIR` sandbox was created with `cleanupPeriodDays: 30` and two session
transcripts of **byte-identical content** (294 bytes each). One was aged 60 days; the other was
touched to the current time. Claude Code was then started against that sandbox to run its real
startup sweep.

| File | mtime | Outcome |
| ---- | ----- | ------- |
| `aaaaaaaa-….jsonl` | 60 days old | **deleted** |
| `bbbbbbbb-….jsonl` | touched to now | **survived, intact** |

Same content, same directory, same sweep. The only variable was the modified time, and it decided
which file lived. That is exactly the lever this app pulls.

## Safety

A touch changes timestamps and nothing else. It never appends to a transcript: these are JSONL
files parsed one JSON object per line, and writing filler into them would corrupt the session.

Raising `cleanupPeriodDays` is a real trade, and the app says so before doing it — transcripts hold
whatever passed through a tool, including any secrets that were printed, so keeping them for ten
years is a decision worth making deliberately.

## Licence

MIT
