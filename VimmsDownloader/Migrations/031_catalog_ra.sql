-- RetroAchievements popularity (epic #123 / R2). ra_players = NumDistinctPlayers from RetroAchievements,
-- hash-joined (RA ROM MD5 == catalog MD5) for cartridge/handheld consoles whose RA hash is a full-file
-- MD5. Blended into rank_score alongside the IGDB quality signal (see Module.Catalog/RankScore.Blend).
-- Nullable — only set for hash-matched games on RA-supported consoles; everything else stays IGDB-only
-- (or unranked). Additive + idempotent.

ALTER TABLE catalog_game ADD COLUMN ra_players INTEGER;
