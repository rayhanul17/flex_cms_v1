# FlexCms — Complete Developer Guide

This guide takes you from **zero to production**. Follow each step in order. Examples included.

---

## 📋 Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [First Time Setup (Clone & Run)](#2-first-time-setup)
3. [Daily Development Workflow](#3-daily-development-workflow)
4. [Creating a New Module](#4-creating-a-new-module)
5. [Building & Packaging](#5-building--packaging)
6. [Local Testing with Docker](#6-local-testing-with-docker)
7. [Deploying to Production](#7-deploying-to-production)
8. [Updating an Existing Production Server](#8-updating-an-existing-production-server)
9. [Module Deployment to Production](#9-module-deployment-to-production)
10. [Troubleshooting](#10-troubleshooting)

---

## 1. Prerequisites

### Install on your development machine:

| Tool | Why | Where to get |
|---|---|---|
| **.NET 10 SDK** | Build the app | https://dotnet.microsoft.com/download |
| **Git** | Version control | https://git-scm.com |
| **Docker Desktop** | Local DB + containers | https://www.docker.com/products/docker-desktop |
| **Visual Studio 2022** OR **JetBrains Rider** OR **VS Code** | Code editor | Pick one |
| **GitHub CLI (`gh`)** | Easier PR creation | https://cli.github.com |

### Check everything is installed:

```bash
dotnet --version       # Should print 10.0.x
git --version          # Should print git version 2.x.x
docker --version       # Should print Docker version 24.x.x
gh --version           # Should print gh version 2.x.x
```

If any of these fail — install that tool first.

---

## 2. First Time Setup

### Step 1: Clone the repo

```bash
# Clone to D:\flex_cms_v1 (Windows) or ~/flex_cms_v1 (Mac/Linux)
cd D:\
git clone https://github.com/rayhanul17/flex_cms_v1.git
cd flex_cms_v1
```

### Step 2: Set your git identity (one time only)

```bash
git config user.name "Your Name"
git config user.email "your-email@example.com"
```

### Step 3: Verify you're on the `main` branch

```bash
git branch          # Shows: * main
git status          # Shows: nothing to commit, working tree clean
```

### Step 4: Copy environment template

```bash
# Windows PowerShell:
copy .env.example .env

# Linux/Mac:
cp .env.example .env

# Open .env and fill in real values (DB password, etc.)
notepad .env        # Windows
nano .env           # Linux/Mac
```

### Step 5: Start local databases via Docker

```bash
docker compose -f docker/docker-compose.dev.yml up -d
```

This starts:
- **MySQL** on `localhost:3306`
- **PostgreSQL** on `localhost:5432`
- **MongoDB** on `localhost:27017` (replica set mode)
- **Mailhog** SMTP test server on `localhost:1025` (UI at `http://localhost:8025`)

Verify they're running:

```bash
docker ps
# You should see 4 containers running
```

### Step 6: Build the solution

```bash
dotnet restore FlexCms.sln
dotnet build FlexCms.sln
```

If build fails — check the error. Most common: missing NuGet package, fix by running `dotnet restore` again.

### Step 7: Run the app for the first time

```bash
cd src/FlexCms.Host
dotnet watch run
```

Open browser: `http://localhost:5000`

You'll see the **Setup Wizard** (first run only):
1. **Database** — pick MySQL, enter `localhost:3306` + credentials → Test Connection
2. **Site Info** — name, tagline, base URL
3. **Admin Account** — your email + strong password
4. **Done** — wait for restart, then login at `/auth/login`

**Done!** You now have FlexCms running locally.

---

## 3. Daily Development Workflow

This is the workflow you'll use **every day**.

### The Golden Rule

> Never commit directly to `main`. Always create a feature branch.

### Step 1: Pull latest changes from main

```bash
git checkout main
git pull origin main
```

### Step 2: Create a feature branch

Branch name format: `<type>/<short-description>`

| Type prefix | When to use | Example |
|---|---|---|
| `feature/` | New feature | `feature/blog-comments` |
| `fix/` | Bug fix | `fix/login-redirect-loop` |
| `chore/` | Maintenance, deps, docs | `chore/update-readme` |
| `hotfix/` | Urgent production fix | `hotfix/payment-webhook` |
| `refactor/` | Code restructure | `refactor/extract-cart-service` |

```bash
git checkout -b feature/blog-comments
```

### Step 3: Do your work — write code, test locally

Keep the dev server running in another terminal:

```bash
cd src/FlexCms.Host
dotnet watch run    # Auto-reloads on file save
```

### Step 4: Commit your changes

We use **Conventional Commits**:

```
<type>(<scope>): <short summary>

[optional longer description]
```

Examples:

```bash
git add .
git commit -m "feat(blog): add comment moderation queue"
git commit -m "fix(auth): redirect to admin after Google OAuth login"
git commit -m "chore(deps): update EF Core to 10.0.1"
git commit -m "docs(readme): clarify module install steps"
```

### Step 5: Push your branch

```bash
git push -u origin feature/blog-comments
```

The `-u` flag links your local branch to the remote one (only needed first time).

### Step 6: Create a Pull Request (PR)

```bash
gh pr create --base main --title "feat(blog): comment moderation queue" --body "Adds moderation UI for blog comments."
```

OR open the GitHub page that's printed in the terminal output and click "Create Pull Request".

### Step 7: Wait for CI to pass

GitHub Actions runs:
- Build (`dotnet build`)
- Tests (`dotnet test`)
- Format check (`dotnet format --verify-no-changes`)

If any fails — fix locally, commit, push. CI re-runs automatically.

### Step 8: Merge to main

Once CI is green, click **"Merge"** on GitHub. This triggers auto-deploy to production (via the Docker workflow).

### Step 9: Clean up

```bash
git checkout main
git pull origin main
git branch -d feature/blog-comments    # delete local branch
```

The remote branch is deleted automatically by GitHub when you click "Delete branch" in the PR.

---

## 4. Creating a New Module

Modules are how you add features without touching the CMS core.

### Step 1: Branch from main

```bash
git checkout main && git pull
git checkout -b feature/blog-module
git push -u origin feature/blog-module
```

### Step 2: Scaffold the module

**Option A — Use the CLI template (when published):**

```bash
dotnet new flexcms-module -n FlexCms.Blog -o modules/FlexCms.Blog
```

**Option B — Manual scaffold (until template is published):**

```bash
mkdir modules/FlexCms.Blog
cd modules/FlexCms.Blog

# Create the project
dotnet new classlib -n FlexCms.Blog -f net10.0
dotnet add reference ../../src/FlexCms.Framework/FlexCms.Framework.csproj
dotnet add reference ../../src/FlexCms.Core/FlexCms.Core.csproj

# Add to solution
cd ../..
dotnet sln add modules/FlexCms.Blog/FlexCms.Blog.csproj
```

### Step 3: Add the required folder structure

```
modules/FlexCms.Blog/
├── FlexCms.Blog.csproj
├── BlogModule.cs              # IFcmsModule implementation
├── module.json                 # Manifest (set as embedded resource)
├── Permissions/
│   └── BlogPermissions.cs
├── Models/
│   ├── Entities/
│   └── Dtos/
├── Services/
├── Controllers/Admin/
├── Views/Admin/
├── Migrations/
├── wwwroot/
│   ├── css/
│   └── js/
└── Resources/
    ├── Strings.en.resx
    └── Strings.bn.resx
```

### Step 4: Create the minimum required files

**`module.json`** (mark as Embedded Resource in csproj):

```json
{
  "ModuleId": "FlexCms.Blog",
  "ModuleName": "Blog",
  "Version": "1.0.0",
  "Author": "Your Name",
  "Description": "Blog posts and categories",
  "MinFrameworkVersion": "1.0.0",
  "TablePrefix": "blog",
  "DependsOn": [],
  "RequestedPermissions": ["email.send"]
}
```

**`BlogModule.cs`**:

```csharp
using Microsoft.Extensions.DependencyInjection;
using FlexCms.Framework.Modules;
using FlexCms.Framework.Models;

namespace FlexCms.Blog;

public class BlogModule : BaseModule
{
    public override string ModuleId => "FlexCms.Blog";
    public override string ModuleName => "Blog";
    public override string Version => "1.0.0";

    public override void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<Services.PostService>();
    }

    public override List<FcmsPermissionDef> GetPermissions() => new()
    {
        new(Permissions.BlogPermissions.PostCreate, "Create Post", group: "Blog"),
        new(Permissions.BlogPermissions.PostEdit,   "Edit Post",   group: "Blog"),
        new(Permissions.BlogPermissions.PostDelete, "Delete Post", group: "Blog"),
    };
}
```

**`Permissions/BlogPermissions.cs`**:

```csharp
namespace FlexCms.Blog.Permissions;

public static class BlogPermissions
{
    public const string PostCreate = "blog.post.create";
    public const string PostEdit   = "blog.post.edit";
    public const string PostDelete = "blog.post.delete";
}
```

### Step 5: Update the csproj to embed module.json

Edit `FlexCms.Blog.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <EmbeddedResource Include="module.json" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\FlexCms.Framework\FlexCms.Framework.csproj" />
    <ProjectReference Include="..\..\src\FlexCms.Core\FlexCms.Core.csproj" />
  </ItemGroup>
</Project>
```

### Step 6: Run the app — the module auto-loads

```bash
cd src/FlexCms.Host
dotnet watch run
```

Open `http://localhost:5000/admin/modules` — you should see "Blog" in the list. Click **Activate**.

### Step 7: Build out your module

Add entities, services, controllers, views following [`MODULE_DEV.md`](MODULE_DEV.md).

### Step 8: Commit and PR

```bash
git add .
git commit -m "feat(blog): initial Blog module scaffold"
git push origin feature/blog-module
gh pr create --base main --title "feat: Blog module v1"
```

---

## 5. Building & Packaging

When your module is ready, you need to **package it as a ZIP** so admins can upload it.

### Step 1: Publish the module (this creates a `publish/` folder with all dependencies)

```bash
cd modules/FlexCms.Blog
dotnet publish -c Release -o publish/
```

### Step 2: Verify the publish output

```bash
ls publish/
# You should see:
# FlexCms.Blog.dll
# module.json
# (any NuGet dependency DLLs)
```

### Step 3: Add Views and wwwroot folders

The module ZIP must contain:

```
FlexCms.Blog.zip
├── module.json
├── bin/           ← contents of publish/
│   ├── FlexCms.Blog.dll
│   └── (deps)
├── Views/         ← copy from your module folder
└── wwwroot/       ← copy from your module folder
```

### Step 4: Create the ZIP

**Windows PowerShell:**

```powershell
cd modules\FlexCms.Blog
Copy-Item -Recurse Views publish\Views
Copy-Item -Recurse wwwroot publish\wwwroot
Compress-Archive -Path publish\* -DestinationPath ..\..\FlexCms.Blog-1.0.0.zip -Force
```

**Linux/Mac:**

```bash
cd modules/FlexCms.Blog
cp -r Views publish/Views
cp -r wwwroot publish/wwwroot
cd publish
zip -r ../../../FlexCms.Blog-1.0.0.zip .
```

You now have `FlexCms.Blog-1.0.0.zip` in the repo root — this is what you upload via Admin UI.

### Step 5: Test the ZIP locally

1. Stop your local dev server
2. Open `http://localhost:5000/admin/modules`
3. Click **Upload Module** → select `FlexCms.Blog-1.0.0.zip`
4. Click **Activate**
5. Wait ~10 seconds for restart
6. Verify the module routes work (e.g., `/admin/blog/posts`)

---

## 6. Local Testing with Docker

Sometimes you need to test the **full Docker setup** locally before deploying.

### Step 1: Build the Docker image locally

```bash
docker build -f docker/Dockerfile -t flexcms:local .
```

This takes ~5 minutes the first time, then cached layers make rebuilds fast.

### Step 2: Run the full production stack locally

```bash
docker compose -f docker/docker-compose.prod.yml up -d
```

**Note:** You need to fill in `.env` with real values first — see Step 4 of First Time Setup.

### Step 3: Verify it's running

```bash
docker compose -f docker/docker-compose.prod.yml ps
# All containers should show "Up (healthy)"

curl http://localhost/health/ready
# Should return: {"status":"Healthy"}
```

### Step 4: Open the site

`http://localhost` — works just like production (without TLS).

### Step 5: Stop everything when done

```bash
docker compose -f docker/docker-compose.prod.yml down
```

To also delete data volumes (full reset):

```bash
docker compose -f docker/docker-compose.prod.yml down -v
```

---

## 7. Deploying to Production

This is the **first-time** production deployment. Once done, see [Section 8](#8-updating-an-existing-production-server) for updates.

### Step 1: Get a VPS

| Provider | Plan | Monthly Cost | Notes |
|---|---|---|---|
| Hetzner | CX21 | €5.83 | Best value (EU/US/Singapore) |
| DigitalOcean | $6 droplet | $6 | Easy UI, many regions |
| Linode | Nanode | $5 | Solid alternative |
| Contabo | VPS S | $4.50 | Cheapest, EU-based |

Pick one. Get **Ubuntu 22.04 LTS** image.

### Step 2: Buy a domain + Cloudflare setup (recommended)

1. Buy domain from Namecheap, GoDaddy, or any registrar (~$10/year)
2. Sign up for free Cloudflare account
3. Add your domain to Cloudflare → it gives you 2 nameservers
4. Update your domain registrar to use Cloudflare's nameservers (takes 1-24 hours to propagate)
5. In Cloudflare → DNS → Add A record:
   - Type: A
   - Name: `@` (root)
   - Value: your VPS IP
   - Proxy: ON (orange cloud) — for free DDoS protection

### Step 3: SSH into your VPS

```bash
ssh root@your-vps-ip
```

### Step 4: Initial server setup

```bash
# Create non-root user
adduser flexcms
usermod -aG sudo flexcms

# Switch to that user
su - flexcms

# Install Docker
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker flexcms
# Logout and login again so Docker group takes effect
exit
ssh flexcms@your-vps-ip

# Install firewall + fail2ban
sudo apt update
sudo apt install -y ufw fail2ban

# Configure firewall
sudo ufw allow 22/tcp
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable

# Install certbot (for HTTPS later)
sudo apt install -y certbot
```

### Step 5: Clone the repo on the VPS

```bash
cd /opt
sudo git clone https://github.com/rayhanul17/flex_cms_v1.git flexcms
sudo chown -R flexcms:flexcms /opt/flexcms
cd /opt/flexcms
```

### Step 6: Create production `.env` file

```bash
cp .env.example .env
nano .env
```

Fill in:

```bash
DOMAIN=mysite.com
SITE_NAME=My FlexCms Site
MYSQL_ROOT_PASSWORD=use-a-very-long-random-password-here-32-chars
DB_PASSWORD=another-different-long-random-password-32-chars

FLEXCMS__ConnectionString=Server=mysql;Database=flexcms;User=flexcms;Password=<DB_PASSWORD value>
FLEXCMS__SiteName=My FlexCms Site
FLEXCMS__BaseUrl=https://mysite.com
```

Save with `Ctrl+O`, `Enter`, `Ctrl+X`.

**Generate strong passwords:**

```bash
openssl rand -base64 32    # Run twice — once for each password
```

### Step 7: Get HTTPS certificate (one time)

```bash
# Stop nginx temporarily so certbot can use port 80
sudo systemctl stop nginx 2>/dev/null

# Get certificate
sudo certbot certonly --standalone \
    -d mysite.com -d www.mysite.com \
    --email admin@mysite.com \
    --agree-tos --no-eff-email

# Copy certificates to nginx folder
sudo mkdir -p /opt/flexcms/docker/nginx/certs/live/mysite.com
sudo cp /etc/letsencrypt/live/mysite.com/fullchain.pem /opt/flexcms/docker/nginx/certs/live/mysite.com/
sudo cp /etc/letsencrypt/live/mysite.com/privkey.pem /opt/flexcms/docker/nginx/certs/live/mysite.com/
sudo chown -R flexcms:flexcms /opt/flexcms/docker/nginx/certs
```

### Step 8: Start the production stack

```bash
cd /opt/flexcms
docker compose -f docker/docker-compose.prod.yml up -d
```

Wait ~30 seconds for everything to boot.

### Step 9: Check it's working

```bash
docker compose -f docker/docker-compose.prod.yml ps
# All containers should be "Up" and healthy

curl https://mysite.com/health/ready
# Should return: {"status":"Healthy"}
```

### Step 10: Run the Setup Wizard

Open `https://mysite.com` in your browser. The Setup Wizard will guide you through:

1. **Database** — should auto-detect from `.env`
2. **Site Info** — name, tagline
3. **Admin Account** — your production admin email + strong password
4. **Done** — short restart, then login at `/auth/login`

**Production is live!** 🎉

### Step 11: Set up daily backup cron

```bash
# Edit cron
crontab -e

# Add this line:
0 3 * * * /opt/flexcms/scripts/backup.sh >> /var/log/flexcms-backup.log 2>&1
```

Backups run nightly at 3 AM and upload to Backblaze B2 (configure B2 credentials in `.env`).

### Step 12: Set up TLS auto-renewal

```bash
# Test renewal
sudo certbot renew --dry-run

# Add to cron (auto-renew every Sunday)
sudo crontab -e

# Add this:
0 3 * * 0 certbot renew --quiet --post-hook "docker compose -f /opt/flexcms/docker/docker-compose.prod.yml restart nginx"
```

---

## 8. Updating an Existing Production Server

### Option A: Automatic (via GitHub Actions — recommended)

When you merge a PR to `main`, GitHub Actions:
1. Builds new Docker image
2. Pushes to GitHub Container Registry (GHCR)
3. SSHs into your VPS
4. Pulls new image and restarts

You don't need to do anything manually.

**To enable this**, set GitHub repo secrets:
- Go to GitHub repo → Settings → Secrets and variables → Actions
- Add: `SERVER_HOST` (VPS IP), `SERVER_USER` (flexcms), `SSH_KEY` (private SSH key), `DOMAIN` (mysite.com)

### Option B: Manual update

```bash
# SSH to VPS
ssh flexcms@your-vps-ip
cd /opt/flexcms

# Before updating — turn on maintenance mode (admin UI):
# https://mysite.com/admin/settings/maintenance → Enable (auto-disable in 30 min)

# Pull latest code + Docker image
git pull origin main
docker compose -f docker/docker-compose.prod.yml pull

# Restart with new image
docker compose -f docker/docker-compose.prod.yml up -d

# Wait for health check
sleep 15
curl https://mysite.com/health/ready

# If healthy — turn off maintenance mode in admin UI
```

---

## 9. Module Deployment to Production

### Step 1: Build the module ZIP locally (see Section 5)

```bash
cd modules/FlexCms.Blog
dotnet publish -c Release -o publish/
cp -r Views publish/Views
cp -r wwwroot publish/wwwroot
cd publish
zip -r ../../../FlexCms.Blog-1.0.0.zip .
```

### Step 2: Upload via Admin UI

1. Open `https://mysite.com/admin/modules`
2. Click **Upload Module** → select `FlexCms.Blog-1.0.0.zip`
3. Validation runs (file integrity, version compatibility, dependency check)
4. Module appears in list as **"Inactive"**
5. Click **Activate**
6. **Brief downtime: 5-15 seconds** (Docker auto-restarts container)
7. Module is now live — verify by visiting its routes

### Alternative: SCP + Docker exec

For automated deploys without using Admin UI:

```bash
# Copy ZIP to server
scp FlexCms.Blog-1.0.0.zip flexcms@vps:/tmp/

# SSH in
ssh flexcms@vps

# Copy into container's modules volume
docker cp /tmp/FlexCms.Blog-1.0.0.zip flexcms_flexcms_1:/app/modules/

# Extract inside container
docker exec flexcms_flexcms_1 \
    unzip -o /app/modules/FlexCms.Blog-1.0.0.zip \
    -d /app/modules/FlexCms.Blog/

# Restart container to load module
docker compose -f /opt/flexcms/docker/docker-compose.prod.yml \
    restart flexcms

# Then login to admin UI and click Activate (one-time)
```

### Step 3: Verify in production

- Visit `https://mysite.com/admin/modules` — module shows "Active"
- Visit module routes — they should respond
- Check `https://mysite.com/admin/system/dashboard` — no errors

---

## 10. Troubleshooting

### Build errors

```bash
dotnet restore
dotnet build
```

If still failing — delete `bin/` and `obj/` folders:

**Windows:**

```powershell
Get-ChildItem -Path . -Include bin,obj -Recurse | Remove-Item -Recurse -Force
```

**Linux/Mac:**

```bash
find . -type d \( -name bin -o -name obj \) -exec rm -rf {} +
```

Then `dotnet restore && dotnet build`.

### Database connection fails

1. Is the DB container running? `docker ps`
2. Is the password in `.env` correct?
3. Can you connect manually? `mysql -h localhost -u flexcms -p`

### Module fails to activate

Check logs:

```bash
# Local
cd src/FlexCms.Host
# Look at console output where dotnet watch is running

# Production
docker logs flexcms_flexcms_1 --tail 100
```

Common causes:
- Module DLL targets wrong .NET version (must be `net10.0`)
- Missing dependency DLL in ZIP (use `dotnet publish`, not `dotnet build`)
- `module.json` missing or invalid JSON

### TLS/HTTPS not working

```bash
sudo certbot certificates    # Check expiry
sudo certbot renew --dry-run # Test renewal
```

If certs expired:

```bash
sudo certbot renew
docker compose -f /opt/flexcms/docker/docker-compose.prod.yml restart nginx
```

### Out of disk space

```bash
df -h    # Check disk usage

# Clean Docker
docker system prune -af

# Clean old logs
sudo journalctl --vacuum-time=7d

# Check FlexCms data
du -sh /opt/flexcms/App_Data/
du -sh /var/lib/docker/volumes/
```

### Container won't start

```bash
docker compose -f docker/docker-compose.prod.yml logs flexcms
# Read the error message — usually missing env var or DB unreachable
```

### Need to rollback a bad release

```bash
# Find previous Docker image tag in GHCR
# (https://github.com/rayhanul17/flex_cms_v1/pkgs/container/flex_cms_v1)

# Pull specific version
docker pull ghcr.io/rayhanul17/flex_cms_v1:<previous-sha>

# Update docker-compose.prod.yml to use that tag
# Then restart
docker compose -f docker/docker-compose.prod.yml up -d
```

### Forgot admin password

You can reset via DB directly:

```bash
docker exec -it flexcms_mysql_1 mysql -u root -p flexcms

UPDATE fcms_users SET PasswordHash = NULL WHERE Email = 'admin@mysite.com';
```

Then visit `/auth/forgot-password` and use the email reset flow.

---

## 📚 Further Reading

- **Architecture details:** [`docs/plan.md`](plan.md) — full 14,500-line spec
- **Module dev rules:** [`docs/MODULE_DEV.md`](MODULE_DEV.md)
- **Production deploy details:** [`docs/DEPLOYMENT.md`](DEPLOYMENT.md)
- **Contributing rules:** [`CONTRIBUTING.md`](../CONTRIBUTING.md)

---

## ❓ Quick Reference Card

| Task | Command |
|---|---|
| Pull latest | `git checkout main && git pull` |
| New feature | `git checkout -b feature/X` |
| Commit | `git commit -m "feat(scope): message"` |
| Push | `git push -u origin <branch>` |
| Open PR | `gh pr create --base main` |
| Run dev | `cd src/FlexCms.Host && dotnet watch run` |
| Run tests | `dotnet test` |
| Format code | `dotnet format` |
| Start local DB | `docker compose -f docker/docker-compose.dev.yml up -d` |
| Stop local DB | `docker compose -f docker/docker-compose.dev.yml down` |
| Build module ZIP | `cd modules/X && dotnet publish -c Release -o publish/` |
| Deploy to prod | Push to `main` → GitHub Actions auto-deploys |
| Update prod manually | `ssh vps && cd /opt/flexcms && git pull && docker compose pull && docker compose up -d` |
| Check prod health | `curl https://mysite.com/health/ready` |

---

**Questions?** Open an issue at https://github.com/rayhanul17/flex_cms_v1/issues
