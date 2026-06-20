-- Catalog: the canonical "all games that exist" set, synced from the No-Intro / Redump
-- DATs (via the libretro-database mirror). catalog_system = one row per console DAT;
-- catalog_game = one entry per title/region/revision (pre-1G1R); catalog_rom = the
-- file(s) per game (disc titles carry several). Additive + idempotent.

CREATE TABLE IF NOT EXISTS catalog_system (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    dat_name TEXT NOT NULL UNIQUE,
    console TEXT NOT NULL,
    source TEXT NOT NULL,
    dat_version TEXT,
    game_count INTEGER NOT NULL DEFAULT 0,
    synced_at TEXT
);

CREATE TABLE IF NOT EXISTS catalog_game (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    system_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    region TEXT,
    serial TEXT,
    languages TEXT
);

CREATE TABLE IF NOT EXISTS catalog_rom (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    game_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    size INTEGER NOT NULL DEFAULT 0,
    crc TEXT,
    md5 TEXT,
    sha1 TEXT
);

CREATE INDEX IF NOT EXISTS idx_catalog_game_system ON catalog_game(system_id);
CREATE INDEX IF NOT EXISTS idx_catalog_game_serial ON catalog_game(serial);
CREATE INDEX IF NOT EXISTS idx_catalog_rom_game ON catalog_rom(game_id);
CREATE INDEX IF NOT EXISTS idx_catalog_rom_sha1 ON catalog_rom(sha1);
