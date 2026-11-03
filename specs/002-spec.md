# 002 — Technical Specification: GoldataWeb2

| | |
|---|---|
| Status | **Draft for review — rev 3** |
| Date | 2026-11-02 |
| Implements | [001-requirements.md](001-requirements.md) rev 2 |

Rev 3.9 changes: **one env file per environment** (local / UAT / prod, §9.4) replaces docker secrets, `appsettings.Development.json`, user-secrets and the hand-written PHP config. UAT beside prod (§9.10, `db/ops/0004`). DB names configurable (UAT).

Rev 3.12 changes: line items, catalogue picker and item conversions (§13).

Rev 3.11 changes: links implemented (§13); the legacy `tableLinkTableCode` values are the API codes (§3.6).

Rev 3.10 changes: P1 generic writes — `POST /modules/{key}` (`{values, parentModule?, parentId?}`), `PUT /modules/{key}/{id}` (If-Match), `DELETE /modules/{key}/{id}`, `GET …/dependents`, `GET …/related`, `POST …/handover` (`{statusTo, personTo, comment}`); `/modules` metadata adds `editFields`, `statusLookup`, `createFrom`, `createChildren` (§13).

Rev 3.8 changes: implementation started (§13 status). MUI v9 / MUI X v9 (current majors); own snackbar and native drag-drop instead of notistack/react-dropzone. Module REST paths are `/api/v1/modules/{key}` (§6.1). Layering: DB-backed use cases live in Infrastructure (§2).

Rev 3.7 changes: deployment runbook `deploy/DEPLOYMENT.md` added and must be kept current. Secrets files are `root:<DOC_GID>` 640 (readable by the non-root container). Migrator connection secret. `goldata.sh` command list.

Rev 3.6 changes: all SQL changes live as numbered scripts in `db/` (§2.1).

Rev 3.5 changes: target is **DSM 7.1** with the legacy **Docker** package (no Container Manager): docker-compose v1 over SSH, Alpine-based images (old-Docker seccomp issue), no `name:` key, ops via Task Scheduler (§9.0, §9.3, §9.4, §9.7).

Rev 3.4 changes: free and open source only (§1.2 licence register, banned list, CI check); self-hosted Gitea registry/CI instead of GHCR; test libraries pinned to OSS.

Rev 3.3 changes: UI kit is **MUI** (Material UI + MUI X) with a company theme (§8.0); component mapping updated.

Rev 3.2 changes: PHP login reads `goldata_app` (§5.5), so one user list serves both apps.

Rev 3.1 changes: user / role / permission admin (§5.1 schema, §5.4 rules, §6.3 endpoints, §8.3 page).

Rev 3 changes: §9 rewritten for **Docker on Synology DSM** (Container Manager project, images, DB grants for the container subnet, shared `pdf_folder` ownership with Web Station, backups, NAS security).

Rev 2 changes: DB host, the role × rank model with bcrypt import, the full catalogue of 30 link types, the 10-table workflow map, folder/tag map for all 25 tags, the product catalogue and inventory, payroll, the cost estimator, `ENT_COMPAT` encoding, full audit coverage, and HR exclusion from MCP.

---

## 1. Architecture

```
┌──────────────┐     HTTPS      ┌──────────────────────────────────────────────┐
│ React SPA    │ ─────────────▶ │  Goldata.Api  (ASP.NET Core, .NET 10 LTS)     │
│ (browser)    │   /api/v1/*    │  ├─ REST endpoints                            │
└──────────────┘                │  ├─ /mcp  (MCP Streamable HTTP endpoint)       │
┌──────────────┐  MCP (HTTP)    │  ├─ Application layer (use cases, RBAC, audit) │
│ Claude / AI  │ ─────────────▶ │  ├─ Data layer  (Dapper + MySqlConnector)      │
│ client       │   /mcp         │  └─ File store adapter (IDocumentStore)        │
└──────────────┘                └───────┬──────────────────────┬────────────────┘
                                        │ TCP 3307             │ bind mount (Synology)
                              ┌─────────▼─────────┐   ┌────────▼──────────────┐
                              │ MariaDB 10.3      │   │ <DocRoot>/pdf_folder/ │
                              │ 192.168.168.235   │   │  (shared with PHP)    │
                              │  goldata (legacy) │   └───────────────────────┘
                              │  goldata_app (new)│            ▲
                              └─────────▲─────────┘            │
                              ┌─────────┴───────────────────────┴──┐
                              │ Legacy PHP app (runs in parallel)   │
                              └─────────────────────────────────────┘
```

* **One deployable back end** hosts both REST and MCP. They share the Application layer, so business rules, RBAC and audit are written once. MCP can be split into its own process later.
* **The legacy DB is the system of record.** `goldata_app` holds identity and app settings only.
* **Strangler cutover**: when a module goes live in React, its PHP pages are made read-only (a small change to `check_page_permission()` + `commit_sql()`).

### 1.1 Technology choices

| Concern | Choice | Reason |
|---|---|---|
| API runtime | ASP.NET Core on **.NET 10 (LTS)** | LTS; minimal APIs + OpenAPI built in |
| DB driver | **MySqlConnector** | Async, MariaDB 10.3, zero-date options |
| Data access | **Dapper** with hand-written SQL | No FKs, polymorphic links, report SQL ported 1:1 from PHP. The legacy app is itself a set of generic helpers (`insert_to_sql`, `update_to_sql`, `conditions_to_sql`), which map naturally to a metadata-driven Dapper layer |
| Password hashes | **BCrypt.Net-Next** | Verifies the existing `$2y$10$` hashes from `login.php` |
| Validation | FluentValidation | zh-CN messages |
| API docs | OpenAPI 3.1 + Scalar | Typed TS client generation |
| MCP | Official **MCP C# SDK** (`ModelContextProtocol.AspNetCore`), Streamable HTTP | First-party SDK |
| PDF text | PdfPig | MCP document-text tool |
| Excel | ClosedXML | Export |
| Logging | Serilog + OpenTelemetry | |
| Front end | **React 19 + TypeScript + Vite** | |
| UI kit + theme | **MUI** — `@mui/material` v9 + MUI X v9 (`x-data-grid`, `x-tree-view`; `x-date-pickers` when forms arrive), MIT tier; Emotion; versions locked in `package-lock.json` | Central `createTheme` theme, zh-CN locales for all components, server-mode Data Grid with virtualisation and cell editing |
| UI helpers | MUI `Snackbar` wrapper (`Notify.tsx`), native drag-and-drop + XHR upload progress, dayjs (`zh-cn`), React Router, TanStack Query | Fewer dependencies than notistack / react-dropzone |
| Data fetching | TanStack Query + generated OpenAPI client | |
| Forms | React Hook Form + Zod | |
| Tests | xUnit + Testcontainers (MariaDB 10.3), Shouldly, NSubstitute, Vitest, Playwright | All OSS (FluentAssertions v8+ and Moq avoided, see §1.2) |
| Print / PDF output (Q13) | Browser print CSS first; PDFsharp/MigraDoc (MIT) if server-side PDFs are needed | QuestPDF excluded (community licence, not OSI) |
| Charts (reports) | MUI X Charts (MIT) | |
| OAuth later (Q11) | OpenIddict (Apache-2.0) | Duende IdentityServer excluded (commercial) |

### 1.2 Licensing: free and open source only (NFR-LIC-1/2)

**Register** (checked 2026-11-02; CI re-checks on every build):

| Component | Licence |
|---|---|
| .NET 10 runtime/SDK, ASP.NET Core, `Microsoft.AspNetCore.OpenApi` | MIT |
| MCP C# SDK (`ModelContextProtocol.AspNetCore`) | Apache-2.0 (moving over from MIT) |
| MySqlConnector | MIT. Oracle's MySQL Connector/NET (GPL + commercial) is not used |
| Dapper | Apache-2.0 |
| BCrypt.Net-Next · DbUp · ClosedXML · Scalar | MIT |
| PdfPig · OpenIddict · Serilog · OpenTelemetry · xUnit · Playwright | Apache-2.0 |
| FluentValidation | Apache-2.0 |
| Testcontainers | MIT |
| Shouldly · NSubstitute | BSD |
| React · Vite · TypeScript* · TanStack Query · React Hook Form · Zod · Vitest | MIT (*Apache-2.0) |
| MUI `@mui/material`, `@mui/icons-material`, `@mui/lab`, `@mui/x-data-grid`, `@mui/x-date-pickers`, `@mui/x-tree-view`, `@mui/x-charts` | MIT (free tier) |
| Emotion · dayjs · React Router · TanStack Query | MIT |
| pdf.js | Apache-2.0 |
| AG Grid **Community** (fallback only) | MIT |
| nginx (`nginx-unprivileged`) · Alpine | BSD-2 · MIT/various OSS |
| .NET Alpine base images (`aspnet:10.0-alpine`) + ICU, tzdata | MIT / OSS |
| Gitea + act_runner (self-hosted git, CI, OCI registry) | MIT |
| Noto Sans SC (font fallback) | OFL-1.1 |
| MariaDB server (existing, runs separately) | GPL-2.0, not linked into our code |

**Banned** (commercial, source-available or paid-tier), with the replacement used:

| Don't use | Why | Use instead |
|---|---|---|
| `@mui/x-*-pro`, `@mui/x-*-premium` | Commercial licence key | MIT MUI X + server-side totals/export |
| AG Grid Enterprise | Commercial | AG Grid Community |
| FluentAssertions ≥ 8 | Commercial (Xceed) | Shouldly |
| Moq | Closed-source SponsorLink episode | NSubstitute |
| MediatR ≥ 13, AutoMapper ≥ 15 | Commercial since 2025 | Plain handlers / hand-written mapping |
| QuestPDF | Community licence (revenue threshold, not OSI) | PDFsharp/MigraDoc (MIT) or browser print |
| EPPlus ≥ 5, Aspose, Syncfusion, DevExpress, Telerik | Commercial | ClosedXML, PdfPig |
| Duende IdentityServer | Commercial | OpenIddict |
| ImageSharp ≥ 3 | Six Labors split licence | Not needed (no server-side image processing) |
| Highcharts, amCharts | Commercial | MUI X Charts |
| MySQL Connector/NET | GPL/commercial dual | MySqlConnector |

**Enforcement (NFR-LIC-2):**
* `web/`: `license-checker-rseidelsohn --onlyAllow "MIT;Apache-2.0;BSD-2-Clause;BSD-3-Clause;ISC;0BSD;MPL-2.0;OFL-1.1;CC0-1.0;BlueOak-1.0.0;Unlicense" --production`, plus an ESLint `no-restricted-imports` rule for `@mui/x-*-pro|premium` and `ag-grid-enterprise`.
* `src/`: `nuget-license` with the same allow-list, plus a banned-package list in `Directory.Packages.props` (central package management with exact versions).
* Both write `THIRD-PARTY-NOTICES.txt`, which is copied into each image.
* Any new dependency or licence change fails CI until it is added to the register above.

**Outside the system** (not built into or needed to run it): MCP clients such as Claude Desktop/Claude Code are the user's choice, and any MCP-compliant client, including open-source ones, works. DSM and its bundled packages (Docker, Web Station, Hyper Backup, Snapshot Replication) are part of the NAS you already own. The system fonts PingFang/Microsoft YaHei come with the OS and are not shipped, so Noto Sans SC is the open fallback.

---

## 2. Solution layout

```
goldataWeb2/
├─ specs/
├─ src/
│  ├─ Goldata.Api/              ← host: endpoints, auth, MCP endpoint, DI, config
│  ├─ Goldata.Application/      ← use cases, module metadata, RBAC, audit, workflow, links
│  ├─ Goldata.Domain/           ← DTOs, lookup keys, money/date helpers, ChineseAmount
│  ├─ Goldata.Infrastructure/   ← Dapper repositories, LegacyCompat, DocumentStore, PdfText
│  └─ Goldata.Mcp/              ← MCP tool classes (thin; call Application)
├─ web/                         ← React SPA
├─ tests/
├─ db/                         ← every SQL change, numbered (§2.1, db/README.md)
│  ├─ migrations/goldata_app/   ← DbUp, automatic: schema + seed for the NEW db
│  ├─ migrations/goldata/       ← DbUp, automatic: additive-only legacy changes (sign-off required)
│  └─ ops/                      ← manual, DB admin: databases, accounts, grants
├─ db/sanitise/                 ← anonymise a prod dump for tests (names, salaries, bank info)
└─ deploy/
   ├─ api.Dockerfile            ← multi-stage, Alpine runtime, linux/amd64 (+arm64 optional)
   ├─ web.Dockerfile            ← vite build → nginx-unprivileged
   ├─ nginx.conf
   ├─ compose.yaml              ← run with docker-compose v1 on DSM 7.1 (§9)
   ├─ seccomp-dsm71.json        ← fallback seccomp profile (§9.0)
   ├─ goldata.sh                ← pull / migrate / import-users / up / check / backup-db / upgrade / rollback
   ├─ compose.dev.yaml          ← local: + MariaDB 10.3.32 with sanitised dump
   ├─ .env.example
   └─ DEPLOYMENT.md             ← step-by-step runbook; updated in the same commit as any deployment change
```

### 2.1 SQL change history (`db/`)

Every database change for GoldataWeb2 is a numbered file (`NNNN__description.sql`) in `db/`, committed before it runs anywhere. Rules are in `db/README.md`. In short: never edit an applied file, forward-only, MariaDB 10.3 syntax, no secrets or personal data, one change per file.

| Folder | Applied by | Journal |
|---|---|---|
| `migrations/goldata_app` | `goldata.sh migrate` → API image `--migrate` (DbUp, account `goldata_migrator`) | `goldata_app.schemaversions` |
| `migrations/goldata` | the same job, after `goldata_app` | `goldata_app.schemaversions_goldata`, via DbUp `JournalToMySqlTable("goldata_app", …)`, so no table is added to `goldata` |
| `ops` | the DB admin by hand (needs root rights) | the *Applied log* table in `db/README.md` |

The scripts are embedded in the API image (`EmbeddedResource`), so the deployed version and its migrations always match. CI runs all migrations on a fresh MariaDB 10.3.32 container, and again on a sanitised copy of production, on every build.

Current scripts: `ops/0001` (create `goldata_app` + accounts), `ops/0002` (PHP bridge grants), `ops/0003` (read-only backup account), `goldata_app/0001` (identity tables, §5.1), `0002` (`v_legacy_login`, §5.5), `0003` (seed ranks, roles, settings), `0004` (default permission matrix, **generated** from `DefaultPermissionMatrix.cs`; a unit test fails if they differ). Users are imported by `--import-users`, never by SQL.

**Layering as built:** `Goldata.Application` holds the module registry, permission catalogue and calculator, list-query builder, admin/password/path rules and the login.php parser — pure, unit-tested. The DB-backed use cases (`AuthService`, `AdminService`, `RecordService`, `FileService`, `TodoService`, `LookupService`) live in `Goldata.Infrastructure` with Dapper, because almost every one needs its own transaction and `SELECT … FOR UPDATE`.

**Before the first commit**, add `*.sql`, `goldataWeb1/**/connectDB*.php`, `goldataWeb1/**/DBController.php` and `goldataWeb1/**/login*.php` to `.gitignore`. The repo root is `~/Documents/git`, and the 77 MB dump contains payroll data.

---

## 3. Data access design

### 3.1 Connections

```jsonc
"ConnectionStrings": {
  "Legacy": "Server=192.168.168.235;Port=3307;Database=goldata;User ID=goldata_api;Password=***;CharacterSet=utf8;ConvertZeroDateTime=true;TreatTinyAsBoolean=false;",
  "App":    "Server=192.168.168.235;Port=3307;Database=goldata_app;User ID=goldata_api;Password=***;CharacterSet=utf8mb4;"
}
```

* New DB user `goldata_api`: DML on `goldata.*`, all rights on `goldata_app.*`. The legacy `webpage` account is not reused.
* Secrets come from env vars / a secret store and are never committed.

### 3.2 Module metadata + repositories

Legacy pages are table-driven (`$data_array['col'/'data'/'type']`). The new API uses a typed **module registry**, which drives generic CRUD, list filters, export, audit and MCP:

```csharp
Module("contracts", table: "contracts", title: "合同",
    statusColumn: "contract_status", statusLookup: "合同情况", workflow: true,
    fileTag: "pdf_contract_file", folder: "contracts",
    permissionArea: Area.Gongzhuang,
    fields: [ Text("contract_name","合同名称", required:true), Text("contract_number","合同编号"),
              Date("contract_date","合同签定日期"), Money("amount","合同金额"),
              Lookup("contract_type","合同类型","合同类别"), Lookup("settlement_type","结算类别","合同结算类别"),
              Text("client_name","甲方名称"), Text("secondparty_name","乙方名称"), FkId("project_id","工装项目","projects"), … ],
    list: new(defaultSort: "-contract_date", closedValues: ["结案态","作废"], totals: ["amount"]));
```

Modules with special logic (buy orders, receipts, deliveries, payroll, labour settlement, quotes, cost estimator) add custom services on top of the generic one.

`ListQuery` replaces `conditions_to_sql()`/`orderby_to_sql()`. Column names come **only** from the module whitelist, values are always parameters, and `LIKE` input is escaped.

### 3.3 Legacy compatibility layer (`LegacyCompat`)

| Concern | Rule |
|---|---|
| HTML entities | **Read:** `WebUtility.HtmlDecode` on text columns. **Write (coexistence, default ON):** encode exactly like PHP 8.0 `htmlspecialchars` default `ENT_COMPAT`: `&`→`&amp;`, `"`→`&quot;`, `<`→`&lt;`, `>`→`&gt;`, with **`'` left as is**. This matches the data (536 `&amp;`, 72 `&quot;`, 0 `&#039;`). Filter terms are encoded the same way. Flag `LegacyCompat:EncodeHtmlOnWrite`. |
| Zero/empty dates | Read → `null`. Write `null` → `'0000-00-00'` where legacy does (most item/date fields, e.g. `payment_date`), else `NULL`. Per-field rule in metadata. |
| Money | `double` columns are exposed as `decimal(18,2)` and written back as `double`. Totals use SQL `ROUND(SUM(x),2)`. |
| Empty amount/qty | `''` → `0` (legacy). |
| 3-byte UTF-8 | Reject code points > U+FFFF with “不支持表情符号或生僻字” unless `Db:Utf8mb4=true`. |
| Status/type | Stored as Chinese `table_item` text. Unknown values are preserved. |
| User fields | `user_name`, `initiator`, `followup_user`, `holder`, `person_from/to` = display name. |
| Brackets | Supplier matching normalises `（）`→`()` (as the legacy report SQL does). |

### 3.4 Concurrency

`timestamp` is a create time, so it can't act as a row version. Instead:
1. GET returns `ETag = SHA-256(canonical row JSON)` (header + items for item-based documents).
2. PUT requires `If-Match`. In a transaction: `SELECT … FOR UPDATE`, recompute the hash, and return **409** with the server values if they differ. Otherwise update.

This also catches edits made through PHP.

### 3.5 Line items

PUT sends `items[]` (with/without `id`) + `deletedItemIds[]` (explicit, never inferred from absence). `sub_total = qty × unit_price` is computed on the server. The header total is recalculated where legacy does it (payroll `amount`/`count`, order/receipt amount if configured). Everything runs in one transaction, with audit rows per statement.

### 3.6 Link catalogue (all 30 types)

One generic link service (replacing `link-create.php` + `project-parent-link-create.php`):

| Code (legacy `tableLinkTableCode`) | From → To | Storage | share_amount |
|---|---|---|---|
| payment-bo-link | 付款 → 订单 | `payment_bo` | ✔ |
| payment-project-link | 付款 → 工装项目 | `payment_project` | ✔ |
| payment-project_retail-link | 付款 → 家装项目 | `payment_project_retail` | ✔ |
| payment-ls-link | 付款 → 人工费结算 | `payment_ls` | ✔ |
| buy_invoice-payment-link | 进项发票 → 付款 | `buy_invoice_payment` | ✔ |
| received-ra-link | 收款 → 请款 | `received_receivable_application` | ✔ |
| received-project_retail-link | 收款 → 家装项目 | `received_project_retail` | ✔ |
| bo-project-link | 订单 → 工装项目 | `buy_order_project` | ✔ |
| bo-project_retail-link | 订单 → 家装项目 | `buy_order_project_retail` | ✔ |
| bo-br-link | 订单 → 请购单 | `buy_order_request` | ✔ |
| bo-bp-link | 订单 → 采购计划 | `buy_order_plan` | ✔ |
| receivable_applications-bo-link | 请款 → 订单 | `buy_order_ra` | ✔ |
| brc-bo-link | 收货单 → 订单 | `buy_receipt_order` | ✔ |
| ls-project-link | 人工费结算 → 工装项目 | `labor_settlement_project` | ✔ |
| ls-project_retail-link | 人工费结算 → 家装项目 | `labor_settlement_project_retail` | ✔ |
| additional-subproject-link | 签证 → 子项目 | `additional_project` | ✔ |
| contract-project-link | 合同 → 工装项目 | `contracts.project_id` | |
| budget-project-link | 预算 → 工装项目 | `budgets.project_id` | |
| additional-contract-link | 签证 → 合同 | `additionals.contract_id` | |
| settlement-contract-link | 结算 → 合同 | `settlements.contract_id` | |
| receivable_applications-contract-link | 请款 → 合同 | `receivable_applications.contract_id` | |
| document-contract-link | 工程文件 → 合同 | `documents.contract_id` | |
| sales_invoice-ra-link | 销项发票 → 请款 | `sales_invoices.receivable_application_id` | |
| br-project-link | 请购单 → 工装项目 | `buy_requests.project_id` | |
| br-project_retail-link | 请购单 → 家装项目 | `buy_requests.project_retail_id` | |
| bp-project-link | 采购计划 → 工装项目 | `buy_plans.project_id` | |
| bp-project_retail-link | 采购计划 → 家装项目 | `buy_plans.project_retail_id` | |
| delivery-project-link | 出库单 → 工装项目 | `deliveries.project_id` | |
| delivery-project_retail-link | 出库单 → 家装项目 | `deliveries.project_retail_id` | |
| project-parent-link | 子项目 → 父项目 | `projects.parent_project_id` | |

FK-column unlink = `SET col = NULL` (as `delete_row.php` does). `GET /links/{code}/candidates?fromId=&q=` returns candidates with already-allocated totals. The legacy codes are kept as the API identifiers to make cross-checking easy.

### 3.7 Create-from-parent (legacy `insert.php?insert_table=X&link_table=Y`)

`POST /{module}?linkFrom={code}&fromId={id}` creates the record and the link in one transaction. Legacy special cases are preserved:
* 收货单 from 订单 (`buy_receipt_order`): also copies the order items into `buy_receipt_items`.
* 合同/请款/签证/结算/预算 created from a project or contract: sets the FK column.
* 订单 from 工装项目 / 家装项目 / 请购单 / 采购计划: inserts the share row.

### 3.8 Delete safety

The `DependencyMap` built from §3.6 answers “what references this row?”. `DELETE /{module}/{id}` returns **409** with the dependents unless an admin passes `force=true`. Only **管理 + 总监** may delete master records (21 tables, requirements §4.10). Special case kept: deleting a `deliveries` row also deletes `buy_receipt_items WHERE delivery_id = id`.

### 3.9 Audit writer

Every statement executed by the API (including items, conversions, workflow, files, lookups, deletes) writes, in the same transaction:

```sql
INSERT INTO update_logs (table_name, row_id, sql_text, user, update_time, tag)
VALUES (@table, @rowId, @sqlText, @user, NOW(), @tag);
```

* `sql_text` = a rendered, human-readable statement (values inlined for display only), HTML-encoded like legacy `commit_sql()`.
* `user` = `"{role}-{display_name}"`. `tag` ∈ `添加记录 `, `更新记录 `, `删除记录 ` (trailing space as in the data), plus ` [MCP]` for MCP calls.

### 3.10 Workflow (转交)

| table | status column |
|---|---|
| contracts | `contract_status` |
| receivable_applications, settlements, documents, additionals, buy_plans, buy_requests, buy_orders, employees, payrolls | `status` |

Transaction: `SELECT status … FOR UPDATE` → `INSERT historys` → `UPDATE {table} SET {statusColumn}` → audit. To-do query:

```sql
SELECT h.* FROM historys h
JOIN (SELECT table_name, table_id, MAX(id) AS max_id FROM historys GROUP BY table_name, table_id) last
  ON last.max_id = h.id
WHERE h.person_to = @me
ORDER BY h.timestamp DESC;
```

The results are then **filtered by the viewer's read permission on each `table_name`**, and display text is looked up with one batched query per table (no N+1). Optional additive index (review approval): `ALTER TABLE historys ADD INDEX ix_hist_tbl (table_name(32), table_id, id);`

### 3.11 Product catalogue and inventory

* Tree: `lookup_tables` rows where the root category is `产品类别`. Levels are 类别 (c) → 细类 (s) → 品名 (i) → 型号 (t), linked by `parent_id`. `*_items.product_id` = the leaf id. `GET /catalogue/tree`, `GET /catalogue/search?q=` (replaces `dropdown/ajax/search-item-name-ep.php`), `GET /catalogue/{id}/unit`.
* Warehouses: lookup category `进库目的地` (1839 = 项目工地（直接出库）, 1840 = 深圳广泰仓库 …).
* Stock: the `inventory_items-view` query, ported as-is:
  ```sql
  SELECT brci.product_id, brci.destination_id, SUM(brci.qty) AS qty, c.table_item cat, s.table_item subcat, i.table_item item, t.table_item type, d.table_item destination
  FROM buy_receipt_items brci
  LEFT JOIN lookup_tables t ON t.id = brci.product_id
  LEFT JOIN lookup_tables i ON i.id = t.parent_id
  LEFT JOIN lookup_tables s ON s.id = i.parent_id
  LEFT JOIN lookup_tables c ON c.id = s.parent_id
  LEFT JOIN lookup_tables d ON d.id = brci.destination_id
  WHERE brci.destination_id <> @directToSiteId      -- config, default 1839
  GROUP BY brci.product_id, brci.destination_id;
  ```
* Deliveries write `buy_receipt_items` with `delivery_id` set. The quantity convention is to be confirmed (Q5) before implementation.

### 3.12 Payroll

`payment_amount = (standard_monthly_salary + housing_allowance + meal_allowance + distant_allowance + bonus) × poration − tax_deduction − absence_deduction − insurance_deduction`. The header `amount` = Σ lines and `count` = number of lines. A new payroll can pre-fill lines from active `employees`. Amount in words via `ChineseAmount.ToUpper()` (port of `convertAmountToCn`).

### 3.13 Cost estimator

`cost_estimate.postArray` holds the **entire calculator form as JSON** (`json_encode($_POST)`). The new UI stores and reads the same JSON shape, so saved estimates open in both apps. The field list is taken from `labor_cost_estimate-edit.php` (2,155 lines) during P4. The calculation formulas there must be extracted and unit-tested.

---

## 4. Document store

### 4.1 Interface

```csharp
public interface IDocumentStore
{
    Task<StoredFile> SaveAsync(string folder, string fileName, Stream content, ConflictMode mode, CancellationToken ct);
    Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct);
    Task<bool> ExistsAsync(string relativePath, CancellationToken ct);
    Task DeleteAsync(string relativePath, DeleteMode mode, CancellationToken ct); // Recycle | Physical
}
```

`Documents:RootPath` = the directory that **contains** `pdf_folder` (the PHP web root), either local or mounted with the same owner/permissions as the PHP process.

### 4.2 Folder and tag map (from the current source)

`relativePath = "pdf_folder/{folder}/{yyyyMM}/{fileName}"` (month in Asia/Shanghai time).

| Module | folder | `file_path.table_name` |
|---|---|---|
| projects | `projects` | `pdf_project_file` |
| budgets | `budgets` | `pdf_budget_file` |
| contracts | `contracts` | `pdf_contract_file` |
| receivable_applications | `receivable_applications` | `pdf_ra_file` |
| additionals | `additionals` | `pdf_additional_file` |
| documents | `documents` | `pdf_document_file` |
| settlements | `settlements` | `pdf_settlement_file` |
| projects_retail | `projects_retail/blueprints` · `/quotes` · `/contracts` | `pdf_blueprint` · `pdf_quote` · `pdf_contract` |
| quotes_retail | `quotes_retail` | `pdf_quote_retail_file` |
| suppliers | `suppliers` | `pdf_supplier_file` (+ legacy column `suppliers.pdf_path`) |
| buy_plans | `buy_plans` | `pdf_bp_file` |
| buy_requests | `buy_requests` | `pdf_br_file` |
| buy_orders | `buy_orders` | `pdf_bo_file` |
| buy_receipts | `buy_receipts` | `pdf_brc_file` |
| deliveries | `deliveries` | `pdf_delivery_file` |
| sales_invoices | `sales_invoices` | `pdf_sales_invoices_file` |
| buy_invoices | `buy_invoices` | `pdf_buy_invoices_file` |
| receiveds | `receiveds` | `pdf_bank_received_record` |
| payments | `payments` | `pdf_bank_record` |
| labor_settlements | `labor_settlements` | `pdf_ls_file` |
| employees | `employees` | `pdf_employee_file` |
| payrolls | `payrolls` | `pdf_payroll_file` |

> Some 2018 rows carry `pdf_contract_file` on `projects_retail/blueprints/…` paths (early legacy bug). Listing queries go by `(tag, table_id)`. A P0 data check reports any tag/path mismatches.

### 4.3 Upload pipeline

1. Authorise: edit permission on the owning record (HR tags need 人事 permission).
2. Validate the extension ∈ `jpeg,jpg,png,pdf,doc,docx,xls,xlsx,dwg,dxf,zip,rar`, size ≤ `Documents:MaxBytes` (default 80 MB = 83 886 080), and a name with none of `/ \ : * ? " < > | # % $ ! & ~ \` ;`. Magic-byte check for pdf/png/jpg/office/zip.
3. Normalise the name to NFC and reject `..`.
4. Conflict: `Reject` → 409 + suggested `name (2).ext`; `AutoRename` → save as the suggestion. (Legacy overwrites silently.)
5. Write to a temp file, then atomically rename it.
6. `INSERT file_path` + audit in one transaction. On DB failure the file is removed.

### 4.4 Download / delete

* `GET /api/v1/files/{fileId}`: permission check on (tag → module, `table_id`), canonical path under the root, RFC 5987 UTF-8 filename, range support. Missing file → 404 `FILE_MISSING`.
* `DELETE /api/v1/files/{fileId}`: default `Recycle` → `pdf_folder/_recycle/{yyyyMMdd}/{fileId}_{name}`, delete the `file_path` row, audit.
* Recommendation for the coexistence period: put PHP's `pdf_folder` behind web-server auth (or deny direct access). Today it is publicly downloadable.

---

## 5. Identity and authorisation

### 5.1 `goldata_app` schema

```sql
CREATE TABLE app_roles (
  id INT AUTO_INCREMENT PRIMARY KEY,
  name VARCHAR(32) NOT NULL UNIQUE,             -- 管理/业务/财务/…; immutable after create, no '-'
  description VARCHAR(255) NULL,
  is_read_only TINYINT(1) NOT NULL DEFAULT 0,   -- replaces the legacy '*查阅' string test
  is_seeded TINYINT(1) NOT NULL DEFAULT 0,      -- seeded from legacy → supports 恢复默认
  sort INT NOT NULL DEFAULT 0,
  row_version INT NOT NULL DEFAULT 1,           -- ETag
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) CHARSET=utf8mb4;

CREATE TABLE app_ranks (name VARCHAR(16) PRIMARY KEY, sort INT NOT NULL DEFAULT 0);   -- 总监, 员工

CREATE TABLE app_users (
  id INT AUTO_INCREMENT PRIMARY KEY,
  login VARCHAR(32) NOT NULL UNIQUE,            -- legacy login, e.g. 'chendx'
  display_name VARCHAR(64) NOT NULL UNIQUE,     -- must equal names stored in goldata (e.g. '陈冬霞')
  role_id INT NOT NULL REFERENCES app_roles(id),
  `rank` VARCHAR(16) NOT NULL REFERENCES app_ranks(name),
  password_hash VARCHAR(255) NOT NULL,          -- bcrypt $2y$ cost 12 (PHP password_verify-compatible, §5.5)
  must_change_password TINYINT(1) NOT NULL DEFAULT 1,
  is_active TINYINT(1) NOT NULL DEFAULT 1,
  failed_logins INT NOT NULL DEFAULT 0,
  locked_until DATETIME NULL,
  security_stamp CHAR(32) NOT NULL,             -- rotated on disable/password reset/role change → kills sessions
  row_version INT NOT NULL DEFAULT 1,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  created_by INT NULL,
  last_login_at DATETIME NULL
) CHARSET=utf8mb4;

CREATE TABLE app_role_permissions (
  role_id INT NOT NULL REFERENCES app_roles(id) ON DELETE CASCADE,
  `rank` VARCHAR(16) NOT NULL DEFAULT '*',      -- '*' = all ranks, else 总监 / 员工
  permission VARCHAR(64) NOT NULL,              -- must exist in PermissionCatalog
  PRIMARY KEY (role_id, `rank`, permission));
CREATE TABLE app_user_permission_overrides (
  user_id INT NOT NULL REFERENCES app_users(id) ON DELETE CASCADE,
  permission VARCHAR(64) NOT NULL, allow TINYINT(1) NOT NULL,   -- 1 grant, 0 deny (deny wins)
  PRIMARY KEY (user_id, permission));
CREATE TABLE app_api_tokens (id INT AUTO_INCREMENT PRIMARY KEY,
  user_id INT NOT NULL REFERENCES app_users(id) ON DELETE CASCADE, name VARCHAR(64),
  token_hash CHAR(64) NOT NULL UNIQUE, scopes VARCHAR(255) NOT NULL, expires_at DATETIME NOT NULL,
  last_used_at DATETIME NULL, revoked_at DATETIME NULL);
CREATE TABLE app_login_audit (id BIGINT AUTO_INCREMENT PRIMARY KEY, login VARCHAR(64), success TINYINT(1),
  source VARCHAR(8) NOT NULL,                   -- web / php / mcp
  reason VARCHAR(32) NULL,                      -- bad_password, unknown_login, disabled, locked
  ip VARCHAR(45), at DATETIME DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE app_admin_audit (
  id BIGINT AUTO_INCREMENT PRIMARY KEY,
  actor_user_id INT NULL, actor_name VARCHAR(64) NOT NULL,  -- names kept as text: survive user delete
  action VARCHAR(48) NOT NULL,                  -- user.create, user.disable, user.delete, user.reset_password,
                                                -- user.unlock, user.overrides, token.revoke, role.create,
                                                -- role.delete, role.permissions, rank.create …
  target_type VARCHAR(16) NOT NULL, target_id INT NULL, target_name VARCHAR(64) NULL,
  before_json JSON NULL, after_json JSON NULL, ip VARCHAR(45), at DATETIME DEFAULT CURRENT_TIMESTAMP);
CREATE TABLE app_settings (`key` VARCHAR(64) PRIMARY KEY, value TEXT);   -- e.g. mcp.hr.enabled, directToSiteId
```

**Seed**: a one-off script parses `login.php` and imports the 22 accounts (login, display name, role, rank, bcrypt hash), with `must_change_password = 1` because several accounts share a hash. Hashes stay **bcrypt (`$2y$`, cost 12)** so PHP's `password_verify()` can read the same table (§5.5). BCrypt.Net-Next is called with `SaltRevision.Revision2Y`; PHP also accepts `$2a$`/`$2b$`. Hashes with a lower cost are re-hashed at the next successful login. The seed also creates the 10 legacy roles (`is_seeded = 1`), the ranks 总监/员工 and the default matrix in §5.2.

### 5.2 Permissions

`{module}:{read|create|update|delete|link|workflow|export}` plus `files:upload`, `files:delete`, `reports:finance`, `reports:salary`, `lookups:admin`, `todo:all`, `feed:recent`, `users:admin`, `roles:admin`, `mcp:read`, `mcp:write`.

The valid permissions are defined in code (`PermissionCatalog`: code, zh-CN label, menu group, action, `isWrite`, `isSensitive`). The admin page builds the matrix from `GET /admin/permissions`, and the API rejects unknown codes. A new module adds its entries to the catalogue, and the seed migration grants them to the default roles.

**Effective permissions** = (role grants for rank `*`) ∪ (role grants for the user's rank) ∪ (override allows) − (override denies), then minus all `isWrite` permissions if the role is read-only. They are cached per user, keyed by `(user_id, security_stamp, perm_version)`. Any admin change bumps the global `perm_version`, so they are recomputed on the next request.

Default seed (reproduces legacy):

| Area (modules) | Roles | Actions |
|---|---|---|
| 工装: projects, budgets, contracts, receivable_applications, additionals, documents, settlements | 管理, 财务, 业务, 采购, 工程文员, 预算 | read/create/update/link/workflow/export |
| 采购: suppliers, buy_plans, buy_requests, buy_orders, buy_receipts, inventory, deliveries, catalogue | same six | same |
| 家装: projects_retail, quotes_retail | same six + 家装 | same |
| 财务: sales_invoices, buy_invoices, receiveds, payments, reports:finance · 劳务: labor_settlements | 管理, 财务 | same |
| 人事: employees, payrolls, reports:salary | (管理 or 财务) **and** 总监 | same |
| 工具: cost_estimate | the six + 家装 | read/create/update |
| lookups:admin | 管理 + 总监 | |
| users:admin, roles:admin | 管理 + 总监 | |
| `*:delete` on master records | 管理 + 总监 | |
| todo:all (其他人员待办) | 管理 (any rank), any 总监, 采购 — as legacy `admin-index` | |
| feed:recent (最近更新) | 管理 (any rank), any 总监 | |
| 采购查阅 / 工程查阅 / 预算查阅 | read on 采购 / (orders, documents) / 工装 subsets (from the commented legacy menu, Q12) | read only |

Enforcement uses policies on every endpoint and every MCP tool, plus the `IRowAccessPolicy` hook (no row rules in phase 1). The SPA uses `/me` permissions for display only.

### 5.3 Sessions and tokens

* SPA: cookie (`HttpOnly`, `Secure`, `SameSite=Strict`), sliding 8 h (configurable; legacy 9600 s), antiforgery header.
* MCP: `Authorization: Bearer <personal access token>` (scopes `mcp:read` / `mcp:write`, max 90 days). OAuth 2.1 + PKCE per the MCP auth spec later if needed (Q11).
* Lockout after 5 failures for 15 min (`failed_logins`, `locked_until`); an admin can unlock. Login audit.
* Both the cookie and bearer tokens carry the user's `security_stamp`, which is checked on every request (cached 30 s). Disable, password reset or role change take effect at once.

### 5.4 Admin rules (enforced in `AdminService`, not only in the UI)

| Rule | Behaviour |
|---|---|
| Last admin | A change that leaves no **active** user holding both `users:admin` and `roles:admin` (through role or override) is rejected: `409 LAST_ADMIN`. Checked inside the transaction for role matrix saves, user disable/delete, role change and override change. |
| Self-protection | An admin cannot disable, delete, lock or remove `users:admin` from **their own** account (`409 SELF_CHANGE`). |
| Display name | Unique in `app_users`, ≤ the length of the shortest `goldata` column it is written to (measured in P0), and has no leading or trailing spaces. Read-only after first login or when it appears in `historys` / `update_logs` / `user_name` columns. |
| Role name | Unique, no `-`, immutable. A role can be deleted only with 0 users (`409 ROLE_IN_USE`, listing the users). |
| Read-only roles | Saving `isWrite` permissions on a read-only role is rejected, and the matrix greys them out. |
| Delete user | Pre-check: open to-dos where the latest `historys` row of a record has `person_to = display_name` and the record is not closed (same query as `/todos`). If any exist → `409 HAS_OPEN_TODOS` with the list. Delete runs in one transaction: overrides, tokens, user. **No writes to `goldata`.** Optionally (checkbox) remove the name from `lookup_tables['公司员工']`, which goes through the lookup service and `update_logs`. |
| Disable / reset / role change | Rotates `security_stamp`. The cookie and every bearer token are validated against it on each request, so access stops at once. Tokens are also revoked on disable. |
| Add user | Optionally adds `display_name` to `公司员工` (lookup service, audited in `update_logs`). The initial password is generated (16 chars) or typed (≥ 10 chars, not on a common-password list), and shown once. `must_change_password = 1`. |
| Concurrency | Users and roles carry `row_version` → `ETag` / `If-Match` like module records. |
| Audit | Every action → `app_admin_audit` with before/after JSON. Password hashes and tokens are never logged. |
| MCP | No admin tools. `/api/v1/admin/*` rejects bearer tokens and accepts the cookie session only. |
| Coexistence | PHP reads the same users (§5.5). Admin changes apply to both apps. A role that PHP doesn't know gives an empty PHP menu. The admin page shows a hint 「旧系统无对应菜单」 for such roles. |

### 5.5 PHP login bridge (REQ-CMP-8)

The PHP app keeps its session model (`$_SESSION['username' | 'role' | 'rank' | 'valid' | 'timeout']`), its menu and `check_page_permission()` unchanged. Only **where users come from** changes. The change is small: one new file, `app_auth.php`, plus edits to `check_user()` and `check_session()` in `login.php`.

**DB side** (created by the `goldata_app` migrations):

```sql
CREATE VIEW goldata_app.v_legacy_login AS
SELECT u.id, u.login, u.display_name, r.name AS role, u.`rank`, u.password_hash,
       u.is_active, u.locked_until, u.failed_logins, u.must_change_password, u.security_stamp
FROM goldata_app.app_users u JOIN goldata_app.app_roles r ON r.id = u.role_id;

CREATE USER 'goldata_php_auth'@'<php host>' IDENTIFIED BY '<secret>';   -- separate from PHP's goldata account
GRANT SELECT ON goldata_app.v_legacy_login TO 'goldata_php_auth'@'<php host>';
GRANT SELECT (id, login), UPDATE (failed_logins, locked_until, last_login_at)
      ON goldata_app.app_users TO 'goldata_php_auth'@'<php host>';
GRANT INSERT ON goldata_app.app_login_audit TO 'goldata_php_auth'@'<php host>';
```

PHP cannot read tokens, overrides, permissions or the admin audit, and cannot change hashes, roles or activation. `<php host>` is whatever address Web Station's PHP connects from (P0 check; `localhost` if it uses the socket). The account's credentials go in a file **outside the web root** (e.g. `/volume1/web_config/goldata_auth.php`, readable by `http` only), not in `connectDB.php`.

**`check_user($login, $password)`** (replaces the 22 `if` branches; returns the display name or `'nobody'` as today, so `index.php` is unchanged):

```php
$st = $db->prepare('SELECT * FROM v_legacy_login WHERE login = ?');   // prepared, no concatenation
$st->bind_param('s', $login); $st->execute();
$u = $st->get_result()->fetch_assoc();
$hash   = $u['password_hash'] ?? DUMMY_BCRYPT;          // same timing for unknown logins
$pwOk   = password_verify($password, $hash);
$locked = $u && $u['locked_until'] && strtotime($u['locked_until']) > time();
$ok     = $u && $u['is_active'] && !$locked && $pwOk;
app_auth_record($db, $login, $u, $ok);   // failed_logins+1 → lock 15 min at 5; on success reset + last_login_at; app_login_audit(source='php')
if (!$ok) return 'nobody';
session_regenerate_id(true);
$_SESSION['valid'] = true;  $_SESSION['timeout'] = time();
$_SESSION['username'] = $u['display_name'];  $_SESSION['role'] = $u['role'];  $_SESSION['rank'] = $u['rank'];
$_SESSION['app_user_id'] = (int)$u['id'];  $_SESSION['app_stamp'] = $u['security_stamp'];
$_SESSION['app_checked'] = time();  $_SESSION['app_must_change'] = (int)$u['must_change_password'];
return $u['display_name'];
```

**`check_session()`** gets one call at the top, `app_auth_revalidate()`:
* It runs if `time() - app_checked > 60` (one indexed lookup per user per minute).
* It re-reads the user by `app_user_id`. If the user is missing, inactive or locked, or `security_stamp` changed → `session_destroy()` and redirect to `index.php` with the message 「账户已停用或密码已重置，请重新登入」.
* Otherwise it refreshes `role` / `rank`, so role changes apply without logging out.
* A session created before the patch (no `app_user_id`) is sent to log in again once.
* If `app_must_change = 1`, the navbar shows 「请到新系统修改密码」 linking to `https://erp.<domain>/change-password`. PHP itself never changes passwords; there is one place to do that.

**Failure behaviour:** if `goldata_app` can't be reached, PHP login shows 「登入服务暂不可用」. There is no fallback to hard-coded accounts: the hashes are removed from `login.php`, which also removes credential material from the repo. Pages already logged in keep working until revalidation fails.

**Rollout (P0, same day as the `goldata_app` seed):**
1. Run the migrations and seed (the 22 users with their current hashes, so passwords don't change), then create the view and the `goldata_php_auth` account.
2. Deploy `app_auth.php` and the patched `login.php`. Remove the stale copies `login2023.php` (it still contains hashes) and `connectDB-81-230.php` from the server.
3. Smoke test with 3 accounts (管理总监, 财务员工, 采购查阅) in PHP, then in the new app.
4. **Rollback:** restore the previous `login.php` from git (no DB changes to undo).

**Tests** (added to §10 "Compatibility"):

| Action on the admin page | Expected in PHP |
|---|---|
| Add user | Can log in with the initial password; the change-password banner is shown |
| Disable | Login refused; an open session ends within 60 s |
| Reset password / user changes password in new app | Old password refused; new one works |
| Change role or rank | Menu and page rules change on the next page view after ≤ 60 s |
| Delete | Login refused |
| 5 wrong passwords (any mix of PHP / new app) | Locked in both for 15 min; admin unlock works for both |
| `goldata_app` down | 「登入服务暂不可用」, no PHP error page |

---

## 6. REST API

Base `/api/v1`, JSON camelCase, RFC 9457 Problem Details with `code` + zh-CN `title`.

> **As built:** module routes are `/api/v1/modules/{key}…` (so module keys can't collide with `/admin`, `/files`, …). List filters: `q`, `year`, `includeClosed`, `sort=-col,col`, `f.{col}=` (contains), `nf.{col}=` (not contains), `from.{col}=` / `to.{col}=` (dates). `GET /modules` returns the metadata (fields with their Chinese labels) that drives the generic pages.

### 6.1 Module endpoints

`{module}` ∈ `projects, budgets, contracts, receivable-applications, additionals, documents, settlements, projects-retail, quotes-retail, suppliers, buy-plans, buy-requests, buy-orders, buy-receipts, deliveries, sales-invoices, buy-invoices, receiveds, payments, labor-settlements, employees, payrolls, cost-estimates`.

| Method | Path | Purpose |
|---|---|---|
| GET | `/{module}?filter[...]&year=&includeClosed=&sort=&page=&pageSize=` | List + totals |
| GET | `/{module}/export.xlsx?…` | Excel |
| GET | `/{module}/{id}` | Detail (+ETag; items inline) |
| GET | `/{module}/{id}/related` | Related panels |
| GET | `/{module}/{id}/logs` | `update_logs` for the record |
| GET | `/{module}/{id}/history` | 转交 timeline |
| POST | `/{module}[?linkFrom={code}&fromId=]` | Create (optionally create + link) |
| PUT | `/{module}/{id}` | Update (`If-Match`) |
| DELETE | `/{module}/{id}` | Delete (409 with dependents) |
| GET/POST | `/{module}/{id}/files` | List / upload (`conflict=reject\|rename`, `slot=` for retail) |
| POST | `/{module}/{id}/handover` | `{comment, statusTo, personTo}` |

### 6.2 Cross-cutting

| Method | Path | Purpose |
|---|---|---|
| POST | `/auth/login`, `/auth/logout`, `/auth/change-password` | |
| GET | `/me` | user, role, rank, permissions |
| GET | `/lookups/{category}` · `/lookups/categories` | dropdowns (`主要表单名称` children) |
| POST/PUT/DELETE | `/lookups/...` | lookup admin (blocks delete when children exist) |
| GET | `/catalogue/tree` · `/catalogue/search?q=` · `/catalogue/{id}/unit` | product picker |
| GET | `/suppliers/search?q=` | supplier picker (name, number, brand) |
| GET | `/inventory?productId=&destinationId=` | stock |
| GET | `/staff` | `公司员工` |
| GET | `/todos?scope=mine\|others` | |
| GET | `/feed/recent?months=3` | latest `update_logs` row per record (permission-filtered) |
| GET | `/links/{code}/candidates?fromId=&q=` · POST `/links/{code}` · DELETE `/links/{code}/{id}` | §3.6 |
| POST | `/conversions/plan-items-to-order` · `/request-items-to-order` · `/order-items-to-receipt` | |
| GET/DELETE | `/files/{fileId}` | |
| GET | `/reports/{report}` | `contracts, invoices-monthly, invoices-by-supplier, receivable-applications, supplier-invoice-balance, order-receipt, request-order, project-buy-order-items, salaries` |
| GET | `/search?q=` | global search |
| GET | `/health` | DB + file-store check |

### 6.3 Admin (`users:admin` / `roles:admin`, cookie session only)

| Method | Path | Purpose |
|---|---|---|
| GET | `/admin/users?q=&roleId=&rank=&status=` | List |
| POST | `/admin/users` | `{login, displayName, roleId, rank, password?, addToStaffLookup}` → `{user, initialPassword}` (shown once) |
| GET / PUT / DELETE | `/admin/users/{id}` | Detail / edit (`If-Match`) / delete (`?removeFromStaffLookup=`; 409 `HAS_OPEN_TODOS`, `LAST_ADMIN`, `SELF_CHANGE`) |
| GET | `/admin/users/{id}/delete-check` | Open to-dos, tokens, last login (feeds the delete dialog) |
| POST | `/admin/users/{id}/disable` · `/enable` · `/unlock` · `/reset-password` | |
| GET / PUT | `/admin/users/{id}/overrides` | `[{permission, allow}]` |
| GET | `/admin/users/{id}/effective-permissions` | `[{permission, sources:["role","role+rank","override"]}]` + resulting menu |
| GET / DELETE | `/admin/users/{id}/tokens[/{tokenId}]` | List / revoke |
| GET / POST | `/admin/roles` | List (with user counts) / create `{name, description, isReadOnly, copyFromRoleId?}` |
| PUT / DELETE | `/admin/roles/{id}` | Edit description / read-only flag; delete (409 `ROLE_IN_USE`) |
| GET / PUT | `/admin/roles/{id}/permissions` | `{"*":[…], "总监":[…], "员工":[…]}` (`If-Match`); `?dryRun=true` → diff + affected users |
| POST | `/admin/roles/{id}/reset-default` | Seeded roles only |
| GET / POST / DELETE | `/admin/ranks[/{name}]` | |
| GET | `/admin/permissions` | Permission catalogue (groups, labels, isWrite, isSensitive) |
| GET | `/admin/audit?actor=&target=&action=&from=&to=` | Admin audit log |

---

## 7. MCP server

### 7.1 Hosting

`app.MapMcp("/mcp")` with `ModelContextProtocol.AspNetCore` and Streamable HTTP. Bearer token → the same `ICurrentUser` as REST. Rate limit 60 calls/min per token.

```json
{ "mcpServers": { "goldata": { "type": "http", "url": "https://erp.goldata.local/mcp",
  "headers": { "Authorization": "Bearer ${GOLDATA_TOKEN}" } } } }
```

### 7.2 Tools (phase 1)

| Tool | Scope | Input → Output |
|---|---|---|
| `search_records` | read | `{query, modules?[], limit?}` → hits |
| `get_record` | read | `{module, id}` → record + links + files |
| `list_records` | read | `{module, filters?, year?, includeClosed?, sort?, limit≤200}` → rows + totals |
| `get_history` | read | `{module, id}` → 转交 + change log |
| `my_todos` | read | `{scope}` |
| `project_financial_summary` | read | `{projectId \| projectRetailId}` → contract, 签证, settled, 请款, invoiced, received, paid, purchase cost, labour cost |
| `run_report` | read | `{report, params}` (excludes `salaries`) |
| `supplier_balance` | read | `{supplier?}` |
| `stock_lookup` | read | `{query \| productId, warehouse?}` |
| `list_lookup` | read | `{category}` |
| `read_document_text` | read | `{fileId, pages?}` → PDF text (≤50k chars) |
| `handover` | **write** | `{module, id, comment, statusTo, personTo, confirm:true}` |
| `create_link` | **write** | `{code, fromId, toId, shareAmount?, confirm:true}` |
| `create_buy_request_draft` | **write** | `{projectId?, projectRetailId?, items[], comment}` |

* Write tools need the `mcp:write` scope + module permission + `confirm:true`, and are audited with `[MCP]`. **There are no delete tools.**
* `employees`, `payrolls`, `payroll_items`, the salary report and HR file tags are **invisible to MCP** unless `app_settings.mcp.hr.enabled = true`.
* Results use the Chinese column `COMMENT`s as labels. Optional resources: `goldata://lookups/{category}`, `goldata://schema/{table}`.

### 7.3 Data governance

The token page shows a policy notice. An admin can disable `read_document_text` or individual modules for MCP (Q10).

---

## 8. Front end

### 8.0 UI kit and theme (MUI)

One theme in `web/src/theme/index.ts`. No page sets colours, fonts or spacing itself.

```ts
import { createTheme } from '@mui/material/styles';
import { zhCN } from '@mui/material/locale';
import { zhCN as gridZhCN } from '@mui/x-data-grid/locales';
import { zhCN as pickersZhCN } from '@mui/x-date-pickers/locales';

export const theme = createTheme(
  {
    cssVariables: { colorSchemeSelector: 'class' },
    colorSchemes: {
      light: { palette: {
        primary:   { main: brand.primary },     // brand colours (Q16); placeholder #1F4E79
        secondary: { main: brand.secondary },   // placeholder #8A5A2B (the legacy #663300 accent, lightened)
        background: { default: '#F5F6F8', paper: '#FFFFFF' },
      } },
      dark: true,                               // optional; toggle in the user menu, saved per browser
    },
    typography: {
      fontFamily: '"PingFang SC","Microsoft YaHei","Noto Sans SC","Hiragino Sans GB",Roboto,Arial,sans-serif',
      fontSize: 13,                             // dense data entry
    },
    shape: { borderRadius: 6 },
    spacing: 6,
    components: {
      MuiButton:     { defaultProps: { size: 'small', disableElevation: true, variant: 'contained' } },
      MuiTextField:  { defaultProps: { size: 'small', fullWidth: true } },
      MuiSelect:     { defaultProps: { size: 'small' } },
      MuiAutocomplete: { defaultProps: { size: 'small' } },
      MuiTooltip:    { defaultProps: { arrow: true } },
      MuiDataGrid:   { defaultProps: { density: 'compact', disableRowSelectionOnClick: true } },
    },
  },
  zhCN, gridZhCN, pickersZhCN,
);
```

* **Fonts:** a system Chinese font stack, with no web-font download (the app runs on the LAN, and CJK web fonts are large). Amount columns use `font-variant-numeric: tabular-nums` and are right-aligned. Money shows as `¥1,234.56`, and 大写金额 comes from the API (`ChineseAmount`).
* **Status colours:** module metadata maps each Chinese status value (e.g. 待审批 → `warning`, 已完成 → `success`, 作废 → `default` + strikethrough) to an MUI `Chip` colour. Unknown values show as a neutral chip (REQ-CMP-3).
* **Layout:** `AppBar` (logo, global search, 待办 badge, user menu with dark-mode toggle and 修改密码) plus a left `Drawer` with the menu groups from §8.1 (collapsible `List`). The drawer is permanent at ≥ 1200 px and temporary (hamburger) at tablet width (NFR-UX-1).
* **Branding:** `brand.ts` holds the logo, primary/secondary colours and the app title 广泰管理系统. Changing these rebuilds only the web image.

**Component mapping**

| Need | MUI component |
|---|---|
| List pages | `DataGrid` (MIT) with `paginationMode/sortingMode/filterMode="server"`. The totals row is a custom `footer` slot fed by the API's `totals` (aggregation is a Premium feature, so it isn't used). Export is done by the server (`/export.xlsx`, ClosedXML) |
| Item grid (明细) | `DataGrid` with `editMode="cell"`, `processRowUpdate` → live subtotal. Built-in keyboard navigation and row virtualisation (100+ rows). Custom edit cells for product and supplier |
| Product picker (4-level tree) | `Autocomplete` searching leaf paths (`类别 / 细类 / 品名 / 型号`, server search). A 「按分类浏览」 button opens a `Dialog` with `RichTreeView` (lazy-loaded levels). Selecting a leaf fills the unit |
| Supplier / staff / lookup dropdowns | `Autocomplete` (async for suppliers), `Select` for short lookup lists |
| Dates | `DatePicker` (`AdapterDayjs`, `zh-cn`). `0000-00-00` shows as empty |
| Forms | `TextField` and others via React Hook Form `Controller` + Zod. Errors in `helperText` |
| Detail page | `Tabs` (基本信息 · 明细 · 关联记录 · 附件 · 转交历史 · 修改记录). Read view = `Grid` of label/value pairs |
| Attachments | drag-and-drop area + `LinearProgress`, file `List` with preview `Dialog` (pdf.js / image) |
| Link dialog, 409 dialog, confirm | `Dialog` |
| 转交 history | `@mui/lab` `Timeline` |
| Notifications | `useNotify()` (MUI `Snackbar` + `Alert`; success / API Problem Details `title`) |
| Admin permission matrix (§8.3) | `Table` + `Checkbox` with grouped rows (not DataGrid), `ToggleButtonGroup` for the rank layer, `Chip` for sources |
| Edit drawers (users) | `Drawer anchor="right"` |

Only MIT MUI X packages are used (NFR-LIC-1). Pinned columns, multi-column sort and tree rows (Pro features) are therefore **not** used. Wide lists rely on column visibility presets per module instead, and the product tree uses `RichTreeView` in a dialog.

### 8.1 Navigation (mirrors the current Bootstrap navbar)

**待办事项** · **工装** (项目, 预算, 合同, 请款, 签证, 工程文件, 结算) · **家装** (项目, 报价) · **采购** (供应商名录, 采购计划, 请购单, 订单, 收货单, 库存, 出库单, 产品分类表) · **财务** (销项发票, 进项发票, 收款, 付款, 财务分析报告) · **劳务** (人工费结算单) · **人事** (员工档案, 工资发放, 工资明细汇总, 工资报告) · **工具** (安装成本计算器, 系统说明) · **设置** (下拉选项, 用户与权限). Items are filtered by permission.

### 8.2 Pages

* `ListPage` (MUI `DataGrid`, server paging, filter bar state in the URL, totals row, export) and `DetailPage` (tabs: 基本信息 · 明细 · 关联记录 · 附件 · 转交历史 · 修改记录), both generated from module metadata shared with the API through OpenAPI.
* Item grid: editable `DataGrid` with the product `Autocomplete` + tree dialog (4-level tree), supplier autocomplete, unit auto-fill, live subtotal, and keyboard entry. AG Grid Community stays the fallback if the free Data Grid proves too limited for 100+ row data entry (P0 spike).
* Attachments: drag-and-drop, a conflict dialog, a pdf.js/image preview.
* Link dialog: candidates, allocated/remaining, `share_amount`.
* 409 dialog: your changes vs the server values.
* Cost estimator: a dedicated form whose state is (de)serialised to the legacy `postArray` JSON.

### 8.3 设置 ▸ 用户与权限 (admin page)

Three tabs, each shown only with the matching permission.

**用户** (`users:admin`)
```
[搜索 登录名/姓名] [角色 ▾] [职级 ▾] [状态 ▾]                         [+ 新增用户]
登录名   姓名     角色   职级  状态    最后登录          令牌  操作
chendx  陈冬霞   财务   员工  ●启用   2026-11-01 09:12   1    编辑 · 重置密码 · 停用 · ⋯(解锁/删除)
yezl    叶仲霖   管理   总监  ●启用   2026-11-02 08:40   0    编辑 · 重置密码 · ⋯
caigou  采购部查阅 采购查阅 员工 ○停用  —                 0    启用 · ⋯
```
* **新增用户 / 编辑** opens a drawer with sections:
  * 基本信息: login, display name with an AutoComplete from `公司员工`; read-only when locked by REQ-ADM-3, with a tooltip explaining why.
  * 角色 / 职级.
  * 个别权限: the permission tree with a 继承/允许/禁止 segmented control per item.
  * 有效权限: read-only list with source tags, plus a "按此用户预览菜单" button.
  * API 令牌: list with revoke.
* **重置密码** shows the generated password once, with a copy button.
* **删除** dialog: runs `delete-check` first. With open to-dos it shows the list (links to the records) and recommends 停用. With none, it asks the admin to type the login, with a checkbox for "同时从「公司员工」下拉列表移除".

**角色与权限** (`roles:admin`)
```
┌ 角色 ────────────┐ ┌ 财务 (5 用户)   适用职级: ( 全部 | 总监 | 员工 )      [复制] [恢复默认] [保存] ┐
│ 管理       3     │ │            查看 新增 修改 删除 关联 转交 导出 上传 删附件                      │
│ 财务       5  ◀  │ │ ▾ 工装                                                                         │
│ 采购       3     │ │   项目      ☑    ☑    ☑    ☐    ☑    –    ☑    ☑    ☐                        │
│ 采购查阅 🔒 1    │ │   合同      ☑    ☑    ☑    ☐    ☑    ☑    ☑    ☑    ☐                        │
│ …               │ │ ▾ 人事  ⚠ 敏感                                                                 │
│ [+ 新增角色]     │ │   员工档案  (总监层 ☑)                                                          │
└─────────────────┘ │ 特殊权限: ☑ 财务报告 ☐ 工资报告 ☐ 其他人员待办 ☐ 用户管理 ☐ 角色管理 ☑ MCP读 …  │
                    └────────────────────────────────────────────────────────────────────────────────┘
```
* The rank selector switches between the `*`, 总监 and 员工 layers. A box granted by the `*` layer shows as checked and disabled on the rank layers.
* Row and column headers toggle a whole row or column. "–" means the action doesn't exist for that module.
* **保存** calls `dryRun` and shows the diff + affected users. Sensitive grants (人事, 工资报告, 用户/角色管理) need a second confirmation.
* Read-only roles (🔒) grey out write columns. **删除角色** is enabled only at 0 users.

**操作日志** (`users:admin` or `roles:admin`): a filterable table of `app_admin_audit` with a before/after diff viewer.

---

## 9. Deployment: Docker on Synology DSM

The React UI and the C# API run as two Docker containers in one **compose project** on the Synology NAS running **DSM 7.1** with Synology's **Docker** package. There is no DB container: the API connects to the existing MariaDB.

> **Inference to confirm (Q3/Q8/Q14):** port **3307** and version **10.3.32** are the defaults of Synology's *MariaDB 10* package on DSM 7.1, and DSM 7.1 Web Station offers PHP 8.0, which matches the legacy app. So `192.168.168.235` is very likely the NAS itself, and the PHP app probably runs in **Web Station** with `pdf_folder` in a shared folder such as `/volume1/web/…`. This section assumes that. §9.9 covers the case where the DB or files live elsewhere.

### 9.0 DSM 7.1 constraints (design decisions)

DSM 7.1 has the older **Docker** package, not Container Manager (which arrived in DSM 7.2). That changes how the project is run:

| DSM 7.1 fact | Consequence for the design |
|---|---|
| Docker package GUI has containers, images, registry and logs, but **no compose "Projects"** | The stack is started with the bundled **`docker-compose` (v1.x)** over SSH, wrapped in `deploy/goldata.sh`. The GUI is used only for viewing status and logs. |
| `docker-compose` v1 doesn't accept the top-level `name:` key | The project name comes from `COMPOSE_PROJECT_NAME=goldata` in `.env`. The compose file uses only keys that work in compose v1.27+ **and** v2, so it still runs unchanged after a later DSM 7.2 upgrade. |
| Docker Engine **20.10.x** (Synology build, older than 20.10.10) | Its default seccomp profile blocks the newer `clone3` syscall with `EPERM`. Recent **glibc** images (Ubuntu 24.04 / Debian 12, incl. the .NET chiseled images) can then fail to start threads. → Runtime images are **Alpine (musl)**, which doesn't use `clone3`. Fallback, if a glibc image is ever needed: `security_opt: seccomp=./seccomp-dsm71.json` (the Docker default profile with `clone3` → `ENOSYS`). **Never** `seccomp=unconfined`. |
| Docker package is offered for x86_64 models (plus a few others) | Build `linux/amd64`. `arm64` is built only if the model needs it (Q14). |
| Older kernel (4.4 on many models; 5.10 on newer ones) | Nothing kernel-specific is used. P0 smoke test on the real NAS. |
| Compose `depends_on: condition: service_healthy` and `env_file` behave slightly differently in compose v1 (quotes are kept literally) | Verified in the P0 smoke test. Fallback: plain `depends_on` (nginx retries the upstream). The env-file format is restricted so v1 and v2 read it identically (§9.4). |
| DSM 7.1 receives only limited updates | Plan an upgrade to DSM 7.2 (Container Manager, Docker 24, compose v2) after go-live. No change to images or compose file needed (Q14). |

### 9.1 Topology

```
 Browser / AI client
        │ HTTPS 443  https://erp.<domain>
┌───────▼──────────────────────── Synology NAS (DSM 7.1) ───────────────────────────────┐
│ DSM reverse proxy (Login Portal ▸ Advanced ▸ Reverse Proxy), TLS cert from DSM        │
│        │ http://127.0.0.1:8085                                                         │
│ ┌──────▼─────────── Docker compose project "goldata" (bridge net 172.30.10.0/24) ─────┐│
│ │ goldata-web  (nginx, unprivileged)          goldata-api (ASP.NET Core, .NET 10)     ││
│ │  /            → React build (static)         :8080  REST /api/v1/*  ·  MCP /mcp     ││
│ │  /api/, /mcp  → proxy ─────────────────────▶ /data/docroot  ◀── bind mount          ││
│ └──────────────────────────────────────────────────┬──────────────┬──────────────────┘│
│                                       TCP 3307 (LAN IP)           │                    │
│ MariaDB 10 package  ◀─────────────────────────────┘     /volume1/web/<site>/pdf_folder│
│  goldata · goldata_app                                   (shared with PHP Web Station) │
│ Web Station: legacy PHP (coexists until P4)                                            │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

* **One origin.** The SPA, `/api` and `/mcp` are served from the same host name, so there is no CORS. Cookies use `SameSite=Strict; Secure`.
* Only `goldata-web` publishes a port, and only on `127.0.0.1`. The API is reachable only via the internal network, and nothing bypasses the DSM TLS front door.

### 9.2 NAS prerequisites

| Item | Requirement |
|---|---|
| DSM | **7.1** with the **Docker** package installed (Package Center), SSH enabled for admins (Control Panel ▸ Terminal), used only for deploys. |
| CPU | x86_64 (the Docker package's main platform) → `linux/amd64` images. `arm64` is built only if the model needs it. Confirm the model and `docker version` (Q14). |
| RAM | About 1 GB free for the containers (API ≈ 300–600 MB, nginx < 50 MB). 4 GB+ total NAS RAM recommended because MariaDB and Web Station run on the same box. |
| MariaDB 10 | "Enable TCP/IP connection" on (port 3307). A new account `goldata_api` (see 9.5). |
| Shared folders | `docker` (project files) and the existing web share holding `pdf_folder`. |
| Host name + cert | e.g. `erp.<company domain>` in local DNS (or the router). Let's Encrypt via DSM if it is publicly resolvable, otherwise a certificate from an internal CA imported into DSM. |

### 9.3 Images

| Image | Base | Contents |
|---|---|---|
| `goldata-api` | build: `mcr.microsoft.com/dotnet/sdk:10.0-alpine`; run: `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` + `apk add icu-libs icu-data-full tzdata`, `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false` | Published API (`Goldata.Api` + MCP). Alpine/musl avoids the DSM 7.1 Docker seccomp issue (§9.0). ICU is added for zh-CN formatting and sorting, tzdata for Asia/Shanghai. Runs non-root. |
| `goldata-web` | build: `node:22-alpine` (`vite build`); run: `nginxinc/nginx-unprivileged:1.27-alpine` | `web/dist` + `nginx.conf` (SPA fallback, proxy, upload limit). |

* Built in CI with `docker buildx build --platform linux/amd64` (add `linux/arm64` only if needed), tagged with **semver + git SHA** (never deploy `latest`), and pushed to a **self-hosted registry**: **Gitea** (MIT) in its own container on the NAS gives git hosting, Gitea Actions CI (`act_runner`) and an OCI container registry in one. On the NAS: `sudo docker login git.<domain>` once with a read-only token (the Docker package GUI's registry settings also work). GitHub/GHCR's free tier also works, but it is a hosted service and not required (NFR-LIC-2).
* Offline alternative: `docker save` → copy to the NAS → `sudo docker load -i …` (or Docker package ▸ Image ▸ Add ▸ From file).
* The images contain no secrets and no connection strings.

### 9.4 Project folder and settings: ONE env file per environment

All settings of an environment, secrets included, are in **one** `KEY=value` file. Templates are committed and filled-in copies never are:

| Environment | Template | Lives at | Read by |
|---|---|---|---|
| local (debug) | `deploy/env/local.env.example` | `deploy/env/local.env` (git-ignored) | `dotnet run` (launch profile `local` → `GOLDATA_ENV_FILE`), Vite proxy (`API_URL`), `compose.dev.yaml` (`--env-file`) |
| UAT | `deploy/env/uat.env.example` | `/volume1/docker/goldata-uat/.env` | docker-compose (`${…}` + `env_file:` into the API), `goldata.sh` |
| prod | `deploy/env/prod.env.example` | `/volume1/docker/goldata/.env` | docker-compose, `goldata.sh` |

```
/volume1/docker/goldata/          (owner root, mode 750; included in Hyper Backup)
├─ compose.yaml
├─ goldata.sh
├─ .env                           ← THE settings file: root:root 600 (tags, ports, subnet, DB, documents, secrets, PHP, backups)
├─ .env.prev                      ← written by `goldata.sh upgrade` (rollback)
└─ data/
   ├─ keys/                       ← ASP.NET Data Protection keys (cookies/antiforgery survive restarts)
   └─ logs/                       ← Serilog rolling files, 14 days
```

* **One mapping in code:** `Goldata.Api/GoldataEnv.cs` turns the env keys into .NET configuration. It builds the three connection strings from `DB_HOST/DB_PORT/DB_LEGACY_NAME/DB_APP_NAME/DB_*_USER/DB_*_PASSWORD` (legacy `utf8` + zero dates, app `utf8mb4`), and maps `DOCUMENTS_*`, `TOKEN_PEPPER`, `PUBLIC_BASE_URL`, `SESSION_*`, `LOCKOUT_*`, `LOG_*`, etc. `appsettings.json` only holds defaults. Variables already in the process (shell, compose `environment:`) win over the file.
* **Compose:** `env_file: .env` passes the file to the API container. `environment:` only overrides the in-container paths (`/data/docroot`, `/data/keys`, `/data/logs`) and `FORWARDED_NETWORKS=${NETWORK_SUBNET}`. Ports, subnet, project name, image tags and `DOC_ROOT/DOC_UID/DOC_GID` are `${…}` from the same file.
* **PHP bridge:** `goldata.sh php-config` **generates** `PHP_AUTH_CONFIG` (`/volume1/web_config/goldata_auth.php`, owner `http`, 600) from the same `.env`. `goldata.sh backup-db` takes `DB_BACKUP_*` from it via a temporary root-only option file.
* **Format** (docker-compose v1 reads values literally): `KEY=value`, no quotes, no spaces, comments on their own line, passwords from `[A-Za-z0-9-_.]`. `goldata.sh` reads the file line by line (no `source`), so `$` is never expanded. A unit test checks the three templates against these rules, and that UAT and prod differ in project, port, subnet, DB names, accounts, documents, pepper, PHP config and backup folder.
* **Trade-off (accepted for consolidation):** secrets are environment variables of the API container (visible to root via `docker inspect`), and the container also receives keys it doesn't use (backup/PHP passwords). Mitigation: `.env` is root-only, Docker is root-only on the NAS, and each account has the least privileges (`db/ops`).

The full compose file is `deploy/compose.yaml`.

`nginx.conf` (key parts):

```nginx
client_max_body_size 85m;                       # 80 MB uploads + multipart overhead
location /api/ { proxy_pass http://api:8080; proxy_read_timeout 300s; proxy_request_buffering off; }
location /mcp  { proxy_pass http://api:8080; proxy_buffering off; proxy_cache off;
                 proxy_read_timeout 3600s; proxy_http_version 1.1; }   # Streamable HTTP / SSE
location /     { try_files $uri /index.html; }  # SPA; hashed assets cached 1 year, index.html no-cache
```

The API uses `ForwardedHeaders` (trusting only the compose subnet), so it sees the real `https` scheme and client IP for audit and rate limiting. The DSM reverse proxy rule adds `X-Forwarded-For`/`X-Forwarded-Proto`, and its timeout is raised to 3600 s for `/mcp`.

### 9.5 Database connectivity

* Connection string host = the production NAS `192.168.168.235`, port 3307 (Q3 resolved). `192.168.21.175` is the local/test server (DB copy, `pdf_folder` copy). From a bridge-network container the DB sees the source address as `172.30.10.x`, **not** localhost:
  ```sql
  CREATE USER 'goldata_api'@'172.30.10.%' IDENTIFIED BY '<DB_API_PASSWORD from .env>';
  GRANT SELECT, INSERT, UPDATE, DELETE ON goldata.*     TO 'goldata_api'@'172.30.10.%';
  GRANT SELECT, INSERT, UPDATE, DELETE ON goldata_app.* TO 'goldata_api'@'172.30.10.%';
  ```
  A separate `goldata_migrator` account (DDL on `goldata_app` only) is used by the DbUp migration job and never by the running API.
* DSM firewall: allow `172.30.10.0/24 → 3307`. Do not open 3307 to the whole LAN beyond what PHP needs.
* Rejected alternative: mounting the MariaDB unix socket (`/run/mysqld/mysqld10.sock`). It ties the app to DSM-internal paths that package updates can change.
* `goldata_app` is created on the same MariaDB instance (REQ-CMP-1). It is migrated by a one-shot container: `goldata.sh migrate` (= `docker-compose run --rm api --migrate`).

### 9.6 Document folder (shared with PHP)

* Bind mount the folder that contains `pdf_folder` at `/data/docroot` (`Documents:RootPath`), read-write.
* **File ownership must match PHP.** Web Station's PHP writes as the DSM user `http`. Confirm with `id http` over SSH; it is usually `1023:1023`, but this must be verified. The API container runs as the same UID:GID (`user:` above), so files written by either app can be read, overwritten and deleted by the other.
* Synology ACLs on the shared folder are enforced inside containers too. Give that user Read/Write on the share, and test in P0 (upload in the new app → open and delete in PHP, and the reverse).
* Names are stored as UTF-8 NFC. Files dropped in via SMB from macOS can arrive as NFD; the store looks up both forms (risk table).
* Recycled files go to `pdf_folder/_recycle/…` on the same volume (a rename, no copy). Block `_recycle` from the PHP site.

### 9.7 Operations

| Task | How |
|---|---|
| First install | Follow **`deploy/DEPLOYMENT.md`** step by step. In short: install the Docker package → create `/volume1/docker/goldata` with `compose.yaml`, `goldata.sh`, `.env` and the secrets files → SSH: `sudo ./goldata.sh migrate`, `ops/0002`, `sudo ./goldata.sh import-users`, then `sudo ./goldata.sh up` (= `docker-compose up -d`) → add the DSM reverse proxy rule + certificate. |
| Upgrade | `sudo ./goldata.sh upgrade 1.5.0-ab12cd3`: writes the tags to `.env` (keeping the old ones in `.env.prev`), pulls, runs `migrate`, then `docker-compose up -d` and waits for `/health/ready`. DB migrations run first via `--migrate`. They are additive and backward-compatible, so the previous image still works. |
| Rollback | `sudo ./goldata.sh rollback` restores `.env.prev` and recreates. No auto-update tools (no Watchtower) in production. |
| Logs | Docker package ▸ Container ▸ Details ▸ Log (stdout, JSON), `docker-compose logs`, plus `data/logs`. Each request ID is also returned in the `X-Request-Id` response header. |
| Health | `/health/live` (process) and `/health/ready` (DB `SELECT 1` on both schemas + `pdf_folder` writable). A DSM **Task Scheduler** job (root, every 5 min) runs `goldata.sh check`: if `/health/ready` fails 3 times, it sends a DSM notification e-mail. `restart: unless-stopped` brings the containers back after a NAS reboot (the Docker package starts at boot). |
| Backup | **Nightly** DSM Task Scheduler job: `mysqldump --single-transaction` of `goldata` and `goldata_app` (MariaDB 10 package binary) to a backup share. **Snapshot Replication** on the `pdf_folder` share (hourly, kept 30 days; protects against ransomware and mistaken deletes). **Hyper Backup** of the dumps, `pdf_folder` and `/volume1/docker/goldata` to an offsite target. A monthly restore test into a scratch DB + compose project (NFR-AVL-1). |
| Time | `TZ=Asia/Shanghai` in both containers. The app also converts explicitly to Asia/Shanghai for `yyyyMM` folders and dates. |

### 9.8 Security on the NAS

* Containers run non-root with a read-only root filesystem, `cap_drop: ALL` and `no-new-privileges`. They are never run in privileged mode, and the Docker socket is never mounted.
* Port 8085 is bound to `127.0.0.1`. DSM firewall rules allow 443 from the LAN only.
* Remote access (Q11): prefer **DSM VPN Server or Tailscale** over a router port-forward. If it must be public: Let's Encrypt, DSM auto-block, 2FA on the DSM admin, and never QuickConnect for this site.
* The legacy PHP stays on Web Station during coexistence. Apply the P0 hot-fixes. Deny direct HTTP access to `pdf_folder`: if Web Station uses Apache 2.4, a `pdf_folder/.htaccess` with `Require all denied` (needs `AllowOverride`). With nginx, a Web Station custom rule. Verify that the URL returns 403.

### 9.9 Variants

* **DB on another server:** only the connection string host changes. The MariaDB grant uses the NAS LAN IP (outgoing NAT), not `172.30.10.%`.
* **`pdf_folder` on another server:** use a compose CIFS volume (`driver_opts: type=cifs, o=uid=…,gid=…,iocharset=utf8,vers=3.0`) instead of the bind mount, with credentials in a root-only file.
* **Local development:** `compose.dev.yaml` adds `mariadb:10.3.32` loaded from the *sanitised* dump and a temp `pdf_folder`. The same images, with Vite dev server optional.

---

## 10. Testing and acceptance

| Level | What |
|---|---|
| Unit | LegacyCompat (ENT_COMPAT round-trip, zero dates, utf8 guard), ListQuery whitelist, path builder, permission matrix, payroll formula, ChineseAmount, cost-estimator formulas |
| Integration | All endpoints on a MariaDB 10.3 Testcontainer with a **sanitised** dump; file store on a temp dir |
| Compatibility | PHP login bridge test table (§5.5). Per module: write in the new app → view/edit in PHP, and the reverse. `show_logs()` displays new-app audit rows correctly |
| Report reconciliation | Legacy SQL vs new SQL for 3 periods; equal to 0.01 |
| Admin | Last-admin and self-change guards; delete blocked by open to-dos; disable kills an active session and an MCP token on the next request; read-only role rejects write grants; matrix diff/dry-run; admin endpoints reject bearer tokens; every action lands in `app_admin_audit` |
| Security | ZAP baseline; endpoint × role matrix test; `*查阅` write-denial on every write endpoint; path traversal; HR invisibility through search/todo/MCP |
| E2E | Playwright: login with a legacy password → forced change; contract CRUD + upload + 转交; order with items + receipt conversion; payment allocation; delivery → stock change |
| MCP | Tool contract tests; permission denial for a 采购 token on finance/HR tools |

**Definition of done per module:** parity checklist signed off by the key user, compatibility test passed, PHP pages for the module set read-only.

---

## 11. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Dual writes without FKs | Orphans / inconsistent links | Dependency checks, ETag concurrency, short coexistence per module |
| Incomplete legacy audit (~40 paths bypass `commit_sql`) | Can't fully reconstruct history | Accept for the past; new app audits 100% |
| HTML-entity drift | Garbled text in one app | ENT_COMPAT encode-on-write + compatibility tests |
| File ownership / Synology ACL / NFC-NFD names between the API container and Web Station PHP | Files unreadable or undeletable by one app | Run the container as PHP's UID:GID (§9.6); P0 round-trip test with Chinese names |
| NAS resources shared by MariaDB, Web Station and the containers | Slow pages at peak | `mem_limit`s, NFR-PERF measurements on the real NAS in P0; ARM/low-RAM model → review (Q14) |
| DSM / Docker package updates change Docker behaviour | Outage after a DSM update | Pin image tags; health notifications; re-run smoke test after each DSM update |
| Old Docker 20.10 on DSM 7.1 (seccomp/`clone3`, compose v1 quirks) | Containers fail to start or behave differently | Alpine images; `seccomp-dsm71.json` fallback; compose file limited to v1+v2-compatible keys; P0 smoke test; DSM 7.2 upgrade planned (§9.0) |
| `double` money rounding | Report mismatches | Round in SQL as legacy; reconciliation tests |
| PHP login depends on `goldata_app` | `goldata_app` outage blocks new PHP logins | Same MariaDB instance as `goldata` (if it is down, PHP is down anyway); clear error message; rollback = previous `login.php` |
| Unauthenticated legacy endpoints stay exposed during coexistence | Data tampering, file leakage | Hot-fix the PHP app in P0: add `check_session()` to `crud_action.php`, `dropdown/ajax/*`, `delete_file.php`, and protect `pdf_folder` in the web server |
| HR data through AI | Privacy | HR off for MCP by default; audit |
| Leaked credentials in the GitHub repo | Unauthorised DB access | Rotate the DB password; private repo; purge history |
| Large legacy pages hide business rules (cost estimator 2.1k lines, orders 1k, payroll 1k) | Missed logic | Rule extraction + tests per page during each phase |

---

## 12. P0 checklist (first 1–2 weeks)

1. Security hot-fixes on the PHP app (risk table above). Rotate the DB password. Make the repo private.
2. Create the `goldata_api` DB user. Read every table from .NET without conversion errors.
3. On the DSM 7.1 NAS, record `docker version` / `docker-compose version` / kernel, then start the compose project with a hello-world API image (Alpine .NET 10). Prove: threads start (no `clone3` failure), healthcheck + `depends_on` + secrets work under compose v1, container → MariaDB 3307 with the `172.30.10.%` grant, the reverse proxy + TLS, an 80 MB upload through both proxies, `/mcp` streaming through the DSM proxy.
4. Mount `pdf_folder`. Sample 500 `file_path` rows and report missing files and tag/path mismatches. Round-trip a Chinese-named upload with PHP.
5. Import the 22 users and 10 roles from `login.php`. Verify a bcrypt login and the forced password change. Deploy the PHP login bridge (§5.5) and run its test table. Ship the 用户 tab (list, add, disable, reset password) and the 角色与权限 matrix, and check the seeded matrix reproduces today's menu for each of the 22 users (preview-as-user).
6. Build the module registry for 2 modules (contracts, buy_orders) end-to-end, including links, files, audit and 转交. Run the compatibility test against PHP.
7. Stand up `/mcp` with `search_records` + `get_record`, and connect from Claude Code.

---

## 13. Implementation status (2026-11-02)

| Area | Built | Still to do |
|---|---|---|
| Solution, CI | 5 projects + tests; central package versions with a banned list; Gitea Actions workflow (tests, NuGet + npm licence allow-lists, image build on tags) | Run CI on the real Gitea runner |
| DB | `db/` scripts ops 0001–0003, migrations 0001–0004; DbUp runner `--migrate` (journals in `goldata_app`) | Run on MariaDB 10.3.32 (no DB on the build machine) |
| Auth | Cookie session + security-stamp check per request, antiforgery header, lockout, login audit, forced first password change, bcrypt `$2y$12$` (PHP-compatible), legacy `$2y$10$` verify + rehash | Rate limits tuned on the NAS |
| Users / roles (M29) | All §6.3 endpoints with §5.4 rules (last admin, self-change, open to-dos, name lock, read-only roles, dry-run diff, 恢复默认, audit); React 用户 / 角色与权限 / 操作日志 tabs | Ranks admin UI (API exists) |
| Legacy read | Generic list (paging, sort, search, year, closed toggle, totals), detail (+ items, ETag), change log, 转交 history, to-dos (mine/others), global search, lookups, staff | Reports, inventory, catalogue |
| Legacy write (P1) | Generic create / edit / delete / 转交 for all modules with a legacy edit page. Fields, dropdown categories and required flags are **generated from the legacy `*-edit.php`** (`ModuleFields.cs`). Values written exactly like PHP (ENT_COMPAT, `0000-00-00`, `0`; DB runs STRICT_TRANS_TABLES). ETag/If-Match (409 on stale, 428 without). Create-from-parent as `insert.php` (项目→合同/预算, 合同→请款/签证/结算). Delete blocked while any FK/link/item/attachment references the record. Related-records panel. Every write → `update_logs`; 转交 → `historys` + status. Verified on the test DB (service + HTTP level, test rows removed) | Delivery / payroll / quote grids, catalogue maintenance (M17), inventory view, reports (M22) |
| Line items (P2) | `ItemSpecs`: editable grids for 订单, 采购计划, 请购单, 收货单, 人工费结算单 with exactly the legacy grid columns; `PUT /modules/{key}/{id}/items` (If-Match) `{items:[{id?, values}], deletedIds}` — explicit deletes, server `sub_total = qty × price`, header `amount = Σ sub_total` for orders/receipts (legacy `$_POST['amount'] = $table_total_amount`), item text stored raw like the legacy grid, unit auto-filled from the product. Catalogue (`CatalogueService`, 4 levels under `产品类别`, unit = leaf `value`): `GET /catalogue/search?q=` (all words in 类别/细类/品名/型号), `/catalogue/children?parentId=`, `/catalogue/products?ids=`; detail lines show the catalogue path. Conversions: `POST /conversions/plan-items-to-order`, `/request-items-to-order` (`variant=report` = 请购报表 copies 建议单价), `/order-to-receipt` (new receipt + `buy_receipt_order` share 0 + all order lines, as `insert.php`). Legacy `buy_order_item-InsertTo-buy_receipt_item.php` is a stale copy of the plan conversion and was not ported. Verified on the test DB (service + HTTP) | |
| Links (P1b) | All 30 legacy link types in `LinkCatalog` (16 share-table with `share_amount`, 14 FK incl. 出库单's "other project = 0" and 子项目→父项目 with cycle guard), written exactly as `link-create.php`; unlink = delete link row / `SET NULL` as `delete_row.php`. `GET /modules/{key}/{id}/links` (out + incoming allocations, allocated vs. amount), `GET /links/{code}/candidates?fromId=&q=&includeClosed=` (already-allocated per candidate, already-linked flag), `POST /links/{code}` `{fromId,toId,shareAmount}`, `DELETE /links/{code}/{fromId}?linkId=`. Duplicate pair → 409 `ALREADY_LINKED`. Permission `{from}:link` + `{to}:read`. UI: 关联记录 tab with 未分摊/超出 badges and a search-and-link dialog defaulting the share to what is open on both sides. Verified on the test DB (service + HTTP, all 30 codes) | |
| Files | List, download (RFC 5987 names, ranges), upload (validation, magic bytes, conflict reject/rename, temp + rename, `file_path` + audit), delete to `_recycle` | Round-trip test with PHP on the real share |
| MCP | `/mcp` Streamable HTTP (stateless), bearer PAT, scopes, HR hidden by default; tools `list_modules`, `search_records`, `get_record`, `list_records`, `get_history`, `my_todos`, `list_lookup`; token page in the UI | Write tools (P3), `read_document_text` |
| PHP | `legacy-php/overlay`: login bridge, revalidation, banner, hot-fixes for `crud_action.php`, `delete_file.php`, `dropdown/ajax/*` | `php -l` and the test table on the NAS |
| Deploy | Alpine API image, nginx web image, `compose.yaml` (compose v1+v2), `goldata.sh`, `seccomp-dsm71.json`, runbook | P0 smoke test on DSM 7.1 |
| Tests | 80 unit + pipeline tests (ENT_COMPAT, list SQL whitelist/params, permissions incl. legacy rules, 0004 drift, login.php parser, bcrypt, paths/NFC/traversal, 401/CSRF/MCP guards) | Integration tests on a MariaDB 10.3 Testcontainer (needs Docker) |

### 9.10 UAT beside prod

UAT runs on the same NAS as a second compose project (`/volume1/docker/goldata-uat`, `COMPOSE_PROJECT_NAME=goldata-uat`, port 8086, subnet `172.30.11.0/24`) with its own databases on the same MariaDB (`goldata_uat`, `goldata_app_uat`), its own accounts (`goldata_uat_*`, **no rights on prod**, `db/ops/0004`), its own copy of `pdf_folder` outside the `web` share (`DOC_ROOT=/volume1/goldata-uat`, never served by Web Station) and its own `TOKEN_PEPPER` (prod tokens don't work on UAT). `ASPNETCORE_ENVIRONMENT=Staging`. Data is refreshed by copying prod (runbook §16); because UAT then holds production personal data, it is protected like prod (Q21).

