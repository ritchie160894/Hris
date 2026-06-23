import { Component, Input, OnDestroy, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { environment } from '../../environments/environment';

@Component({
  selector: 'app-employee-detail',
  imports: [DatePipe, DecimalPipe, FormsModule, RouterLink],
  styles: [`
    .head { display: flex; gap: 18px; align-items: center; flex-wrap: wrap; }
    .photo { width: 84px; height: 84px; border-radius: 16px; background: var(--primary-soft); display: flex;
      align-items: center; justify-content: center; font-size: 28px; font-weight: 800; color: var(--primary-dark); overflow: hidden;
      img { width: 100%; height: 100%; object-fit: cover; } }
    .kv { display: grid; grid-template-columns: 160px 1fr; gap: 6px 12px; font-size: 13.5px;
      .k { color: var(--text-soft); font-weight: 500; } }
    .bio-actions { display: flex; flex-wrap: wrap; gap: 8px; align-items: flex-end; margin-top: 10px; }
    .bio-actions .field { margin: 0; min-width: 140px; }
    .ded-row { display: grid; grid-template-columns: 24px 1fr auto auto; gap: 8px; align-items: center;
      padding: 8px 0; border-bottom: 1px solid var(--border); font-size: 13px; }
    .ded-amt { width: 100px; text-align: right; }
    .enroll-row { display: flex; justify-content: space-between; align-items: center; gap: 8px;
      padding: 8px 0; border-bottom: 1px solid var(--border); font-size: 13px; }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div class="row"><a routerLink="/employees" class="btn ghost sm">‹ Back</a><h1>Employee Profile</h1></div>
        @if (auth.isHr() && e()) {
          <button class="btn" (click)="editing.set(!editing())">{{ editing() ? 'Cancel Edit' : '✎ Edit' }}</button>
        }
      </div>

      @if (e(); as emp) {
        <div class="card mb">
          <div class="head">
            <div class="photo">
              @if (emp.photoUrl) { <img [src]="apiBase + emp.photoUrl" alt="photo" /> } @else { {{ initials(emp) }} }
            </div>
            <div style="flex:1">
              <h2 style="margin:0">{{ emp.firstName }} {{ emp.middleName }} {{ emp.lastName }} {{ emp.suffix }}</h2>
              <div class="muted">{{ emp.employeeCode }} · {{ emp.position?.title }} · {{ emp.department?.name }}</div>
              <div class="row mt" style="gap:8px">
                <span class="badge {{ statusKey(emp.status) }}">{{ statusName(emp.status) }}</span>
                <span class="badge muted">{{ emp.branch?.name }}</span>
                <span class="badge muted">{{ emp.site?.name }}</span>
              </div>
            </div>
            @if (auth.isHr()) {
              <label class="btn secondary sm">
                Upload Photo<input type="file" hidden accept="image/*" (change)="uploadPhoto($event)" />
              </label>
            }
          </div>
        </div>

        @if (saved()) { <div class="alert success">Changes saved.</div> }
        @if (error()) { <div class="alert error">{{ error() }}</div> }

        @if (editing()) {
          <div class="card mb">
            <div class="card-title">Edit Employment & Compensation</div>
            <div class="form-grid">
              <label class="field"><span class="lbl">First Name</span><input class="ctl" [(ngModel)]="emp.firstName" /></label>
              <label class="field"><span class="lbl">Last Name</span><input class="ctl" [(ngModel)]="emp.lastName" /></label>
              <label class="field"><span class="lbl">Email</span><input class="ctl" [(ngModel)]="emp.email" /></label>
              <label class="field"><span class="lbl">Contact</span><input class="ctl" [(ngModel)]="emp.contactNumber" /></label>
              <label class="field"><span class="lbl">Address</span><input class="ctl" [(ngModel)]="emp.address" /></label>
              <label class="field"><span class="lbl">Status</span>
                <select class="ctl" [(ngModel)]="emp.status">
                  @for (s of statuses; track s.v) { <option [ngValue]="s.v">{{ s.n }}</option> }
                </select></label>
              @if (emp.status === 2) {
                <label class="field"><span class="lbl">Regularization Date</span><input class="ctl" type="date" [(ngModel)]="emp.regularizationDate" /></label>
              }
              <div class="muted small" style="grid-column:1/-1">HR may change Probationary → Regular to grant <b>5 days SIL</b>. Emergency Leave (10 days) is assigned at hire.</div>
              <label class="field"><span class="lbl">Monthly Salary</span><input class="ctl" type="number" [(ngModel)]="emp.monthlySalary" /></label>
              <label class="field"><span class="lbl">Shift Start</span><input class="ctl" [(ngModel)]="emp.shiftStart" placeholder="08:00:00" /></label>
              <label class="field"><span class="lbl">Shift End</span><input class="ctl" [(ngModel)]="emp.shiftEnd" placeholder="17:00:00" /></label>
              <label class="field"><span class="lbl">Biometric User ID</span><input class="ctl" [(ngModel)]="emp.biometricUserId" /></label>
            </div>
            <div class="modal-actions"><button class="btn" (click)="save()">Save Changes</button></div>
          </div>
        }

        <div class="grid grid-2">
          <div class="card">
            <div class="card-title">Employment Information</div>
            <div class="kv">
              <span class="k">Hire Date</span><span>{{ emp.hireDate }}</span>
              <span class="k">Regularization</span><span>{{ emp.regularizationDate || '—' }}</span>
              <span class="k">Pay Type</span><span>{{ payType(emp.payType) }}</span>
              @if (auth.isHr() || auth.isPayroll()) {
                <span class="k">Monthly Salary</span><span>₱{{ emp.monthlySalary | number:'1.2-2' }}</span>
                <span class="k">Daily Rate</span><span>₱{{ dailyRate(emp) | number:'1.2-2' }}</span>
                <span class="k">Hourly Rate</span><span>₱{{ hourlyRate(emp) | number:'1.2-2' }}</span>
                <span class="k muted small" style="grid-column:1/-1">Basic ÷ 24 days = daily · daily ÷ 8 hrs = hourly</span>
              }
              <span class="k">Shift</span><span>{{ emp.shiftStart }} – {{ emp.shiftEnd }}</span>
              <span class="k">Work Days</span><span>{{ emp.workDays }}</span>
              <span class="k">Biometric ID</span><span>{{ emp.biometricUserId || '—' }}</span>
            </div>
            <div class="card-title mt">Government Numbers</div>
            <div class="kv">
              <span class="k">SSS</span><span>{{ emp.sssNumber || '—' }}</span>
              <span class="k">PhilHealth</span><span>{{ emp.philHealthNumber || '—' }}</span>
              <span class="k">Pag-IBIG</span><span>{{ emp.pagIbigNumber || '—' }}</span>
              <span class="k">TIN</span><span>{{ emp.tin || '—' }}</span>
            </div>
          </div>

          <div class="card">
            <div class="card-title">Emergency Contacts</div>
            @for (c of emp.emergencyContacts; track c.id) {
              <div class="row" style="justify-content:space-between;padding:8px 0;border-bottom:1px solid var(--border)">
                <div><b>{{ c.name }}</b> <span class="muted small">({{ c.relationship }})</span><div class="muted small">{{ c.contactNumber }}</div></div>
                @if (auth.isHr()) { <button class="btn ghost sm" (click)="deleteContact(c.id)">Remove</button> }
              </div>
            } @empty { <div class="empty">No emergency contacts</div> }
            @if (auth.isHr()) {
              <div class="form-grid mt">
                <label class="field"><span class="lbl">Name</span><input class="ctl" [(ngModel)]="newContact.name" /></label>
                <label class="field"><span class="lbl">Relationship</span><input class="ctl" [(ngModel)]="newContact.relationship" /></label>
                <label class="field"><span class="lbl">Contact No.</span><input class="ctl" [(ngModel)]="newContact.contactNumber" /></label>
              </div>
              <button class="btn secondary sm" (click)="addContact()">＋ Add Contact</button>
            }

            <div class="card-title mt">Biometric Enrollment (SenseFace 2A)</div>
            <div class="muted small mb">Register face and fingerprints on the device for time in/out. Templates sync to HRIS automatically.</div>

            @for (t of emp.biometricTemplates; track t.id) {
              <div class="enroll-row">
                <span>
                  <span class="badge {{ t.type === 1 ? 'success' : 'info' }}">{{ t.type === 1 ? '👤 Face' : '👆 Finger ' + t.fingerIndex }}</span>
                  v{{ t.version }} · {{ t.capturedAt | date:'MMM d, y h:mm a' }}
                  @if (t.capturedOnDeviceSerial) { <span class="muted small">· {{ t.capturedOnDeviceSerial }}</span> }
                </span>
                @if (auth.isHr()) {
                  <button class="btn ghost sm" (click)="deleteTemplate(t.id)">Remove</button>
                }
              </div>
            } @empty {
              <div class="muted small mb">No templates enrolled yet.</div>
            }

            @if (auth.isHr()) {
              @if (enrollMessage()) { <div class="alert success">{{ enrollMessage() }}</div> }
              @if (enrollError()) { <div class="alert error">{{ enrollError() }}</div> }
              @if (biometricConfig()?.simulated) {
                <div class="alert warning">Development mode: biometric provider is Simulated — enrollments complete without a device scan.</div>
              }

              <div class="bio-actions">
                <label class="field">
                  <span class="lbl">Device</span>
                  <select class="ctl" [(ngModel)]="enrollForm.deviceId">
                    @for (d of devices(); track d.id) {
                      <option [ngValue]="d.id">{{ d.name }} ({{ d.serialNumber }}){{ d.online ? '' : ' · offline' }}</option>
                    }
                  </select>
                </label>
                <label class="field">
                  <span class="lbl">Type</span>
                  <select class="ctl" [(ngModel)]="enrollForm.type">
                    <option [ngValue]="1">Face</option>
                    <option [ngValue]="2">Fingerprint</option>
                  </select>
                </label>
                @if (enrollForm.type === 2) {
                  <label class="field">
                    <span class="lbl">Finger (0–9)</span>
                    <select class="ctl" [(ngModel)]="enrollForm.fingerIndex">
                      @for (f of fingers; track f.v) { <option [ngValue]="f.v">{{ f.n }}</option> }
                    </select>
                  </label>
                }
                <button class="btn" [disabled]="!canStartEnrollment()" (click)="startEnrollment()">
                  {{ enrollBusy() ? 'Starting…' : 'Start Enrollment' }}
                </button>
              </div>
              @if (!biometricConfig()?.simulated && enrollForm.deviceId && !selectedEnrollDeviceOnline()) {
                <div class="alert error mt">Selected device is offline. Connect the device and ensure the site gateway is running before enrolling.</div>
              }
              <div class="muted small mt">
                @if (biometricConfig()?.simulated) {
                  Simulated provider: templates are created automatically without a physical scan.
                } @else {
                  After starting, the employee must scan face or fingerprint at the online device. Enrollment completes only after the device captures the template.
                }
              </div>

              @if (enrollments().length) {
                <div class="card-title mt">Recent Enrollment Sessions</div>
                @for (s of enrollments(); track s.id) {
                  <div class="enroll-row">
                    <span>{{ s.type }} · {{ s.device }} · <span class="badge {{ statusBadge(s.status) }}">{{ s.status }}</span></span>
                    <span class="muted small">{{ s.createdAt | date:'MMM d, h:mm a' }}</span>
                  </div>
                }
              }
            } @else {
              <div class="muted small">Contact HR to register biometrics on the SenseFace device.</div>
            }
          </div>
        </div>

        @if (canManagePayroll()) {
          <div class="card mt">
            <div class="card-title row" style="justify-content:space-between">
              <span>Recurring Deductions</span>
              <div class="row" style="gap:6px">
                @if (deductionTemplates().length) {
                  <select class="ctl sm" [(ngModel)]="selectedTemplateId">
                    <option [ngValue]="null">Apply template…</option>
                    @for (t of deductionTemplates(); track t.id) { <option [ngValue]="t.id">{{ t.name }}</option> }
                  </select>
                  <button class="btn secondary sm" [disabled]="!selectedTemplateId" (click)="applyTemplate()">Apply</button>
                }
              </div>
            </div>
            <div class="muted small mb">Configure amounts once. Payroll officers check/uncheck per cutoff when processing.</div>
            @if (deductionSaved()) { <div class="alert success mb">Deduction settings saved.</div> }
            @if (deductionError()) { <div class="alert error mb">{{ deductionError() }}</div> }

            @for (d of employeeDeductions(); track d.id) {
              @if (d.isActive) {
                <div class="ded-row">
                  <input type="checkbox" [checked]="d.isProfileEnabled" (change)="toggleDeductionProfile(d, $event)" />
                  <div>
                    <b>{{ d.typeName }}</b>
                    @if (d.loanReference) { <span class="muted small"> · {{ d.loanReference }}</span> }
                    <div class="muted small">{{ d.frequency }}@if (d.remainingBalance != null) { · Bal ₱{{ d.remainingBalance | number:'1.2-2' }} }@if (d.totalInstallments) { · {{ d.paidInstallments }}/{{ d.totalInstallments }} paid }</div>
                  </div>
                  <input class="ctl ded-amt" type="number" min="0" step="0.01" [ngModel]="d.amount" (ngModelChange)="updateDeductionAmount(d, $event)" />
                  <button class="btn ghost sm" (click)="removeDeduction(d.id)">Remove</button>
                </div>
              }
            } @empty { <div class="empty">No recurring deductions configured</div> }

            <div class="form-grid mt">
              <label class="field"><span class="lbl">Deduction Type</span>
                <select class="ctl" [(ngModel)]="newDeduction.deductionTypeId">
                  <option [ngValue]="null">Select…</option>
                  @for (t of deductionTypes(); track t.id) { <option [ngValue]="t.id">{{ t.name }}</option> }
                </select>
              </label>
              <label class="field"><span class="lbl">Amount</span><input class="ctl" type="number" min="0" step="0.01" [(ngModel)]="newDeduction.amount" /></label>
              <label class="field"><span class="lbl">Frequency</span>
                <select class="ctl" [(ngModel)]="newDeduction.frequency">
                  @for (f of deductionFrequencies; track f.v) { <option [ngValue]="f.v">{{ f.n }}</option> }
                </select>
              </label>
              <label class="field"><span class="lbl">Installments (optional)</span><input class="ctl" type="number" min="1" [(ngModel)]="newDeduction.totalInstallments" /></label>
            </div>
            <button class="btn secondary sm mt" [disabled]="!newDeduction.deductionTypeId" (click)="addDeduction()">＋ Add Deduction</button>
          </div>
        }

        <div class="card mt">
          <div class="card-title">Employee History</div>
          <div class="table-wrap">
            <table class="data">
              <thead><tr><th>Date</th><th>Event</th><th>Description</th><th>By</th></tr></thead>
              <tbody>
                @for (h of history(); track h.id) {
                  <tr><td>{{ h.effectiveDate }}</td><td><span class="badge muted">{{ h.eventType }}</span></td><td>{{ h.description }}</td><td class="muted">{{ h.changedByUserName }}</td></tr>
                } @empty { <tr><td colspan="4"><div class="empty">No history</div></td></tr> }
              </tbody>
            </table>
          </div>
        </div>
      } @else { <div class="card"><div class="empty">Loading…</div></div> }
    </div>
  `
})
export class EmployeeDetailComponent implements OnInit, OnDestroy {
  @Input() id!: string;
  e = signal<any | null>(null);
  history = signal<any[]>([]);
  devices = signal<any[]>([]);
  enrollments = signal<any[]>([]);
  editing = signal(false);
  saved = signal(false);
  error = signal('');
  enrollMessage = signal('');
  enrollError = signal('');
  enrollBusy = signal(false);
  biometricConfig = signal<{ simulated: boolean; message?: string } | null>(null);
  newContact: any = {};
  enrollForm: any = { type: 1, fingerIndex: 0, deviceId: null };
  employeeDeductions = signal<any[]>([]);
  deductionTypes = signal<any[]>([]);
  deductionTemplates = signal<any[]>([]);
  deductionSaved = signal(false);
  deductionError = signal('');
  selectedTemplateId: number | null = null;
  newDeduction: any = { deductionTypeId: null, amount: 0, frequency: 'EveryCutoff', totalInstallments: null };
  deductionFrequencies = [
    { v: 'EveryCutoff', n: 'Every cutoff (semi-monthly)' },
    { v: 'Monthly', n: 'Monthly (1st cutoff)' },
    { v: 'FirstHalfOnly', n: '1–15 cutoff only' },
    { v: 'SecondHalfOnly', n: '16–30 cutoff only' },
    { v: 'FixedInstallments', n: 'Fixed installments' }
  ];
  private pollTimer: ReturnType<typeof setInterval> | undefined;
  apiBase = environment.apiBase;
  fingers = [
    { v: 0, n: '0 — Left thumb' }, { v: 1, n: '1 — Left index' }, { v: 2, n: '2 — Left middle' },
    { v: 3, n: '3 — Left ring' }, { v: 4, n: '4 — Left pinky' }, { v: 5, n: '5 — Right thumb' },
    { v: 6, n: '6 — Right index' }, { v: 7, n: '7 — Right middle' }, { v: 8, n: '8 — Right ring' },
    { v: 9, n: '9 — Right pinky' }
  ];
  statuses = [
    { v: 1, n: 'Probationary' }, { v: 2, n: 'Regular' }, { v: 3, n: 'Contractual' }, { v: 4, n: 'ProjectBased' },
    { v: 5, n: 'Resigned' }, { v: 6, n: 'Terminated' }, { v: 7, n: 'Retired' }, { v: 8, n: 'OnLeave' }
  ];

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void {
    this.load();
    this.loadEnrollments();
    this.loadBiometricConfig();
    if (this.auth.isPayroll() || this.auth.isHr()) this.loadDeductionMeta();
  }

  ngOnDestroy(): void {
    if (this.pollTimer) clearInterval(this.pollTimer);
  }

  load(): void {
    this.api.get<{ employee: any; history: any[] }>(`employees/${this.id}`).subscribe({
      next: res => {
        this.e.set(res.employee);
        this.history.set(res.history);
        this.loadDevices(res.employee?.siteId);
        if (this.canManagePayroll()) this.loadDeductions();
      },
      error: () => this.error.set('Unable to load this employee.')
    });
  }

  loadDevices(siteId?: number): void {
    this.api.get<any[]>('biometric/devices', siteId ? { siteId } : {}).subscribe({
      next: d => {
        this.devices.set(d);
        if (!this.enrollForm.deviceId && d.length) this.enrollForm.deviceId = d[0].id;
      },
      error: () => {}
    });
  }

  loadBiometricConfig(): void {
    this.api.get<any>('biometric/config').subscribe({
      next: c => this.biometricConfig.set(c),
      error: () => this.biometricConfig.set({ simulated: false })
    });
  }

  selectedEnrollDeviceOnline(): boolean {
    const d = this.devices().find(x => x.id === this.enrollForm.deviceId);
    return !!d?.online;
  }

  canStartEnrollment(): boolean {
    if (this.enrollBusy() || !this.enrollForm.deviceId) return false;
    if (this.biometricConfig()?.simulated) return true;
    return this.selectedEnrollDeviceOnline();
  }

  loadEnrollments(): void {
    this.api.get<any[]>('biometric/enrollments', { employeeId: this.id, take: 8 }).subscribe({
      next: r => this.enrollments.set(r),
      error: () => {}
    });
  }

  startEnrollment(): void {
    if (!this.enrollForm.deviceId) return;
    this.enrollBusy.set(true);
    this.enrollError.set('');
    this.enrollMessage.set('');
    this.api.post<any>('biometric/enrollments', {
      employeeId: +this.id,
      deviceId: this.enrollForm.deviceId,
      type: this.enrollForm.type,
      fingerIndex: this.enrollForm.type === 2 ? this.enrollForm.fingerIndex : 0
    }).subscribe({
      next: res => {
        this.enrollBusy.set(false);
        this.enrollMessage.set(res.message ?? 'Enrollment started.');
        this.loadEnrollments();
        this.pollUntilDone(res.id, !res.simulated);
      },
      error: err => {
        this.enrollBusy.set(false);
        this.enrollError.set(err?.error?.message ?? 'Could not start enrollment.');
      }
    });
  }

  pollUntilDone(enrollmentId: number, gatewayMode = false): void {
    if (this.pollTimer) clearInterval(this.pollTimer);
    let attempts = 0;
    this.pollTimer = setInterval(() => {
      attempts++;
      this.api.get<any>(`biometric/enrollments/${enrollmentId}`).subscribe({
        next: s => {
          this.loadEnrollments();
          if (s.statusName === 'WaitingOnDevice' || s.statusName === 'Pending') {
            if (gatewayMode) this.enrollMessage.set('Waiting for employee to scan at the device…');
          }
          if (['Completed', 'Failed', 'Cancelled', 'Expired'].includes(s.statusName)) {
            clearInterval(this.pollTimer!);
            this.pollTimer = undefined;
            this.load();
            if (s.statusName === 'Completed') this.enrollMessage.set('Enrollment completed successfully.');
            else if (s.errorMessage) this.enrollError.set(s.errorMessage);
            else if (s.statusName === 'Expired') this.enrollError.set('Enrollment timed out — no scan was received from the device.');
          }
        }
      });
      if (attempts > 120) {
        clearInterval(this.pollTimer!);
        this.pollTimer = undefined;
        this.enrollError.set('Enrollment timed out waiting for the device.');
      }
    }, 3000);
  }

  deleteTemplate(id: number): void {
    if (!confirm('Remove this biometric template? Employee must re-enroll on the device.')) return;
    this.api.delete(`biometric/templates/${id}`).subscribe(() => this.load());
  }

  statusBadge(status: string): string {
    return ({ Completed: 'success', WaitingOnDevice: 'info', Pending: 'muted', Failed: 'danger', Expired: 'warning', Cancelled: 'muted' } as any)[status] ?? 'muted';
  }

  initials(emp: any): string { return `${emp.firstName?.[0] ?? ''}${emp.lastName?.[0] ?? ''}`.toUpperCase(); }

  dailyRate(emp: any): number {
    return emp.dailyRate ?? Math.round(emp.monthlySalary / 24 * 100) / 100;
  }

  hourlyRate(emp: any): number {
    return Math.round(this.dailyRate(emp) / 8 * 100) / 100;
  }

  statusName(v: number): string { return this.statuses.find(s => s.v === v)?.n ?? String(v); }
  statusKey(v: number): string { return this.statusName(v).toLowerCase(); }
  payType(v: number): string { return ({ 1: 'Monthly', 2: 'Semi-Monthly', 3: 'Daily', 4: 'Hourly' } as any)[v] ?? v; }

  canManagePayroll(): boolean {
    return this.auth.isHr() || this.auth.isPayroll();
  }

  save(): void {
    this.api.put(`employees/${this.id}`, this.e()).subscribe({
      next: () => { this.saved.set(true); this.editing.set(false); this.load(); setTimeout(() => this.saved.set(false), 3000); },
      error: err => this.error.set(err?.error?.message ?? 'Save failed.')
    });
  }

  uploadPhoto(ev: Event): void {
    const file = (ev.target as HTMLInputElement).files?.[0];
    if (!file) return;
    const form = new FormData();
    form.append('file', file);
    this.api.upload(`employees/${this.id}/photo`, form).subscribe(() => this.load());
  }

  addContact(): void {
    if (!this.newContact.name) return;
    this.api.post(`employees/${this.id}/contacts`, this.newContact).subscribe(() => { this.newContact = {}; this.load(); });
  }

  deleteContact(cid: number): void {
    this.api.delete(`employees/contacts/${cid}`).subscribe(() => this.load());
  }

  loadDeductionMeta(): void {
    this.api.get<any[]>('payroll/deduction-types').subscribe(r => this.deductionTypes.set(r));
    this.api.get<any[]>('payroll/deduction-templates').subscribe(r => this.deductionTemplates.set(r));
  }

  loadDeductions(): void {
    this.api.get<any[]>(`employees/${this.id}/deductions`).subscribe(r => this.employeeDeductions.set(r));
  }

  addDeduction(): void {
    if (!this.newDeduction.deductionTypeId) return;
    const body = {
      deductionTypeId: this.newDeduction.deductionTypeId,
      amount: this.newDeduction.amount || 0,
      frequency: this.newDeduction.frequency,
      isProfileEnabled: true,
      totalInstallments: this.newDeduction.totalInstallments || null,
      remainingBalance: this.newDeduction.totalInstallments ? (this.newDeduction.amount || 0) * this.newDeduction.totalInstallments : null
    };
    this.api.post(`employees/${this.id}/deductions`, body).subscribe({
      next: () => {
        this.newDeduction = { deductionTypeId: null, amount: 0, frequency: 'EveryCutoff', totalInstallments: null };
        this.loadDeductions();
        this.deductionSaved.set(true);
        setTimeout(() => this.deductionSaved.set(false), 3000);
      },
      error: err => this.deductionError.set(err?.error?.message ?? 'Could not add deduction.')
    });
  }

  updateDeductionAmount(d: any, amount: number): void {
    this.api.put(`employees/${this.id}/deductions/${d.id}`, {
      deductionTypeId: d.deductionTypeId, amount, frequency: d.frequency,
      isProfileEnabled: d.isProfileEnabled, remainingBalance: d.remainingBalance,
      totalInstallments: d.totalInstallments
    }).subscribe({ next: () => this.loadDeductions(), error: () => {} });
  }

  toggleDeductionProfile(d: any, ev: Event): void {
    const enabled = (ev.target as HTMLInputElement).checked;
    this.api.put(`employees/${this.id}/deductions/${d.id}`, {
      deductionTypeId: d.deductionTypeId, amount: d.amount, frequency: d.frequency,
      isProfileEnabled: enabled, remainingBalance: d.remainingBalance, totalInstallments: d.totalInstallments
    }).subscribe({ next: () => this.loadDeductions() });
  }

  removeDeduction(id: number): void {
    if (!confirm('Remove this recurring deduction?')) return;
    this.api.delete(`employees/${this.id}/deductions/${id}`).subscribe(() => this.loadDeductions());
  }

  applyTemplate(): void {
    if (!this.selectedTemplateId) return;
    this.api.post(`employees/${this.id}/deductions/apply-template/${this.selectedTemplateId}`, { defaultAmount: 0, frequency: 'EveryCutoff' }).subscribe({
      next: () => { this.loadDeductions(); this.selectedTemplateId = null; },
      error: err => this.deductionError.set(err?.error?.message ?? 'Could not apply template.')
    });
  }
}
