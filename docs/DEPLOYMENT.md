# Deployment Guide

> Full production deployment recipe — see [`plan.md`](plan.md) **PART 0.9** for complete details (Docker compose, nginx, fail2ban, Cloudflare, backups).

---

## 🏃 Quick Production Deploy (single VPS, Docker)

### Prerequisites

- VPS: Hetzner CX21 (€5.83/mo) OR DigitalOcean ($6/mo) OR similar
- Domain pointed to VPS via Cloudflare (recommended) or directly
- SSH access

### One-time Server Setup

```bash
# Install Docker:
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER

# Install fail2ban + ufw:
sudo apt install -y fail2ban ufw
sudo ufw allow 22,80,443/tcp
sudo ufw enable
```

### Deploy FlexCms

```bash
# 1. Clone repo on server:
git clone https://github.com/rayhanul17/flex_cms_v1.git /opt/flexcms
cd /opt/flexcms

# 2. Copy env template + edit:
cp .env.example .env
nano .env   # set DOMAIN, SITE_NAME, MYSQL_ROOT_PASSWORD, DB_PASSWORD

# 3. Bootstrap TLS via certbot (one-time):
docker compose -f docker/docker-compose.prod.yml run --rm certbot \
    certonly --webroot -w /var/www/certbot \
    -d $DOMAIN -d www.$DOMAIN \
    --email admin@$DOMAIN --agree-tos --no-eff-email

# 4. Start full stack:
docker compose -f docker/docker-compose.prod.yml up -d

# 5. Verify:
curl https://$DOMAIN/health/ready
# {"status":"Healthy"}
```

### First-time Setup

Open `https://$DOMAIN` → Setup wizard auto-redirects → 4 steps:

1. **Database** — your DB credentials (Test Connection)
2. **Site Info** — name, tagline, URL, default language
3. **Admin Account** — first SuperAdmin user
4. **Done** — auto-restart → admin login at `/auth/login`

---

## 🔄 Updates / New Releases

```bash
# Push to main triggers GitHub Actions → builds image → SSH deploys.
# Manual deploy:
ssh user@vps
cd /opt/flexcms
git pull
docker compose -f docker/docker-compose.prod.yml pull
docker compose -f docker/docker-compose.prod.yml up -d
curl -f https://$DOMAIN/health/ready
```

---

## 📦 Module Deployment

```bash
# Locally — build module:
cd modules/MyCompany.Blog
dotnet publish -c Release -o publish/
cd publish && zip -r ../../MyCompany.Blog.zip . && cd ..

# Upload via Admin UI:
# https://$DOMAIN/admin → Modules → [Upload] → MyCompany.Blog.zip → Activate
# Brief 5-15s downtime during activation, then module live
```

---

## 💾 Backup

Auto-runs daily at 3 AM via cron (configured in `scripts/backup.sh`):

- DB dump → Backblaze B2
- Volume snapshots → B2
- Local 7-day retention; B2 30-day retention

Manual backup:

```bash
ssh user@vps
cd /opt/flexcms
./scripts/backup.sh
```

Restore:

```bash
./scripts/restore.sh 2026-04-15   # date of backup to restore
```

---

## 🛡 Monitoring

- **Admin dashboard:** `https://$DOMAIN/admin/system/dashboard` — CPU, memory, disk, request rate, error rate, recent errors
- **Health probes:** `/health` (full), `/health/ready` (readiness), `/health/live` (liveness)
- **Uptime:** Uptime Robot free tier monitors `/health/ready` every 5 min → email alert
- **Logs:** `/opt/flexcms/App_Data/logs/` (Serilog rolling files, 30-day retention)

---

## 🚨 Maintenance Mode

When deploying / running migrations:

```
Admin → Settings → Maintenance Mode → Enable (auto-disable in 30 min)
# Visitors see 503 + maintenance page
# Admins still access /admin via role bypass
# Bypass URL: https://$DOMAIN?fcms_bypass=<token>
```

Live-reload setting — no restart needed to toggle.

---

## 📋 Production Checklist (before going live)

- [ ] Domain DNS pointed to VPS (Cloudflare proxy ON)
- [ ] TLS certificate active (HTTPS works)
- [ ] `.env` configured with strong passwords
- [ ] DataProtection keyring on persistent volume
- [ ] Email SMTP tested (admin Settings → Email → Test)
- [ ] Backup cron running (verify after 24h)
- [ ] Health probe returns 200
- [ ] First SuperAdmin user created via setup wizard
- [ ] Email verification working (test register)
- [ ] Maintenance mode tested
- [ ] Cloudflare WAF rules enabled
- [ ] fail2ban service active

---

For full details see [`docs/plan.md`](plan.md) **PART 0.5** (Production Hardening) + **PART 0.9** (Docker Deployment).
