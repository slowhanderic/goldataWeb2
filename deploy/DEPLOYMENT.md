# GoldataWeb2: deployment runbook (Synology DSM 7.1, Docker)

| | |
|---|---|
| Target | Synology NAS, **DSM 7.1**, Synology **Docker** package (docker-compose v1), MariaDB 10 package (port 3307), Web Station (legacy PHP) |
| Design | [`specs/002-spec.md` §9](../specs/002-spec.md) (deployment), §5.5 (PHP login bridge), §2.1 + [`db/README.md`](../db/README.md) (SQL changes) |
| Status | **Draft.** Steps marked **(P0)** are confirmed or adjusted during the P0 smoke test on the real NAS. `goldata.sh`, the images and the seed tool are built in P0 |
| Last updated | 2026-11-02 |

> **Keep this file current.** Any change that affects how the system is installed, configured, upgraded or rolled back must update this runbook in the **same commit**. That covers compose/env/secrets, images, ports, DB scripts in `db/ops`, the PHP patch, the reverse proxy, and backups. Add a line to the [change log](#change-log) at the bottom.

Placeholders used below: `<NAS_IP>` = `192.168.168.235` (Q3), `<DOMAIN>` = e.g. `erp.example.com` (Q14), `<DOC_ROOT>` = the folder that **contains** `pdf_folder` (e.g. `/volume1/web/goldata`, Q8), `<DOC_UID>:<DOC_GID>` = owner of the PHP files (from `id http`), `<php host>` = the address PHP connects to MariaDB from. **Never write real passwords into this file.**

---

## 0. Before you start (collect once)

Run over SSH on the NAS (`ssh <admin>@<NAS_IP>`) and record the answers in the [install record](#install-record):

```sh
cat /etc.defaults/VERSION | grep -E 'productversion|buildnumber'   # DSM 7.1.x
uname -m; uname -r                                                 # x86_64? kernel 4.4 / 5.10
sudo docker version; sudo docker-compose version                   # Docker 20.10.x, compose 1.x
id http                                                            # e.g. uid=1023(http) gid=1023(http) → DOC_UID/DOC_GID
ls -ld <DOC_ROOT>/pdf_folder; du -sh <DOC_ROOT>/pdf_folder         # path, owner, size
free -m; df -h /volume1                                            # RAM / disk
```

- [ ] DSM 7.1, x86_64 (else: build arm64 images too)
- [ ] ≥ 1 GB RAM free for containers, enough disk for images (~1 GB) + backups
- [ ] Host name `<DOMAIN>` resolves to `<NAS_IP>` on the LAN (router or local DNS)
- [ ] Certificate plan: Let's Encrypt (public DNS) or an internal CA certificate

## 1. NAS preparation (one-time)

1. **Package Center** → install **Docker** (and keep **MariaDB 10**, **Web Station** as they are).
2. **Control Panel ▸ Terminal & SNMP** → enable SSH (admins only; disable again after deploying if that is your policy).
3. **Control Panel ▸ Shared Folder** → make sure the `docker` share exists (Docker package creates it).
4. **MariaDB 10** package → *Enable TCP/IP connection* is on, port **3307**.
5. **Shared folder permissions** for the share holding `pdf_folder`: user `http` (= `<DOC_UID>`) has **Read/Write**. Docker containers are subject to Synology ACLs too.

## 2. Database: create `goldata_app` and accounts (manual, DB admin)

Scripts: [`db/ops/0001__create_goldata_app_and_accounts.sql`](../db/ops/0001__create_goldata_app_and_accounts.sql)

1. Generate three different strong passwords (API, migrator, PHP bridge) and keep them in your password manager.
2. Copy the script to the NAS, replace `<secret-api>`, `<secret-migrator>`, `<secret-php>` and `<php host>` **in the copy only**. Never commit the filled-in copy.
3. Run it as the MariaDB root user:
   ```sh
   /usr/local/mariadb10/bin/mysql -u root -p -h 127.0.0.1 -P 3307 < /tmp/0001.filled.sql   # (P0: confirm client path)
   shred -u /tmp/0001.filled.sql   # or: rm -P
   ```
4. Add a row to the *Applied log* in [`db/README.md`](../db/README.md).

## 3. DSM firewall

**Control Panel ▸ Security ▸ Firewall** (if enabled):
- Allow `172.30.10.0/24` → port **3307** (containers → MariaDB).
- Allow LAN → **443** (and 80 only if needed for Let's Encrypt).
- Do **not** allow anything → **8085** (the web container listens on `127.0.0.1` only).

## 4. Project folder

```sh
sudo mkdir -p /volume1/docker/goldata/{secrets,data/keys,data/logs}
cd /volume1/docker/goldata
# copy compose.yaml, goldata.sh and .env.example from the release (deploy/ folder)
sudo cp .env.example .env
sudo chmod 750 . && sudo chmod 700 goldata.sh
```

Edit `.env` (no passwords in it):

```ini
COMPOSE_PROJECT_NAME=goldata
REGISTRY=git.<your domain>          # or leave as-is when using docker load
API_TAG=1.0.0-<sha>
WEB_TAG=1.0.0-<sha>
DOC_ROOT=<DOC_ROOT>
DOC_UID=<DOC_UID>
DOC_GID=<DOC_GID>
```

Create the **secrets** (one value per file, no trailing newline). The API container runs as `<DOC_UID>`, so the files are **owned by root, group `<DOC_GID>`, mode 640**:

```sh
cd /volume1/docker/goldata/secrets
sudo sh -c 'umask 027; printf %s "Server=<NAS_IP>;Port=3307;Database=goldata;User ID=goldata_api;Password=<secret-api>;CharacterSet=utf8;ConvertZeroDateTime=true;TreatTinyAsBoolean=false;" > ConnectionStrings__Legacy'
sudo sh -c 'umask 027; printf %s "Server=<NAS_IP>;Port=3307;Database=goldata_app;User ID=goldata_api;Password=<secret-api>;CharacterSet=utf8mb4;" > ConnectionStrings__App'
sudo sh -c 'umask 027; printf %s "Server=<NAS_IP>;Port=3307;Database=goldata_app;User ID=goldata_migrator;Password=<secret-migrator>;CharacterSet=utf8mb4;" > ConnectionStrings__Migrator'
sudo sh -c 'umask 027; head -c 48 /dev/urandom | base64 > Auth__TokenPepper'
sudo chown root:<DOC_GID> * && sudo chmod 640 * && sudo chown root:<DOC_GID> . && sudo chmod 750 .
sudo chown -R <DOC_UID>:<DOC_GID> ../data && sudo chmod -R 750 ../data
```

> Type the commands interactively; afterwards clear the shell history (`history -c`) so passwords don't stay in `~/.bash_history`.

## 5. Get the images

Option A, self-hosted registry (Gitea):
```sh
sudo docker login git.<your domain>          # read-only token
sudo ./goldata.sh pull
```
Option B, offline:
```sh
# on the build machine: docker save goldata-api:<tag> goldata-web:<tag> | gzip > goldata-<tag>.tar.gz
sudo docker load -i goldata-<tag>.tar.gz
```

## 6. Migrate the database

```sh
sudo ./goldata.sh migrate        # = docker-compose run --rm api --migrate
```
Applies `db/migrations/goldata_app/*` (and `db/migrations/goldata/*` if any), recorded in `goldata_app.schemaversions`. Then run the PHP bridge grants **by hand** as in step 2:

- [`db/ops/0002__grant_php_login_bridge.sql`](../db/ops/0002__grant_php_login_bridge.sql) (replace `<php host>`). Log it in `db/README.md`.

## 7. Import the legacy users (one-time)

```sh
# login.php is copied to the NAS temporarily, read-only, and deleted afterwards
sudo ./goldata.sh import-users /volume1/docker/goldata/import/login.php    # (P0: seed tool)
sudo shred -u /volume1/docker/goldata/import/login.php
```
Imports the 22 accounts with their **existing bcrypt hashes** (passwords unchanged, `must_change_password = 1`), maps them to the seeded roles and ranks, and seeds the default permission matrix.

## 8. Start

```sh
sudo ./goldata.sh up             # = docker-compose up -d, then waits for /health/ready
sudo docker-compose ps           # both containers "Up (healthy)"
curl -s http://127.0.0.1:8085/api/v1/health
```

## 9. DSM reverse proxy and certificate

1. **Control Panel ▸ Security ▸ Certificate**: add a Let's Encrypt certificate for `<DOMAIN>` or import the internal-CA certificate. Assign it to the reverse proxy entry after step 2.
2. **Control Panel ▸ Login Portal ▸ Advanced ▸ Reverse Proxy ▸ Create**:
   | Field | Value |
   |---|---|
   | Source | HTTPS, host `<DOMAIN>`, port 443, HSTS on |
   | Destination | HTTP, `localhost`, port **8085** |
   | Custom header | *Create ▸ WebSocket* (adds Upgrade/Connection), plus `X-Forwarded-Proto: $scheme` and `X-Forwarded-For: $proxy_add_x_forwarded_for` |
   | Advanced settings | Proxy read/send timeout **3600 s** (needed for `/mcp` streaming) **(P0: confirm max value in DSM 7.1)** |
3. Open `https://<DOMAIN>` → the login page should appear.

## 10. Legacy PHP changes (same day as step 7)

On the Web Station site (keep a backup copy of every file you replace):

1. **Config outside the web root:** create `/volume1/web_config/goldata_auth.php` (owner `http`, mode 600) holding the `goldata_php_auth` connection settings.
2. Deploy `app_auth.php` and the patched `login.php` (§5.5 of the spec).
3. **Delete** the stale copies `login2023.php` and `connectDB-81-230.php` (they contain hashes and passwords).
4. **Security hot-fixes** (spec §11): `check_session()` in `crud_action.php`, `dropdown/ajax/*.php`, `delete_file.php`. Deny direct web access to `pdf_folder` (Apache: `pdf_folder/.htaccess` with `Require all denied`).
5. **Rotate the legacy DB password** used by `connectDB.php` / `DBController.php`, and update both files.
6. **Rollback:** restore the backed-up `login.php` (no DB change to undo).

## 11. Smoke test (sign off before announcing)

- [ ] Log in on `https://<DOMAIN>` with 3 accounts: 管理总监, 财务员工, 采购查阅. Forced password change works.
- [ ] Same 3 accounts log in to the PHP app, and the change-password banner appears for those who haven't changed yet.
- [ ] Admin page: add a test user → can log in to both apps. Disable → PHP session ends within 60 s. Delete it.
- [ ] Open a contract with attachments: list and download work. Upload a 50–80 MB file with a Chinese name, then open and delete it in PHP.
- [ ] `https://<DOMAIN>/pdf_folder/...` (PHP site) returns **403**.
- [ ] MCP: connect a client to `https://<DOMAIN>/mcp` with a personal token and run `search_records`.
- [ ] Restart the NAS (or `sudo docker restart`): the containers come back healthy.

## 12. Scheduled tasks and backups

**Control Panel ▸ Task Scheduler ▸ Create ▸ Scheduled Task ▸ User-defined script**, user **root**:

| Task | Schedule | Command |
|---|---|---|
| Health check | every 5 min | `/volume1/docker/goldata/goldata.sh check` (mails a DSM notification after 3 failures) |
| DB dump | daily 01:30 | `/volume1/docker/goldata/goldata.sh backup-db /volume1/backup/goldata` (`mysqldump --single-transaction` of `goldata` + `goldata_app`, keeps 30 days) |

- **Snapshot Replication**: share holding `pdf_folder`, hourly, keep 30 days.
- **Hyper Backup**: `/volume1/backup/goldata`, `pdf_folder`'s share and `/volume1/docker/goldata` → offsite target, nightly.
- **Monthly**: restore test into a scratch DB and project (record it in the install record).

## 13. Upgrade

```sh
cd /volume1/docker/goldata
sudo ./goldata.sh upgrade 1.1.0-<sha>   # saves .env → .env.prev, pulls, migrates, up -d, waits for health
```
- Read the release notes first: any **new `db/ops` script** must be run by hand **before** the upgrade, and any PHP file changes applied as listed.
- Migrations are additive, so the previous version keeps working against the upgraded DB.

## 14. Rollback

```sh
sudo ./goldata.sh rollback              # restores .env.prev and recreates the containers
```
DB migrations are not reversed (forward-only). If a migration itself must be undone, follow its `Rollback:` header note.

## 15. Troubleshooting

| Symptom | Likely cause → fix |
|---|---|
| API exits with `Failed to create thread` / `pthread_create … EPERM` | Old Docker seccomp + glibc image. Use the Alpine image, or as a last resort `security_opt: seccomp=./seccomp-dsm71.json` (spec §9.0) |
| `Access denied for user 'goldata_api'@'172.30.10.x'` | Grant host mismatch (step 2) or the compose subnet changed. Check `docker network inspect goldata_goldata` |
| `Unable to connect` to 3307 from container | DSM firewall (step 3) or MariaDB TCP disabled (step 1.4) |
| Upload fails with `Permission denied` | `DOC_UID/GID` doesn't match `id http`, or the share ACL lacks Read/Write for `http` (step 1.5) |
| Upload fails with **413** | DSM reverse proxy or nginx body limit. The web image allows 85 MB. Check the DSM proxy (P0) |
| `/mcp` disconnects after ~60 s | DSM reverse proxy timeouts (step 9.2) |
| `secrets` not found / config empty | Secrets file mode or group (step 4); compose v1 mounts them from `./secrets` |
| `ERROR: … Additional properties are not allowed ('name' …)` | compose v1 doesn't accept top-level `name:`; use `COMPOSE_PROJECT_NAME` in `.env` |
| Garbled Chinese file names | NFD names from macOS SMB clients. The API matches both NFC and NFD; report the file if it still fails |

---

## Install record

| Item | Value |
|---|---|
| DSM version / model / RAM | |
| Docker / docker-compose / kernel | |
| `DOC_ROOT`, `DOC_UID:DOC_GID` | |
| `<php host>` | |
| `<DOMAIN>`, certificate | |
| First install date / by | |
| Last restore test | |

## Change log

| Date | Change |
|---|---|
| 2026-11-02 | First version: DSM 7.1 / Docker package, `db/` scripts ops 0001–0002 + migrations 0001–0003, PHP login bridge, reverse proxy, backups. |
