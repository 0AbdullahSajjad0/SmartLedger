# SmartLedger

AI-powered expense and invoice manager, built as a portfolio project (Upwork profile). Core loop: user scans a
receipt → AI extracts vendor, date, amounts, line items → user reviews and approves → spending analytics.

- Live site: https://red-coast-0ea7ce800.5.azurestaticapps.net
- Live API: https://ca-smartledger-api.kindfield-c023326b.centralindia.azurecontainerapps.io (`/health`, `/health/ready`)

## Working with the owner

- **The owner commits, pushes and opens PRs themselves.** Write code on a feature branch, explain each change, and
  give a commit message. Never commit or push unless asked.
- Explain decisions and trade-offs; the owner is learning and wants the "best option, not the easiest".
- Azure and GitHub setup is done by the owner in the **Azure Portal / GitHub website** (no az/gh CLI installed).
  Give exact click paths.
- Never ask for secrets in chat (the Claude account is shared). Secrets go to .NET user secrets locally (read from the
  clipboard, e.g. `@{ "Gemini:ApiKey" = (Get-Clipboard).Trim() } | ConvertTo-Json | dotnet user-secrets set --project backend/src/SmartLedger.Api`)
  and to Container App secrets in production.
- Owner's terminal: Windows; Command Prompt and PowerShell are both in use. Say which shell a command is for.
- Verify before claiming: build with CI settings, run tests, and click through in the browser for UI work.

## Repository layout

```
backend/                 .NET 10 solution (SmartLedger.slnx), opened in Visual Studio 2026
  src/SmartLedger.Api            Controllers, middleware, Program.cs (composition root)
  src/SmartLedger.Application    Services, DTOs, interfaces (repositories, storage, AI), Result<T>
  src/SmartLedger.Domain         Entities and rules, no dependencies
  src/SmartLedger.Infrastructure EF Core, Identity, Azure Blob, Gemini, background worker
  tests/SmartLedger.UnitTests              xUnit
  tests/SmartLedger.Api.IntegrationTests   WebApplicationFactory against real SQL Server
frontend/                Angular 22 app (opened in VS Code)
.github/workflows/ci-cd.yml   single CI/CD workflow
```

Dependencies point inward: Api → Infrastructure → Application → Domain. Repository/storage/AI interfaces live in
Application, implementations in Infrastructure (registered in each layer's `DependencyInjection.cs`).

## Commands

```bash
# Backend (from backend/)
dotnet build SmartLedger.slnx
dotnet test SmartLedger.slnx              # integration tests need LocalDB (SmartLedger_Tests DB, recreated each run)
dotnet run --project src/SmartLedger.Api --launch-profile http     # http://localhost:5217, docs at /scalar
dotnet ef migrations add <Name> --project src/SmartLedger.Infrastructure --startup-project src/SmartLedger.Api --output-dir Persistence/Migrations

# CI-equivalent build (warnings are errors on CI): set GITHUB_ACTIONS=true before building

# Frontend (from frontend/)
npm ci
npm start                                 # http://localhost:4200, talks to http://localhost:5217
npx ng test --watch=false                 # Vitest
npx ng build
```

Local development: API on LocalDB `(localdb)\MSSQLLocalDB`, database `SmartLedger`, migrated and seeded with a demo user
on startup (Development only). Uploaded files go to `backend/src/SmartLedger.Api/App_Data/receipts` (git-ignored).

## Hosting and deployment (all free tier, Azure Pay-As-You-Go with a $1 budget alert)

| Part | Service | Notes |
|---|---|---|
| API | Azure Container Apps `ca-smartledger-api` (rg `rg-smartledger`, Central India) | Docker image from GHCR, 0.25 vCPU, min 0 / max 1 replicas, logs off |
| Frontend | Azure Static Web Apps (Free) | `frontend/public/staticwebapp.config.json`: SPA fallback, strict CSP, security headers |
| Database | Azure SQL free offer, server `sql-smartledger-as26`, db `sqldb-smartledger` | Entra-only auth; serverless auto-pause; API uses managed identity (read/write only) |
| Files | Azure Blob Storage, container `receipts` | key access disabled; API identity has Storage Blob Data Contributor |
| AI | Google Gemini API, **free tier** (in-app privacy notice) | key in Container App secret → env `Gemini__ApiKey` |

CI/CD (`ci-cd.yml`): change detection per folder; backend build/test (SQL Server service container) → migration
bundle → Docker image → deploy (OIDC login, temporary SQL firewall rule for the runner, apply migrations, update
image by commit SHA, smoke test `/health` and `/health/ready`); frontend test/build → Static Web Apps. "CI result" is
the single required check on `main`. Production config not in the repo: `ConnectionStrings__Default` (managed
identity), `Cors__AllowedOrigins__0`, `Jwt__SigningKey` (secret), `Storage__BlobServiceUri`, `Gemini__ApiKey` (secret).
New required settings must be added in Azure **before** merging, because options are validated on startup.

## Key decisions

- **Auth:** ASP.NET Core Identity + JWT access tokens (15 min, in memory) and rotating refresh tokens (7 days, 30 with
  "keep me signed in"), stored hashed; reuse of a used token revokes the whole session family. Refresh token is in
  `localStorage` for now (no custom domain); planned move to an HttpOnly cookie once a domain exists. Sign-in is
  required by default (fallback policy); lockout after 5 failures; per-IP auth rate limit.
- **Money and currency:** every receipt keeps its own currency. Users have a base currency. On approval the total is
  converted using the `ExchangeRates` table (rates vs USD by date) and frozen on the receipt (`BaseCurrencyTotal`).
  Reports sum base-currency totals. Category budgets apply to every month until changed (no budget history yet).
- **IDs:** Guid v7 created in code; EF configured `ValueGeneratedNever` for `Entity` types.
- **Uploads:** type detected from file bytes (JPG, PNG, WebP, HEIC, PDF), 10 MB per file enforced while receiving,
  10 files per request, per-user upload rate limit, stored under `{userId}/{receiptId}.ext`, served only to the owner
  with sandbox CSP. The upload action reads the form itself (no action parameters) so oversize files get a 413.
- **AI extraction:** `IReceiptExtractor` → `GeminiReceiptExtractor` with structured output (strict schema; category
  enum = the user's categories). Background worker (`ReceiptExtractionWorker`) with in-memory queue + 5-minute sweep,
  7 s between requests (free-tier pacing). `ExtractionSanitizer` validates everything (ISO currency, plausible dates,
  last-4 card numbers, totals vs line items) and turns doubts into `ExtractionFlags`. 3 failed attempts → Failed.
  Model setting defaults to `gemini-flash-lite-latest` (1–2 s per receipt; full Flash models were overloaded or
  very slow on the free tier, and `gemini-2.5-*` returns 404 for new keys).
- **Queries:** EF Core LINQ in repositories (parameterized). No stored procedures; raw SQL only if a report needs it,
  added through a migration.

## Frontend conventions

- Angular 22: standalone components, zoneless, `ChangeDetectionStrategy.OnPush`, `inject()`, signal `input()`,
  `@if/@for/@switch`, `httpResource` for data loading, signal-based stores (`AuthStore`, `UploadQueue`).
- Typed reactive forms (`NonNullableFormBuilder`). Router query/path params bound as inputs (`withComponentInputBinding`).
- Folders: `core/` (singletons: auth, API clients, config), `layout/` (AuthLayout, AppShell), `shared/` (UI pieces,
  pipes), `features/` (lazy-loaded pages).
- Design: **"Fintech Editorial"**: espresso `#1C1A17`, cream `#F4EFE4`, single accent burnt terra cotta `#A84B26`;
  Playfair Display (headings, figures), Space Grotesk (UI), JetBrains Mono (numbers, dates, tables); square corners,
  1px ledger rules, asymmetric columns. Tokens in `frontend/src/styles/_tokens.scss`, shared classes in `styles.scss`
  (`.btn`, `.field`, `.ledger`, `.page-header`, `.label`, `.eyebrow`, `.num`, `.link-button`). Use Angular CDK (not
  Material) when overlays/dialogs are needed. No generic AI-SaaS look (no indigo, gradients, rounded card grids).
- Static prototype of all screens lives outside the repo (owner's Documents folder).

## Status

Done: API foundation, CI/CD, Azure SQL, auth (API + Angular), design system, upload API with hardening, Scan page,
receipts ledger, receipt page, AI extraction (branch `feature/ai-extraction`: built and tested with a fake extractor;
a real Gemini run is still to be verified locally, and the production `Gemini__ApiKey` secret must exist before merge).

Next, in order:
1. Verify a real receipt through Gemini locally (check which models the key can use; print model names only).
2. Image re-encoding (strip EXIF/GPS, HEIC → JPEG preview, cap dimensions); PDF first page as image.
3. Review and approve: edit fields, approve/reject, currency conversion, exchange-rate job (free source that covers
   PKR), optional "amount charged" override.
4. Categories and budgets page; Overview dashboard; Reports with AI monthly insights.
5. Later: cold-start UX (`/health/ready` warm-up on page load), demo account, custom domain + HttpOnly cookie.
