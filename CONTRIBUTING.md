# Contributing to FlexCms

Thanks for your interest! This guide covers branching, code style, and PR conventions.

---

## 🌿 Branching

We follow **GitHub Flow**:

- `main` — always production-ready; protected; auto-deploys on push
- `develop` — integration branch (optional safety net for batched changes)
- `feature/short-description` — new features
- `fix/short-description` — bug fixes
- `hotfix/short-description` — urgent production fixes
- `chore/short-description` — non-code changes (deps, docs, CI)

```bash
# Daily workflow:
git checkout main && git pull
git checkout -b feature/blog-comments
# ... commit work ...
git push -u origin feature/blog-comments
gh pr create
```

Keep branches **short-lived** (< 1 week ideal). Long-running branches accumulate merge conflicts.

---

## 💬 Commit Message Convention

We use [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<scope>): <subject>

[optional body]

[optional footer]
```

**Types:** `feat`, `fix`, `chore`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`

**Scopes (FlexCms-specific):** `framework`, `core`, `host`, `auth`, `cms`, `media`, `module`, `theme`, `payment`, `chat`, `ecom`, `i18n`, `docker`, `deploy`

**Examples:**

```
feat(blog): add comment moderation queue
fix(payment): bkash webhook signature validation
chore(deps): bump EF Core 10.0.1
docs(plan): add cancellation token coverage
refactor(repository): extract soft delete filter to base class
ci(github-actions): add docker build cache
```

---

## ✅ Code Style (enforced via .editorconfig + Roslyn)

- **Indent:** 4 spaces (C#), 2 spaces (web/yaml/json)
- **Line endings:** LF (CRLF only for `.sln`)
- **Async naming:** suffix with `Async`
- **CancellationToken:** REQUIRED on all async public methods (Roslyn CA2016 enforces forwarding)
- **Private fields:** `_camelCase` prefix
- **Interfaces:** `IPascalCase` prefix
- **Use `var`** when type is apparent

Run `dotnet format` before committing — auto-applies all rules.

---

## 🧪 Testing

```bash
dotnet test                          # all tests
dotnet test tests/FlexCms.Framework.Tests   # specific project
```

Add tests when:

- Fixing bugs (regression test)
- Adding new public API
- Touching auth, payment, or any security-sensitive path
- Refactoring complex logic

---

## 📋 PR Checklist

Before opening a PR:

- [ ] Branch created from latest `main`
- [ ] All tests pass locally (`dotnet test`)
- [ ] No lint warnings (`dotnet format --verify-no-changes`)
- [ ] Commit messages follow Conventional Commits
- [ ] PR description explains *what* and *why* (not just *how*)
- [ ] Linked to issue/discussion if applicable
- [ ] No secrets committed (check `.env`, `setup.json`, `*.key`)
- [ ] CancellationToken propagated to all new async methods
- [ ] Architecture plan (`docs/plan.md`) updated if you introduce new patterns

---

## 🚦 CI Requirements

Your PR must pass:

- ✅ Build succeeds (no warnings)
- ✅ All tests pass
- ✅ No new linter violations
- ✅ Docker image builds (for changes to Host/Dockerfile)

---

## 📝 Module Development

See [`docs/MODULE_DEV.md`](docs/MODULE_DEV.md) for module structure, scaffold commands, and packaging.

```bash
# Quick scaffold:
dotnet new flexcms-module -n MyCompany.Blog -o modules/MyCompany.Blog
```

---

## 📜 License

By contributing, you agree your contributions are licensed under the MIT License (see [`LICENSE`](LICENSE)).
