-- Per-emulator console gating for name-keyed compat (#135 / #209). Name keys (game titles) collide
-- across consoles, so a name-keyed emulator's rows carry the consoles it targets (CSV, e.g. "gc,wii")
-- and the compat join requires the game's console to be among them. Serial-keyed rows leave it NULL
-- (serials are globally unique — no gating). The next compat sync repopulates it; until then, existing
-- name-keyed rows (Dolphin) won't match (NULL consoles), so their badges return after a re-sync.
-- Additive + idempotent.

ALTER TABLE catalog_compat ADD COLUMN consoles TEXT;
