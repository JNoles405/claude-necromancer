# Claude Necromancer — project instructions

## Read HANDOVER.md first

[`HANDOVER.md`](HANDOVER.md) at the repository root records how the work is run, what has been
decided and why, and which mistakes have already been paid for. The README says what the thing
does; the handover says how to work on it.

**`HANDOVER.md` is updated in the same commit as the change it describes.** Not at the end of a
session, not when somebody remembers. A handover that is three rounds out of date is worse than
none, because it gets believed.

Something belongs in it when it changes how the project is built, run or tested; adds a trap or a
non-obvious API name; settles a decision or records why the obvious alternative is wrong; or
completes an outstanding item.

## Commit and push after every change

The remote is `https://github.com/JNoles405/claude-necromancer.git`. Every change or update is
committed and pushed — not batched up for later.

## Versioning

`x.xx.xx` — `[major release].[feature release].[patch or fix]`.

The version lives in exactly one place: `<Version>` in
`src/ClaudeNecromancer/ClaudeNecromancer.csproj`. `VersionInfo` reads it from the assembly at
runtime and `scripts/make-release.ps1` reads it from the csproj, so the binary, the git tag and the
asset filename cannot drift apart by hand.

Displayed padded — `1.0.0` reads `v1.00.00` — because a release number is a label, not a quantity,
and a column of them is easier to compare when they are all the same width.

## The one rule that must not be broken

**A touch writes zero bytes.** Transcripts are JSONL, parsed one JSON object per line. Appending
anything that is not a valid JSON object — a bare `.` especially — corrupts the session and
destroys the thing this app exists to preserve. Keeping a session alive is a metadata operation
and nothing else.
