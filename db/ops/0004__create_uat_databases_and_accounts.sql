-- Purpose : UAT environment on the same MariaDB as prod: its own databases and accounts, with NO rights on the
--           prod databases (goldata, goldata_app). Matches deploy/env/uat.env.example.
-- Ref     : NFR-DEP-11, deploy/DEPLOYMENT.md "UAT", 002-spec §9.10
-- Author  : GoldataWeb2, 2026-11-02
-- Rollback: DROP USER the five goldata_uat_* accounts; DROP DATABASE goldata_uat, goldata_app_uat.
--
-- Replace the <secret-uat-*> placeholders (= DB_*_PASSWORD in the UAT .env) and <php host> in a COPY of this file.
-- 172.30.11.% = NETWORK_SUBNET of the UAT .env. The UAT data is a copy of prod (see the refresh steps in the runbook).

CREATE DATABASE IF NOT EXISTS goldata_uat     CHARACTER SET utf8 COLLATE utf8_general_ci;
CREATE DATABASE IF NOT EXISTS goldata_app_uat CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE USER IF NOT EXISTS 'goldata_uat_api'@'172.30.11.%' IDENTIFIED BY '<secret-uat-api>';
GRANT SELECT, INSERT, UPDATE, DELETE ON goldata_uat.*     TO 'goldata_uat_api'@'172.30.11.%';
GRANT SELECT, INSERT, UPDATE, DELETE ON goldata_app_uat.* TO 'goldata_uat_api'@'172.30.11.%';

CREATE USER IF NOT EXISTS 'goldata_uat_migrator'@'172.30.11.%' IDENTIFIED BY '<secret-uat-migrator>';
GRANT ALL PRIVILEGES ON goldata_app_uat.* TO 'goldata_uat_migrator'@'172.30.11.%';

CREATE USER IF NOT EXISTS 'goldata_uat_backup'@'localhost' IDENTIFIED BY '<secret-uat-backup>';
CREATE USER IF NOT EXISTS 'goldata_uat_backup'@'127.0.0.1' IDENTIFIED BY '<secret-uat-backup>';
GRANT SELECT, LOCK TABLES, SHOW VIEW, TRIGGER, EVENT ON goldata_uat.*     TO 'goldata_uat_backup'@'localhost', 'goldata_uat_backup'@'127.0.0.1';
GRANT SELECT, LOCK TABLES, SHOW VIEW, TRIGGER, EVENT ON goldata_app_uat.* TO 'goldata_uat_backup'@'localhost', 'goldata_uat_backup'@'127.0.0.1';

-- PHP login bridge for a UAT copy of the PHP site (optional). Grants on the view need `goldata.sh migrate` first:
CREATE USER IF NOT EXISTS 'goldata_uat_php_auth'@'<php host>' IDENTIFIED BY '<secret-uat-php>';
-- After the UAT migration, run:
-- GRANT SELECT ON goldata_app_uat.v_legacy_login TO 'goldata_uat_php_auth'@'<php host>';
-- GRANT SELECT (id, login), UPDATE (failed_logins, locked_until, last_login_at) ON goldata_app_uat.app_users TO 'goldata_uat_php_auth'@'<php host>';
-- GRANT INSERT ON goldata_app_uat.app_login_audit TO 'goldata_uat_php_auth'@'<php host>';

FLUSH PRIVILEGES;
