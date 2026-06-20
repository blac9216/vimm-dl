-- Catalog ownership: which catalog games are present on disk, matched from completed/ by
-- POST /api/catalog/scan. Replaced wholesale on each scan. Additive + idempotent.

CREATE TABLE IF NOT EXISTS catalog_owned (
    game_id INTEGER PRIMARY KEY,
    filepath TEXT NOT NULL,
    matched_at TEXT
);
