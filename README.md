# VIMM // DL

A self-hosted ROM management toolkit. Browse the full **No-Intro / Redump** catalog of console games, see what you own, and download from **archive.org** — with an automatic fallback to **[Vimm's Lair](https://vimm.net)** — then convert and organize into EmuDeck-style per-console folders. Built with .NET Native AOT.

> We owe a lot to Vimm’s Lair for keeping the history of gaming accessible for nearly 30 years. To honor that legacy, this project does not—and will not—provide ways to bypass their download limits. Following the rules is a small price to pay to keep these archives online. Let’s learn from the loss of sites like Myrient and show our gratitude to Vimm by being responsible users.

## Quick Start

```bash
docker run -d -p 5000:5000 \
  -v ~/vimm:/vimms \
  --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
```

Open **http://localhost:5000** — browse the catalog and queue games, or paste vault URLs directly.

## Volume

Everything lives under a single `/vimms` mount:

```
/vimms/
├── data/          ← SQLite database (queue, catalog, metadata, events, settings)
└── downloads/     ← All files: downloading/, completed/, ps3_temp/
```

| Path | Purpose |
|------|---------|
| `/vimms/data/` | Database — persists queue, catalog, history, settings, events |
| `/vimms/downloads/` | Files — partial downloads, archives, ISOs, temp conversion |

> **Important:** Without the volume mount, everything is lost on container update. See [UPDATE.md](UPDATE.md).

**Examples:**

```bash
# Linux / macOS
docker run -d -p 5000:5000 \
  -v ~/vimm:/vimms \
  --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest

# With sync to external drive
docker run -d -p 5000:5000 \
  -v ~/vimm:/vimms \
  -v /mnt/usb/PS3ISO:/sync-target \
  --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest

# Windows
docker run -d -p 5000:5000 \
  -v %USERPROFILE%\vimm:/vimms \
  --name vimm-dl ghcr.io/eduvhc/vimm-dl:latest
```

> **Note:** Sync is in beta. Enable it in Settings under Feature Flags. The target drive must be bind-mounted (e.g., `/sync-target`).

## Features

### Library & sources

- **Catalog** — browse the canonical **No-Intro / Redump** set (every console with a libretro DAT), filter by console / name / owned-vs-missing / 1G1R, with emulator (RPCS3) compatibility
- **Sources** — download from archive.org "sets" (preferred) with an automatic fallback to Vimm's Lair; configurable archive parallelism / retries / idle + optional Internet Archive S3 keys
- **Vimm hash binding** — a per-console sync matches each catalog game to its Vimm vault entry **by CRC32/MD5/SHA1**, binding a vault URL + every available download format (a "no Vimm match" badge flags anything to reconcile manually); pick the format at download time
- **Owned + verify** — scan your library to mark owned games, verify files by CRC32 against the catalog

### Downloads & queue

- Paste URLs or queue from the catalog — downloads start automatically with format fallback
- Pause/resume with HTTP Range, auto-resume on restart
- Archive validation, multithreaded extraction, optional archive preservation
- Real-time progress, platform icons, format selection, drag-and-drop queue
- Metrics dashboard — download speed chart, disk usage, system info
- Event audit log — full event history with filters and detail view
- JSON import/export with background metadata fetch
- Feature flags (Beta/Developer) for the Library, Sync, and Events tabs
- Native AOT — fast startup, small footprint, no runtime needed

### PS3 conversion pipeline

- JB Folder → ISO conversion (parallel pipeline, crash recovery)
- .dec.iso download + rename (default format, configurable)
- ISO filename formatting — fix "The" placement, append serial, strip region
- Per-platform settings (default format, rename rules, parallelism, archive preservation)

> The pipeline architecture (`IPipeline`) is designed for multi-console support. PS3 is the current focus — contributions for other consoles are welcome.

## Thanks

This project wouldn't exist without the people who decided that preserving games was worth the effort.

**[Vimm's Lair](https://vimm.net)** has been keeping classic games accessible since 1997 — long before anyone called it "digital preservation." This project is a love letter to that mission.

**[bucanero/ps3iso-utils](https://github.com/bucanero/ps3iso-utils)** gave us `makeps3iso` and `patchps3iso`. The entire PS3 conversion pipeline exists because of this work.

**[NullShield's VimmsDownloader](https://github.com/NullShield-Official/VimmsDownloader)** was the original Python/Selenium downloader that planted the seed. VIMM // DL is a from-scratch .NET rewrite, but the idea started there.

## License

MIT © 2026 eduvhc
