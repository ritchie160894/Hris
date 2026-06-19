# Offline Attendance Synchronization

Enterprise HRIS is designed for **unstable or intermittent internet** at branch sites. Attendance is never lost when the central server is unreachable.

---

## Development environment & dependencies

Offline mode spans **three components**. For local development you typically run all of them on one Windows PC (or central + gateway on one machine, device optional).

```
┌─────────────────────────────────────────────────────────────────┐
│  YOUR DEV MACHINE (or split across LAN VMs)                     │
│                                                                 │
│  Terminal 1: Hris.Api          → http://localhost:5000        │
│  Terminal 2: Angular frontend    → http://localhost:4200        │
│  Terminal 3: Hris.SiteGateway  → http://0.0.0.0:8090          │
│                                                                 │
│  Optional: SenseFace 2A on LAN   → pushes to gateway :8090      │
└─────────────────────────────────────────────────────────────────┘
```

### Required software

Install these before working on offline sync:

| Software | Version | Role |
|----------|---------|------|
| **Git** | Latest | Clone the repository |
| **.NET SDK** | **10.0** | Build `Hris.Api`, `Hris.SiteGateway`, `Hris.Domain` |
| **Node.js** | **20 LTS** or **22 LTS** | Run the Angular HRIS UI (Sync Monitor, Sites, Devices) |
| **npm** | Bundled with Node.js | Frontend dependencies (`npm install` in `frontend/`) |
| **SQL Server** | Express, Developer, or Standard | **Central** database (`HrisCentral`) — required for `Hris.Api` |
| **Web browser** | Chrome or Edge | HRIS admin UI, gateway status page |

> **Site Gateway local DB:** defaults to **SQLite** (`sitegateway.db` next to the gateway process) — **no extra install** for dev. For production-like testing, switch to **SQL Server Express** locally (see [Site gateway configuration](#site-gateway-configuration)).

### Recommended (optional)

| Software | Purpose |
|----------|---------|
| **Visual Studio** or **VS Code** + **C# Dev Kit** | Debug API and Site Gateway |
| **SSMS** or **Azure Data Studio** | Inspect `HrisCentral` and local gateway DB |
| **SenseFace 2A** (or ZKTeco PUSH device) | Real hardware testing on the LAN |
| **Windows Firewall rule** for **TCP 8090** | Allow device → gateway punches when gateway runs on another PC |

### NuGet dependencies (restored automatically)

Run `dotnet restore` from the repo (or `dotnet run`, which restores for you). Key packages:

**`Hris.SiteGateway`**

| Package | Purpose |
|---------|---------|
| `Microsoft.EntityFrameworkCore.Sqlite` | Default local queue DB (offline dev, zero config) |
| `Microsoft.EntityFrameworkCore.SqlServer` | Optional durable local DB (`LocalDb:Provider = SqlServer`) |
| `Microsoft.Extensions.Hosting.WindowsServices` | Run gateway as a Windows service in production |

**`Hris.Api`** (central sync ingest)

| Package | Purpose |
|---------|---------|
| `Microsoft.EntityFrameworkCore.SqlServer` | Central attendance + sync conflict storage |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | HRIS user auth (separate from site `X-Site-Key`) |

Shared entity definitions live in **`Hris.Domain`** (referenced by both projects).

### Network ports (development)

| Service | Port | URL / endpoint |
|---------|------|----------------|
| Central API | **5000** | `http://localhost:5000` |
| Angular SPA | **4200** | `http://localhost:4200` |
| Site Gateway | **8090** | `http://localhost:8090/status`, `/iclock/cdata` |
| SenseFace ADMS | **8090** (to gateway) | Device → gateway IP, not central server |

The gateway talks to central via **`Central:BaseUrl`** in `appsettings.json` (default dev: `http://localhost:5000/`). Site auth uses header **`X-Site-Key`**, not JWT.

### Quick start — offline sync on your machine

**1. Central stack** (if not already running):

```powershell
# Terminal 1 — from repo root
dotnet run --project backend/src/Hris.Api
# → http://localhost:5000 (creates HrisCentral + seed data)

# Terminal 2
cd frontend
npm install          # first time only
npx ng serve --port 4200
# → http://localhost:4200
```

**2. Site Gateway API key**

1. Log in to HRIS → **Organization → Sites**.
2. Copy the site's **Gateway API Key** (dev seed for Head Office: `b79469bc815040ea9d6fc21eb68c91f3`).
3. Paste into `backend/src/Hris.SiteGateway/appsettings.json`:

```json
"Central": {
  "BaseUrl": "http://localhost:5000/",
  "SiteApiKey": "<your-site-gateway-api-key>"
}
```

**3. Start the gateway**

```powershell
# Terminal 3 — from repo root
dotnet run --project backend/src/Hris.SiteGateway
# → http://0.0.0.0:8090
```

**4. Verify**

| Check | URL / action |
|-------|----------------|
| Gateway alive | http://localhost:8090/status |
| Central sees site | HRIS → **Dashboard** or **System → Sync Monitor** |
| Pending queue | Stop `Hris.Api`, post a test punch (below), restart API — records should sync |

### Test offline mode without a device

You do **not** need SenseFace hardware to develop the queue and sync path. Simulate a device punch with PowerShell:

```powershell
# Employee PIN must match Biometric User ID in HRIS (default = employee code, e.g. EMP-0001)
$body = "EMP-0001`t$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')t0t15"
Invoke-WebRequest -Uri "http://localhost:8090/iclock/cdata?SN=DEV001&table=ATTLOG" `
  -Method POST -Body $body -ContentType "text/plain"
```

Then open http://localhost:8090/status — `pendingSync` should increase. Start (or restart) `Hris.Api` and wait for the sync interval (default **10 seconds**); pending count should drop and attendance appears in **Attendance → Raw Logs**.

**Simulate central outage:** stop `Hris.Api` while the gateway keeps running. Post more punches — they accumulate locally (`pendingSync` rises). Start the API again — sync resumes with backoff, no duplicate rows on central (GUID + natural-key dedupe).

### Biometric enrollment without hardware

Central API default (`backend/src/Hris.Api/appsettings.json`):

```json
"Biometric": {
  "Provider": "Simulated",
  "SimulatedDelaySeconds": 3
}
```

Use **Simulated** for UI and payroll testing without a device. When testing real iclock PUSH enrollment and template upload, set `"Provider": "Gateway"` and ensure the site gateway is running. See [`senseface-enrollment.md`](senseface-enrollment.md).

### What you do **not** need for offline dev

| Not required | Why |
|--------------|-----|
| Docker | Services run natively on Windows |
| Separate SQLite installer | EF Core SQLite provider creates `sitegateway.db` automatically |
| SenseFace / ZKTeco device | Simulate punches via HTTP POST to `/iclock/cdata` |
| IIS | `dotnet run` or Windows Service in production |
| VPN to central | Dev: gateway and API on same machine via `localhost:5000` |

### Production vs development

| Setting | Development | Production (each branch) |
|---------|-------------|---------------------------|
| `Central:BaseUrl` | `http://localhost:5000/` | `https://hris.yourcompany.com/` |
| `LocalDb:Provider` | `Sqlite` (default) | `SqlServer` recommended |
| Gateway host | Dev PC | Dedicated Windows PC on site LAN |
| Device | Simulated POST or SenseFace | SenseFace 2A → gateway IP:8090 |
| Install | `dotnet run` | `dotnet publish` + Windows Service (`sc create …`) |

See also: [README — Quick Start § Site Gateway](../../README.md), [senseface-enrollment.md](senseface-enrollment.md).

---

## How it works

```
SenseFace 2A  ──LAN──►  Site Gateway (local DB)  ──internet (when up)──►  Central HRIS API  ──►  SQL Server
     │                         │                                              │
     │  Real-time punches      │  SQLite / MSSQL Express queue                │  Idempotent ingest
     │  (no internet needed)   │  Survives reboots & outages                  │  Dedupe by GUID + natural key
```

### Layer 1 — Device → Site (always local)

- SenseFace pushes punches to the **site gateway** on the LAN (`http://<gateway-ip>:8090/iclock/cdata`).
- No central internet required.
- Duplicate punches on the same device are rejected locally.

### Layer 2 — Site → Central (when internet is available)

- `SyncWorker` runs every **10 seconds** (configurable).
- Each sync step is **isolated** — if heartbeat fails, attendance push still runs when central is reachable.
- Unsynced records stay in the local DB until accepted by central.
- **Exponential backoff** on transient failures (30s → 60s → 120s … up to 1 hour).
- Records that fail permanently (e.g. unknown employee PIN) are flagged and stop retrying.

### Layer 3 — Central ingest (idempotent)

- Accepts batches of up to **500** records per request.
- **SyncGuid** — duplicate GUIDs are skipped (safe to re-send after partial failure).
- **Natural key** — same employee + punch time + type is treated as duplicate.
- Unknown biometric IDs create a **sync conflict** for HR to resolve (record stays at site until fixed).

---

## Site gateway configuration

`backend/src/Hris.SiteGateway/appsettings.json`:

```json
{
  "Central": {
    "BaseUrl": "https://hris.yourcompany.com/",
    "SiteApiKey": "<from Organization → Site → Gateway API Key>"
  },
  "Sync": {
    "IntervalSeconds": 10,
    "PushBatchSize": 500,
    "MaxRetryAttempts": 50,
    "BackoffBaseSeconds": 30
  },
  "LocalDb": {
    "Provider": "Sqlite"
  }
}
```

For production sites, set `"Provider": "SqlServer"` and use a local SQL Express connection string for durability.

---

## Monitoring

| Where | What to check |
|-------|----------------|
| **Gateway** `http://<gateway>:8090/status` | `pendingSync`, `centralOnline`, `oldestPendingRecord`, `permanentFailures` |
| **HRIS** Dashboard → Sites & Sync Status | Online/offline, pending queue count |
| **HRIS** System → Sync Monitor | All sites, batches, conflicts |

Sites show **Offline** when no heartbeat for 10+ minutes, but punches still accumulate locally.

---

## Multi-site deployment

Each branch runs its **own** `Hris.SiteGateway` with a **unique** `SiteApiKey` matching the site record in central HRIS.

Example seeded keys (development only):

| Site | Gateway API Key |
|------|-----------------|
| Head Office | `b79469bc815040ea9d6fc21eb68c91f3` |
| Cebu Plant | `ceb9site0gateway00000000000000001` |

Regenerate keys in production via **Organization → Sites → Regenerate Gateway Key**.

---

## Failure scenarios

| Scenario | Behavior |
|----------|----------|
| Internet down for hours | Punches queue locally; auto-sync when internet returns |
| Central server maintenance | Gateway retries with backoff; no data loss |
| Partial batch failure | Accepted records marked synced; failed ones retry |
| Unknown employee on device | Permanent failure at site + conflict in central Sync Monitor |
| Gateway PC reboot | Local DB persists (SQLite file or MSSQL); sync resumes on startup |
| Duplicate re-send after reconnect | Central deduplicates — safe |

---

## Related code

- `Hris.SiteGateway/IclockEndpoints.cs` — receive device punches
- `Hris.SiteGateway/SyncWorker.cs` — push/pull with retry
- `Hris.SiteGateway/LocalDb.cs` — local queue schema
- `Hris.Api/Controllers/SyncController.cs` — central ingest API
