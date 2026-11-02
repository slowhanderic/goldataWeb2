-- Purpose : Least-privilege grants for the PHP login bridge (PHP reads users from goldata_app).
--           Run AFTER `goldata.sh migrate` has applied migrations/goldata_app 0001 and 0002.
-- Ref     : REQ-CMP-8, 002-spec §5.5
-- Author  : GoldataWeb2 spec, 2026-11-02
-- Rollback: REVOKE ALL PRIVILEGES, GRANT OPTION FROM 'goldata_php_auth'@'<php host>';
--
-- Replace <php host> with the same value used in ops/0001.

GRANT SELECT ON goldata_app.v_legacy_login TO 'goldata_php_auth'@'<php host>';

-- Lockout counters + last login only (column-level). SELECT on id/login is needed for the WHERE clause.
GRANT SELECT (id, login), UPDATE (failed_logins, locked_until, last_login_at)
  ON goldata_app.app_users TO 'goldata_php_auth'@'<php host>';

GRANT INSERT ON goldata_app.app_login_audit TO 'goldata_php_auth'@'<php host>';

FLUSH PRIVILEGES;
