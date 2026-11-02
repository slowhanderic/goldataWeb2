-- Purpose : Create the new goldata_app database and the DB accounts used by GoldataWeb2
--           (API runtime, migrations, PHP login bridge). Run once per server, as a DB admin.
-- Ref     : REQ-CMP-1, NFR-SEC-6, 002-spec §9.5 and §5.5
-- Author  : GoldataWeb2 spec, 2026-11-02
-- Rollback: DROP USER the three accounts; DROP DATABASE goldata_app (only if nothing else has used it yet).
--
-- Before running, replace:
--   <secret-api> / <secret-migrator> / <secret-php>  → three different strong passwords (store them in the NAS secrets files, NOT here)
--   <php host>                                        → the address the Web Station PHP connects from ('localhost' if it uses the socket; check in P0)
-- 172.30.10.% is the fixed Docker compose subnet (compose.yaml `networks.goldata`).

CREATE DATABASE IF NOT EXISTS goldata_app CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- 1. Runtime account for the API container: data only (DML), no DDL.
CREATE USER IF NOT EXISTS 'goldata_api'@'172.30.10.%' IDENTIFIED BY '<secret-api>';
GRANT SELECT, INSERT, UPDATE, DELETE ON goldata.*     TO 'goldata_api'@'172.30.10.%';
GRANT SELECT, INSERT, UPDATE, DELETE ON goldata_app.* TO 'goldata_api'@'172.30.10.%';

-- 2. Migration account (used only by `goldata.sh migrate`): DDL on goldata_app.
--    Grants on `goldata` are added per script, only when a migrations/goldata script needs one (e.g. INDEX on one table).
CREATE USER IF NOT EXISTS 'goldata_migrator'@'172.30.10.%' IDENTIFIED BY '<secret-migrator>';
GRANT ALL PRIVILEGES ON goldata_app.* TO 'goldata_migrator'@'172.30.10.%';

-- 3. PHP login bridge account: created here; its grants on the view/table come in ops/0002,
--    after migrations 0001–0002 have created those objects.
CREATE USER IF NOT EXISTS 'goldata_php_auth'@'<php host>' IDENTIFIED BY '<secret-php>';

FLUSH PRIVILEGES;
