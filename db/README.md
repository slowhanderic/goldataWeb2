# db/ — SQL change history for GoldataWeb2

Every database change made for GoldataWeb2 is saved here as a numbered `.sql` file. Nothing is changed by hand on the server without a file here first.

```
db/
├─ migrations/
│  ├─ goldata_app/   ← schema + seed for the NEW database (identity, permissions, settings)
│  └─ goldata/       ← changes to the LEGACY database (additive only, review required)
└─ ops/              ← server-level changes run by an admin: databases, accounts, grants
```

## Who runs what

| Folder | Runs how | Run as | Journal (what has been applied) |
|---|---|---|---|
| `migrations/goldata_app` | Automatically: `goldata.sh migrate` (DbUp in the API image, `--migrate`) | `goldata_migrator` | `goldata_app.schemaversions` |
| `migrations/goldata` | Automatically, same job, after `goldata_app` | `goldata_migrator` (needs the specific grant the script states) | `goldata_app.schemaversions_goldata`: the journal is kept in `goldata_app`, so **no new table is added to `goldata`** (REQ-CMP-1) |
| `ops` | **Manually** by the NAS/DB admin with the MariaDB 10 client (`/usr/local/mariadb10/bin/mysql`). These scripts need `root`-level rights | DB admin | The **Applied log** table below (date, server, who) |

## File rules

1. **Name:** `NNNN__short_description.sql`: a 4-digit sequence per folder, two underscores, lower-case words with `_`. Example: `0004__add_user_last_ip.sql`. The next number is the highest one in that folder + 1.
2. **Header:** every file starts with this block:
   ```sql
   -- Purpose : what and why (one or two lines)
   -- Ref     : requirement / spec section (e.g. REQ-ADM-6, 002-spec §5.1)
   -- Author  : name, YYYY-MM-DD
   -- Rollback: how to undo (or "forward-only: fix with a new script")
   ```
3. **Never edit or rename a file once it has been applied anywhere** (test or production). To fix it, add a new file. DbUp skips files already in its journal, so an edited file would silently never run.
4. **Forward-only.** No down-scripts. The `Rollback` note says how to undo the change by hand if it is ever needed.
5. **MariaDB 10.3.32 syntax.** Watch out for these:
   * Declare foreign keys with `FOREIGN KEY (...) REFERENCES ...` at table level. Inline column `REFERENCES` is parsed but **ignored** on 10.3.
   * `JSON` is an alias for `LONGTEXT`. Add `CHECK (JSON_VALID(col))`.
   * Use `CREATE … IF NOT EXISTS`, `CREATE OR REPLACE VIEW`, `CREATE USER IF NOT EXISTS`, so `ops` scripts can be re-run safely.
6. **Charset:** `goldata_app` is `utf8mb4` / `utf8mb4_unicode_ci`. `goldata` is `utf8` (utf8mb3). Don't join across the two in SQL on text columns ("Illegal mix of collations"). Compare in the API with parameters instead.
7. **`migrations/goldata` (legacy DB):** only **additive, PHP-safe** changes such as a new index. No dropped or renamed columns, no type changes, no new tables, no triggers. Each script needs sign-off from the owner (recorded in the header `Ref`) and a check that the PHP app still works.
8. **No secrets** in any file. Passwords appear only as `<secret>` placeholders and are typed in by the admin when running `ops` scripts. **No personal data** (names, salaries); user accounts are imported by the seed tool, not by SQL files.
9. **One change per file.** Keep it small enough to review in a couple of minutes. A PR that changes the DB must include its file here.

## Applied log (ops scripts and production runs)

| Date | Server | Script | Run by | Notes |
|---|---|---|---|---|
| | | | | |
