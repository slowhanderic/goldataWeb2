-- Purpose : Read-only account for the nightly `goldata.sh backup-db` dump (DSM Task Scheduler, runbook step 12).
-- Ref     : NFR-AVL-1, NFR-DEP-7, 002-spec §9.7
-- Author  : GoldataWeb2, 2026-11-02
-- Rollback: DROP USER 'goldata_backup'@'localhost';
--
-- Replace <secret-backup>. The dump runs on the NAS itself (MariaDB 10 client), so the account is @'localhost'
-- (use @'127.0.0.1' if the client connects over TCP — goldata.sh uses -h 127.0.0.1, so create both or check in P0).
-- The password goes in the prod .env as DB_BACKUP_PASSWORD (goldata.sh hands it to mysqldump in a temporary
-- root-only option file, never on the command line).

CREATE USER IF NOT EXISTS 'goldata_backup'@'localhost' IDENTIFIED BY '<secret-backup>';
CREATE USER IF NOT EXISTS 'goldata_backup'@'127.0.0.1' IDENTIFIED BY '<secret-backup>';
GRANT SELECT, LOCK TABLES, SHOW VIEW, TRIGGER, EVENT ON goldata.*     TO 'goldata_backup'@'localhost', 'goldata_backup'@'127.0.0.1';
GRANT SELECT, LOCK TABLES, SHOW VIEW, TRIGGER, EVENT ON goldata_app.* TO 'goldata_backup'@'localhost', 'goldata_backup'@'127.0.0.1';

FLUSH PRIVILEGES;
