# FlexCms

A modular, plug-and-play CMS built on .NET 10 — designed for Bangladesh market, Bangla-first, single-instance deployment-friendly.

> **🚀 New developer? Start here:** [`docs/DEVELOPER_GUIDE.md`](docs/DEVELOPER_GUIDE.md) — step-by-step from clone to production deploy.
>
> **Architecture plan:** see [`docs/plan.md`](docs/plan.md) (~14,500-line self-contained spec covering all 17 development phases)

---

## ✨ Features

- 🌐 **Multi-database** — MySQL, PostgreSQL, SQL Server, MongoDB (provider-agnostic via `IRepository<T>`)
- 🧩 **Plug-and-play modules** — drop ZIP into Admin → click activate → done
- 🎨 **3 themes** — AdminLTE (admin + fallback), Bootstrap 5 (public), Tailwind CSS (public)
- 🌏 **i18n built-in** — English + Bangla, easy to add more languages (RTL-friendly)
- 💳 **Bangladesh payment gateways** — bKash, SSLCommerz, Nagad
- 📱 **SMS** — Alpha, MRAM, Onnorokom (BD market)
- 💬 **Real-time chat** — SignalR-based admin↔user chat
- 📰 **Editorial workflow** — submit → review → approve → publish (multi-author safe)
- 🔒 **Production security** — 2FA TOTP, OAuth (Google/Facebook/Microsoft/GitHub), API tokens, rate limiting, IP allowlist, CSP nonces
- 🛒 **Ecommerce-ready** — payment gateways, inventory race protection, cart merge, tax calc, shipping abstraction (next module)
- 🐳 **Docker deployment** — single-host docker-compose, no Kubernetes needed

---

## 🚀 Quick Start

### Local Development (Docker for DB + dotnet watch for app)

```bash
# 1. Clone:
git clone https://github.com/rayhanul17/flex_cms_v1.git
cd flex_cms_v1

# 2. Start DB + Mailhog containers:
docker compose -f docker/docker-compose.dev.yml up -d

# 3. Run app from host (faster iteration than running in container):
cd src/FlexCms.Host
dotnet watch run

# 4. Open browser:
# http://localhost:5000  →  Setup wizard auto-redirects on first run
```

### Production Deployment (Docker on single VPS)

See [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md). Total cost: ~$10-15/month for full production stack on Hetzner/DigitalOcean।

---

## 📁 Repository Structure

```
flex_cms_v1/
├── src/
│   ├── FlexCms.Framework/      # Core abstractions (IRepository, IFcmsModule, etc.)
│   ├── FlexCms.Core/            # Built-in: Admin, Auth, CMS, Users, Media
│   └── FlexCms.Host/            # MVC entry point + setup wizard
├── modules/                     # Plug-and-play modules (Blog, Ecommerce, etc.)
├── themes/
│   ├── FlexCms.Theme.AdminLte/  # Admin + fallback theme
│   ├── FlexCms.Theme.Bootstrap/ # Public Bootstrap 5
│   └── FlexCms.Theme.Tailwind/  # Public Tailwind CSS
├── tests/                       # xUnit test projects
├── docker/                      # Dockerfile + docker-compose files
├── scripts/                     # Deploy, backup, scaffold helpers
├── docs/                        # Documentation (incl. plan.md)
└── .github/workflows/           # CI/CD pipelines
```

---

## 🌿 Branching Strategy

**GitHub Flow** (solo / small team friendly):

| Branch | Purpose |
|---|---|
| `main` | Always production-ready; auto-deploy on push |
| `develop` | Integration branch (optional safety net) |
| `feature/*` | Short-lived work branches → PR → merge to `main` |
| `hotfix/*` | Urgent production fixes → PR → merge to `main` |

```bash
# Daily workflow:
git checkout main && git pull
git checkout -b feature/blog-comments
# ... work ...
git push -u origin feature/blog-comments
gh pr create   # creates pull request → CI runs → merge → auto-deploy
```

**Tagging releases:**

```bash
git tag -a v1.0.0 -m "Release 1.0.0"
git push origin v1.0.0
# GitHub Actions release.yml fires → builds + publishes Docker image to GHCR
```

---

## 🛠 Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 (LTS-track) |
| Web | ASP.NET Core MVC |
| ORM | EF Core 10 (MySQL / Postgres / MSSQL) + MongoDB.Driver |
| Auth | ASP.NET Core Identity (custom stores — DB-agnostic) |
| Real-time | SignalR (in-process, no Redis) |
| Editor | Toast UI Editor (MIT — true free) |
| PDF | PdfSharp 6.x (MIT) |
| Excel | ClosedXML (MIT) |
| Logging | Serilog + Async sink |
| Cache | IMemoryCache (in-process, single-instance optimized) |
| Background jobs | IHostedService + PeriodicTimer (NO Hangfire — keep it simple) |
| Deployment | Docker Compose + nginx + Let's Encrypt |

All packages MIT/Apache/BSD licensed — no GPL contagion।

---

## 🗺 Development Phases

The plan covers **17 phases** across CMS Core, Production Hardening, Modern UX & AI:

1-12. **Core CMS** (Phase 1-12): DB layer, Auth, Modules, CMS, Media, i18n, Email/SMS, Admin UX, Chat, Themes, Payment
13. **Auth Hardening** (health, sessions, 2FA, OAuth, status pages)
14. **API + Integrations + Engagement** (API tokens, webhooks, comments, forms, newsletter)
15. **SEO + Performance + Ops + Compliance** (output cache, backup, maintenance, GDPR)
16. **Performance Critical + A11y + Editorial** (image optimize, full-text search, WCAG)
17. **Modern UX + AI + Marketplace** (Cmd+K search, PWA, AI provider, Prometheus, marketplace)

See [`docs/plan.md`](docs/plan.md) for full details with checkbox verification per phase।

---

## 📜 License

MIT License — see [`LICENSE`](LICENSE). You can change to any other license (Apache 2.0, GPL, proprietary) before public release if needed.

> **Note:** This applies to FlexCms code itself. Third-party libraries (.NET, EF Core, Toast UI Editor, etc.) retain their own licenses (all MIT/Apache/BSD compatible).

---

## 🤝 Contributing

This is currently a personal project. PRs welcome from collaborators.

For commercial inquiries / support: rayhanulraj210@gmail.com

---

## 🇧🇩 Made for Bangladesh

বাংলাদেশের context-এ design করা — bKash/SSLCommerz/Nagad payment, Onnorokom/Alpha/MRAM SMS, Bangla i18n, Bangla PDF font (Kalpurush), single-VPS-friendly।
