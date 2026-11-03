# 001 — Requirements: GoldataWeb2 (广泰管理系统 modernisation)

| | |
|---|---|
| Status | **Draft for review — rev 3** (rev 2: current production source; rev 3: Synology Docker deployment) |
| Date | 2026-11-02 |
| Source system | `goldataWeb1/web_php_20261020` (PHP 8.0 + MariaDB 10.3.32, Bootstrap 5 UI, ~140 pages) |
| Target | React web app + C# REST API + MCP server |
| Companion doc | [002-spec.md](002-spec.md) |

### Revision history
| Rev | Date | Change |
|---|---|---|
| 1 | 2026-11-02 | First draft, based on an older (2019) snapshot of the PHP code |
| 2 | 2026-11-02 | Re-analysed against the current source: new modules (签证, 库存, 出库, 产品分类, 人事/工资, 成本计算器, 家装报价, lookup admin), the real role × rank model, the generic `insert.php`/`link-create.php`/`commit_sql()` framework, new DB host, new upload limits. Old Q1 resolved. |
| 3 | 2026-11-02 | Deployment target fixed: React UI and C# API as Docker containers on the Synology NAS (DSM Container Manager). Added NFR-DEP-1…8, Q14; Q8 narrowed. |
| 3.1 | 2026-11-02 | Added M29 用户与权限 (user, role and permission admin page): REQ-ADM-1…14, Q15. |
| 3.2 | 2026-11-02 | Q15 decided: the PHP app reads the same user list (REQ-CMP-8). |
| 3.3 | 2026-11-02 | UI kit changed from Ant Design to **MUI** with a company theme (NFR-UX-2). Q16 added. |
| 3.4 | 2026-11-02 | **Free and open source only** (NFR-LIC-1/2). MUI X Pro ruled out (Q16 narrowed to brand colours). Self-hosted registry/CI option. |
| 3.5 | 2026-11-02 | NAS runs **DSM 7.1** (Docker package, no Container Manager). NFR-DEP-1/2/9 updated, Q14 narrowed. |
| 3.6 | 2026-11-02 | REQ-CMP-9: every SQL change is a numbered script in `db/`. |
| 3.7 | 2026-11-02 | NFR-DEP-10: deployment runbook `deploy/DEPLOYMENT.md`, kept current. NFR-DEP-5 wording on secrets file permissions. |
| 3.8 | 2026-11-02 | Implementation started (spec §13). New decisions to confirm: Q17–Q20. |
| 3.9 | 2026-11-02 | One env file per environment (local / UAT / prod): NFR-DEP-5 rewritten, NFR-DEP-11 (UAT). Q21. |
| 3.14 | 2026-11-02 | REQ-EDIT-3 (grids for 订单/计划/请购/收货/人工费) and REQ-LINK-3 implemented; delivery (Q5), payroll and quote grids still open. |
| 3.13 | 2026-11-02 | REQ-LINK-1/2 implemented: all 30 link types with allocated/unallocated amounts. |
| 3.12 | 2026-11-02 | P1 started: create/edit/delete/转交 for all modules with a legacy edit page (REQ-EDIT-1/2/4/5/6, REQ-WF-1/2, REQ-DEL); links, items, reports still open. |
| 3.11 | 2026-11-02 | Q3 resolved: prod 192.168.168.235, local/test 192.168.21.175. |
| 3.10 | 2026-11-02 | Logins may be 2–32 chars (legacy account `ci`); migration `0005`. |

---

## 1. Background

广泰 runs an internal ERP/OA web app written in plain PHP (no framework) against a MariaDB database `goldata`. It covers commercial projects (工装), retail/home projects (家装), contracts, change orders (签证), settlement, billing, invoicing, payments, procurement, inventory, labour settlement, HR/payroll and engineering documents. Uploaded files live on disk under `pdf_folder/…` in the PHP web root, and the `file_path` table indexes them.

The goal is to rebuild the front end and back end on a modern stack **without migrating data**: the new system must read and write the **existing database and the existing document folders**, so the legacy app and the new app can run side by side during cutover.

### 1.1 What the analysis found

| Area | Finding |
|---|---|
| Database | MariaDB 10.3.32, db `goldata`, 52 tables, all InnoDB, charset `utf8` (3-byte, **not** utf8mb4). The current code connects to **`192.168.168.235:3307`** (`connectDB.php`, `DBController.php`). `.230` and `192.168.11.130` appear only in old or commented config. |
| Volume (by AUTO_INCREMENT) | `update_logs` ≈87.6k, `file_path` ≈39k, `buy_order_items` ≈29.5k, `buy_receipt_items` ≈11.5k, `historys` ≈7.8k, `payments` ≈5.3k, `payroll_items` ≈4.9k, `buy_orders` ≈3k, `lookup_tables` ≈2.7k. Small: a single MariaDB instance is plenty. |
| Integrity | **No foreign keys.** Relations are by convention (`*_id` columns + 17 link tables with `share_amount`). Money is `double`. Many dates default to `'0000-00-00'`. `timestamp` is a create time (it does not change on update). Columns carry Chinese `COMMENT`s, which are good field labels. |
| Lookups | `lookup_tables(table_name, table_item, parent_id, ranking, value, data)` is **hierarchical**. Root `主要表单名称` lists ~46 lookup categories (statuses such as 合同情况/项目情况/采购订单执行情况, types, tax rates, 公司员工, 账户简称, warehouses 进库目的地…). The **product catalogue** is a 4-level tree in the same table (类别 → 细类 → 品名 → 型号), and `*_items.product_id` points at the leaf. Status values are stored as **Chinese text**. |
| Framework | Current pages share helpers: `insert.php?insert_table=…` (generic create, optionally creating a link at the same time via `&link_table=…`), `link-create.php?tableLinkTableCode=…` (29 link types), `commit_sql()` (insert/update + audit), `submit_history()` (workflow), `format_filter_header_html_code()`/`conditions_to_sql()` (list filters). The older per-module `*-insert.php` and `*-link-create.php` pages are no longer linked from anywhere (dead code). |
| Documents | Path `pdf_folder/{folder}/{yyyyMM}/{original file name}`. `file_path.table_name` is a tag (`pdf_contract_file`, `pdf_bank_record`…, 25 tags) and `table_id` is the owning row. Allowed extensions: jpeg/jpg/png/pdf/doc/docx/xls/xlsx/dwg/**dxf/zip/rar**. Max size **80 MB**. A file with the same name is **silently overwritten**. Chinese names are kept as-is. |
| Workflow | `historys` implements “转交”: comment + status_from → status_to + person_from → person_to, and it updates the record's status column. Used on **10 tables**: contracts, receivable_applications, settlements, documents, additionals, buy_plans, buy_requests, buy_orders, employees, payrolls. The home page lists 待办 = records whose latest `historys` row has `person_to` = me. |
| Audit | `commit_sql()` writes `update_logs` (`sql_text` = the executed SQL, HTML-encoded; `user` = `{role}-{name}`, e.g. `财务-陈冬霞`; `tag` = `添加记录 `/`更新记录 `). **About 40 write paths bypass it** (item grids, item conversions, workflow, deletes, file uploads), so the log is incomplete today. |
| Auth | 22 accounts hard-coded in `login.php` with **bcrypt** hashes (`password_verify`). Several accounts share the same hash. The session holds `username` (Chinese display name), `role` (department) and `rank` (`总监` / `员工`). That display name is what gets written to `user_name`, `historys.person_from/to`, etc. |
| Text encoding quirk | Input goes through `htmlspecialchars` (PHP 8.0 default `ENT_COMPAT`: `& < > "` are encoded, `'` is not). The DB therefore contains `&amp;`/`&quot;` and the PHP pages echo them raw. |

---

## 2. Goals and non-goals

### Goals
* G1. Replace the PHP UI with a responsive React SPA (zh-CN first) that covers every module still in use.
* G2. Provide a documented, secure C# REST API that is the only path to the DB and the file store for the new UI.
* G3. Provide an MCP server so AI assistants (Claude etc.) can query the ERP and do a limited set of actions under the user's own permissions.
* G4. **Zero data migration**: read and write the existing `goldata` schema and the existing `pdf_folder` tree in place, and stay compatible with the legacy PHP app while both run.
* G5. Fix the security defects of the legacy app (§6).
* G6. Make audit complete: every write is logged, which today only ~60% are.

### Non-goals (phase 1)
* Redesigning the data model, normalising tables, or converting `double` money to `DECIMAL`.
* Moving documents to object storage. This can come later behind the same storage interface.
* New business modules.
* A native mobile app.

---

## 3. Users, roles and ranks

Access today is **role (department) × rank**:

| Role (`$_SESSION['role']`) | Accounts (display name, rank) |
|---|---|
| 管理 | 叶仲霖 (总监), 王蓓琦 (总监), 叶炜青 (总监) |
| 业务 | 陶雪凤 (总监), 李乐思 (员工) |
| 财务 | 陈国梅 (总监), 叶琼穗, 黄芳兰, 陈冬霞 (员工) |
| 采购 | 钟帼英, 陈露露, 黄泳仪 (员工) |
| 预算 | 马海波, 梁锦辉 (员工) |
| 家装 | 陈少炜, 刘伟国, 曾静敏, 惠州广泰财务 (员工) |
| 工程文员 | 葛小翠 (员工) |
| 采购查阅 / 工程查阅 / 预算查阅 | 采购部查阅用户, 工程部查阅用户, 预算部查阅用户 (read-only) |
| (家装录入) | seen in old `update_logs` entries only, with no current account |

Current access rules (from the menu, `check_page_permission()`, `commit_sql()` and `delete_row.php`):

| Area | Who |
|---|---|
| 工装 (项目, 预算, 合同, 请款, 签证, 工程文件, 结算) · 采购 (供应商, 采购计划, 请购单, 订单, 收货单, 库存, 出库单, 产品分类表) · 工具 (安装成本计算器) | 管理, 财务, 业务, 采购, 工程文员, 预算 |
| 家装 (项目) | the above + 家装 |
| 财务 (销项/进项发票, 收款, 付款, 财务分析报告) · 劳务 (人工费结算单) | 管理, 财务 |
| 人事 (员工档案, 工资发放, 工资明细, 工资报告) | (管理 or 财务) **and** 总监 — the only area also enforced at page level |
| 设置 (lookup_tables) | menu: 叶仲霖 only; page: 管理 + 总监 |
| Delete master records | 管理 + 总监 |
| Any write | blocked for `*查阅` roles, but only on paths that go through `commit_sql()` |

**REQ-ROLE-1** Roles, ranks and permissions are data (configurable by an admin), not code.
**REQ-ROLE-2** Each user's **display name and role are identical to the values already stored in the DB** (`user_name`, `historys.person_to`, `lookup_tables['公司员工']`, `update_logs.user` prefix), so existing to-dos, ownership and history keep working.
**REQ-ROLE-3** Permissions are enforced **in the API on every endpoint and MCP tool**. Today any logged-in user can open almost any page by URL, and several endpoints need no login at all (§6).
**REQ-ROLE-4** Read-only roles (`*查阅`) are enforced on every write path.
**REQ-ROLE-5** The default permission matrix reproduces the table above. The business can then tighten it (e.g. hide 财务 modules from 采购) without a code change.

---

## 4. Functional requirements

### 4.1 Modules in scope

Each module needs: **list** (filters, sort, year, show/hide closed/void, totals), **create** (optionally from a parent, with the link created at the same time), **detail/edit**, **attachments**, **related records**, **delete** (role-restricted), **change log** (`update_logs` view, as `show_logs()` does today) and **workflow 转交** where marked ✔.

| # | Menu | Module | Main table(s) | Links / related | 转交 |
|---|---|---|---|---|---|
| M1 | 工装 | 项目 Projects | `projects` (self-parent) | contracts, budgets, buy_*, payment_project, buy_order_project, labor_settlement_project, deliveries | |
| M2 | 工装 | 预算 Budgets | `budgets` | → project | |
| M3 | 工装 | 合同 Contracts | `contracts` | → project; ← settlements, 请款, documents, additionals | ✔ |
| M4 | 工装 | 请款 Receivable applications | `receivable_applications` | → contract; ← sales_invoices; received_receivable_application; buy_order_ra | ✔ |
| M5 | 工装 | 签证 Change orders | `additionals` + `additional_project` | → contract; → sub-project (share) | ✔ |
| M6 | 工装 | 工程文件 Engineering documents | `documents` | → contract | ✔ |
| M7 | 工装 | 结算 Settlements | `settlements` | → contract | ✔ |
| M8 | 家装 | 家装项目 Retail projects | `projects_retail` | blueprint/quote/contract files; buy_*, payment/received *_project_retail, labor_settlement_project_retail, deliveries | |
| M9 | 家装 | 家装报价 Retail quotes | `quotes_retail` + `quote_retail_items` + `quote_retail_requirement_items` | product catalogue | (Q4) |
| M10 | 采购 | 供应商名录 Suppliers | `suppliers` | buy_orders.supplier_id | |
| M11 | 采购 | 采购计划 Purchase plans | `buy_plans` + items | → project / project_retail; buy_order_plan | ✔ |
| M12 | 采购 | 请购单 Purchase requests | `buy_requests` + items | → project / project_retail; buy_order_request | ✔ |
| M13 | 采购 | 订单 Purchase orders | `buy_orders` + items | buy_order_project(_retail), _plan, _request, _ra, payment_bo, buy_receipt_order | ✔ |
| M14 | 采购 | 收货单 Goods receipts | `buy_receipts` + `buy_receipt_items` | buy_receipt_order; item → product, destination (warehouse) | |
| M15 | 采购 | 库存 Inventory (view) | `buy_receipt_items` aggregated | by product × warehouse | |
| M16 | 采购 | 出库单 Deliveries | `deliveries` (+ `buy_receipt_items.delivery_id`) | → project / project_retail | |
| M17 | 采购 | 产品分类表 Product catalogue | `lookup_tables` (4-level tree) | | |
| M18 | 财务 | 销项发票 Sales invoices | `sales_invoices` | → 请款 | |
| M19 | 财务 | 进项发票 Purchase invoices | `buy_invoices` | buy_invoice_payment | |
| M20 | 财务 | 收款 Money received | `receiveds` | received_receivable_application, received_project_retail | |
| M21 | 财务 | 付款 Payments | `payments` | payment_bo, payment_ls, payment_project, payment_project_retail, buy_invoice_payment | |
| M22 | 财务 | 财务分析报告 Reports | read-only | §4.5 | |
| M23 | 劳务 | 人工费结算单 Labour settlements | `labor_settlements` + items | labor_settlement_project(_retail), payment_ls | |
| M24 | 人事 | 员工档案 Employees | `employees` | payroll_items | ✔ |
| M25 | 人事 | 工资发放 Payroll (+ 明细汇总, 工资报告) | `payrolls` + `payroll_items` | → employees | ✔ |
| M26 | 工具 | 安装成本计算器 Installation cost estimator | `cost_estimate` (saved form as JSON) | | |
| M27 | 设置 | 下拉选项维护 Lookup admin | `lookup_tables` | | |
| M28 | — | 待办事项 Home / to-do | `historys` | | |
| M29 | 设置 | 用户与权限 Users, roles & permissions | `goldata_app` tables (§4.13) | `lookup_tables['公司员工']`, `historys.person_to` | |

### 4.2 Lists
* **REQ-LIST-1** Text filters (contains / not-contains), date ranges and year filters, as on each legacy list.
* **REQ-LIST-2** The default view hides closed/void records (e.g. status like `结案态`/`作废`), with a toggle to show them.
* **REQ-LIST-3** Server-side paging and sorting. Legacy pages load every row.
* **REQ-LIST-4** Column totals (amounts, settlement %, received %) as today, computed by the server over the filtered set.
* **REQ-LIST-5** Export the filtered list to Excel (new).
* **REQ-LIST-6** Year filter options come from the data.

### 4.3 Create, detail and edit
* **REQ-EDIT-1** All fields editable today stay editable. Dropdowns are populated from `lookup_tables` by the same category keys.
* **REQ-EDIT-2** Create-from-parent: as `insert.php?insert_table=X&link_table=Y` does today (e.g. create a 结算 from a 合同, or a 收货单 from an 订单), the new record is created and linked in one transaction. Receipt-from-order also copies the order items.
* **REQ-EDIT-3** Line-item grids (plan, request, order, receipt, delivery, labour, payroll, quote): add/remove rows inline, with **product picker** search over the 4-level catalogue (replaces `dropdown/` ajax), supplier search, and unit auto-fill. `sub_total = qty × unit_price` is computed on save, and the header total is updated as legacy does.
* **REQ-EDIT-4** Related-record panels on each detail page, each with a link to open the record.
* **REQ-EDIT-5** Detect concurrent edits. If another user (legacy or new) changed the record since it was opened, warn the user and don't overwrite silently.
* **REQ-EDIT-6** Mandatory-field and type validation with zh-CN messages.
* **REQ-EDIT-7** Amount in Chinese capitals (人民币…元整, legacy `convertAmountToCn`) shown where legacy shows it (employee and payroll pages), and available for print.

### 4.4 Links and allocations
* **REQ-LINK-1** All **29 link types** of `link-create.php` are supported (list in spec §3.6). There are two kinds: the FK column kind (e.g. 结算→合同 sets `settlements.contract_id`; unlink sets `NULL`) and the share-table kind with `share_amount` (e.g. 付款→订单 inserts into `payment_bo`).
* **REQ-LINK-2** Show allocated vs. unallocated amounts when linking.
* **REQ-LINK-3** Item conversions keep their semantics: 采购计划明细 → 订单明细, 请购明细 → 订单明细 (incl. the report variant), 订单明细 → 收货明细. Fields copied: item, cat, type, unit, standard, brand, unit price.

### 4.5 Reports (read-only)
Parity with: 合同报告, 发票汇总入口, 月度发票 (buy vs sales, tax = amount × rate/(1+rate)), 按供应商发票, 请款报告, 供应商付款-发票余额 (payments vs invoices by supplier, normalising `（）`→`()`), 订单-收货报告, 请购-订单报告, **项目采购明细报告** (`report-project-buy_order_items`), **工资报告** (`report-salaries`, 人事 permission only).
* **REQ-RPT-1** Same numbers as legacy for the same filter (acceptance: reconcile 3 sample periods).

### 4.6 Workflow (转交) and to-do
* **REQ-WF-1** On the 10 ✔ modules: add a comment, choose the new status (from the module's status lookup) and the next handler (from `公司员工`). This writes a `historys` row **and** updates the status column.
* **REQ-WF-2** History timeline per record.
* **REQ-WF-3** Home: “我的待办” and “其他人员待办” grouped by person (excluding department placeholders containing `部`). Legacy shows the second list to role 管理, any 总监, and role 采购. **To-do items are only shown when the viewer has read permission on that module**. Legacy already shows “权限不足” for 员工档案/工资 items.
* **REQ-WF-4** Home: “最近更新” feed, i.e. the latest `update_logs` entry per record over the last 3 months, for 管理 or any 总监 (legacy `admin-index` section 3). It is also filtered by module permission.
* **REQ-WF-5** (Optional) Notification badge for new to-dos.

### 4.7 Inventory and deliveries
* **REQ-INV-1** Stock = `SUM(qty)` of `buy_receipt_items` grouped by product × warehouse (`destination_id`), excluding destination `项目工地（直接出库）` (lookup id 1839), as `inventory_items-view` does today.
* **REQ-INV-2** A 出库单 (delivery) creates/updates `buy_receipt_items` rows tagged with `delivery_id`. Deleting a delivery also deletes its item rows, as legacy does. The quantity sign convention must be confirmed (Q5).
* **REQ-INV-3** Catalogue maintenance (M17) edits the product tree. A node with children can't be deleted (legacy rule).

### 4.8 HR and payroll
* **REQ-HR-1** Payroll line amount = (standard_monthly_salary + housing + meal + distant allowances + bonus) × poration − tax − absence − insurance deductions. The payroll header total = sum of lines.
* **REQ-HR-2** HR modules require 人事 permission (default: 管理/财务 + 总监) on **every** endpoint, including attachments, to-dos, search and MCP.

### 4.9 Documents (files)
* **REQ-DOC-1** Upload attachments to any record. The file is saved under the **same folder convention** with the same `file_path` tag, so legacy pages still see it.
* **REQ-DOC-2** Same allowed types (incl. dxf/zip/rar) and the 80 MB limit (configurable). Same forbidden characters in names. Chinese names preserved.
* **REQ-DOC-3** **No silent overwrite.** If the name already exists, offer rename-with-suffix or cancel.
* **REQ-DOC-4** View/download through the API with an authorization check (today any file is downloadable by URL without login). Preview PDF and images in the browser.
* **REQ-DOC-5** Delete is role-restricted. The default is to **move** the file to a recycle folder rather than `unlink` it, and to write an audit entry.
* **REQ-DOC-6** Show the naming rule 【文件类型】甲方名称-项目名称-日期 as guidance, with optional pre-fill.
* **REQ-DOC-7** A `file_path` row whose file is missing shows as “文件缺失” rather than an error.

### 4.10 Delete rules
* **REQ-DEL-1** Master records (the 21 tables in `delete_row.php`: budgets, buy_invoices, buy_orders, buy_receipts, buy_plans, buy_requests, contracts, documents, payments, projects, projects_retail, receivable_applications, receiveds, sales_invoices, settlements, labor_settlements, additionals, suppliers, payrolls, employees, deliveries) can be deleted by **管理 + 总监** only. The API checks for linked records and blocks the delete or lists them for confirmation.
* **REQ-DEL-2** Link records can be removed by users with edit rights on the parent.
* **REQ-DEL-3** Every delete is confirmed in the UI, done with HTTP DELETE, and audited.

### 4.11 Audit
* **REQ-AUD-1** **Every** create/update/delete from the API and from MCP, including item grids, conversions, workflow, file upload/delete and lookup edits, writes to `update_logs` in the existing format (`user` = `{role}-{display name}`, `tag` = `添加记录 ` / `更新记录 ` / `删除记录 `, `sql_text` HTML-encoded), so the existing log stays continuous and readable by the legacy `show_logs()`.
* **REQ-AUD-2** MCP-originated changes are marked (tag suffix `[MCP]`).
* **REQ-AUD-3** Each record has a “修改记录” tab showing its `update_logs` entries, as legacy does.

### 4.12 MCP server (AI access)
* **REQ-MCP-1** Read tools: search across modules, get a record with its links and files, my to-dos, record history, standard reports, supplier balance, project financial summary (contract / 签证 / settled / 请款 / invoiced / received / paid), stock by product.
* **REQ-MCP-2** Extract document text from PDF attachments for Q&A (read-only, permission-checked).
* **REQ-MCP-3** Write tools are allow-listed (phase 1: 转交, add a link, create a draft 请购单). Each requires an explicit confirmation and is audited.
* **REQ-MCP-4** MCP calls run **as the authenticated user** with exactly that user's permissions.
* **REQ-MCP-5** HR/payroll modules are **excluded from MCP by default**, even for users who can see them in the UI (admin setting to enable).
* **REQ-MCP-6** Works with Claude Desktop / Claude Code (Streamable HTTP, per-user token, OAuth later).

### 4.13 User, role and permission admin (M29 设置 ▸ 用户与权限)

Today users and roles are hard-coded in `login.php` and permissions in `if` statements across the pages. Adding a person, changing a password or changing access needs a developer. This page replaces that. It is open only to holders of `users:admin` (users) and `roles:admin` (roles and permissions). The default holders are 管理 + 总监.

**Users**
* **REQ-ADM-1 List** all users: login, display name, role, rank, status (active / disabled / locked), must-change-password flag, last login, number of active API tokens. Search and filter by role, rank and status.
* **REQ-ADM-2 Add user**: login (unique, `a-z 0-9 . _ -`, 2–32 chars; legacy has `ci`), display name, role, rank, and an initial password (generated or typed). The user must change it at first login. The display name must be unique among users. It is what gets written to `user_name`, `historys.person_to/from` and `update_logs.user`, so the form warns if the name is not in the `公司员工` lookup and offers to add it there (a dropdown source for 转交).
* **REQ-ADM-3 Edit user**: role, rank, active flag. **Display name and login are read-only once the user has logged in or appears in legacy data**, because renaming would break to-dos, ownership and history matching (REQ-ROLE-2). A rename is done by creating a new user and disabling the old one.
* **REQ-ADM-4 Reset password** (temporary password shown once, forced change), **unlock** after lockout, **revoke** one or all API/MCP tokens.
* **REQ-ADM-5 Disable / enable**: a disabled user cannot log in. Their sessions and tokens stop working **immediately** (next request), not when the cookie expires.
* **REQ-ADM-6 Delete user**: allowed only for a user with **no open to-dos** (`historys.person_to` on a record still awaiting them). Otherwise the dialog lists those records and asks for them to be handed over first. Typing the login confirms the delete. Deleting removes the account, its tokens and its overrides from `goldata_app`. **It never changes `goldata`**: names in existing records and logs stay as they are. *Disable* is the recommended default; delete is for accounts created by mistake or leavers after handover.
* **REQ-ADM-7 Per-user overrides**: grant or deny individual permissions on top of the role (e.g. let one 采购 user see 付款). Each permission is tri-state: 继承 / 允许 / 禁止. Deny wins.
* **REQ-ADM-8 Effective permissions view**: for any user, show the final permission list and where each one comes from (role, role + rank, override). "Preview as this user" shows the menu that user will see.

**Roles and permissions**
* **REQ-ADM-9 Manage roles**: create, copy from an existing role, edit description, mark read-only, delete. A role can be deleted only when no user has it. Role names are **immutable after creation**: the name is written into `update_logs.user` as `{role}-{name}`, and the PHP app compares it in code. A role name may not contain `-`.
* **REQ-ADM-10 Permission matrix per role**: rows are modules grouped like the menu (工装, 家装, 采购, 财务, 劳务, 人事, 工具, 设置). Columns are actions (查看, 新增, 修改, 删除, 关联, 转交, 导出, 上传附件, 删除附件). A separate block holds special permissions (其他人员待办, 最近更新, 财务报告, 工资报告, 下拉选项维护, 用户管理, 角色管理, MCP 读, MCP 写). Each grant applies to **all ranks** or **总监 only / 员工 only**, which reproduces today's "(管理 or 财务) and 总监" rules.
* **REQ-ADM-11 Read-only roles**: a role flagged read-only can never hold create/update/delete/link/workflow/upload permissions. The matrix disables those boxes, and the API enforces it (replaces the `*查阅` string check).
* **REQ-ADM-12 Ranks** are a short managed list (seed 总监, 员工). A rank in use cannot be deleted.
* **REQ-ADM-13 Safe changes**:
  * Saving a matrix shows a diff ("+ 付款:查看, − 订单:删除") and how many users are affected.
  * Granting HR permissions (员工档案, 工资) or `users:admin`/`roles:admin` asks for a second confirmation.
  * **恢复默认** resets a seeded role to the legacy default matrix (§3).
  * Admins cannot disable, delete or demote themselves.
  * The system refuses any change that would leave **no active user with `users:admin` and `roles:admin`**.
  * Changes take effect on each user's next request.
* **REQ-ADM-14 Audit**: every admin action (user add/edit/disable/delete, password reset, unlock, token revoke, role add/delete, every permission change, with before and after) is written to an admin audit log, with actor, time and IP, and shown on a **操作日志** tab. Admin actions are **not** available through MCP.

---

## 5. Coexistence and compatibility (critical)

* **REQ-CMP-1 No destructive schema changes** to `goldata`. New tables (users, roles, tokens, settings) go in a **separate database** `goldata_app` on the same server. Additive indexes need review approval.
* **REQ-CMP-2** Write values exactly as the legacy app would: Chinese status text, `user_name` = display name, `'0'` for empty amounts, `'0000-00-00'` for empty dates where legacy does so, HTML-entity encoding (`ENT_COMPAT`) of text fields while PHP is in use, and `NULL` ids when unlinking.
* **REQ-CMP-3** Read tolerantly: zero dates → empty, entities decoded for display, full/half-width brackets, missing files, unknown status values shown as-is.
* **REQ-CMP-4** Code points above U+FFFF (emoji, some rare CJK) can't be stored in `utf8` columns. The API rejects them with a clear message, unless the DB is converted (Q6).
* **REQ-CMP-5** The document root is configurable. On the NAS it is a bind mount of the folder that contains `pdf_folder` (a CIFS mount if the files are on another server). Files written by the new app must be readable, replaceable and deletable by the PHP app, and the reverse.
* **REQ-CMP-6** Cut over module by module. Until a module is switched off in PHP, both apps may edit the same records, and REQ-EDIT-5 protects against lost updates.
* **REQ-CMP-7** Existing user passwords keep working: the API verifies the legacy bcrypt hashes, then forces a password change (shared hashes mean several people share a password today).
* **REQ-CMP-9 SQL change history.** From now on, every change to `goldata_app`, `goldata` or the DB server (tables, views, seed data, indexes, accounts, grants) is saved as a numbered script in `goldataWeb2/db/` before it is run. The history of what was applied, and where, can always be traced. Rules: `db/README.md`.
* **REQ-CMP-8 One user list for both apps.** Decided (Q15). The PHP login reads users, roles and ranks from `goldata_app`. The hard-coded accounts and hashes in `login.php` are removed. While PHP is in use:
  * A user added on the admin page can log in to PHP with the same password, and one deleted or disabled can't. A disabled user's open PHP session ends within 1 minute.
  * A password reset or change applies to both apps. Lockout counts failed logins from both apps.
  * A role or rank change applies to PHP on the user's next page view (within 1 minute), using the existing PHP menu and page rules.
  * Users who still have to change their password can log in to PHP as before, but see a banner linking to the new app's change-password page. The old PHP keeps working for everyone from day one.
  * PHP only knows the 10 legacy role names. A user with a new role made on the admin page logs in to PHP with an empty menu.
  * Every PHP login, successful or not, is written to the same login audit, marked `php`.

---

## 6. Non-functional requirements

| ID | Requirement |
|---|---|
| NFR-SEC-1 | Every query parameterised. Legacy concatenates `$_POST`/`$_GET` into SQL almost everywhere (SQL injection). |
| NFR-SEC-2 | Every endpoint authenticated and authorised. Legacy defects found: **`crud_action.php` (lookup add/edit/delete), `dropdown/ajax/*.php`, `delete_file.php` and all of `pdf_folder/` have no login check**. Deletes are done by GET. There is no CSRF protection. |
| NFR-SEC-3 | No credentials in source control. **Rotate the DB password** (plaintext in `connectDB.php`, `DBController.php`). The first commit of the GitHub repo `slowhanderic/web_php_20261020` contains the **plaintext user passwords** from the old `login.php`, and the current one contains bcrypt hashes. Make the repo private and purge it from history. |
| NFR-SEC-4 | HTTPS only. Secure cookie session, configurable timeout (legacy 9600 s), CSRF protection, login rate limiting. |
| NFR-SEC-5 | Files are served only through the API. Path traversal is blocked. |
| NFR-SEC-6 | The API's DB account has DML rights only (no DDL) on `goldata` and `goldata_app`, granted to the container subnet only. Schema migrations use a separate account. |
| NFR-PERF-1 | List pages P95 < 1 s (50-row page). Reports < 3 s. |
| NFR-AVL-1 | LAN deployment, office hours. Nightly DB + file backups with a monthly restore test. |
| NFR-I18N-1 | zh-CN UI first. Labels in resource files. ¥ with 2 decimals, `YYYY-MM-DD`. |
| NFR-UX-1 | Current Chrome/Edge. Usable at tablet width (the legacy app already uses a responsive Bootstrap navbar). Keyboard-friendly item grids. |
| NFR-UX-2 | The UI uses **MUI (Material UI)** with one central company theme: brand colours (from the goldata logo), a Chinese system font stack, a compact density suitable for data entry, and consistent status colours. All components use the zh-CN locale (grid, date pickers, pagination). Light theme by default, with an optional dark mode. Only the free MIT packages of MUI and MUI X are used. Pro/Premium are excluded (NFR-LIC-1). |
| NFR-OBS-1 | Structured logs, request IDs, health endpoint. |
| NFR-MNT-1 | API integration tests against a MariaDB 10.3 container loaded from a **sanitised** dump. Report reconciliation tests. |
| NFR-LIC-1 | **Free and open source only.** Every library, framework, tool and container image that is built into, or needed to build or run, the system uses an OSI-approved licence (MIT, Apache-2.0, BSD, ISC, MPL-2.0, or OFL for fonts). It costs nothing and needs no licence key. Excluded: commercial, "source-available" (BUSL, SSPL, Elastic), revenue-threshold or community-licence products, and the paid tiers of open-core products (MUI X Pro/Premium, AG Grid Enterprise). |
| NFR-LIC-2 | CI fails the build if any npm or NuGet dependency, including indirect ones, has a licence outside the allow-list. A generated `THIRD-PARTY-NOTICES` file ships in each image. A paid SaaS service must never be required: build, registry and CI can all run self-hosted on the LAN/NAS. |
| NFR-DEP-1 | The React UI and the C# API are each delivered as a **Docker image** and run on the **Synology NAS** under **DSM 7.1** with Synology's **Docker** package, as one docker-compose project (started over SSH by a wrapper script, since DSM 7.1 has no Container Manager). No build tools, SDKs or source code on the NAS. |
| NFR-DEP-2 | Images are built for the NAS CPU (`linux/amd64`; `arm64` only if the model needs it), are Alpine-based so they work with DSM 7.1's older Docker engine, versioned (semver + git SHA) and pulled from a self-hosted open-source registry on the LAN or imported as a tar (NFR-LIC-2). Upgrade and rollback = change the image tag and recreate, in under 5 minutes. |
| NFR-DEP-3 | HTTPS terminates at the **DSM reverse proxy** with a DSM-managed certificate. The containers publish only one port, bound to `127.0.0.1`. UI, REST and MCP share one host name. |
| NFR-DEP-4 | The containers use the **existing MariaDB** (no DB container in production) and the **existing `pdf_folder`** (bind mount). They run as the same UID:GID as the PHP Web Station files. |
| NFR-DEP-5 | **One settings file per environment** (`deploy/env/{local,uat,prod}.env`): every setting, secrets included, in one place, read identically by docker-compose, `goldata.sh` and the API; the PHP bridge config is generated from it. Only the `*.env.example` templates (no real secrets) are committed. On the NAS the file is `root:root` mode 600. No secrets in images, `compose.yaml` or the Docker package UI. |
| NFR-DEP-6 | Containers run non-root with a read-only root FS, no added capabilities, memory limits and health checks. They restart automatically after a NAS reboot or DSM update. |
| NFR-DEP-7 | Backups use DSM tools: nightly DB dumps (Task Scheduler), Snapshot Replication for `pdf_folder`, Hyper Backup offsite for dumps, files and the compose project folder. |
| NFR-DEP-9 | The compose file works unchanged on DSM 7.1 (docker-compose v1) and DSM 7.2+ (Container Manager / compose v2), so a later DSM upgrade needs no redeploy. Deploy, upgrade, rollback and health checks are scripted (`goldata.sh`) and can run from DSM Task Scheduler. |
| NFR-DEP-10 | A step-by-step **deployment runbook** (`deploy/DEPLOYMENT.md`) covers first install, PHP changes, smoke test, backups, upgrade, rollback and troubleshooting. Any change affecting installation, configuration, upgrade or rollback updates the runbook in the same commit, with a change-log line. |
| NFR-DEP-11 | **UAT** runs beside prod on the same NAS and MariaDB with its own project, port, subnet, databases, DB accounts (no rights on prod), document copy and token pepper. It can never write production data or accept production tokens. |
| NFR-DEP-8 | The legacy PHP app keeps running on the same NAS (Web Station) until P4, unaffected by the containers. |
| NFR-DATA-1 | The dump contains payroll and personal data. It must never be committed. Test data must be anonymised. |

---

## 7. Phasing (proposal)

| Phase | Scope | Exit criteria |
|---|---|---|
| P0 Foundation | Repo, CI (multi-arch image build), Docker compose project on the DSM 7.1 NAS, API skeleton, DB connectivity, auth (legacy hashes), user/role/permission admin page (M29), PHP login switched to `goldata_app` (REQ-CMP-8), lookups + product catalogue, file store, audit writer, generic link service | Login works over HTTPS on the NAS; any record readable; files downloadable |
| P1 Finance core | 工装 (M1–M7), 财务 (M18–M22), 待办 | Finance runs a month in the new app; reports reconcile |
| P2 Procurement + inventory | M10–M17, M23, item conversions | Buyers stop using PHP |
| P3 Retail + MCP | M8, M9, MCP read tools, then the allow-listed write tools | Retail switched; MCP pilot with 2–3 users |
| P4 HR + tools + admin | M24–M27 | Legacy PHP switched off (read-only archive) |

---

## 8. Open questions

| # | Question | Why it matters |
|---|---|---|
| ~~Q1~~ | ~~Where is the production source?~~ **Resolved**: the current source was provided on 2026-11-02. | |
| Q2 | Keep today's broad access (e.g. 采购/预算/工程文员 can open 工装 and 采购 modules, and pages are reachable by URL), or tighten it per module? | Default permission matrix |
| ~~Q3~~ | **Resolved 2026-11-02:** production = **192.168.168.235** (MariaDB 3307 + live `pdf_folder`). `192.168.21.175` = local/test server (DB copy, documents copy up to 2019-09). `.230` presumed obsolete. | |
| Q4 | Is 家装报价 (`quotes_retail`, only ~1 record, page not linked from the menu) still needed? | Scope of M9 |
| Q5 | Inventory: is a 出库 recorded as negative-qty `buy_receipt_items`, or by moving `destination_id`? Is there anything beyond 深圳广泰仓库 + 项目工地 as a warehouse? | Stock calculation |
| Q6 | Can we convert the DB to `utf8mb4`, or must it stay `utf8`? | Emoji/rare-character support |
| Q7 | Until PHP is retired, keep saving HTML-entity-encoded text (compatible)? Recommended: yes. | Data consistency |
| Q8 | Hosting **decided**: Docker on the Synology NAS. Still to confirm: is the PHP app on the same NAS in **Web Station** (Apache or nginx backend)? Full path of `pdf_folder` (e.g. `/volume1/web/…`) and its size? Which DSM user owns the files (`id http`)? | Bind mount, file ownership, backup sizing |
| Q9 | Deleting files: recycle folder OK, or must files be physically removed? | REQ-DOC-5 |
| Q10 | Which AI clients will use MCP? Is sending document text and financial data to a cloud LLM acceptable? | MCP auth + data governance |
| Q11 | Remote access (outside the LAN) needed? | Security perimeter, MCP OAuth |
| Q12 | Accounts 采购部/工程部/预算部查阅用户 currently get an empty menu (no branch matches). Still in use? Is the role 家装录入 retired? | Role seed |
| Q13 | Printouts or official forms that must keep an exact layout (purchase order, payroll slip with 大写金额)? | Print templates |
| Q14 | DSM version **known: 7.1**. Still needed: Synology model, RAM, free disk, and the output of `sudo docker version` and `uname -r` (or: is the Docker package installed yet?). Is a later upgrade to DSM 7.2 acceptable? Host name for the new app (e.g. `erp.<domain>`), and is there an internal CA or a public domain for the certificate? | Image architecture, sizing, TLS |
| ~~Q15~~ | ~~Should PHP read users from `goldata_app`?~~ **Decided 2026-11-02: yes**, see REQ-CMP-8 and spec §5.5. | |
| Q16 | Brand colours: do you have them, or should they be taken from the goldata logo? (MUI X Pro is ruled out by NFR-LIC-1.) | Theme |
| Q17 | MCP default access (relates to Q10): implemented as **only 管理 can create MCP tokens, read-only**; nobody has MCP write. Widen to other roles? | Default permission matrix |
| Q18 | 删除附件: legacy `delete_file.php` let anyone delete. Implemented default: everyone who can **edit** the module can delete its attachments (to the recycle folder). Restrict to 管理 + 总监 instead? | Default permission matrix |
| Q19 | Default list view: legacy hides records whose status text *contains* a closed value (e.g. 请款 also hides 「未编制」「未递出」), and (because `NULL NOT LIKE …` is never true) also hides records whose status is NULL. Implemented: same closed values, but NULL-status records stay visible. OK? | REQ-LIST-2 parity |
| Q20 | New staff in 「公司员工」 need a department code (`value`: super / admin / cw / buyer / ys / retail / gc / oa). Implemented: chosen on the add-user form, default by role (总监 → super). Is that mapping right? | Admin page |
| Q21 | UAT data: a full copy of production (real names, salaries) as implemented in the runbook, or an **anonymised** copy (`db/sanitise`, more work but safer)? Who may access UAT? | UAT data protection |
