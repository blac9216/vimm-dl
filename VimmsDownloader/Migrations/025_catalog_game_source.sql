-- D2b (#162): record which DAT data-source ORIGIN(s) contributed a catalog game, so libretro and the
-- daily-bundle (D3b) can coexist for the same console. Catalog sync becomes additive + hash-deduped
-- (Module.Catalog/CanonicalKey): syncing a console from libretro then bundle ACCUMULATES coverage —
-- games shared by both collapse to one catalog_game (matched by canonical_key, within the system),
-- bundle-only games are added — instead of one origin's sync wiping the other's. One row per
-- (game, origin). The catalog_game.system_id anchor (= console) is unchanged. Additive + idempotent.

CREATE TABLE IF NOT EXISTS catalog_game_source (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    game_id INTEGER NOT NULL,
    origin TEXT NOT NULL,          -- data-source origin: 'libretro' | 'daily-bundle'
    dat_version TEXT,              -- the DAT version this origin recorded for the game's system
    UNIQUE(game_id, origin)
);

CREATE INDEX IF NOT EXISTS idx_catalog_game_source_game ON catalog_game_source(game_id);
