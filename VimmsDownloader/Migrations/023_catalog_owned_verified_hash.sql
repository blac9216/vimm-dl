-- Phase C (C3): hash-based owned detection. Record which hash (sha1/md5/crc) confirmed a game as
-- owned, so the verify pass can report it. Additive + idempotent; NULL for owned rows not yet
-- hash-confirmed (e.g. name-scanned but unverified, or archives we can't hash without extracting).

ALTER TABLE catalog_owned ADD COLUMN verified_hash TEXT;
