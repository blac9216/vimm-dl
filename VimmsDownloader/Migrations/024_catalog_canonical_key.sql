-- D2 (#129): a canonical per-game content key on catalog games, computed in C# at sync time
-- (Module.Catalog/CanonicalKey): single-ROM -> the ROM's strongest hash; multi-ROM -> a set hash of
-- the sorted per-ROM hashes. This is the cross-source identity by which the same game collapses to
-- one entity across DATs/sources (the actual cross-system row-merge lands with the 2nd source in D3).
-- NULL when identity is unknown (a ROM lacks SHA1/MD5). The next sync recomputes it. Additive + idempotent.

ALTER TABLE catalog_game ADD COLUMN canonical_key TEXT;

CREATE INDEX IF NOT EXISTS idx_catalog_game_canonical ON catalog_game(canonical_key);
