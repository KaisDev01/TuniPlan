# TuniPlan — Back-end API (.NET 10)

REST API for the TuniPlan booking app (Expo / React Native front-end).
N-layer architecture with **Repository + Unit of Work**, **managers** (business layer), **DTOs** and **controllers**.

---

## 1. Solution structure

```
TuniPlan.sln
├── AI/                         (solution folder)
│   ├── AIL/                    AI layer: "secrétaire IA" agent (rule-based + LLM)
│   └── IAConnectors/           LLM providers (Anthropic connector, "None" fallback)
├── Data/                       (solution folder)
│   ├── DAL/                    DbContext, EF Core configurations, interceptor, demo seeder
│   └── DAO/                    Generic Repository, specific repositories, Unit of Work
├── Tools/                      (solution folder)
│   └── Common/                 Exceptions, password hashing, TOTP, secure random, helpers
├── BL/                         Managers (business rules) + slot calculator + mapping
├── DTOs/                       Request / response objects (with validation attributes)
├── Entities/                   Models (tables) and enums
├── LoggerService/              ILoggerManager + rolling file logger
├── NotificationService/        SMS / WhatsApp / e-mail / push senders
├── OperationStorage/           Image uploads (validated, stored on disk)
├── TuniPlan/                   ASP.NET Core Web API: controllers, JWT, security, Program.cs
└── Tests.Unit/                 xUnit tests (security tools, slot calculator, authentication)
```

Dependency direction (each layer only knows the ones below it):

```
TuniPlan (Controllers) → BL (Managers) → DAO (Repositories + UoW) → DAL (DbContext) → Entities
                          ↘ DTOs   ↘ AIL → IAConnectors   ↘ NotificationService / OperationStorage / LoggerService / Common
```

- **Controllers** only receive DTOs, call a manager, return DTOs. No business logic.
- **Managers** hold all business rules and use `IUnitOfWork` (never the DbContext directly).
- **Unit of Work** exposes all repositories on one DbContext, `SaveChangesAsync()` and
  `ExecuteInTransactionAsync()` (used with `Serializable` isolation to prevent double bookings).

---

## 2. Run it

**Requirements**: .NET 10 SDK, Visual Studio 2022 17.14+ / VS 2026 or Rider, SQL Server (LocalDB is enough in development).

```bash
# 1. Restore tools and packages
dotnet tool restore
dotnet restore

# 2. (Recommended) set your own JWT key instead of the dev one
cd TuniPlan
dotnet user-secrets set "Jwt:SigningKey" "<at least 32 random characters>"

# 3. Create the first migration (the database is created automatically in Development)
cd ..
dotnet ef migrations add InitialCreate --project Data/DAL --startup-project TuniPlan
dotnet ef database update --project Data/DAL --startup-project TuniPlan   # optional: Development migrates at startup

# 4. Run
dotnet run --project TuniPlan
```

Open **https://localhost:7180/scalar** for the interactive API documentation (click "Authorize" and paste an access token).
Change the connection string `ConnectionStrings:TuniPlanDb` in `appsettings.json` if you don't use LocalDB.

Run tests: `dotnet test`.

> ⚠️ This code was written in an environment without the .NET SDK, so it has **not been compiled yet**.
> If `dotnet build` shows errors, they should be small (a namespace, a package version). Ask Claude Code on your computer:
> *"Build the solution and fix all compile errors without changing the architecture."*
> The most version-sensitive file is `TuniPlan/Infrastructure/BearerSecuritySchemeTransformer.cs` (OpenAPI 2.0 API).

### Demo accounts (seeded when `Database:SeedDemoData=true`)

Password for all: **`TuniPlan#2026`** (constant `DbSeeder.DemoPassword`) — login with the email or the phone number.
The seeder only runs on an **empty** database; on a database that already has users, these accounts do not exist.

| Account | Email | Phone | Role |
|---|---|---|---|
| Client | kais@tuniplan.demo | +21622000001 | Client |
| Cabinet médical (Sousse) | olfagharbi@tuniplan.demo | +21698000001 | Business + Client |
| Location voiture (rental mode) | voit25@tuniplan.demo | +21698000002 | Business |
| Avocate (Sfax) | salmakarray@tuniplan.demo | +21698000003 | Business |
| Pharmacie (queue/ticket mode) | pharmawifak@tuniplan.demo | +21698000004 | Business |
| Salon de coiffure (team of 2) | maisonlilia@tuniplan.demo | +21698000005 | Business |
| Admin | admin@tuniplan.demo | +21620000000 | Admin + Business + Client |

> Cabinet Dr. Karim Ben Ammar (Tunis) belongs to the Admin account.

### Test data clean-up

`scripts/cleanup-test-data.sql` removes the integration-test account `+21699887766` and business
`01a0e8ab-9bcf-7bae-a829-eb369a444665` (soft delete + anonymisation, in a transaction you commit manually).

---

## 3. Authentication (strong security)

| Feature | How |
|---|---|
| Passwords | PBKDF2-HMAC-SHA512, 210 000 iterations, random salt, constant-time check, automatic re-hash. Policy: 8+ chars, letters + digits, common passwords refused |
| Phone verification | 6-digit SMS code, stored **hashed**, expires in 10 min, 5 attempts max, 60 s resend cooldown, 5 codes/hour |
| Access token | JWT HS256, **15 minutes**, issuer/audience/lifetime/algorithm validated, 30 s clock skew |
| Refresh token | 512-bit random, **only the SHA-256 hash is stored**, 30 days, **rotation** on every use, **reuse detection** (an old token presented again revokes the whole session family) |
| Instant revocation | `SecurityStamp` in every token: password change/reset, "logout all devices", account deletion → all access tokens rejected (checked on each request, 2-min cache invalidated on change) |
| Brute force | Account **lockout** after 5 failed logins (15 min) + rate limiting: 30 req/min/IP on `/api/auth/*` (`RateLimits:AuthPerMinute`, 300 in Development), 20 msg/min on the AI, 300 req/min globally |
| No enumeration | Same error for unknown user / wrong password (with equal timing), forgot-password always answers 202 |
| 2FA (optional) | TOTP (Google/Microsoft Authenticator); secret encrypted with ASP.NET Data Protection; login returns a 5-min challenge |
| Sessions | List active devices, revoke one session |
| Authorization | Secure by default (fallback policy = authenticated), roles `Client` / `Business` / `Admin`, and every business endpoint checks that the user is a member (or owner) of that organization |
| Hardening | HTTPS + HSTS, security headers (CSP, X-Frame-Options, nosniff…), CORS whitelist, ProblemDetails errors without internal details, audit log of security events, upload validation (type, size, magic bytes, random names) |

### Front-end flow

1. `POST /api/auth/register` `{ accountType: "Client" | "Business", firstName, lastName, phoneNumber, email?, password }`
   → SMS code (`devCode` is returned in Development).
2. `POST /api/auth/verify-phone` `{ phoneNumber, code }` → `{ accessToken, refreshToken, user }`.
3. Send `Authorization: Bearer <accessToken>` on every call.
4. On **401**, call `POST /api/auth/refresh` `{ refreshToken }` once, store the **new** pair, replay the request.
   If refresh fails → go back to login. Store tokens in `expo-secure-store` (not AsyncStorage).
5. Login: `POST /api/auth/login` `{ identifier: email or phone, password }` → may return
   `requiresPhoneVerification` or `requiresTwoFactor` + `challengeId` (then `POST /api/auth/login/2fa`).
6. A client can become a business: `POST /api/account/business-mode`, then refresh/login to get the new role.
7. Forgotten password: `POST /api/auth/forgot-password` `{ identifier }` then `POST /api/auth/reset-password`
   `{ identifier, code, newPassword }` — `identifier` is the email or the phone (the old `phoneNumber` field still works).
8. All dates are UTC and serialized with a trailing `Z` (e.g. `2026-09-28T08:00:00Z`): convert to local time in the app.

---

## 4. Main endpoints

**Public** — `GET /api/reference` (categories + 24 governorates) · `GET /api/organizations?q=&category=&governorate=&city=&date=&openNow=&minRating=&maxPrice=&sort=&lat=&lng=` ·
`GET /api/organizations/{id}` and `/by-slug/{slug}` (full business page: photos, description, team, services with prices and promotions, hours, **review summary + latest reviews**, **next free slots**) ·
`/{id}/availability?serviceId=&from=&days=&resourceId=` (`serviceId` optional: first active service; unpublished businesses answer 404) · `/{id}/reviews?sort=&withPhotos=&serviceId=` · `/{id}/reviews/summary`

**Client** — `/api/account/*` (profile, avatar, notification settings, family members, favorites, delete) ·
`POST /api/appointments` (book: slot, rental days or queue ticket) · `GET /api/appointments?scope=upcoming|past` ·
`POST /{id}/cancel` · `/{id}/reschedule` · `/{id}/counter-offer/respond` · `GET /{id}/calendar.ics` · `GET /to-review` ·
`/api/waitlist` · `POST /api/reviews` (verified: completed appointment only) · `/api/notifications` ·
`/api/ai-secretary/chat` + `/requests` · `/api/payments/deposit`

**Business** (`/api/business/organizations/...`) — create/update/`DELETE` the business (wizard steps), `transfer` (new owner by email or phone), `opening-hours`, `rules`,
`verification`, `publish`, `images/{logo|cover|photo}`, `completion` (profile %), `public-link`, `closed-periods`,
`services`, `resources`, `promotions`, `appointments` (agenda, `requests`, confirm / refuse / counter-offer / complete /
no-show / cancel / reschedule / note / `walk-in`), `clients` (CRM), `reviews` (+ reply), `dashboard`.

**Admin** — `/api/admin/verifications` (approve / reject businesses).

Booking rules implemented: opening hours + breaks, closed periods (whole business or one resource), team members / rooms /
vehicles, slot step, lead time, booking horizon, manual or automatic confirmation, cancellation deadline, deposits,
promotions (days, hours, last-minute), blocked clients, max pending requests, **no double booking** (serializable transaction).
The AI secretary **never confirms**: it proposes slots and creates a *pending* request that the owner confirms.

Background job (every 5 min): reminders 24 h and 2 h before (template editable by the business), review requests 2 h after
a completed appointment, expiry of unanswered requests.

---

## 5. Configuration (`appsettings.json`)

| Section | Purpose |
|---|---|
| `ConnectionStrings:TuniPlanDb` | SQL Server |
| `Jwt` | Issuer, audience, **SigningKey (secret!)**, token lifetimes |
| `Security` | Lockout, code rules, `ExposeDevCodes` (dev only), `EnableMockPayments` (dev/demo only) |
| `App:PublicWebUrl` | Front-end URL used in public business links (`/b/{slug}`) |
| `Cors:AllowedOrigins` | Allowed front-end origins. `*` inside a host label is a wildcard (e.g. `https://agenda-pro-front-*.vercel.app` for Vercel previews); a lone `"*"` allows any origin |
| `RateLimits:AuthPerMinute` | Requests per minute per IP on `/api/auth/*` |
| `Database` | `MigrateOnStartup`, `SeedDemoData` (true in Development) |
| `Notifications` | `SmsProvider`: `Console` (logs only) or `Http` + your SMS gateway URL/key |
| `AI` | `Provider`: `None` (rule-based, understands FR / AR / darija keywords) or `Anthropic` + `ApiKey` |
| `Storage` | Upload folder, public URL, max size |

Secrets in production: environment variables (`Jwt__SigningKey`, `ConnectionStrings__TuniPlanDb`, `AI__ApiKey`…) or a vault.

### What is still simulated

- **SMS / WhatsApp / push**: written to the log (`ConsoleSmsSender`…). Plug a Tunisian SMS gateway via `Notifications:SmsProvider=Http`
  (adapt `HttpSmsSender` payload), WhatsApp Business API in `IWhatsAppSender`, Expo push in `IPushSender`.
- **Payments**: `MockPaymentGateway`. Implement `IPaymentGateway` for Konnect / Flouci / D17 and register it in `BL/DependencyInjection.cs`.
- **AI**: rule-based until you set `AI:Provider=Anthropic` and an API key.

### Production checklist

- [ ] Strong `Jwt:SigningKey` from a secret store, never in git
- [ ] `Security:ExposeDevCodes=false`, `EnableMockPayments=false`, `Database:SeedDemoData=false`
- [ ] Real SMS provider, HTTPS certificate, `Cors:AllowedOrigins` = your domains only
- [ ] Persist Data Protection keys to a shared store if you run several servers
- [ ] Put uploads on blob storage (implement `IFileStorage`) and a CDN
- [ ] Backups of the SQL database, log retention
