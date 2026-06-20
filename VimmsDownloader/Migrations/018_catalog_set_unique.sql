-- Catalog sets hardening (#40): one row per (console, source, identifier). Normalize existing
-- source casing, drop any duplicates created before this constraint (keeping the lowest id), then
-- enforce uniqueness so AddSetAsync can upsert. Additive + idempotent.

UPDATE catalog_set SET source = LOWER(source);

DELETE FROM catalog_set WHERE id NOT IN (SELECT MIN(id) FROM catalog_set GROUP BY console, source, identifier);

CREATE UNIQUE INDEX IF NOT EXISTS idx_catalog_set_unique ON catalog_set(console, source, identifier);
