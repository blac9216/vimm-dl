-- Phase 2b: metadata cache keyed by (source, source_id) instead of the vault url.
-- Additive: a new table, populated from existing url_meta as source='vimm'.
-- url_meta is intentionally kept (not dropped) this release as a rollback fallback.
-- Idempotent: CREATE IF NOT EXISTS + INSERT OR IGNORE on the (source, source_id) PK.

CREATE TABLE IF NOT EXISTS source_meta (
    source TEXT NOT NULL,
    source_id TEXT NOT NULL,
    title TEXT NOT NULL,
    platform TEXT NOT NULL,
    size TEXT NOT NULL,
    formats TEXT,
    serial TEXT,
    PRIMARY KEY (source, source_id)
);

-- Copy the existing Vimm metadata cache across, keyed by its vault url.
INSERT OR IGNORE INTO source_meta (source, source_id, title, platform, size, formats, serial)
    SELECT 'vimm', url, title, platform, size, formats, serial FROM url_meta;
