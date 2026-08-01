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

-- Only the two indexes the hot paths actually use: link_id (invalidate/replace a link's rows) and
-- game_id (the fast-resolve lookup). Matching runs entirely IN MEMORY against catalog_rom's own hash
-- index, and nothing ever selects from catalog_set_file by hash — so per-hash B-trees would be three
-- extra writes on every one of the thousands of inserts a single link's index run makes, for no read.
CREATE INDEX IF NOT EXISTS idx_catalog_set_file_link ON catalog_set_file(link_id);
CREATE INDEX IF NOT EXISTS idx_catalog_set_file_game ON catalog_set_file(game_id);

-- This migration briefly created those three hash indexes before shipping in any release. Dropping them
-- here (rather than in a new migration) keeps the intent in one file: fresh databases never create them,
-- and a database that ran the earlier 032 converges the moment it re-runs this file — which the migrator
-- does whenever its schema_migrations row is absent, its own idempotency contract. IF EXISTS makes all
-- three no-ops on a fresh database.
DROP INDEX IF EXISTS idx_catalog_set_file_sha1;
DROP INDEX IF EXISTS idx_catalog_set_file_md5;
DROP INDEX IF EXISTS idx_catalog_set_file_crc;

ALTER TABLE catalog_set_link ADD COLUMN indexed_at TEXT;
ALTER TABLE catalog_game ADD COLUMN archive_match TEXT;
