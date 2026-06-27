-- Multi-emulator compat (#133 / epic #121): generalize the compat join key beyond the RPCS3-era
-- serial. catalog_compat now carries a match_kind (serial | title_id | name) alongside match_key,
-- so each emulator joins to catalog_game by the key its data exposes — serial (PlayStation family),
-- 16-hex Title ID (Nintendo systems), or normalized name (Dolphin). The table is a network-resyncable
-- cache (POST /api/catalog/compat/sync replaces it per emulator), so this rebuilds it rather than
-- renaming a primary-key column — fully idempotent (DROP IF EXISTS + CREATE IF NOT EXISTS). The next
-- compat sync repopulates it.

DROP TABLE IF EXISTS catalog_compat;

CREATE TABLE IF NOT EXISTS catalog_compat (
    emulator   TEXT NOT NULL,
    match_kind TEXT NOT NULL,
    match_key  TEXT NOT NULL,
    status     TEXT NOT NULL,
    PRIMARY KEY (emulator, match_kind, match_key)
);

CREATE INDEX IF NOT EXISTS idx_catalog_compat_key ON catalog_compat(match_kind, match_key);
