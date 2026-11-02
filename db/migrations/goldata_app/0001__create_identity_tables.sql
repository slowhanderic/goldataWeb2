-- Purpose : Identity, roles, ranks, permissions, tokens, login/admin audit and settings for GoldataWeb2.
-- Ref     : REQ-ROLE-1, REQ-ADM-1…14, 002-spec §5.1
-- Author  : GoldataWeb2 spec, 2026-11-02
-- Rollback: forward-only (drop the tables by hand on a fresh install only).

CREATE TABLE app_ranks (
  name VARCHAR(16) NOT NULL PRIMARY KEY,                -- 总监 / 员工
  sort INT NOT NULL DEFAULT 0
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE app_roles (
  id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  name VARCHAR(32) NOT NULL,                            -- immutable after create; no '-' (update_logs.user = '{role}-{name}')
  description VARCHAR(255) NULL,
  is_read_only TINYINT(1) NOT NULL DEFAULT 0,           -- replaces the legacy '*查阅' string test
  is_seeded TINYINT(1) NOT NULL DEFAULT 0,              -- legacy role → supports 恢复默认
  sort INT NOT NULL DEFAULT 0,
  row_version INT NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_app_roles_name (name),
  CONSTRAINT ck_app_roles_name CHECK (name NOT LIKE '%-%')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE app_users (
  id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  login VARCHAR(32) NOT NULL,                           -- legacy login, e.g. 'chendx'
  display_name VARCHAR(64) NOT NULL,                    -- must equal names stored in goldata (user_name, historys.person_to)
  role_id INT NOT NULL,
  `rank` VARCHAR(16) NOT NULL,
  password_hash VARCHAR(255) NOT NULL,                  -- bcrypt $2y$ cost 12 (PHP password_verify-compatible)
  must_change_password TINYINT(1) NOT NULL DEFAULT 1,
  is_active TINYINT(1) NOT NULL DEFAULT 1,
  failed_logins INT NOT NULL DEFAULT 0,
  locked_until DATETIME NULL,
  security_stamp CHAR(32) NOT NULL,                     -- rotated on disable / password reset / role change
  row_version INT NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by INT NULL,
  last_login_at DATETIME NULL,
  UNIQUE KEY uq_app_users_login (login),
  UNIQUE KEY uq_app_users_display_name (display_name),
  KEY ix_app_users_role (role_id),
  CONSTRAINT fk_app_users_role FOREIGN KEY (role_id) REFERENCES app_roles (id),
  CONSTRAINT fk_app_users_rank FOREIGN KEY (`rank`) REFERENCES app_ranks (name) ON UPDATE CASCADE,
  CONSTRAINT ck_app_users_login CHECK (login REGEXP '^[a-z0-9._-]{3,32}$')
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE app_role_permissions (
  role_id INT NOT NULL,
  `rank` VARCHAR(16) NOT NULL DEFAULT '*',              -- '*' = all ranks, else 总监 / 员工
  permission VARCHAR(64) NOT NULL,                      -- validated against PermissionCatalog in the API
  PRIMARY KEY (role_id, `rank`, permission),
  CONSTRAINT fk_app_role_permissions_role FOREIGN KEY (role_id) REFERENCES app_roles (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE app_user_permission_overrides (
  user_id INT NOT NULL,
  permission VARCHAR(64) NOT NULL,
  allow TINYINT(1) NOT NULL,                            -- 1 grant, 0 deny (deny wins)
  PRIMARY KEY (user_id, permission),
  CONSTRAINT fk_app_overrides_user FOREIGN KEY (user_id) REFERENCES app_users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE app_api_tokens (
  id INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  user_id INT NOT NULL,
  name VARCHAR(64) NULL,
  token_hash CHAR(64) NOT NULL,                         -- SHA-256 of token + pepper; the token itself is never stored
  scopes VARCHAR(255) NOT NULL,                         -- e.g. 'mcp:read mcp:write'
  expires_at DATETIME NOT NULL,
  last_used_at DATETIME NULL,
  revoked_at DATETIME NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE KEY uq_app_api_tokens_hash (token_hash),
  KEY ix_app_api_tokens_user (user_id),
  CONSTRAINT fk_app_api_tokens_user FOREIGN KEY (user_id) REFERENCES app_users (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE app_login_audit (
  id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  login VARCHAR(64) NULL,
  success TINYINT(1) NOT NULL,
  source VARCHAR(8) NOT NULL,                           -- web / php / mcp
  reason VARCHAR(32) NULL,                              -- bad_password, unknown_login, disabled, locked
  ip VARCHAR(45) NULL,
  at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  KEY ix_app_login_audit_login_at (login, at),
  KEY ix_app_login_audit_at (at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE app_admin_audit (
  id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  actor_user_id INT NULL,                               -- no FK: rows must survive user deletion
  actor_name VARCHAR(64) NOT NULL,
  action VARCHAR(48) NOT NULL,                          -- user.create, user.disable, user.delete, role.permissions, …
  target_type VARCHAR(16) NOT NULL,                     -- user / role / rank / token / setting
  target_id INT NULL,
  target_name VARCHAR(64) NULL,
  before_json LONGTEXT NULL,
  after_json LONGTEXT NULL,
  ip VARCHAR(45) NULL,
  at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  KEY ix_app_admin_audit_at (at),
  KEY ix_app_admin_audit_target (target_type, target_id),
  CONSTRAINT ck_app_admin_audit_before CHECK (before_json IS NULL OR JSON_VALID(before_json)),
  CONSTRAINT ck_app_admin_audit_after  CHECK (after_json  IS NULL OR JSON_VALID(after_json))
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE app_settings (
  `key` VARCHAR(64) NOT NULL PRIMARY KEY,               -- e.g. mcp.hr.enabled, perm.version
  value TEXT NULL,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
