-- Phase C (C1): source-agnostic pipeline identity. Add the catalog game_id (+ download format /
-- source on events) to the event log and the completed_urls projection, so downstream consumers can
-- key on the catalog game instead of the filename. Additive + idempotent + null-safe: no consumer
-- reads these columns yet (the cutover is C2), so behavior is unchanged this slice.

-- events: carry the resolved catalog game identity alongside the legacy item_name (filename).
ALTER TABLE events ADD COLUMN game_id INTEGER;
ALTER TABLE events ADD COLUMN format INTEGER;
ALTER TABLE events ADD COLUMN source TEXT;

-- completed_urls already has source/source_id/format; add the resolved catalog game.
ALTER TABLE completed_urls ADD COLUMN game_id INTEGER;

-- Best-effort backfill: Vimm completions carry source='vimm' and source_id = the vault URL
-- (https://vimm.net/vault/{vault_id}), which maps directly onto catalog_game.vault_id. Archive
-- completions can't be resolved without a hash here, so they stay NULL (legacy, still queryable).
UPDATE completed_urls
SET game_id = (
    SELECT g.id FROM catalog_game g
    WHERE g.vault_id IS NOT NULL
      AND completed_urls.source_id = 'https://vimm.net/vault/' || g.vault_id
    -- vault_id isn't UNIQUE; if one vault entry binds several games, pick deterministically (1G1R
    -- parent first, then lowest id) so this backfill agrees with QueueRepository.ResolveGameIdAsync.
    ORDER BY g.is_parent DESC, g.id
    LIMIT 1
)
WHERE game_id IS NULL
  AND source = 'vimm'
  AND source_id LIKE 'https://vimm.net/vault/%';

-- Grouped lookups by catalog game + format.
CREATE INDEX IF NOT EXISTS idx_events_game ON events(game_id, format);
CREATE INDEX IF NOT EXISTS idx_completed_game ON completed_urls(game_id);
