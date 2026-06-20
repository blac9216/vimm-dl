-- Hash verification result per owned game: null = unchecked, 1 = CRC matches the catalog, 0 = mismatch.
-- Set by POST /api/catalog/verify. Additive + idempotent (migrator ignores "duplicate column").

ALTER TABLE catalog_owned ADD COLUMN verified INTEGER;
