-- Catalog download sources ("sets"): per-console locations that hold a console's roms.
-- For source='archive', identifier is an archive.org item id whose files are the roms.
-- User-managed (no defaults). Additive + idempotent.

CREATE TABLE IF NOT EXISTS catalog_set (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    console TEXT NOT NULL,
    source TEXT NOT NULL,
    identifier TEXT NOT NULL,
    label TEXT,
    created_at TEXT
);

CREATE INDEX IF NOT EXISTS idx_catalog_set_console ON catalog_set(console);
