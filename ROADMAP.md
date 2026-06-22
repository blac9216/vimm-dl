# Roadmap

Planned features and architectural improvements. Each item describes the problem, the solution, and what it unblocks.

The north star: **a browsable catalog of every game on every console** — driven by the No-Intro/Redump
DATs as the authoritative "all games that exist" set — that you can filter by console, by name, and by
remote-vs-local, see real metadata for, and queue to download from the best available source. The
catalog entry is the **identity**; download sources (archive.org, Vimm) bind onto it.

---

## Catalog ↔ Vimm Hash Binding (one entry per game, many formats)

**Status:** ✅ Shipped (Phase B) — built across PRs #72–#92 under epic #78. The design below is retained
as the as-built reference. One divergence from the sketch: the console→Vimm-code map shipped as a
**code registry** (`Module.Catalog/VimmSystems`), not a `catalog_vimm_system` table.

### Vision

Every library row is **one game**, identified by its No-Intro/Redump catalog entry (name + per-ROM
CRC32/MD5/SHA1 + size). Onto that entry we bind, at sync time:

- a **Vimm vault URL** (so every catalog game that Vimm carries has a download fallback), and
- **all of Vimm's available download formats** for that game (e.g. PS3 JB Folder *and* `.dec.iso`),
  with per-format sizes.

So a single library row can offer multiple formats and multiple sources, while still being **one
entry** — no duplicate rows for the same game in a different format. Games that exist on Vimm but
fail to match get a **"no Vimm match" badge** so they can be reconciled manually.

### Why hash-based (not name-based)

Names drift across regions/revisions and between No-Intro/Redump and Vimm's own titling, so name
matching is fuzzy and wrong often enough to matter. **Vimm exposes the full Redump/No-Intro hash
triple (CRC32 + MD5 + SHA1)**, so the binding is *authoritative*: a Vimm entry matches a catalog
game iff their hashes agree. Name/size are at most weak secondary signals.

Verified on live vault pages:

- **Single-file systems** (NES/SNES/GB/GBA/Genesis/PS2/…): the vault page embeds a `media` JSON
  array carrying `GoodHash` (CRC32), `GoodMd5`, `GoodSha1`, plus `Serial` and `GoodTitle` (base64 of
  the canonical `Name (Region).ext`). Hashes are inline — free with the page fetch.
- **Multi-file / multi-disc systems** (PS1/Saturn/Sega CD/…): the page carries no inline `GoodHash`;
  instead the AJAX endpoint `vault/ajax/hashes2.php?id=<mediaId>` returns an HTML fragment titled
  **"Redump File Hashes"** with per-file (`.bin`/`.cue`) Crc/Md5/Sha1. One extra request per title.

These match directly against the existing `catalog_rom` table (`crc`/`md5`/`sha1`, already indexed on
`sha1`). Match priority: SHA1 → MD5 → CRC32.

### Formats & sizes (captured during the same scrape)

- Download **format** = a `<select id="dl_format">` whose option `value` is the `alt` index and
  `title` is the human label (`JB Folder`, `.dec.iso`, …). Single-file systems have no select → one
  implicit format 0. This is exactly the `format` the downloader already understands.
- Per-format **sizes** come from the `media` JSON: `Zipped`/`ZippedText` (alt 0),
  `AltZipped`/`AltZippedText` (alt 1), `AltZipped2`/`AltZipped2Text` (alt 2). These are the
  *compressed download* sizes (use the hash, not size, for identity).
- The **download trigger** is a POST to `//dl3.vimm.net/` with `mediaId` + `alt` (the JS enables and
  sets `alt` to the chosen format index). `VaultPageParser` already resolves this.

### Schema (additive)

- `catalog_game` gains `vault_id INTEGER` (nullable; the bound Vimm media/vault id — URL is
  `https://vimm.net/vault/{vault_id}`) and a match marker (e.g. `vimm_match` = `sha1`/`md5`/`crc`/
  `none`/`null`-unscraped) for the badge.
- New `catalog_vimm_format(id, game_id, alt INTEGER, label TEXT, size_bytes INTEGER, size_text TEXT)`
  — one row per (game, downloadable format). This is what makes "one entry, many formats" real.
- The console→Vimm-code map (e.g. `psx`→`PS1`, `gc`→`GameCube`, `pcengine`→`TG16`) shipped as the
  `VimmSystems` code registry (33 consoles), not a DB table. Consoles Vimm doesn't carry are simply
  "no Vimm match" by construction (the badge is expected there, not an error).

### The scrape (throttled, incremental, cached)

Hashes/formats are **not** in the list view — each must be read from the title's vault page (+1
request for multi-disc `hashes2.php`). A full multi-console match is *tens of thousands of requests
over hours*. So the binding runs as a **polite background job**, per the user's "per-console,
incremental" choice:

1. **Enumerate** a console's titles from the list view — `vault/?p=list&system=<CODE>&section=<A..Z,number>`
   (~27 requests/console) — capturing `vault_id`, title, and region flag. Robust row regex:
   `href\s*=\s*"/vault/(\d+)"\s*>([^<]+)</a>` (HTML-decode the title; skip the placeholder
   `/vault/999999` anchor in each row; note the literal space in `href= "`).
2. **Resolve hashes** per title (inline `GoodHash`/… or `hashes2.php`), throttled, with progress and
   resumable cursor so a run can stop/continue. Vimm runs plain nginx (no rate-limit headers seen),
   but we stay polite (small concurrency, backoff on 429).
3. **Match & bind** against `catalog_rom`; set `catalog_game.vault_id` + `vimm_match`, upsert
   `catalog_vimm_format` rows. Cache by `vault_id` so re-runs only fetch new/changed entries.

### Download resolution (the fallback you noticed missing)

`CatalogResolveService` becomes source-aware:

1. **Prefer archive.org** sets (parallel) — current behavior.
2. **Fall back to the pre-bound Vimm vault URL + chosen format** (serial via `VimmSource`) when no
   archive set provides the game. No live guessing — it uses the binding captured at sync time.
3. If neither resolves → a clear "not available from configured sources" state (and, if Vimm should
   have it, the "no Vimm match" badge points the user at manual reconciliation).

### What it unblocks

- The user-reported bug: catalog downloads now fall back to Vimm instead of 404-ing.
- One library row per game with selectable format + source.
- Hash-accurate **owned** detection and cross-format dedup (next section).
- A complete, source-agnostic identity for the pipeline-identity work below.

---

## Pipeline Identity: Vault URL + Format

**Status:** ✅ Shipped (Phase C) — built across PRs #143/#145/#146/#148/#150 under epic #96, on top of the
hash binding above. One as-built divergence: identity is carried on the persistent layer (the `events`
columns + `completed_urls.game_id`) and stamped at the SignalR bridge, rather than re-keying the
in-memory `PipelineState.Statuses` dict — `ItemName` stays the frontend's display/abort label and the
PS3 pipeline's cancellation/resume machinery is filename-bound. The live/UI per-game grouping that
implies is tracked as #151.

### Problem

The pipeline uses the **filename** as the item identity everywhere:
- `PipelineState.Statuses` dictionary key
- `PipelineStatusEvent.ItemName`
- `events.item_name` column
- `completed_urls.filename` for conversion state tracking
- Duplicate detection matches by filename

This breaks in several real scenarios:

1. **Same game, two formats.** Downloading Uncharted as `.dec.iso` (format 1) and as JB Folder `.7z` (format 0) produces different filenames. The system treats them as completely unrelated — no duplicate warning, no shared event history, no way to see they're the same vault item.

2. **Filename collisions.** Two different games could produce archives with similar names. The pipeline would confuse their status events.

3. **Event correlation across retries.** If a user downloads, deletes, and re-downloads the same game, events from all attempts share the same `item_name` but have no vault-level grouping.

4. **Cross-format duplicate detection.** The current duplicate check matches by URL only. If the user downloads `vault/12345` as format 0, then tries format 1, the URL matches but the system can't tell the user "you already have this game in a different format."

### Solution

Replace filename with the **catalog game identity** — and where the download came from Vimm, its
**vault URL + format** — as the pipeline's item key. With the hash binding in place, *every* item
(archive.org or Vimm) can resolve back to a single catalog game, so identity is unified across
sources, not Vimm-only.

- The catalog game (or vault URL `https://vimm.net/vault/12345`) uniquely identifies the game
- The format (0, 1, ...) identifies which pipeline path it takes
- Together they form a composite key: one game can have multiple pipeline runs (one per format)
- The filename remains for display and filesystem operations — it just stops being the identity key

### What changes

| Area | Current | After |
|------|---------|-------|
| `PipelineState.Statuses` key | filename | catalog game id / vault id + format |
| `PipelineStatusEvent.ItemName` | filename | composite item key |
| `events.item_name` | filename | composite item key |
| `completed_urls` tracking | matched by filename | matched by game/URL + format |
| Duplicate detection | URL match only | hash/game match + cross-format awareness |
| Frontend event grouping | filter by filename | filter by game, distinguish formats |

### What it unblocks

- Cross-format duplicate detection ("you already have this as JB Folder")
- **Hash-based owned dedup** — a game is "owned" if a local file's hash matches its `catalog_rom`,
  regardless of which format/source produced it; the library shows one row, "owned", with the formats
  on hand.
- Accurate event timeline per game per format
- Cleaner correlation ID grouping (correlation per game + format + run)

### Migration

- Add `vault_url`/`game_id` and `format` columns to `events` (or a composite `item_key`)
- Backfill from `completed_urls.url` where possible
- Old events with filename-only `item_name` remain queryable but lack grouping

---

## Phasing overview

- **Phase A — Archive sets & settings — ✅ shipped.** Sets modeled as name + console + links[] (with
  migration), RomGoGetter archive defaults seeded, archive settings incl. Internet Archive S3 keys,
  Library filter persistence, and full No-Intro/Redump console coverage in the catalog.
  *Still open:* source-aware download parallelism (archive parallel, Vimm serial) — the
  `archive_parallelism`/`retries`/`idle` settings are stored but not yet wired into the engine.
- **Phase B — Vimm hash-identity binding — ✅ shipped.** The schema, the throttled per-console scrape +
  hash match, format capture, the "no Vimm match" badge, and source-aware download resolution
  (archive preferred, bound vault URL fallback + format choice).
- **Phase C — Identity/dedup deepening — ✅ shipped.** Catalog-game identity (`game_id` + `format`) on
  the event log + `completed_urls` (migrations 022/023), events stamped with it at the SignalR bridge,
  hash-based owned detection (CRC32/MD5/SHA1, name-independent), cross-format/source duplicate
  detection, and one library row per game with consolidated formats/sources. Follow-ons: per-game
  grouping for queue/history + live conversions (#151).

---

## Future Console Support

**Status:** Architecture ready, waiting for demand

The `IPipeline` interface supports any console. Adding a new one requires:

1. `Module.{Console}Tools` — console-specific tools (e.g., ISO conversion, patching)
2. `Module.{Console}Pipeline` — implements `IPipeline` with `BuildFlow`, `CheckDuplicate`, `GetStepDurations`
3. `{Console}Phase` — console-specific sub-phases extending `PipelinePhase`
4. Host wiring — register in DI, add to `DownloadQueue.GetPipeline()`, add platform check in `HandlePostDownload`

The pipeline-owned flow system means no endpoint changes needed — the host delegates generically.

### Candidates

- **PS2** — simpler than PS3, likely just extraction + rename
- **PSP** — ISO handling, CSO compression
- **Wii** — WBFS format handling
- **GameCube** — ISO/GCZ handling
- **Wii U** — clean-room NUS download + AES title-key decryption (MIT-preserving; never copy the GPLv3 Go)
