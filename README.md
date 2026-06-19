# Enterprise HRIS — Multi-Site Online/Offline Attendance, Payroll & Executive Approvals

An enterprise-grade Human Resource Information System built with **Angular 20**, **ASP.NET Core (C#/.NET 10)**, **Microsoft SQL Server**, and **SenseFace 2A** biometric devices.

The system supports multiple branches/sites, keeps collecting attendance **even with no internet connectivity**, synchronizes automatically when connectivity returns, processes payroll centrally (Philippine statutory: SSS, PhilHealth, Pag-IBIG, withholding tax), and provides a mobile-friendly **Executive Approval Portal** for Leave / SIL / Overtime / Cash Advance / Loan workflows.

---

## Repository Layout

```
hris/
├── backend/
│   ├── Hris.slnx                      # .NET solution
│   └── src/
│       ├── Hris.Domain/               # Entities + enums for all modules
│       ├── Hris.Api/                  # Central API (JWT auth, payroll engine,
│       │                              #   approvals, notifications, reports, sync API)
│       └── Hris.SiteGateway/          # Branch/site service: SenseFace 2A push listener,
│                                      #   local DB (offline queue), auto-sync worker
└── frontend/                          # Angular 20 SPA (HRIS + ESS + Executive Portal)
```

## Architecture

```
                          CENTRAL SERVER
   ┌────────────────────────────────────────────────────┐
   │  Angular 20 SPA  ⇄  ASP.NET Core API  ⇄  SQL Server│
   │     (HRIS / ESS / Executive Approval Portal)       │
   └────────────────────────▲───────────────────────────┘
                            │ HTTPS (X-Site-Key auth)
            heartbeat / attendance push / employee+template pull
                            │
        ┌───────────────────┴────────────────────┐
        │            BRANCH / SITE                │
        │  Hris.SiteGateway (Windows service)     │
        │   • iclock push listener  :8090         │
        │   • Local DB (SQLite or MSSQL Express)  │
        │   • Sync worker (retry + dedupe)        │
        │            ▲                            │
        │            │ ZKTeco PUSH protocol       │
        │   SenseFace 2A device(s)                │
        └─────────────────────────────────────────┘
```

**Offline behavior:** devices push punches to the local gateway, which stores them in the local database. When the central server is unreachable, records queue locally (nothing is lost). The sync worker retries every cycle with **exponential backoff**; each sync step (heartbeat, attendance push, employee pull) runs **independently** so a failed heartbeat does not block upload. The central API deduplicates by sync GUID *and* by natural key (employee + time + punch type), so re-sends are safe.

See `[backend/docs/offline-sync.md](backend/docs/offline-sync.md)` for the full offline architecture, monitoring, and multi-site setup.

---

## Development Environment

Everything you need to run and develop this HRIS on a **Windows** machine (the stack also works on macOS/Linux for API + frontend, but **Site Gateway** and SenseFace integration are Windows-oriented).

### Required software


| Software        | Version                         | Purpose                                                     | Download                                                                              |
| --------------- | ------------------------------- | ----------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| **Git**         | Latest                          | Clone and version-control the repo                          | [git-scm.com](https://git-scm.com/download/win)                                       |
| **.NET SDK**    | **10.0**                        | Build and run `Hris.Api`, `Hris.Domain`, `Hris.SiteGateway` | [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download)                |
| **Node.js**     | **20 LTS** or **22 LTS**        | Run the Angular frontend (`npm`, `ng`)                      | [nodejs.org](https://nodejs.org/)                                                     |
| **npm**         | Included with Node.js           | Install frontend dependencies                               | (bundled with Node.js)                                                                |
| **SQL Server**  | Express, Developer, or Standard | Central database (`HrisCentral`)                            | [SQL Server Express](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) |
| **Web browser** | Chrome or Edge (current)        | Test the SPA and Executive Portal                           | —                                                                                     |


> **Note:** The API uses **Entity Framework `EnsureCreated` + schema bootstrap** (not EF migrations). On first run it creates the `HrisCentral` database and seeds demo users if the server is reachable.

### Recommended (optional)


| Software                                                         | Purpose                                                                 |
| ---------------------------------------------------------------- | ----------------------------------------------------------------------- |
| **Visual Studio 2022/2026** or **VS Code**                       | IDE — C# debugging for the API; VS Code works well with the Angular app |
| **C# Dev Kit** (VS Code extension)                               | IntelliSense, debugging for `.NET` projects                             |
| **Angular Language Service** (VS Code extension)                 | Template/type support for `.ts` / `.html`                               |
| **SQL Server Management Studio (SSMS)** or **Azure Data Studio** | Inspect tables, run queries, verify seed data                           |
| **GitHub CLI (`gh`)**                                            | Create pull requests and inspect CI from the terminal                   |
| **PowerShell 7+**                                                | Same commands as in this README (Windows PowerShell 5.1 also works)     |


### Optional — branch / biometric sync only

Install these **only if** you are developing or deploying **offline attendance sync** at a site (not needed for core HRIS + payroll development):


| Software                                            | Purpose                                                              |
| --------------------------------------------------- | -------------------------------------------------------------------- |
| **Hris.SiteGateway** (this repo, .NET 10)           | Local push listener for SenseFace 2A devices; queues punches offline |
| **Windows service host**                            | Site Gateway is designed to run as a Windows service at each branch  |
| **SenseFace 2A** (or compatible ZKTeco PUSH device) | Biometric timekeeping hardware on the site LAN                       |


> **Offline dev guide:** required tools, NuGet packages, ports, simulated punches, and testing without hardware → `[backend/docs/offline-sync.md](backend/docs/offline-sync.md#development-environment--dependencies)`.

### Verify your installation

Run these in a terminal after installing the required software:

```powershell
git --version
dotnet --version          # expect 10.0.x
node --version            # expect v20.x or v22.x
npm --version
```

Confirm SQL Server is running and the connection string in `backend/src/Hris.Api/appsettings.json` matches your instance (default: `localhost\SQLEXPRESS`).

```powershell
# Start the API — creates DB + seed data on first successful connection
dotnet run --project backend/src/Hris.Api
```

### What you do **not** need for day-to-day development

- **Docker** — not required; SQL Server and services run natively on Windows.
- **Angular CLI global install** — use `npx ng` from the `frontend` folder (or `npm start`).
- **Separate EF migration tools** — schema is applied automatically at API startup.
- **Site Gateway** — skip unless you are working on device sync / offline attendance.

## Quick Start (Development)

Run the **backend API** and **frontend** in separate terminals. Both must be running for the app to work.

### 1. Backend (ASP.NET Core API)

```powershell
# From repo root — edit backend/src/Hris.Api/appsettings.json if your SQL Server instance differs
# (default: Server=localhost\SQLEXPRESS, database HrisCentral is auto-created and seeded)
dotnet run --project backend/src/Hris.Api
```

- **URL:** [http://localhost:5000](http://localhost:5000)  
- **OpenAPI (dev):** [http://localhost:5000/openapi/v1.json](http://localhost:5000/openapi/v1.json)

### 2. Frontend (Angular SPA)

```powershell
cd frontend
npm install          # first time only
npx ng serve --port 4200
```

- **URL:** [http://localhost:4200](http://localhost:4200)  
- The SPA calls the API at `http://localhost:5000` (see `frontend/src/environments/environment.ts`).

> **Tip:** If port 4200 is already in use, stop the other process or use `npx ng serve --port 4201` and open that port instead.

### Restarting Backend & Frontend

After code changes, or if you see **"address already in use"** / **SocketException (10048)**, stop the old process first, then start again. Only **one** instance of each service should run per port.

**Backend API (port 5000)**

```powershell
# Stop any running API instance
Get-Process Hris.Api -ErrorAction SilentlyContinue | Stop-Process -Force

# Start again (from repo root)
dotnet run --project backend/src/Hris.Api
```

If the process name differs, find what is using port 5000:

```powershell
Get-NetTCPConnection -LocalPort 5000 | Select-Object OwningProcess
Get-Process -Id <OwningProcess>
Stop-Process -Id <OwningProcess> -Force
```

**Frontend (port 4200)**

In the terminal where `ng serve` is running, press **Ctrl+C** to stop it, then:

```powershell
cd frontend
npx ng serve --port 4200
```

If port 4200 is still held by a stuck Node process:

```powershell
Get-NetTCPConnection -LocalPort 4200 | Select-Object OwningProcess
Stop-Process -Id <OwningProcess> -Force
cd frontend
npx ng serve --port 4200
```

> **Common mistake:** Running `dotnet run` or `ng serve` in a **second** terminal while the first instance is still running. The new process fails because the port is already taken — the original service is usually still healthy; restart by stopping the old one first.

### 3. Site Gateway (one per branch/site)

```powershell
# 1) In the web app: Organization → Sites → copy the site's Gateway API Key
# 2) Paste it in backend/src/Hris.SiteGateway/appsettings.json → Central:SiteApiKey
#    and set Central:BaseUrl to the central API URL
dotnet run --project backend/src/Hris.SiteGateway
# → listens on http://0.0.0.0:8090 (status page at /status)
```

### 4. SenseFace 2A device setup

On each device: **Comm. Settings → Cloud Server Setting (ADMS)**

- Server Address: *IP of the site gateway machine*
- Server Port: `8090`
- Enable Domain Name: OFF, HTTPS: OFF (or per your network)

The device then **pushes attendance in real time** to the gateway (ZKTeco PUSH/iclock protocol — `/iclock/cdata`). The gateway also queues user/face/fingerprint template updates back to devices via `/iclock/getrequest`.

> Employee ↔ device mapping uses **Biometric User ID** (defaults to the employee code).

### Biometric enrollment (face + fingerprint)

HR can register employees from **Employees → Profile → Biometric Enrollment (SenseFace 2A)**:

1. Select device, type (Face / Fingerprint), and finger index (0–9).
2. Click **Start Enrollment** — the site gateway sends an iclock ENROLL command to the device.
3. Employee scans at the SenseFace unit; the template syncs back to HRIS automatically.

**Without hardware yet:** set `"Biometric": { "Provider": "Simulated" }` in `backend/src/Hris.Api/appsettings.json` (default). Enrollment completes in ~3 seconds with a mock template for testing.

**When you purchase SenseFace 2A:** set `"Provider": "Gateway"`. Optional direct SDK: implement `SenseFaceDeviceAdapter.StartDirectEnrollmentAsync` and set `"Provider": "SenseFaceSdk"`.

Full guide: `[backend/docs/senseface-enrollment.md](backend/docs/senseface-enrollment.md)`

---

## Seeded Login Accounts (change passwords in production)


| Username  | Password   | Role                            |
| --------- | ---------- | ------------------------------- |
| admin     | Admin@123  | Super Administrator             |
| hradmin   | Hr@12345   | HR Administrator                |
| hrofficer | Hr@12345   | HR Officer (level-2 approver)   |
| payroll   | Pay@12345  | Payroll Officer                 |
| ithead    | Head@12345 | Department Head (IT)            |
| opshead   | Head@12345 | Department Head (Operations)    |
| vp        | Vp@12345   | Vice President & HR Head (exec) |
| ceo       | Ceo@12345  | President & CEO (exec, final)   |
| juan      | Emp@12345  | Employee (ESS)                  |
| maria     | Emp@12345  | Employee (ESS)                  |


## Approval Workflows (seeded, editable in `WorkflowTemplateSteps`)


| Request               | Chain                                                       |
| --------------------- | ----------------------------------------------------------- |
| Leave                 | Dept Head → HR Officer → VP & HR Head → President & CEO     |
| SIL                   | VP & HR Head → President & CEO                              |
| Overtime              | Dept Head → HR Officer → VP & HR Head                       |
| Cash Advance / Loan   | Dept Head → HR Officer → VP & HR Head                       |
| Attendance Correction | Dept Head → HR Officer                                      |
| Payroll               | Payroll Officer processes → VP & HR Head approves → release |


Approvers can **Approve / Reject / Return for Revision** with remarks; every action is audit-logged and triggers in-app notifications to the requester and the next approver. Department Heads only see their own department's requests; the CEO only receives requests that reach the final level.

**Executive portal (VP & HR Head, President & CEO)**: as company owners, executives get an approval-only portal — just Approvals, Payroll Review, and Announcements. They are **exempt from biometric timekeeping and payroll computation** (no attendance logs, daily monitoring rows, or payslips). The VP additionally approves processed payroll cutoffs directly inside the approval portal.

## Department Head — Who Belongs to My Department?

A **Department Head** only sees and approves employees whose **Department** on their employee profile matches the department head’s own **Department** on their linked employee profile.

### How the system decides “my department”


| Rule                      | Detail                                                                         |
| ------------------------- | ------------------------------------------------------------------------------ |
| **Match field**           | `Employee.DepartmentId` on each staff member                                   |
| **Compared to**           | The logged-in Department Head user’s linked **Employee → Department**          |
| **Same department?**      | Yes → employee appears in lists, attendance, approvals, leave/OT for that head |
| **Different department?** | Hidden from that Department Head’s views                                       |


The **Department Head** name shown under **Organization → Departments** is the official assignment (`HeadEmployeeId`). For day-to-day filtering, the head’s **user account must be linked to an employee in that same department**.

### Setup (HR Administrator)

Do these in order when onboarding a new Department Head:

1. **Assign each employee to a department**
  **Navigate:** `Employees` → open employee → **Department** field → save.  
   Every staff member you want under a head must have the correct department here.
2. **Create or update the head’s employee record**
  The head must also be an employee with **Department** set to the department they manage (e.g. IT Head → Information Technology).
3. **Create the login with Department Head role**
  **Navigate:** `Users & Roles` → **＋ New User** (or edit) → **Role:** `Department Head` → under **Link to Employee**, pick the head’s employee record (filter by **Department** if needed) → save.  
   The user’s **Department** column in the list comes from the linked employee profile.
4. **Confirm official head on the org chart (optional but recommended)**
  **Navigate:** `Organization` → **Departments** tab → **Department Head** column shows who is assigned.  
   HR Admin can set `Head Employee` when editing a department (API field `HeadEmployeeId`; should be the same person as step 3).

### What a Department Head sees (navigation)

After logging in (e.g. `ithead` / `Head@12345` for IT, `opshead` for Operations):


| Module               | Path                               | Scope                                                                                                                                  |
| -------------------- | ---------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| **Approvals**        | Sidebar → **Approvals**            | Pending leave, OT, loans, cash advance, attendance correction from **own department only**                                             |
| **Employees**        | Sidebar → **Employees**            | List filtered to **own department** (search/status still work within that dept)                                                        |
| **Attendance**       | Sidebar → **Attendance**           | Logs and daily monitoring for **own department only**                                                                                  |
| **Leave / Overtime** | Sidebar → **Leave** / **Overtime** | Request lists for **own department only**                                                                                              |
| **Reports**          | Sidebar → **Reports**              | Attendance report auto-limited to own department; also Leave, Overtime, and Employee Masterlist (not Payroll or Government Remittance) |


Department Heads **cannot** access: Organization setup, Payroll, Government tables, Users & Roles, Devices, Sync Monitor, Audit Trail.

### Seeded examples


| Login     | Employee     | Department             | Sees                                   |
| --------- | ------------ | ---------------------- | -------------------------------------- |
| `ithead`  | Marco Santos | Information Technology | IT staff only (e.g. Juan, Maria)       |
| `opshead` | Pedro Garcia | Operations (Cebu)      | Operations staff only (e.g. Ana, Jose) |


### Quick check

1. Log in as the Department Head.
2. Open **Employees** — count and names should match only that department.
3. Open **Approvals** — requests should be from the same department.
4. If someone is missing, HR Admin should verify their **Employees → Department** field matches the head’s department.

## Users & Roles

**Navigate:** Sidebar → **Users & Roles** (Super Administrator and HR Administrator only)

Manages login accounts, role assignment, and linking users to employee profiles.

### List & filters


| Feature               | Detail                                                                                                                                     |
| --------------------- | ------------------------------------------------------------------------------------------------------------------------------------------ |
| **Pagination**        | 25 accounts per page (Previous / Next) — avoids slow loading with large user lists                                                         |
| **Search**            | Username, display name, or email                                                                                                           |
| **Role filter**       | Limit to one of the 9 system roles                                                                                                         |
| **Department filter** | Shows users whose **linked employee** belongs to the selected department (includes departments added under **Organization → Departments**) |


### Table columns

Username · Display Name · Role · **Department** (from linked employee) · Linked Employee · Last Login · Status · Edit / Unlock

Users without a linked employee (e.g. `admin`) show **—** for Department and Linked Employee.

### Create / edit user


| Field                              | Notes                                                                                                     |
| ---------------------------------- | --------------------------------------------------------------------------------------------------------- |
| **Username / Display Name / Role** | Required for new accounts                                                                                 |
| **Department (filter employees)**  | Narrows the employee dropdown — lists **all** organization departments, including newly created ones      |
| **Link to Employee**               | Optional dropdown: `EMP-0004 · Marco Santos (Information Technology)` — replaces manual Employee ID entry |
| **Email / Password**               | Email optional; password required on create, optional on edit                                             |


**How department appears on a user:** Department is **not** stored on the user record directly. It is read from the linked **Employee → Department**. To assign a user to a new department:

1. **Organization → Departments** — create the department (if not already there)
2. **Employees** — set the employee’s **Department** field
3. **Users & Roles** — link the user to that employee

### API

- `GET /api/users?page&pageSize&search&role&departmentId` → `{ total, page, pageSize, items }`  
- `GET /api/users/employee-options?departmentId&forUserId` → active employees available to link (excludes employees already linked to another user)

## Employee Management (HR)

**Navigate:** Sidebar → **Employees** (HR roles; Department Heads see own department only)

### Create employee

**Navigate:** **Employees** → **＋ New Employee**


| Field                                                           | Notes                                                          |
| --------------------------------------------------------------- | -------------------------------------------------------------- |
| Employee Code, First Name, Last Name, Hire Date, Monthly Salary | Required                                                       |
| Department, Position, Branch, Site                              | Optional — use departments from **Organization → Departments** |
| Government IDs                                                  | SSS, PhilHealth, Pag-IBIG, TIN (optional)                      |


New hires are saved as **Probationary** with **10 days Emergency Leave** for the hire year. SIL (5 days) is granted when HR changes status to **Regular** on the employee profile.

> **Tip:** If create fails with a generic error, ensure the form is not sending invalid enum values — the API expects standard JSON; status is set automatically to Probationary on create.

### Remove employee (separation)

For staff who are **no longer with the company**, HR can remove them without deleting payroll or attendance history.

**Navigate:** **Employees** → **Remove** (HR Administrator & HR Officer)


| Action                              | Effect                                                              |
| ----------------------------------- | ------------------------------------------------------------------- |
| Mark **Resigned** or **Terminated** | Sets separation status and date                                     |
| Deactivate login                    | Any user account linked to that employee is disabled                |
| Clear department head               | If they were assigned as department head, the assignment is cleared |
| History                             | Separation is recorded in employee history                          |


The default employee list shows **active employees only**. Check **Include separated** or filter by status **Resigned** / **Terminated** to view former staff.

**API:** `POST /api/employees/{id}/separate` with `{ status, separationDate, reason? }`  
**List filter:** `GET /api/employees?activeOnly=true` (default in UI when no status filter is selected)

## Module Coverage

Dashboard · Employee Management (profiles, photos, contacts, documents, history, biometric templates, **create**, **soft separation/remove**) · Organization (company/branches/sites/departments/positions/holidays/hierarchy) · Attendance (logs, daily monitoring, manual entry, corrections with workflow) · Leave (VL/SL/SIL/EL/LWOP, credits, calendar) · Overtime (computation at 125%) · Payroll (cutoffs, processing, approval, release, payslips, allowances/deductions) · Loans & Cash Advances (workflow + auto payroll deduction + repayment schedule) · Government (SSS/PhilHealth/Pag-IBIG/tax tables + remittance report) · Benefits (HMO etc.) · Recruitment (postings, applicants, interviews) · Performance (KPI reviews) · Training (participants, certifications, expiry alerts) · Document Management (uploads + expiry monitoring) · Announcements · ESS Portal · Executive Approval Portal · Notifications · **Users & Roles** (9 roles, pagination, department filter, employee link picker) · Audit Trail · Reports (view / CSV / print-to-PDF, **paginated**) · Device Management · Sync Monitor (site status, batches, conflicts with resolution UI)

## Payroll Computation Notes (per HR policy)

- **Rates**: Basic Salary ÷ 24 days = daily rate; daily rate ÷ 8 hours = hourly rate (e.g. ₱25,000 → ₱1,041.67/day → ₱130.21/hr). The payroll computation is based on the hourly rate.
- **Semi-monthly base**: half of monthly salary, minus absences (daily rate), tardiness (hourly rate, 5-min grace), and uncovered undertime.
- **Cutoff & deduction schedule**:
  - 1st–15th cutoff → released on the **20th** of the same month; deducts **withholding tax** (full monthly, TRAIN law) and **government loans** (SSS/Pag-IBIG loans).
  - 16th–EOM cutoff → released on the **5th** of the following month; deducts **SSS, PhilHealth, and Pag-IBIG** contributions (full monthly amounts).
  - Company loans and cash advances are deducted automatically every cutoff.
  - Pay dates are entered manually by the payroll officer when creating a cutoff (typical schedule: 1st–15th → 20th, 16th–EOM → 5th of next month).
- **Undertime**: hours ÷ 24 = leave-day equivalent, charged first against the annual leave balance (posted on payroll release); any portion not covered by credits is deducted from pay at the hourly rate.
- **Overtime**: only recognized once it exceeds **30 minutes** beyond regular working hours; paid at 125% of the hourly rate. Regular holidays in the period are paid automatically.
- **Leave policy**: 10 annual leave days per year (reset yearly, not convertible to cash). SIL: 5 days per year, convertible to cash, available only to **Regular** employees.
- Releasing a cutoff posts loan amortizations, charges undertime to leave balances, and notifies every employee that a payslip is available.
- Contribution tables live in the database (`SssBrackets`, `PhilHealthConfigs`, `PagIbigConfigs`, `TaxBrackets`) so they can be updated each year without code changes. See **[Philippine Statutory Deductions (Reference)](#philippine-statutory-deductions-reference)** below for formulas and schedules.

## Philippine Statutory Deductions (Reference)

Payroll statutory amounts follow **Philippine rules** (SSS, PhilHealth, Pag-IBIG, BIR TRAIN withholding tax). All computation is centralized in `GovernmentContributionCalculator` (`backend/src/Hris.Api/Services/GovernmentContributionCalculator.cs`) so payslips and the **Government Remittance** report stay consistent.

Rates are stored in the database and editable from **Government** in the web app (no code deploy needed when agencies publish new tables).

### Deduction schedule (semi-monthly payroll)


| Cutoff period  | Typical pay date   | Deducted from employee                                                  |
| -------------- | ------------------ | ----------------------------------------------------------------------- |
| **1st – 15th** | 20th of same month | **Withholding tax** (full monthly TRAIN amount), **SSS/Pag-IBIG loans** |
| **16th – EOM** | 5th of next month  | **SSS**, **PhilHealth**, **Pag-IBIG** (full monthly amounts)            |


Company loans and cash advances are deducted on **every** cutoff. Pay dates are set manually when creating a cutoff.

> **After rate-table or formula updates:** re-process open payroll cutoffs so existing payslips pick up corrected statutory amounts.

### SSS (Social Security System)


| Item               | Rule                                                                                        |
| ------------------ | ------------------------------------------------------------------------------------------- |
| Basis              | **Monthly Salary Credit (MSC)** from bracket table — not a flat % of raw salary             |
| Employee share     | **5%** of MSC                                                                               |
| Employer share     | **10%** of MSC                                                                              |
| MSC range (seeded) | ₱5,000 – ₱35,000 (2025 schedule; update `SssBrackets` when SSS publishes new tables)        |
| Lookup             | Match employee **monthly basic salary** to compensation range → use that row’s EE/ER shares |
| When deducted      | **2nd cutoff** of the month (16th – EOM)                                                    |


### PhilHealth


| Item               | Rule                                                                        |
| ------------------ | --------------------------------------------------------------------------- |
| Premium rate       | **5%** of monthly basic salary (configurable in `PhilHealthConfigs`)        |
| Contribution basis | Clamped between **₱10,000** (floor) and **₱100,000** (ceiling)              |
| Split              | **50% employee / 50% employer** (default `EmployeeSharePercent = 50`)       |
| Example            | Salary ₱45,000 → basis ₱45,000 → premium ₱2,250 → **₱1,125 EE + ₱1,125 ER** |
| When deducted      | **2nd cutoff** of the month                                                 |


### Pag-IBIG (HDMF)


| Item                     | Rule                                                                |
| ------------------------ | ------------------------------------------------------------------- |
| Employee share           | **1%** if monthly compensation **≤ ₱1,500**; **2%** if **> ₱1,500** |
| Employer share           | **2%** (always)                                                     |
| Contribution basis       | Monthly compensation capped at **₱10,000** (`MaxCompensation`)      |
| Example (₱28,000 salary) | Basis ₱10,000 → EE ₱200 + ER ₱200                                   |
| When deducted            | **2nd cutoff** of the month                                         |


Configurable fields in `PagIbigConfigs`: `EmployeeLowThreshold` (default 1500), `EmployeeLowRatePercent` (default 1), `EmployeeRatePercent` (default 2), `EmployerRatePercent` (default 2), `MaxCompensation` (default 10000).

### Withholding tax (BIR — TRAIN Law, monthly)


| Item                      | Rule                                                                                                               |
| ------------------------- | ------------------------------------------------------------------------------------------------------------------ |
| Taxable compensation      | **Monthly basic salary** minus mandatory **employee** shares of SSS + PhilHealth + Pag-IBIG                        |
| Table                     | BIR **monthly** graduated schedule in `TaxBrackets` (TRAIN, 2023 onwards)                                          |
| “In excess of” thresholds | 15% over **₱20,833** · 20% over **₱33,333** · 25% over **₱66,667** · 30% over **₱166,667** · 35% over **₱666,667** |
| When deducted             | **1st cutoff** of the month (1st – 15th), full monthly tax amount                                                  |


Tax uses the **full monthly** statutory EE deductions for the taxable base even though SSS/PhilHealth/Pag-IBIG are physically withheld on the later cutoff.

### Government Remittance report

**Reports → Government Remittance** (and **Government → Remittance** tab) aggregate finalized payslips per calendar month:


| Column              | Meaning                                                  |
| ------------------- | -------------------------------------------------------- |
| **SSS EE / SSS ER** | Employee and employer SSS shares (ER is typically 2× EE) |
| **PhilHealth**      | Total premium remitted (EE + ER combined)                |
| **Pag-IBIG**        | Total contribution remitted (EE + ER combined)           |
| **Tax**             | Withholding tax withheld for the month                   |


Only payslips from **Approved**, **Released**, or **Closed** cutoffs whose period falls entirely within the selected month are included. Reports are **paginated** (25 rows per page). Report cards shown in the UI are **filtered by role** (e.g. HR Officer sees attendance/leave/OT; Payroll Officer sees payroll and government remittance).

### Database tables (update yearly)


| Table               | Purpose                                        |
| ------------------- | ---------------------------------------------- |
| `SssBrackets`       | MSC ranges with EE/ER shares (`EffectiveYear`) |
| `PhilHealthConfigs` | Premium %, min/max salary, EE share %          |
| `PagIbigConfigs`    | EE/ER rates, low-salary tier, compensation cap |
| `TaxBrackets`       | TRAIN monthly withholding brackets             |


Seed data is loaded on first run (`DbSeeder`). Existing databases get Pag-IBIG tier columns via schema bootstrap (`EmployeeLowRatePercent`, `EmployeeLowThreshold`).

## Performance & Scale Architecture

Built for 900+ employees and millions of attendance punches without slow payroll or UI.


| Solution                      | Status | Implementation                                                                                                                    |
| ----------------------------- | ------ | --------------------------------------------------------------------------------------------------------------------------------- |
| **Daily attendance summary**  | ✅      | `AttendanceDailySummaries` — payroll reads this, not raw logs                                                                     |
| **Database indexing**         | ✅      | `IX_AttendanceLogs_AttendanceDate`, `EmployeeId+Date`, `DeviceId`, `Year+Date`                                                    |
| **Background payroll engine** | ✅      | `PayrollBackgroundService` — API queues, worker computes                                                                          |
| **Server-side pagination**    | ✅      | List APIs use `page`/`pageSize` — Employees, Users & Roles, Reports (attendance, payroll, leave, OT, government remittance), etc. |
| **Dashboard caching**         | ✅      | `IMemoryCache` (60s, configurable via `Performance:DashboardCacheSeconds`)                                                        |
| **Attendance archival**       | ✅      | Logs older than 3 years → `AttendanceLogArchives` (nightly worker)                                                                |
| **Local site collector**      | ✅      | `Hris.SiteGateway` — SenseFace → local DB → sync to central (offline-first)                                                       |
| **Offline sync & retry**      | ✅      | Isolated sync steps, exponential backoff, permanent-failure flag, `/status` endpoint                                              |
| **Face templates on device**  | ✅      | Logs store verify mode only; templates in `BiometricTemplates` / device                                                           |
| **Year partitioning prep**    | ✅      | `AttendanceYear` column + script at `backend/docs/sql/attendance-partitioning.sql`                                                |
| **Separate databases**        | 📋     | Single DB today (simpler ops); split to HRIS/Payroll/Attendance DBs at very large scale — see script comments                     |


Configure in `appsettings.json` → `Performance` section (`AttendanceRetainYears`, `ArchiveBatchSize`, `DashboardCacheSeconds`).

## Production Deployment

1. **Central**: `dotnet publish backend/src/Hris.Api -c Release`, host behind IIS/Kestrel + HTTPS. Set a strong `Jwt:Key`, real connection string and CORS origins. Build the SPA with `npx ng build` and serve `frontend/dist` from any web server (or wwwroot).
2. **Sites**: `dotnet publish backend/src/Hris.SiteGateway -c Release`, install as a Windows service:
  `sc create HrisSiteGateway binPath="C:\hris\gateway\Hris.SiteGateway.exe"` (the app already supports `AddWindowsService`). Switch `LocalDb:Provider` to `SqlServer` to use MSSQL Express locally, or keep the zero-config SQLite default.
3. **Backups**: schedule SQL Server full backups (weekly) + differential/log backups (daily) on the central DB; site databases are disposable caches — re-sync re-populates them.

## Smoke-Tested End to End

- 4-level leave approval (employee → dept head → HR officer → VP → CEO) with balance deduction and notifications ✔
- Payroll: process → VP approval → release with statutory deductions and loan posting ✔
- SenseFace push: device handshake → ATTLOG push → local queue → auto-sync to central → site/device health on dashboard ✔
- Duplicate punch re-send rejected at both gateway and central ✔
- Employee create (Probationary + EL balance) and soft separation (Resigned/Terminated, login deactivated) ✔
- Users & Roles: pagination, department filter, employee link picker synced with Organization departments ✔

