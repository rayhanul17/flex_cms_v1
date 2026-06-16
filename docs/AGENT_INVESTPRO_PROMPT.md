# Prompt template — building `investpro` as a FlexCMS module

> Copy everything between the `--- BEGIN PROMPT ---` and `--- END PROMPT ---`
> markers into a fresh AI agent session (Claude / GPT / Cursor / etc.). Replace
> `<TASK>` at the bottom with what you actually want done. Save the rest as-is.

---

--- BEGIN PROMPT ---

You are working inside a FlexCMS .NET 10 checkout. Two git repositories
are involved and **commits must be split between them correctly**:

| Working dir | Repo | Remote | Used for |
|---|---|---|---|
| `D:\flex_cms_v1` (parent) | flex_cms_v1 | `https://github.com/rayhanul17/flex_cms_v1.git` (branch: `new-version`) | Framework + Host + tests + docs |
| `D:\flex_cms_v1\modules\investpro` (child, gitignored in parent) | investpro | `https://github.com/rayhanul17/investpro.git` | The `investpro` module's own source code |

## Hard rules

1. **`modules/investpro/` is gitignored in the parent.** It is its own git
   repository with its own remote. Run `git status` inside it before
   committing — you must see "On branch main … nothing to commit"-style
   output from THAT repo, not from the parent.
2. **Decide which repo a change belongs to BEFORE editing.** Use this rule:
   - Files under `D:\flex_cms_v1\modules\investpro\…` → **investpro repo**.
   - Files anywhere else under `D:\flex_cms_v1\` (src/, samples/, tests/,
     docs/, templates/, *.csproj, *.slnx, .gitignore, README.md, etc.)
     → **parent flex_cms_v1 repo**.
   - Verify with `git rev-parse --show-toplevel` from any directory you're
     about to edit. If you ever need to commit changes touching both
     repos, do **two separate commits** — one in each — and reference the
     module commit's SHA from the parent commit message.
3. **Never `git add`/`commit`/`push` from the wrong working directory.**
   The shell working directory drives which `.git` git operates on.
   `cd D:\flex_cms_v1\modules\investpro` before any investpro git
   command; `cd D:\flex_cms_v1` before any parent-repo git command.
4. **Read the module-dev guide first.** Open `docs/AGENT_MODULE_GUIDE.md`
   in the parent repo — it has the canonical pattern (Sample.Hello-style
   service + `EfRepository<T>(ctx)` + Razor SDK csproj + audit logging
   with `module:` tag). Mirror that pattern. Don't invent new patterns.
5. **Tests must keep passing.** Before any commit:
   - `dotnet build` (run from the dir whose .csproj you touched)
   - `cd D:\flex_cms_v1 && dotnet test --nologo` — expect
     **661 unit + 296 integration passing**. If integration tests need
     a DB, PostgreSQL must be reachable at the connection string below.

## Credentials (dev environment)

> ⚠ These are **local-dev only** — the database is a Docker container
> bound to `localhost`, never internet-exposed. Do **not** reuse this
> password for production. Treat the values below as fixtures for this
> developer machine, not secrets.

| What | Value |
|---|---|
| **DB provider** | PostgreSQL |
| DB host / port | `localhost` / `5432` (Docker container named `postgres`, image `postgres:16`) |
| DB name | `flexcms` |
| DB user | `dev` |
| DB password | `Dev@123456` |
| Setup wizard "Database Provider" | PostgreSQL |
| **Admin email** | `rayhanul.cse@gmail.com` |
| **Admin password** | `Dev@123456` |
| Admin Full Name | (anything sensible, e.g. `Rayhanul Islam Raj`) |
| Site name | (anything, e.g. `FlexCMS Dev`) |
| Site Base URL | `http://localhost:5099` |
| Timezone | `(UTC+06:00) Dhaka` |
| Default language | English |

### Setup-wizard scenario (fresh install)

If `D:\flex_cms_v1\src\FlexCms.Host\App_Data\setup.json` does NOT exist
(or `IsSetupComplete: false`), the host redirects everything to `/Setup`.

**⚠ Before driving the wizard — drop + recreate the database.** The
wizard does NOT delete leftover tables; if a previous setup partially
populated the schema, you'll hit confusing migration / duplicate-key
errors during seed. Always start from an empty `flexcms` database:

```bash
# Drop + recreate the Postgres database (kills every table, role grant,
# enum, sequence — clean slate). Run from any working directory.
docker exec postgres psql -U dev -d postgres -c "DROP DATABASE IF EXISTS flexcms;"
docker exec postgres psql -U dev -d postgres -c "CREATE DATABASE flexcms;"

# Sanity check — should print 0 user tables.
docker exec postgres psql -U dev -d flexcms -c "\dt" 2>&1 | tail -3
```

Now drive the 3-step wizard via Playwright (preferred — see § Playwright
notes below) or fill the form manually in a real browser:

1. **Step 1 — Database**: pick PostgreSQL, host `localhost`, port `5432`,
   database `flexcms`, user `dev`, password `Dev@123456`. Click
   **Test Connection** (expect ✓ Connected successfully) → **Next**.
2. **Step 2 — Site Info**: site name + tagline (anything),
   Base URL `http://localhost:5099`, Timezone Dhaka, language English
   → **Next**.
3. **Step 3 — Admin Account**: Full Name (anything),
   Email `rayhanul.cse@gmail.com`, Password / Confirm `Dev@123456`
   → **Finish Setup**. The wizard creates the DB schema, seeds
   permissions / menu items, and writes `setup.json`. **Stop and
   restart the host** so the runtime exits setup mode and starts
   the module-discovery + DB connection in production-mode services.

### Already-set-up scenario

If `setup.json` exists with `IsSetupComplete: true` AND the DB still
has the schema, just log in at `http://localhost:5099/auth/login` with
the admin email + password above. If the admin email is something else
(e.g. a previous run used `admin@flexcms.test`), either log in with
that email or fully reset (see § Reset below).

### Reset to a clean fresh setup

Use this when an earlier wizard run left stale state (wrong admin email,
half-finished setup, broken seed). Order matters — wipe filesystem state
first, then drop the DB, so the host doesn't try to write to a vanishing
database:

```bash
# 1. Stop the host (Ctrl+C the dotnet run shell).

# 2. Wipe setup state + runtime artifacts:
rm -f  D:\flex_cms_v1\src\FlexCms.Host\App_Data\setup.json
rm -rf D:\flex_cms_v1\src\FlexCms.Host\App_Data\keys
rm -rf D:\flex_cms_v1\src\FlexCms.Host\App_Data\logs
rm -rf D:\flex_cms_v1\src\FlexCms.Host\modules\*       # keep .gitkeep
rm -rf D:\flex_cms_v1\src\FlexCms.Host\wwwroot\uploads

# 3. Drop + recreate the flexcms database (same commands as the wizard
#    scenario above — DON'T skip this; the wizard won't drop tables itself):
docker exec postgres psql -U dev -d postgres -c "DROP DATABASE IF EXISTS flexcms;"
docker exec postgres psql -U dev -d postgres -c "CREATE DATABASE flexcms;"

# 4. Start the host — `dotnet run` will land on /Setup. Drive the wizard.
```

## Initial setup (run once at the start)

```bash
cd D:\flex_cms_v1\modules\investpro
git status                              # should say "On branch main" or
                                        # "You appear to have cloned an
                                        # empty repository."
```

If the repo is empty, populate it from the FlexCMS module template
(`templates/flexcms-module/content/FlexCms.Module.Name/`). The host's
scaffold endpoint does the token-replacement work — use it like this:

1. Start the host: `cd D:\flex_cms_v1\src\FlexCms.Host && dotnet run`
2. Sign in at `http://localhost:5099/auth/login` as SuperAdmin.
3. Go to `/admin/modules/scaffold`:
   - **ModuleId**: `FlexCms.InvestPro` (dotted PascalCase — becomes the
     assembly name, audit-log Module tag, and permission key prefix)
   - **TablePrefix**: `investpro` (snake — becomes the table prefix and
     the URL slug at `/admin/investpro`)
4. Submit. The scaffold writes to `D:\flex_cms_v1\modules\FlexCms.InvestPro\`
   (folder name = ModuleId). Move everything into `modules\investpro\`
   so it lives in the right git repo:

   ```powershell
   # PowerShell — works with hidden .template.config too
   Move-Item D:\flex_cms_v1\modules\FlexCms.InvestPro\* D:\flex_cms_v1\modules\investpro\ -Force
   Get-ChildItem D:\flex_cms_v1\modules\FlexCms.InvestPro -Force | Move-Item -Destination D:\flex_cms_v1\modules\investpro\ -Force
   Remove-Item D:\flex_cms_v1\modules\FlexCms.InvestPro -Recurse -Force
   ```

5. Stop the host (Ctrl+C). Generate the EF migration:

   ```bash
   cd D:\flex_cms_v1\modules\investpro
   dotnet ef migrations add InitialSchema
   dotnet build
   ```

6. First commit to the investpro repo:

   ```bash
   cd D:\flex_cms_v1\modules\investpro
   git add -A
   git commit -m "Initial scaffold from FlexCMS template"
   git branch -M main
   git push -u origin main
   ```

7. Restart the host (`cd D:\flex_cms_v1\src\FlexCms.Host && dotnet run`).
   Confirm the host log shows
   `Module FlexCms.InvestPro: migrations applied.` and
   `seed completed.` The sidebar should now have an **InvestPro** entry
   (visible for SuperAdmin or anyone with `flexcms.investpro.*.view`).

If the repo already has files, skip 1–6 and continue with the task below.

## Commit & push convention

- **Module changes** (anything under `modules/investpro/`):

  ```bash
  cd D:\flex_cms_v1\modules\investpro
  git add <files>
  git commit -m "<conventional commit subject>"
  git push origin main
  ```

  Conventional Commit types: `feat:`, `fix:`, `chore:`, `docs:`, `refactor:`,
  `test:`.

- **Parent-repo changes** (framework fix, host enhancement, doc update):

  ```bash
  cd D:\flex_cms_v1
  git add <files>
  git commit -m "<conventional commit subject>"
  git push origin new-version
  ```

- **If a task spans both repos** (e.g. module needs a new framework helper),
  commit the framework change first, push the parent repo, then commit the
  module change referencing the parent's SHA:

  ```
  feat(forms): add Razor checkbox helper used by FlexCms.InvestPro

  Pairs with flex_cms_v1 commit abc1234 (framework: add FcmsCheckbox helper).
  ```

## Task

You have full discussion in this chat

## Report back

When done, state in plain English:

1. Which repo(s) you committed to and the resulting commit SHAs.
2. The files added/modified per repo, grouped (controller / view / service /
   entity / migration / doc).
3. Build + test result: `dotnet build` (per repo) and
   `dotnet test --nologo` from the parent.
4. Anything you skipped or deferred, with a one-line reason.
5. The exact commands the user can run to verify (e.g.
   `curl http://localhost:5099/api/investpro` or "navigate to
   `/admin/investpro` and click 'New'").

--- END PROMPT ---

---

