# GoldataWeb2: 广泰管理系统 (React + C# REST API + MCP)

Modernisation of the legacy PHP ERP. It reads and writes the **existing** MariaDB `goldata` database and the shared `pdf_folder`, while the PHP app keeps running.

| Folder | What |
|---|---|
| `specs/` | Requirements (`001`) and technical spec (`002`) |
| `src/` | .NET 10 API: `Goldata.Api` (host, REST, auth, CLI), `Goldata.Application` (module registry, permissions, rules), `Goldata.Infrastructure` (Dapper data access, identity, files, migrations), `Goldata.Mcp` (MCP tools), `Goldata.Domain` (legacy text rules, errors) |
| `web/` | React 19 + TypeScript + Vite + MUI (free tier) |
| `db/` | **Every SQL change**, numbered (see `db/README.md`) |
| `deploy/` | Dockerfiles, nginx, `compose.yaml` (DSM 7.1), `goldata.sh`, **`DEPLOYMENT.md` runbook** |
| `legacy-php/` | Changes to the running PHP app (login bridge + security hot-fixes) |
| `tests/` | Unit and pipeline tests |

## Develop

All settings live in **one env file per environment** (`deploy/env/local.env`, `uat.env`, `prod.env`; templates `*.env.example`). See `deploy/DEPLOYMENT.md`.

```sh
cp deploy/env/local.env.example deploy/env/local.env          # the ONE local settings file (git-ignored)
docker compose --env-file deploy/env/local.env -f deploy/compose.dev.yaml up -d   # MariaDB 10.3.32 (sanitised dump)

# API (needs .NET 10 SDK); the "local" launch profile loads deploy/env/local.env
dotnet test                                     # also checks the 0004 seed and the env templates
dotnet run --project src/Goldata.Api -- --migrate
dotnet run --project src/Goldata.Api -- --import-users /path/to/login.php
dotnet run --project src/Goldata.Api            # http://localhost:5080 (+ /scalar)

# Web: the Vite proxy reads API_URL from the same local.env
cd web && npm ci && npm run dev                 # http://localhost:5173
npm run licenses                                # free & open source allow-list (NFR-LIC-2)
```

**VS Code:** open the `goldataWeb2` folder → Run and Debug → **Full stack: API + Web**. That builds and debugs the API on :5080 (breakpoints in C#) and starts Vite + Chrome on :5173 (breakpoints in the React source). Other configs: Web in Edge, `--migrate`, `--import-users`, attach to a process. Tasks (Terminal ▸ Run Task): local MariaDB start/stop, `dotnet watch`, tests, web checks, API health, MCP Inspector. `deploy/env/local.env` is created from the template on the first run.

Never use the real production dump on a dev machine.

Rules for contributors are in `CLAUDE.md`.
