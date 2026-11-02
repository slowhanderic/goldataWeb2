# legacy-php: changes to the running PHP app (Web Station)

These files go **on top of** the legacy site (base: `goldataWeb1/web_php_20261020`). They keep the old PHP app working while it shares one user list with GoldataWeb2 (REQ-CMP-8, spec §5.5) and close the P0 security holes (spec §11).

| File in `overlay/` | Change |
|---|---|
| `app_auth.php` | **New.** `check_user()` now reads users from `goldata_app.v_legacy_login` (bcrypt via `password_verify`), with lockout, login audit (`source = 'php'`), `app_auth_revalidate()` and the change-password banner. |
| `login.php` | The 22 hard-coded accounts **and their password hashes are removed**. `require_once app_auth.php`. `check_session()` calls `app_auth_revalidate()`. The navbar shows `app_auth_banner()`. Menu and `check_page_permission()` are unchanged. |
| `require_login.php` | **New.** Guard for endpoints that had no login check; also blocks `*查阅` roles from writes. |
| `crud_action.php` | Lookup add/edit/delete: login + 管理 + 总监 (same rule as the `lookup_tables` page). |
| `delete_file.php` | Login required, read-only roles blocked. |
| `dropdown/ajax/*.php` | Login required. |

The bridge's DB settings are **generated** from the environment's `.env` (the one settings file): `sudo ./goldata.sh php-config` writes `PHP_AUTH_CONFIG` (default `/volume1/web_config/goldata_auth.php`, outside the web root, owner `http`, mode 600) with `DB_PHP_AUTH_USER/PASSWORD`, `DB_APP_NAME`, `DB_PORT` and `PUBLIC_BASE_URL`.

## Deploy (runbook step 10)

1. Back up the site folder: `sudo tar czf /volume1/backup/php-site-$(date +%F).tgz -C <DOC_ROOT> .` (exclude `pdf_folder`).
2. Generate the config: `cd /volume1/docker/goldata && sudo ./goldata.sh php-config`.
3. Copy `overlay/*` over the site, keeping the paths (`dropdown/ajax/…`).
4. Delete the stale copies **`login2023.php`** (still has password hashes) and **`connectDB-81-230.php`**.
5. Deny direct downloads of `pdf_folder`: Apache 2.4 backend → `pdf_folder/.htaccess` containing `Require all denied` (needs `AllowOverride`). With the nginx backend, add a Web Station rule. Check that `https://<php site>/pdf_folder/…` returns **403**. Attachments are then served only through the new app.
6. Run the tests below.

## Tests (spec §5.5)

| Action | Expected |
|---|---|
| Log in with an existing account + its current password | Works. Banner 「请到新系统修改密码」 is shown |
| Wrong password ×5 (PHP and/or new app) | Locked 15 min in both. Admin page → 解锁 works for both |
| Admin page: add user → log in to PHP | Works with the initial password |
| Admin page: disable / reset password | Open PHP session ends within 60 s; old password refused |
| Admin page: change role/rank | PHP menu changes after ≤ 60 s |
| Admin page: delete user | PHP login refused |
| Stop MariaDB access for `goldata_php_auth` | 「登入服务暂不可用」, no PHP error page |
| `crud_action.php` / `dropdown/ajax/*.php` / `delete_file.php` without login | 401 「请先登入」 |
| `crud_action.php` as a non-管理总监 user | 403 「权限不足」 |

## Rollback

Restore the backed-up files (`login.php` first). No database change has to be undone. The old hard-coded accounts come back with their old passwords.

> ⚠ These files were checked by reading only: there is no PHP interpreter on the build machine. Run `php -l` on each file on the NAS (`/usr/local/bin/php80 -l <file>`) before going live.
