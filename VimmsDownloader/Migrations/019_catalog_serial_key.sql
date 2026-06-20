-- Compat join symmetry (#48): a precomputed normalized serial key on catalog games. The compat
-- lookup now joins catalog_game.serial_key = catalog_compat.serial_key (both produced by the same
-- normalizer) instead of an inline REPLACE that only stripped dashes, so a serial with any other
-- separator still resolves. The next sync recomputes serial_key precisely via the C# normalizer;
-- the backfill below matches the old dash-only behavior for existing rows (real serials are
-- dash-only, so it is exact for them). Additive + idempotent.

ALTER TABLE catalog_game ADD COLUMN serial_key TEXT;

UPDATE catalog_game SET serial_key = UPPER(REPLACE(serial, '-', '')) WHERE serial IS NOT NULL AND serial_key IS NULL;

CREATE INDEX IF NOT EXISTS idx_catalog_game_serial_key ON catalog_game(serial_key);
