-- 1G1R: group key + preferred-variant flag on catalog games, computed at sync time.
-- is_parent defaults to 1 so the non-deduped view is correct until the next sync recomputes.
-- Additive + idempotent (migrator ignores "duplicate column").

ALTER TABLE catalog_game ADD COLUMN title_key TEXT;
ALTER TABLE catalog_game ADD COLUMN is_parent INTEGER NOT NULL DEFAULT 1;

CREATE INDEX IF NOT EXISTS idx_catalog_game_parent ON catalog_game(system_id, is_parent);
