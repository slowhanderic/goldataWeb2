-- Purpose : Allow 2-character logins: the legacy account `ci` (login.php) failed ck_app_users_login ({3,32}) on import.
-- Ref     : REQ-CMP-7, REQ-ADM-2, 002-spec §5.1
-- Author  : GoldataWeb2, 2026-11-02
-- Rollback: forward-only (re-adding {3,32} would reject the existing `ci` row).

ALTER TABLE app_users
  DROP CONSTRAINT ck_app_users_login,
  ADD CONSTRAINT ck_app_users_login CHECK (login REGEXP '^[a-z0-9._-]{2,32}$');
