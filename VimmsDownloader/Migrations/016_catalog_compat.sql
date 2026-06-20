-- Per-emulator game compatibility (e.g. RPCS3 Playable/Ingame/…), keyed by normalized serial.
-- Populated by POST /api/catalog/compat/sync; replaced per emulator. Additive + idempotent.

CREATE TABLE IF NOT EXISTS catalog_compat (
    emulator TEXT NOT NULL,
    serial_key TEXT NOT NULL,
    status TEXT NOT NULL,
    PRIMARY KEY (emulator, serial_key)
);

CREATE INDEX IF NOT EXISTS idx_catalog_compat_serial ON catalog_compat(serial_key);
