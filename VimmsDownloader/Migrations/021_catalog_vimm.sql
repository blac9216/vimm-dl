-- Catalog <-> Vimm binding (ROADMAP "Catalog <-> Vimm Hash Binding"). Each catalog_game can be bound
-- to a Vimm vault entry by hash, carrying that game's available Vimm download formats. The binding is
-- populated by a background scrape; vimm_match records how the match was made (sha1/md5/crc), or
-- 'none' once a console has been scraped without a match (drives the "no Vimm match" badge), or NULL
-- when not yet scraped. Additive + idempotent.

ALTER TABLE catalog_game ADD COLUMN vault_id INTEGER;
ALTER TABLE catalog_game ADD COLUMN vimm_match TEXT;

CREATE TABLE IF NOT EXISTS catalog_vimm_format (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    game_id INTEGER NOT NULL,
    alt INTEGER NOT NULL,
    label TEXT NOT NULL,
    size_bytes INTEGER NOT NULL DEFAULT 0,
    size_text TEXT
);

CREATE INDEX IF NOT EXISTS idx_catalog_vimm_format_game ON catalog_vimm_format(game_id);
CREATE INDEX IF NOT EXISTS idx_catalog_game_vault ON catalog_game(vault_id);
