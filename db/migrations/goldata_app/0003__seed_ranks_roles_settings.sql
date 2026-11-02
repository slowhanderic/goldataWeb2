-- Purpose : Seed the two legacy ranks, the 10 legacy roles and the initial settings.
--           Users (and their bcrypt hashes) are NOT seeded here: they are imported from login.php by the seed tool,
--           so no credentials ever land in this folder. The default permission matrix is seeded by a later
--           script generated from PermissionCatalog (002-spec §5.2).
-- Ref     : 001 §3, REQ-ADM-9/11/12, 002-spec §5.1–5.2
-- Author  : GoldataWeb2 spec, 2026-11-02
-- Rollback: forward-only.

INSERT INTO app_ranks (name, sort) VALUES
  ('总监', 1),
  ('员工', 2);

INSERT INTO app_roles (name, description, is_read_only, is_seeded, sort) VALUES
  ('管理',     '管理层',                 0, 1, 1),
  ('业务',     '业务部',                 0, 1, 2),
  ('财务',     '财务部',                 0, 1, 3),
  ('采购',     '采购部',                 0, 1, 4),
  ('预算',     '预算部',                 0, 1, 5),
  ('家装',     '家装部',                 0, 1, 6),
  ('工程文员', '工程部文员',             0, 1, 7),
  ('采购查阅', '采购部只读（Q12）',      1, 1, 8),
  ('工程查阅', '工程部只读（Q12）',      1, 1, 9),
  ('预算查阅', '预算部只读（Q12）',      1, 1, 10);

INSERT INTO app_settings (`key`, value) VALUES
  ('perm.version',          '1'),
  ('mcp.hr.enabled',        'false'),
  ('auth.lockout.attempts', '5'),
  ('auth.lockout.minutes',  '15'),
  ('session.idle.minutes',  '480');
