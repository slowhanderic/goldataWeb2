-- Purpose : Read-only view the PHP login bridge uses to authenticate against goldata_app
--           (exposes only what PHP's check_user()/check_session() need).
-- Ref     : REQ-CMP-8, 002-spec §5.5
-- Author  : GoldataWeb2 spec, 2026-11-02
-- Rollback: DROP VIEW v_legacy_login;  (PHP login then fails until login.php is reverted)

CREATE OR REPLACE SQL SECURITY DEFINER VIEW v_legacy_login AS
SELECT u.id,
       u.login,
       u.display_name,
       r.name AS role,
       u.`rank`,
       u.password_hash,
       u.is_active,
       u.locked_until,
       u.failed_logins,
       u.must_change_password,
       u.security_stamp
FROM app_users u
JOIN app_roles r ON r.id = u.role_id;
