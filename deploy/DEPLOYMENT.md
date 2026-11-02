# GoldataWeb2: deployment runbook (Synology DSM 7.1, Docker)

| | |
|---|---|
| Target | Synology NAS, **DSM 7.1**, Synology **Docker** package (docker-compose v1), MariaDB 10 package (port 3307), Web Station (legacy PHP) |
| Design | [`specs/002-spec.md` §9](../specs/002-spec.md) (deployment), §5.5 (PHP login bridge), §2.1 + [`db/README.md`](../db/README.md) (SQL changes) |
| Status | **Draft, artefacts built (P0 code).** `deploy/goldata.sh`, `compose.yaml`, both Dockerfiles, the importer (`--import-users`) and `legacy-php/overlay` exist. Steps marked **(P0)** are still to be confirmed on the real NAS |
| Last updated | 2026-11-02 |

> **Keep this file current.** Any change that affects how the system is installed, configured, upgraded or rolled back must update this runbook in the **same commit**. That covers compose/env/secrets, images, ports, DB scripts in `db/ops`, the PHP patch, the reverse proxy, and backups. Add a line to the [change log](#change-log) at the bottom.

> **Settings: ONE env file per environment.** Everything (images, ports, database, documents, secrets, PHP bridge, backups) is in `.env` in the project folder, copied from [`deploy/env/prod.env.example`](env/prod.env.example) or [`uat.env.example`](env/uat.env.example). Local debug uses [`deploy/env/local.env.example`](env/local.env.example) → `deploy/env/local.env`. Docker-compose, `goldata.sh` and the API all read the same file. The PHP bridge config is **generated** from it (`goldata.sh php-config`). The file holds secrets: `root:root`, mode 600, never committed.

Placeholders used below: `<NAS_IP>` = `192.168.168.235` (production NAS + MariaDB; `192.168.21.175` is the local/test server), `<DOMAIN>` = e.g. `erp.example.com` (Q14), `<DOC_ROOT>` = the folder that **contains** `pdf_folder` (expected `/volume1/web`, i.e. `pdf_folder` at the root of the `web` share as on the test server; confirm on `.235` in step 0), `<DOC_UID>:<DOC_GID>` = owner of the PHP files (from `id http`), `<php host>` = the address PHP connects to MariaDB from. **Never write real passwords into this file.**

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

Scripts: [`db/ops/0001__create_goldata_app_and_accounts.sql`](../db/ops/0001__create_goldata_app_and_accounts.sql) and [`db/ops/0003__create_backup_account.sql`](../db/ops/0003__create_backup_account.sql) (read-only account for the nightly dump)

1. Generate four different strong passwords (API, migrator, PHP bridge, backup), using letters, digits and `- _ .` only (e.g. `openssl rand -base64 33 | tr '+/' '-_' | tr -d '='`). They go into the `.env` in step 4 (`DB_API_PASSWORD`, `DB_MIGRATOR_PASSWORD`, `DB_PHP_AUTH_PASSWORD`, `DB_BACKUP_PASSWORD`).
2. Copy the scripts to the NAS, replace `<secret-api>`, `<secret-migrator>`, `<secret-php>`, `<secret-backup>` and `<php host>` **in the copies only**. Never commit the filled-in copies.
3. Run it as the MariaDB root user:
   ```sh
   /usr/local/mariadb10/bin/mysql -u root -p -h 127.0.0.1 -P 3307 < /tmp/0001.filled.sql   # (P0: confirm client path)
   /usr/local/mariadb10/bin/mysql -u root -p -h 127.0.0.1 -P 3307 < /tmp/0003.filled.sql
   shred -u /tmp/0001.filled.sql /tmp/0003.filled.sql   # or: rm -P
   ```
4. Add a row to the *Applied log* in [`db/README.md`](../db/README.md).

## 3. DSM firewall

**Control Panel ▸ Security ▸ Firewall** (if enabled):
- Allow `172.30.10.0/24` → port **3307** (containers → MariaDB).
- Allow LAN → **443** (and 80 only if needed for Let's Encrypt).
- Do **not** allow anything → **8085** (the web container listens on `127.0.0.1` only).

## 4. Project folder and the env file

```sh
sudo mkdir -p /volume1/docker/goldata/data/{keys,logs}
cd /volume1/docker/goldata
# copy compose.yaml, goldata.sh, env/prod.env.example (and seccomp-dsm71.json, only needed as fallback) from the release's deploy/ folder
sudo cp prod.env.example .env
sudo chown root:root .env && sudo chmod 600 .env
sudo chmod 750 . && sudo chmod 700 goldata.sh
sudo chown -R <DOC_UID>:<DOC_GID> data && sudo chmod -R 750 data
```

Edit **`.env`**, the only settings file (`sudo vi .env`). Fill in at least:

| Key | Value |
|---|---|
| `REGISTRY`, `API_TAG`, `WEB_TAG` | registry host and the release tag |
| `WEB_PORT`, `NETWORK_SUBNET` | `8085`, `172.30.10.0/24` (must match the grants of step 2) |
| `PUBLIC_BASE_URL` | `https://<DOMAIN>` |
| `DB_HOST`, `DB_PORT` | `<NAS_IP>`, `3307` |
| `DB_*_PASSWORD` | the four passwords from step 2 |
| `DOC_ROOT`, `DOC_UID`, `DOC_GID` | from step 0 |
| `TOKEN_PEPPER` | `head -c 36 /dev/urandom \| base64 \| tr '+/' '-_' \| tr -d '='` |

Format rules (docker-compose v1 reads the file literally): `KEY=value`, **no quotes, no spaces, comments only on their own line**. Check it with `sudo ./goldata.sh env`.

> Edit with an editor, not with `echo …` on the command line, so passwords don't end up in `~/.bash_history`.

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
# copy the CURRENT login.php (before the step-10 patch) to the NAS temporarily, then delete it
sudo mkdir -p import && sudo cp <DOC_ROOT>/login.php import/login.php
sudo ./goldata.sh import-users ./import/login.php      # = docker-compose run --rm -v ./import:/import:ro api --import-users /import/login.php
sudo shred -u import/login.php
```
Imports the 22 accounts with their **existing bcrypt hashes** (passwords unchanged, `must_change_password = 1`) and maps them to the seeded roles and ranks. Re-running skips accounts that already exist. The default permission matrix was already seeded by migration `0004` in step 6.

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

Follow **[`legacy-php/README.md`](../legacy-php/README.md)**. In short, on the Web Station site (back up first):

1. **Generate the PHP bridge config** from `.env`: `sudo ./goldata.sh php-config` → writes `PHP_AUTH_CONFIG` (`/volume1/web_config/goldata_auth.php`, outside the web root, owner `http`, mode 600). Re-run it whenever `DB_PHP_AUTH_PASSWORD`, `DB_APP_NAME` or `PUBLIC_BASE_URL` change.
2. Copy `legacy-php/overlay/*` over the site: `app_auth.php`, `require_login.php`, patched `login.php` (hashes removed), and the hot-fixed `crud_action.php`, `delete_file.php`, `dropdown/ajax/*.php`.
3. `php -l` every copied file on the NAS (`/usr/local/bin/php80 -l <file>`) **(P0)**.
4. **Delete** the stale copies `login2023.php` and `connectDB-81-230.php` (they contain hashes and passwords).
5. Deny direct web access to `pdf_folder` (Apache: `pdf_folder/.htaccess` with `Require all denied`).
6. **Rotate the legacy DB password** used by `connectDB.php` / `DBController.php`, and update both files.
7. Run the test table in `legacy-php/README.md`.
8. **Rollback:** restore the backed-up files (no DB change to undo).

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
| DB dump | daily 01:30 | `/volume1/docker/goldata/goldata.sh backup-db` (`mysqldump --single-transaction` of `DB_LEGACY_NAME` + `DB_APP_NAME` as `DB_BACKUP_USER` into `BACKUP_DIR`, gzip, keeps `BACKUP_KEEP_DAYS`; the password is passed in a temporary root-only option file) |

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
| `Connection string 'Legacy' is missing` | `DB_HOST` / `DB_API_USER` / `DB_API_PASSWORD` not set in `.env`; check with `./goldata.sh env` |
| Login fails only in the container, password has quotes/`#`/spaces | compose v1 keeps quotes and doesn't strip comments: use plain `KEY=value` with `[A-Za-z0-9-_.]` passwords |
| `WARNING: .env holds secrets` | `sudo chown root:root .env && sudo chmod 600 .env` |
| `ERROR: … Additional properties are not allowed ('name' …)` | compose v1 doesn't accept top-level `name:`; use `COMPOSE_PROJECT_NAME` in `.env` |
| Garbled Chinese file names | NFD names from macOS SMB clients. The API matches both NFC and NFD; report the file if it still fails |

## 16. UAT (beside prod on the same NAS)

UAT uses its own project folder, `.env`, port, subnet, databases, accounts, document copy and token pepper, so it can never write production data. A unit test checks that `uat.env.example` and `prod.env.example` don't share these.

1. **DB:** run [`db/ops/0004__create_uat_databases_and_accounts.sql`](../db/ops/0004__create_uat_databases_and_accounts.sql) as in step 2 (UAT passwords).
2. **Data copy** (refresh whenever UAT needs fresh data; UAT then holds production personal data, so treat it like prod):
   ```sh
   cd /volume1/docker/goldata
   B=/usr/local/mariadb10/bin
   sudo $B/mysqldump -u root -p -h 127.0.0.1 -P 3307 --single-transaction goldata | sudo $B/mysql -u root -p -h 127.0.0.1 -P 3307 goldata_uat
   sudo mkdir -p /volume1/goldata-uat && sudo rsync -a --delete /volume1/web/pdf_folder/ /volume1/goldata-uat/pdf_folder/   # outside the web share
   ```
   The UAT `goldata_app_uat` starts empty: run `migrate` + `import-users` there (or copy `goldata_app` the same way to test with real users).
3. **Folder + env file:** as step 4 in `/volume1/docker/goldata-uat`, from `uat.env.example` (`WEB_PORT=8086`, `NETWORK_SUBNET=172.30.11.0/24`, `DB_*_NAME=…_uat`, `DOC_ROOT=/volume1/goldata-uat`).
4. **Firewall:** allow `172.30.11.0/24` → 3307.
5. `sudo ./goldata.sh pull && sudo ./goldata.sh migrate && sudo ./goldata.sh up` in the UAT folder.
6. **Reverse proxy:** `https://erp-uat.<domain>` → `localhost:8086` (step 9).
7. A UAT copy of the PHP site is optional. If you make one, set `GOLDATA_AUTH_CONFIG` to `PHP_AUTH_CONFIG` of the UAT `.env` before `login.php` is included, run `php-config` in the UAT folder, and run the commented grants at the end of `ops/0004`.

## Local (debug)

The local/test server is **192.168.21.175** (DB copy + `\\192.168.21.175\web\pdf_folder`). Mount `smb://192.168.21.175/web` and set `DOCUMENTS_ROOT=/Volumes/web`. Its `pdf_folder` copy currently ends in 2019-09, so newer attachments show 「文件缺失」 until the documents are refreshed from production, e.g. on `.175`:
`sudo rsync -a --ignore-existing admin@192.168.168.235:/volume1/web/pdf_folder/ /volume1/web/pdf_folder/` (read-only towards prod; adjust the source path to the live one).


`cp deploy/env/local.env.example deploy/env/local.env`, then `docker compose --env-file deploy/env/local.env -f deploy/compose.dev.yaml up -d` (MariaDB 10.3.32 with a **sanitised** dump), `dotnet run --project src/Goldata.Api` (launch profile `local` loads the file) and `cd web && npm run dev` (Vite proxies to `API_URL` from the same file).

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
| 2026-11-02 | Servers clarified: **prod = 192.168.168.235**, local/test = 192.168.21.175 (DB copy + `pdf_folder` copy only up to 2019-09). Templates' `DB_HOST` back to `.235`. Local section: how to refresh the test documents. |
| 2026-11-02 | Document folder confirmed: `\\192.168.21.175\web\pdf_folder` → prod `DOC_ROOT=/volume1/web`; UAT copy moved outside the web share (`/volume1/goldata-uat`); NAS IP `192.168.21.175` in the templates. |
| 2026-11-02 | **One env file per environment** (`deploy/env/{local,uat,prod}.env.example` → `.env`): replaces `secrets/`, `mysqldump.cnf`, the hand-written PHP config (now `goldata.sh php-config`) and appsettings.Development.json. Step 4 rewritten, step 10.1 and 12 updated, new section 16 (UAT) + Local, `ops/0004` (UAT DB/accounts), new `goldata.sh env` / `php-config`. |
| 2026-11-02 | P0 code built: `goldata.sh` commands, `.env` keys (`WEB_PORT`, `PUBLIC_BASE_URL`, `MARIADB_*`), `ops/0003` backup account + `secrets/mysqldump.cnf`, migration `0004` (default permission matrix, generated), import via `./import`, PHP overlay in `legacy-php/`. |
