# SenseFace 2A — Biometric Enrollment Integration

This HRIS supports **face** and **fingerprint** registration for employee time in/out on **SenseFace 2A** (ZKTeco PUSH / iclock protocol).

You can run **without the physical device** today and plug in the vendor SDK later.

---

## What is implemented

| Layer | Feature |
|-------|---------|
| **HRIS UI** | Start face/fingerprint enrollment from Employee Profile → Biometric Enrollment |
| **Central API** | Enrollment sessions, template storage, sync endpoints |
| **Site Gateway** | iclock PUSH: receive templates from device, queue ENROLL commands |
| **Adapter** | `ISenseFaceDeviceAdapter` — swap Simulated / Gateway / SDK modes |

---

## Enrollment flow (production — with device)

```
HR Admin → Start Enrollment (employee + device)
    ↓
Central API creates BiometricEnrollment (Pending)
    ↓
Site Gateway pulls pending enrollments → queues iclock command on device
    ↓
Employee scans face/finger at SenseFace 2A
    ↓
Device pushes BIODATA / FINGERTMP to gateway (/iclock/cdata)
    ↓
Gateway uploads template to central (/api/sync/biometric-templates)
    ↓
HRIS marks enrollment Completed; template syncs to all site devices
```

---

## Configuration (`appsettings.json`)

```json
"Biometric": {
  "Provider": "Simulated",
  "EnrollmentTimeoutMinutes": 15,
  "SimulatedDelaySeconds": 3
}
```

| Provider | When to use |
|----------|-------------|
| **`Simulated`** | No hardware yet — creates mock templates after a few seconds (dev/demo) |
| **`Gateway`** | Real SenseFace via site gateway iclock PUSH (recommended when device arrives) |
| **`SenseFaceSdk`** | Direct TCP/SDK to device IP — implement in `SenseFaceDeviceAdapter.StartDirectEnrollmentAsync` |

### Switching when you buy the device

1. Register the device under **System → Biometric Devices** (serial number must match the unit).
2. Point the SenseFace **ADMS / Cloud Server** to your site gateway: `http://<gateway-ip>:8090/iclock/cdata`.
3. Set `"Provider": "Gateway"` in central API `appsettings.json`.
4. Restart API and site gateway.
5. Open an employee profile → **Start Enrollment** → employee scans at the device.

---

## Plugging in the SenseFace 2A SDK (optional)

File: `backend/src/Hris.Api/Services/Biometric/SenseFaceDeviceAdapter.cs`

Implement `StartDirectEnrollmentAsync`:

1. Connect to `device.IpAddress` / `device.Port` (default 4370).
2. Set user info (PIN = `BiometricUserId`, usually employee code).
3. Call vendor API to start face or fingerprint capture.
4. Read template bytes → `Convert.ToBase64String(...)`.
5. Return `SenseFaceEnrollmentResult(true, templateBase64, null)`.

Set `"Provider": "SenseFaceSdk"` to prefer direct SDK over gateway-only commands.

Typical ZKTeco SDK entry points (names vary by SDK package):

- `Connect_Net`, `SSR_SetUserInfo`, `StartEnrollEx`, `GetUserFaceStr`, `GetUserTmpExStr`

---

## API endpoints (HR / admin JWT)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/biometric/devices` | List SenseFace devices |
| GET | `/api/biometric/enrollments?employeeId=` | Enrollment history |
| GET | `/api/biometric/enrollments/{id}` | Poll session status |
| POST | `/api/biometric/enrollments` | Start enrollment `{ employeeId, deviceId, type, fingerIndex }` |
| POST | `/api/biometric/enrollments/{id}/cancel` | Cancel session |
| DELETE | `/api/biometric/templates/{id}` | Remove stored template |

**Type:** `1` = Face, `2` = Fingerprint. **Finger index:** 0–9 (left thumb = 0, right thumb = 5).

---

## Gateway sync endpoints (site API key)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/sync/enrollments/pending` | Commands to push to devices |
| POST | `/api/sync/enrollments/dispatched` | Mark commands queued |
| POST | `/api/sync/biometric-templates` | Upload captured template from device |

---

## Testing without hardware

1. Ensure `"Provider": "Simulated"` in `appsettings.json`.
2. Log in as HR (`admin` / `Admin@123`).
3. Open **Employees → Juan Dela Cruz** (or any employee).
4. Scroll to **Biometric Enrollment** → select a device → **Start Enrollment**.
5. Status becomes **Completed** within ~3 seconds; template appears in the list.

---

## Fingerprint index reference

| Index | Finger |
|-------|--------|
| 0–4 | Left thumb → pinky |
| 5–9 | Right thumb → pinky |

---

## Related files

- `Hris.Domain/Entities/Employee.cs` — `BiometricTemplate`, `BiometricEnrollment`
- `Hris.Api/Services/Biometric/` — adapter + enrollment service
- `Hris.Api/Controllers/BiometricController.cs` — HR API
- `Hris.SiteGateway/IclockEndpoints.cs` — device PUSH protocol
- `Hris.SiteGateway/SyncWorker.cs` — enrollment dispatch + template upload
