-- Sets become {name, console, links[]} (RomGoGetter-style): a set is a named, per-console list of
-- archive.org links instead of a single identifier. Add a name column + a child link table,
-- migrate each existing one-identifier set to a single archive.org link, and drop the old
-- (console, source, identifier) unique index so the new name/console sets aren't constrained by it.
-- Additive + idempotent; the old source/identifier/label columns are left in place but unused.

CREATE TABLE IF NOT EXISTS catalog_set_link (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    set_id INTEGER NOT NULL,
    url TEXT NOT NULL,
    position INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS idx_catalog_set_link_set ON catalog_set_link(set_id);

ALTER TABLE catalog_set ADD COLUMN name TEXT;

UPDATE catalog_set SET name = COALESCE(NULLIF(label, ''), identifier) WHERE name IS NULL;

INSERT INTO catalog_set_link (set_id, url, position)
SELECT id, 'https://archive.org/download/' || identifier, 0 FROM catalog_set
WHERE identifier IS NOT NULL AND identifier != ''
  AND NOT EXISTS (SELECT 1 FROM catalog_set_link l WHERE l.set_id = catalog_set.id);

DROP INDEX IF EXISTS idx_catalog_set_unique;
