-- Catalog game descriptions (epic #122 / M2). A free-text description per game, populated by the IGDB
-- sync (POST /api/catalog/igdb-sync) and joined to the catalog by normalized name + platform. Lives on
-- catalog_game so it survives a DAT re-sync (rows merge by canonical key). Additive + idempotent.

ALTER TABLE catalog_game ADD COLUMN description TEXT;
