# GoldataWeb2: working rules

- Specs: `specs/001-requirements.md` (requirements), `specs/002-spec.md` (technical spec). Keep both in sync and add a revision-history line for each change.
- **SQL changes:** every DB change (`goldata_app`, `goldata`, accounts/grants) is a new numbered script in `db/` following `db/README.md`. Never edit an applied script; never put passwords or personal data in SQL files.
- **Deployment runbook:** any change that affects install, configuration, upgrade or rollback (compose/env/secrets, images, ports, `db/ops` scripts, PHP patch, reverse proxy, backups, `goldata.sh`) must update `deploy/DEPLOYMENT.md` in the same change, with a change-log line at the bottom.
- **Settings:** ONE env file per environment (`deploy/env/{local,uat,prod}.env`, templates `*.env.example`). New settings go into all three templates and `src/Goldata.Api/GoldataEnv.cs` (the only env→config mapping); never add settings files, docker secrets or user-secrets. Template format: `KEY=value`, no quotes/spaces/inline comments (tested).
- Target: Synology **DSM 7.1**, Docker package (docker-compose v1), MariaDB 10.3.32 on port 3307, legacy PHP 8.0 in Web Station.
- Free and open-source dependencies only (spec §1.2 register / banned list).
- `goldataWeb1/` holds the legacy PHP source and a DB dump with payroll/personal data: never commit `*.sql` dumps or files containing credentials.
- Commands: `dotnet test` (needs `~/.dotnet` on PATH; also checks `0004` seed = `DefaultPermissionMatrix.cs`, regenerate with `GOLDATA_REGEN=1 dotnet test`), `cd web && npx tsc -b && npx oxlint src && npm run build && npm run licenses`.
- Changes to the running PHP app go in `legacy-php/overlay/` (never edit `goldataWeb1/`); never put password hashes there.
