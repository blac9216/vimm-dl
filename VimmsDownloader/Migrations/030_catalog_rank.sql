-- Game ranking (epic #123 / R1). IGDB quality signals + a materialized rank_score the Library can sort
-- by ("best games" first). igdb_rating is IGDB's total_rating (0-100, a blend of user + critic scores);
-- igdb_rating_count is its total_rating_count (vote volume / confidence). rank_score is a Bayesian-
-- weighted rating derived from those (see Module.Catalog/RankScore) so a 1-vote 100 doesn't top the
-- list; R2 later blends RetroAchievements popularity into it. All nullable — an unranked game (no IGDB
-- match) sorts last. Lives on catalog_game so it survives a DAT re-sync (rows merge by canonical key).
-- Additive + idempotent.

ALTER TABLE catalog_game ADD COLUMN igdb_rating REAL;
ALTER TABLE catalog_game ADD COLUMN igdb_rating_count INTEGER;
ALTER TABLE catalog_game ADD COLUMN rank_score REAL;

CREATE INDEX IF NOT EXISTS idx_catalog_game_rank ON catalog_game(rank_score);
