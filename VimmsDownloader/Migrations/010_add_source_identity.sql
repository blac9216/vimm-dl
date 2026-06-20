-- Phase 2a: introduce (source, source_id) item identity alongside the legacy url.
-- Additive + backfilled to 'vimm' so existing rows and the running app keep working.
-- Idempotent: re-running is safe (DatabaseMigrator ignores "duplicate column" /
-- "already exists", and the UPDATEs are guarded by source_id IS NULL).

-- queued_urls: which source the item came from + the source-specific id
ALTER TABLE queued_urls ADD COLUMN source TEXT NOT NULL DEFAULT 'vimm';
ALTER TABLE queued_urls ADD COLUMN source_id TEXT;

-- completed_urls: same identity carried onto the completion record
ALTER TABLE completed_urls ADD COLUMN source TEXT NOT NULL DEFAULT 'vimm';
ALTER TABLE completed_urls ADD COLUMN source_id TEXT;

-- Backfill the source id from the existing vault url for legacy rows
UPDATE queued_urls SET source_id = url WHERE source_id IS NULL;
UPDATE completed_urls SET source_id = url WHERE source_id IS NULL;

-- Lookups by (source, source_id)
CREATE INDEX IF NOT EXISTS idx_queued_source ON queued_urls(source, source_id);
CREATE INDEX IF NOT EXISTS idx_completed_source ON completed_urls(source, source_id);
