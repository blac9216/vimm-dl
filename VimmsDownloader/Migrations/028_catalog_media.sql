-- Catalog media cache (epic #122 / M1). One row per (game, image type) recording the on-disk cache of
-- a libretro-thumbnails image (box art / title screen), or a negative-cache 'missing' marker so a 404
-- isn't re-fetched on every view. Lazily populated by GET /api/catalog/games/{id}/image. The cached
-- bytes live under data/media/; this table is just the index + hit/miss record. Additive + idempotent.

CREATE TABLE IF NOT EXISTS catalog_media (
    game_id INTEGER NOT NULL,
    type TEXT NOT NULL,        -- 'boxart' | 'title'
    source TEXT NOT NULL,      -- 'libretro'
    status TEXT NOT NULL,      -- 'ok' (cached on disk) | 'missing' (negative-cached 404)
    path TEXT,                 -- cached file path when status = 'ok'
    fetched_at TEXT,
    PRIMARY KEY (game_id, type)
);
