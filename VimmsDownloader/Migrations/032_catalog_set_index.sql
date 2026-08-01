-- Set-content index (RomGoGetter-style, #289): persist every configured archive.org set link's file
-- listing up front instead of listing live at queue time. catalog_set_file = one row per indexed file
-- (name/size/hashes when the source provides them, plus the catalog game it was bound to, if any, and
-- how). indexed_at on catalog_set_link stamps when a link was last listed (staleness marker; the live
-- listing stays as the resolve fallback).
--
-- archive_match on catalog_game mirrors vimm_match's shape ('sha1'/'md5'/'crc'/'name' matched, 'none'
-- indexed-but-unmatched, NULL not yet indexed) so the Library's per-row projection stays a plain column
-- read — no join against the (potentially huge) catalog_set_file table on every games page. Additive +
-- idempotent.

CREATE TABLE IF NOT EXISTS catalog_set_file (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    link_id INTEGER NOT NULL,
    name TEXT NOT NULL,
    size INTEGER NOT NULL DEFAULT 0,
    crc TEXT,
    md5 TEXT,
    sha1 TEXT,
    game_id INTEGER,
    match_kind TEXT
);

CREATE INDEX IF NOT EXISTS idx_catalog_set_file_link ON catalog_set_file(link_id);
CREATE INDEX IF NOT EXISTS idx_catalog_set_file_game ON catalog_set_file(game_id);
CREATE INDEX IF NOT EXISTS idx_catalog_set_file_sha1 ON catalog_set_file(sha1);
CREATE INDEX IF NOT EXISTS idx_catalog_set_file_md5 ON catalog_set_file(md5);
CREATE INDEX IF NOT EXISTS idx_catalog_set_file_crc ON catalog_set_file(crc);

ALTER TABLE catalog_set_link ADD COLUMN indexed_at TEXT;
ALTER TABLE catalog_game ADD COLUMN archive_match TEXT;
