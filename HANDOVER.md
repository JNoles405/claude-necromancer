# HANDOVER — Claude Necromancer

The README says what this does. This file says how the work has been run, what was decided and why,
and which mistakes have already been paid for. It exists so the work can be picked up by somebody —
or some future session — with no memory of how it got here.

**Updated in the same commit as the change it describes.**

---

## 1. What it is

A Windows system-tray application that stops Claude Code session transcripts being deleted by the
retention sweep.

- **Language / framework:** C# on .NET 9, WinForms (`net9.0-windows`).
- **Build system:** plain `dotnet build`. One project, no solution file, **no NuGet dependencies at
  all** — see §2.
- **Entry point:** `src/ClaudeNecromancer/Program.cs`.
- **UI:** built in code, not with the designer, so the layout is reviewable as text and diffs
  cleanly.

---

## 2. Standing constraints

These are settled. Do not re-litigate them without a reason written down here.

1. **A touch writes zero bytes.** Only `LastWriteTimeUtc` / `LastAccessTimeUtc` move. Transcripts
   are JSONL — one JSON object per line — so appending anything that is not a valid JSON object
   corrupts the session. The original request suggested appending "just a period (.)"; that would
   have destroyed the thing the app exists to preserve. Metadata is the whole job.

2. **No NuGet packages.** DPAPI is reached by P/Invoking `crypt32.dll` directly rather than taking
   `System.Security.Cryptography.ProtectedData`, so the project restores and builds with no network
   and no package cache.

3. **The updater never runs an unverified download.** Every release publishes the SHA-256 of its
   `.exe` in the release notes; a download whose hash does not match is deleted and reported. A
   release with no published hash is deliberately **not offered** at all.

4. **The app never writes to a claude.ai account.** Chat backup reads conversations and nothing
   else. No message is ever posted.

5. **Auto-*install* is off by default.** Checking is on (it was asked for), but replacing the
   running executable is not done behind the user's back.

6. **`projects/<project>/memory/` is left alone.** Anthropic's docs exclude it from the sweep, so
   there is nothing to protect and no reason to churn its timestamps.

---

## 3. How the work is run

- Repository: `https://github.com/JNoles405/claude-necromancer.git`
- **Commit and push after every change or update.** Not batched.
- **Versioning is `x.xx.xx`** = `[major].[feature].[patch]`, displayed padded (`1.0.0` → `v1.00.00`).
- `HANDOVER.md` is updated in the same commit as the change it describes.

---

## 4. Building, running, testing

```bash
dotnet build src/ClaudeNecromancer/ClaudeNecromancer.csproj -c Release
```

Headless modes, which are also how behaviour gets verified without driving the GUI:

```bash
ClaudeNecromancer.exe --list
```

```bash
ClaudeNecromancer.exe --touch-now
```

Packaging a release:

```bash
pwsh -File scripts/make-release.ps1 -Notes "What changed in this one."
```

### Trap: you cannot see console output from this exe the usual way

It is `WinExe` (GUI subsystem), so PowerShell does **not** wait for it and does **not** capture its
stdout. `& $exe --list` prints nothing and returns immediately. Use:

```powershell
Start-Process -FilePath $exe -ArgumentList '--list' -Wait -NoNewWindow -RedirectStandardOutput $out
```

`Program.WriteConsole` calls `AttachConsole(-1)` so the output has somewhere to go when a real
console is present.

---

## 5. Architecture worth knowing

### The retention sweep, and why touching the parent is enough

From <https://code.claude.com/docs/en/claude-directory>, the sweep deletes, at Claude Code startup,
anything older than `cleanupPeriodDays` (**default 30, minimum 1**):

| Path under `~/.claude/` | Note |
| --- | --- |
| `projects/<project>/<session>.jsonl` | the transcript — **this is the file the sweep judges** |
| `projects/<project>/<session>/subagents/` | *"removed with the parent session transcript when it ages out"* |
| `projects/<project>/<session>/tool-results/` | spilled large tool outputs |
| `file-history/<session>/` | checkpoint snapshots — **swept on its own path, not with the parent** |

So refreshing `<session>.jsonl` shelters the whole session directory, but `file-history/` needs its
own touch. This was confirmed by measurement before it was coded: a subagent transcript dated
2026-05-27 (78 days old) had survived a sweep that ran that same day, because its parent transcript
was 6 days old.

Exceptions the sweep makes, which the app respects: `sessions/`, `projects/<project>/memory/`, bare
mode, and a **paused sweep** when Claude Code cannot determine the retention period.

### Files

| File | Responsibility |
| --- | --- |
| `Core/ClaudePaths.cs` | Every `~/.claude` location, and the sweep's default |
| `Core/SessionScanner.cs` | Finds sessions; reads only the head of each transcript for a title |
| `Core/Toucher.cs` | The zero-byte touch |
| `Core/Archiver.cs` | Copies outside `~/.claude` |
| `Core/SettingsPatcher.cs` | Reads/writes `cleanupPeriodDays` |
| `Core/ChatBackup.cs` | claude.ai conversation export |
| `Core/Updater.cs` | GitHub releases, SHA-256 verification, self-replacement |
| `Core/VersionInfo.cs` | Version display and tag parsing |
| `Core/Scheduler.cs` | One-minute heartbeat, due-time comparison |
| `UI/TrayApp.cs` | Owns process lifetime; the window is incidental |
| `UI/MainForm.cs` | The window |

### Non-obvious rules that prevent a class of bug

- **`Archiver` judges staleness by SIZE, never mtime.** This app rewrites mtimes by design, so mtime
  carries no information about content here. Transcripts are append-only, so size is a sound proxy.
- **`Scheduler` ticks every minute and compares against a stored due time**, rather than arming a
  timer for the whole interval. A long timer does not survive sleep, hibernation, the machine being
  off over a weekend, or the clock being changed.
- **`SessionScanner` reads at most 60 lines per transcript.** They run to tens of megabytes; the
  opening prompt is always near the top.
- **Top-level `*.jsonl` only.** Nested ones are subagent transcripts and are not judged on their own
  age.
- **`SettingsPatcher` writes via a temp file then moves.** A truncated `settings.json` would pause
  Claude Code's sweep and trip a `/status` warning.
- **`NotifyIcon.Text` throws above 63 characters.** `TrayApp.UpdateTrayState` truncates.

---

## 6. What changed and why

### v1.00.00 — first release

- Session scanning, zero-byte touching, scheduling, tray app, selective protection.
- `cleanupPeriodDays` reader and one-click raise, with a backup of `settings.json` first.
- Archiving outside `~/.claude`.
- claude.ai chat backup (see §7 for why this is a backup and not a "touch").
- Self-updater against GitHub releases with mandatory SHA-256 verification.
- Headless `--list` / `--touch-now` / `--version`.

Fixed during first-round GUI verification, before release:

- The window opened on the wrong tab (§7.9).
- `ShortProject` rendered four different projects as an identical "App" (§7.8).
- The all-clear message read "Nothing within 23 days of the sweep", which stated the wrong number
  in the wrong direction. It now names the actual margin: "the closest has 30 days left".

---

## 7. Traps already paid for

**The highest-value section. Do not repeat these.**

1. **Appending "." to a transcript would corrupt it.** The original spec proposed the smallest
   possible write, "even if it's just a period". Transcripts are JSONL; a bare `.` is not valid
   JSON. The touch writes zero bytes for this reason.

2. **claude.ai chats do not need keeping alive at all.** They are server-side and are not deleted
   for inactivity — they persist until deleted. Building a "toucher" for them would have posted
   real messages into real conversations to solve a problem that does not exist. The Chat tab is a
   *backup* instead, which addresses the risk that is actually there.

3. **`SelectionMode` collides with `System.Windows.Forms.SelectionMode`.** Ours is
   `ProtectionMode`. Do not rename it back.

4. **`$"""` raw strings and PowerShell do not mix.** In an interpolated raw string a single `{`
   opens interpolation, and `{{` is *not* an escape. `Updater.InstallAndRestart` generates a
   PowerShell script full of braces, so it uses `$$"""`, where single braces are literal and
   `{{ }}` marks interpolation.

5. **A GUI-subsystem exe returns immediately from PowerShell and captures no output.** See §4.

6. **A running `.exe` cannot replace itself.** The updater writes a PowerShell script that waits on
   the PID, moves the old build to `.bak`, copies the new one in and restarts it. If the copy fails
   it moves the backup back, so a failed swap never leaves nothing runnable.

7. **The subagent-file puzzle was solved by measuring, not reasoning.** A 78-day-old file surviving
   a 30-day sweep looked like a bug in the docs; it was the documented "removed with the parent"
   behaviour. Checking the timestamps took seconds and settled it.

8. **A project folder name is genuinely ambiguous, and the leaf of a path is not a label.**
   `F--CarKeep-App` encodes *either* `F:\CarKeep App` *or* `F:\CarKeep\App` — the encoding replaces
   both spaces and separators with dashes, so it cannot be inverted. On this machine the real paths
   are the nested ones, which put four different projects (`CarKeep`, `Lobbii`, `Qoder`,
   `NetRef-IT-Pro`) at a directory literally called `App`. `ShortProject` therefore shows the last
   **two** segments. Prefer the `cwd` recorded inside the transcript over the folder name; it is the
   only non-ambiguous source.

9. **`TabControl.SelectedIndex` must be set explicitly.** Adding pages leaves the selection
   wherever the last-added page put it, and the window opened on "Schedule & Settings".

10. **Never leave the app focused while automating around it.** During GUI verification, stray
    keystrokes reached the foreground window and silently ticked "Start with Windows" and "Start
    minimised", which wrote a `HKCU\…\Run` entry and made the next launch appear to fail with no
    window. Both were reverted. If the app suddenly starts hidden, check
    `%APPDATA%\ClaudeNecromancer\config.json` for `StartMinimized` before debugging anything else.

### Verifying the GUI without a person at the keyboard

Three dead ends, in order, each of which looked like it worked:

- **`CopyFromScreen` captures whatever is physically on top.** Windows refuses foreground steals
  from a background process, so `SetForegroundWindow` silently fails and you screenshot an
  unrelated window. Use **`PrintWindow` with flag 2** (`PW_RENDERFULLCONTENT`), which renders the
  target window regardless of z-order or occlusion.
- **`TCM_SETCURSEL` moves the tab highlight but not the page.** WinForms manages page visibility
  itself, so the header changes and the content does not — which looks exactly like a layout bug.
  Drive tab selection through **UI Automation's `SelectionItemPattern`** instead.
- **WinForms mangles window class names** — the tab control is
  `WindowsForms10.SysTabControl32.app.0.…`, so an exact match on `SysTabControl32` finds nothing.

The working scripts are not in the repo (they are throwaway harness), but the technique above is
what to rebuild if the GUI needs checking again.

---

## 8. How things are verified

Verified on 2026-08-13 against the real `~/.claude` on this machine, using
`--touch-now` and comparing before/after:

| Measurement | Result |
| --- | --- |
| Sessions found | 25 |
| Sessions touched | **25**, 0 failed |
| Files under `projects/` | 154 |
| Files with refreshed mtime | **77** |
| Files left untouched | 77 — **all of them under `memory/`**, which the sweep excludes |
| Total bytes before | 792,216,145 |
| Total bytes after | 792,216,145 |
| **Bytes changed** | **0** |

The zero-byte claim is measured, not asserted. Coverage is exactly right: everything the sweep can
delete was touched; everything it spares was left alone.

Closest session to deletion before the run was `Specd`, with 10.4 days left; after it, every
protected session read 30.0.

**GUI:** all five tabs were rendered and inspected via `PrintWindow` (see §7). The updater's check
was exercised against the live GitHub API: with no releases published it receives a 404 and
correctly reports "You are on the latest release", with Download and Install disabled.

**Packaging:** `scripts/make-release.ps1` was run end to end. It produces a 48.03 MB self-contained
single-file `ClaudeNecromancer-1.0.0-win-x64.exe`, which runs and reports `v1.00.00`, plus notes
whose checksum line matches the file byte for byte. That line —

```
| ClaudeNecromancer-1.0.0-win-x64.exe | `6249a2bb…` |
```

— is exactly the shape `Updater.FindSha256` parses: a 64-character hex run on a line that also
names the asset. **If you reformat the notes template, re-check that parser.**

**Not yet verified:** the update download/verify/swap path (needs a published release), and chat
backup against a live account (needs the owner's `sessionKey`).

---

## 9. Outstanding

- **No release has been published yet.** `scripts/make-release.ps1` produces the asset and the
  notes, but creating the GitHub release needs the owner's credentials and is deliberately manual.
  Until a release exists, the updater's check correctly reports "up to date" (a 404 from the
  releases API is treated as a normal empty state, not a fault).
- **The self-update path has not been exercised end-to-end**, because that requires two published
  releases. The download-and-verify half is implemented; the swap script has not been run against a
  real update. **Test this before relying on it.**
- **Chat backup depends on undocumented endpoints** (`/api/organizations/...`). They can change
  without notice. It has not been run against a live account in this session — needs the owner's
  `sessionKey`.
- **Icon** is drawn at runtime in `IconFactory`. Fine, but a designed `.ico` would look better.
- Not tested on a machine with **managed/enterprise settings**, which can override
  `cleanupPeriodDays` invisibly to this app.

---

## 10. Code conventions

- **Comments explain *why*, not *what*.** Several rules here exist to prevent a specific bug, and
  the comment says which one. Do not strip these as "obvious".
- **UI is built in code**, top to bottom, with a running `y` cursor. No designer files.
- `_loading` guards every settings control so populating from config does not write back to it.
- Config, logs and backups live in `%APPDATA%\ClaudeNecromancer` — deliberately **outside**
  `~/.claude`, so this app's own state can never be eaten by the sweep it exists to defeat.
- Failures are logged and surfaced, never swallowed silently — except in `Log` itself, which must
  never be the reason the app falls over.
