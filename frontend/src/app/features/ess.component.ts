import { Component, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService, parsePagedResponse } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-ess',
  imports: [DatePipe, DecimalPipe, FormsModule, RouterLink],
  styles: [`
    .profile-card { max-width: 560px; width: 100%; }
    .pagination { display: flex; align-items: center; gap: 10px; padding: 12px 0 0; font-size: 13px; color: var(--text-soft); flex-wrap: wrap; }
    .day-punches { display: flex; flex-direction: column; gap: 6px; }
    .day-punch { display: flex; flex-direction: column; gap: 1px; font-size: 13px; line-height: 1.35; }
    .day-punch.missing { opacity: .55; }
    .day-punch .time { font-variant-numeric: tabular-nums; }
    .att-subtabs { margin-bottom: 0; }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>My Portal</h1><div class="sub">Self-service: attendance, payslips, requests and personal info</div></div>
      </div>

      @if (!auth.user()?.employeeId) {
        <div class="alert info">This account has no linked employee profile. Self-service data is unavailable.</div>
      }

      <div class="grid grid-4 mb">
        <a class="stat-card accent" routerLink="/leave" style="text-decoration:none"><div class="label">Apply Leave</div><div class="value">🌴</div><div class="hint">VL · SL · SIL</div></a>
        <a class="stat-card" routerLink="/overtime" style="text-decoration:none"><div class="label">File Overtime</div><div class="value">⏱</div><div class="hint">with approval workflow</div></a>
        <a class="stat-card" routerLink="/loans" style="text-decoration:none"><div class="label">Cash Advance / Loan</div><div class="value">💳</div><div class="hint">apply & track balance</div></a>
        <a class="stat-card" routerLink="/notifications" style="text-decoration:none"><div class="label">Notifications</div><div class="value">🔔</div><div class="hint">approvals & alerts</div></a>
      </div>

      <div class="tabs">
        <button [class.active]="tab() === 'attendance'" (click)="tab.set('attendance'); loadLogDays()">My Attendance</button>
        <button [class.active]="tab() === 'payslips'" (click)="tab.set('payslips'); loadPayslips()">My Payslips</button>
        <button [class.active]="tab() === 'balances'" (click)="tab.set('balances'); loadBalances()">My Leave Credits</button>
        <button [class.active]="tab() === 'profile'" (click)="tab.set('profile')">Personal Info</button>
      </div>

      @if (tab() === 'attendance') {
        <div class="tabs att-subtabs">
          <button [class.active]="attTab() === 'logs'" (click)="attTab.set('logs'); loadLogDays()">Raw Logs</button>
          <button [class.active]="attTab() === 'corrections'" (click)="attTab.set('corrections'); loadCorrections()">Corrections</button>
        </div>

        @if (attTab() === 'logs') {
          <div class="card">
            <div class="row mb">
              <input class="ctl" style="max-width:170px" type="date" [(ngModel)]="logFrom" (change)="logPage.set(1); loadLogDays()" />
              <span class="muted">to</span>
              <input class="ctl" style="max-width:170px" type="date" [(ngModel)]="logTo" (change)="logPage.set(1); loadLogDays()" />
              <div class="spacer"></div>
              <span class="muted small">{{ logTotal() }} day(s)</span>
            </div>
            <div class="table-wrap">
              <table class="data logs-by-day">
                <thead><tr><th>Employee</th><th>Punch</th><th>Date & Time</th><th>Source</th><th>Verify</th><th>Site / Device</th></tr></thead>
                <tbody>
                  @for (row of logDays(); track row.date + row.employee?.id) {
                    <tr>
                      <td style="vertical-align:top">
                        <div class="bold">{{ row.employee?.name }}</div>
                        <div class="muted small">{{ row.employee?.employeeCode }}</div>
                        <div class="muted small">{{ row.date | date:'EEE, MMM d, y' }}</div>
                      </td>
                      <td style="vertical-align:top;padding:0">
                        <div class="day-punches">
                          @for (p of row.punches; track p.slot) {
                            <div class="day-punch" [class.missing]="p.missing">
                              <span class="badge {{ p.label === 'Time In' ? 'success' : 'info' }}">{{ p.label }}</span>
                              @if (p.isCorrected) { <span class="badge warning" style="margin-left:4px">corrected</span> }
                            </div>
                          }
                        </div>
                      </td>
                      <td style="vertical-align:top;padding:0">
                        <div class="day-punches">
                          @for (p of row.punches; track p.slot) {
                            <div class="day-punch" [class.missing]="p.missing">
                              <span class="time">{{ p.punchTime ? (p.punchTime | date:'MMM d, y h:mm:ss a') : '—' }}</span>
                            </div>
                          }
                        </div>
                      </td>
                      <td style="vertical-align:top;padding:0">
                        <div class="day-punches">
                          @for (p of row.punches; track p.slot) {
                            <div class="day-punch" [class.missing]="p.missing"><span>{{ p.source || '—' }}</span></div>
                          }
                        </div>
                      </td>
                      <td style="vertical-align:top;padding:0">
                        <div class="day-punches">
                          @for (p of row.punches; track p.slot) {
                            <div class="day-punch" [class.missing]="p.missing"><span class="muted">{{ p.verifyMode || '—' }}</span></div>
                          }
                        </div>
                      </td>
                      <td style="vertical-align:top;padding:0">
                        <div class="day-punches">
                          @for (p of row.punches; track p.slot) {
                            <div class="day-punch" [class.missing]="p.missing">
                              <span>{{ p.site || '—' }}</span>
                              @if (p.device) { <span class="muted small">{{ p.device }}</span> }
                            </div>
                          }
                        </div>
                      </td>
                    </tr>
                  } @empty { <tr><td colspan="6"><div class="empty">No logs found</div></td></tr> }
                </tbody>
              </table>
            </div>
            @if (logTotal() > logPageSize) {
              <div class="row mt">
                <button class="btn secondary sm" [disabled]="logPage() <= 1" (click)="logPage.set(logPage() - 1); loadLogDays()">← Prev</button>
                <span class="muted small">Page {{ logPage() }} of {{ logTotalPages() }}</span>
                <button class="btn secondary sm" [disabled]="logPage() >= logTotalPages()" (click)="logPage.set(logPage() + 1); loadLogDays()">Next →</button>
              </div>
            }
          </div>
        }

        @if (attTab() === 'corrections') {
          <div class="card">
            <div class="row mb">
              <div class="spacer"></div>
              <button class="btn" (click)="openCorrection()">＋ Request Correction</button>
            </div>
            <div class="table-wrap">
              <table class="data">
                <thead><tr><th>Date</th><th>Issue</th><th>Punch</th><th>Corrected Time</th><th>Reason</th><th>Status</th></tr></thead>
                <tbody>
                  @for (c of corrections(); track c.id) {
                    <tr>
                      <td>{{ c.attendanceDate }}</td>
                      <td class="muted small">{{ formatIssue(c.issueType) }}</td>
                      <td>{{ c.punchType }}</td>
                      <td>{{ c.correctedTime | date:'MMM d, h:mm a' }}</td>
                      <td class="muted">{{ c.reason }}</td>
                      <td>
                        <span class="badge {{ c.status.toLowerCase() }}">{{ c.status }}</span>
                        @if (c.status === 'InProgress') { <div class="muted small">Level {{ c.currentApprovalLevel }}</div> }
                        @if (c.payrollAppliedAt) { <div class="muted small">Applied to payroll</div> }
                      </td>
                    </tr>
                  } @empty { <tr><td colspan="6"><div class="empty">No correction requests</div></td></tr> }
                </tbody>
              </table>
            </div>
            @if (correctionTotal() > correctionPageSize) {
              <div class="row mt">
                <button class="btn secondary sm" [disabled]="correctionPage() <= 1" (click)="correctionPage.set(correctionPage() - 1); loadCorrections()">← Prev</button>
                <span class="muted small">Page {{ correctionPage() }} of {{ correctionTotalPages() }} · {{ correctionTotal() }} record(s)</span>
                <button class="btn secondary sm" [disabled]="correctionPage() >= correctionTotalPages()" (click)="correctionPage.set(correctionPage() + 1); loadCorrections()">Next →</button>
              </div>
            }
          </div>
        }
      }

      @if (tab() === 'payslips') {
        <div class="card">
          <div class="table-wrap">
          <table class="data">
            <thead><tr><th>Cutoff</th><th>Pay Date</th><th class="num">Gross</th><th class="num">Deductions</th><th class="num">Net Pay</th><th></th></tr></thead>
            <tbody>
              @for (p of payslips(); track p.id) {
                <tr>
                  <td class="bold">{{ p.cutoff }}</td><td>{{ p.payDate }}</td>
                  <td class="num">₱{{ p.grossPay | number:'1.2-2' }}</td>
                  <td class="num">₱{{ p.totalDeductions | number:'1.2-2' }}</td>
                  <td class="num bold">₱{{ p.netPay | number:'1.2-2' }}</td>
                  <td><button class="btn ghost sm" (click)="viewSlip(p.id)">View</button></td>
                </tr>
              } @empty { <tr><td colspan="6"><div class="empty">No released payslips yet</div></td></tr> }
            </tbody>
          </table>
          </div>
          @if (payslipTotal() > payslipPageSize) {
            <div class="pagination">
              <span>Page {{ payslipPage() }} of {{ payslipTotalPages() }} · {{ payslipTotal() }} record(s) · {{ payslipPageSize }} rows/page</span>
              <button class="btn secondary sm" [disabled]="payslipPage() <= 1" (click)="payslipPage.set(payslipPage() - 1); loadPayslips()">Previous</button>
              <button class="btn secondary sm" [disabled]="payslipPage() >= payslipTotalPages()" (click)="payslipPage.set(payslipPage() + 1); loadPayslips()">Next</button>
            </div>
          }
        </div>
      }

      @if (tab() === 'balances') {
        <div class="card">
          @if (!auth.user()?.employeeId) {
            <div class="alert info">This account has no linked employee profile. Leave credits are unavailable.</div>
          } @else {
            <div class="grid grid-4">
              @for (b of balances(); track b.id) {
                <div class="stat-card">
                  <div class="label">{{ b.leaveType.name }}</div>
                  <div class="value">{{ b.remaining }}</div>
                  <div class="hint">of {{ b.credits }} credits · {{ b.used }} used</div>
                </div>
              } @empty { <div class="empty">No leave balances for this year</div> }
            </div>
          }
        </div>
      }

      @if (tab() === 'profile') {
        <div class="card profile-card">
          <div class="card-title">Update Personal Information</div>
          @if (saved()) { <div class="alert success">Information updated.</div> }
          <label class="field"><span class="lbl">Address</span><input class="ctl" [(ngModel)]="profile.address" /></label>
          <label class="field"><span class="lbl">Contact Number</span><input class="ctl" [(ngModel)]="profile.contactNumber" /></label>
          <label class="field"><span class="lbl">Email</span><input class="ctl" [(ngModel)]="profile.email" /></label>
          <label class="field"><span class="lbl">Civil Status</span>
            <select class="ctl" [(ngModel)]="profile.civilStatus">
              <option value="Single">Single</option><option value="Married">Married</option>
              <option value="Widowed">Widowed</option><option value="Separated">Separated</option>
            </select></label>
          <button class="btn" (click)="saveProfile()">Save Changes</button>

          <div class="card-title mt">Change Password</div>
          @if (pwMessage()) { <div class="alert" [class.success]="!pwError()" [class.error]="pwError()">{{ pwMessage() }}</div> }
          <label class="field"><span class="lbl">Current Password</span><input class="ctl" type="password" [(ngModel)]="pw.current" /></label>
          <label class="field"><span class="lbl">New Password (min 8 chars)</span><input class="ctl" type="password" [(ngModel)]="pw.next" /></label>
          <button class="btn secondary" (click)="changePassword()">Update Password</button>
        </div>
      }

      @if (showCorrection()) {
        <div class="modal-backdrop" (click)="showCorrection.set(false)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">Request Attendance Correction</div>
            @if (attError()) { <div class="alert error">{{ attError() }}</div> }
            <label class="field"><span class="lbl">Issue Type *</span>
              <select class="ctl" [(ngModel)]="correction.issueType">
                @for (i of issueTypes; track i.v) { <option [ngValue]="i.v">{{ i.n }}</option> }
              </select>
            </label>
            <label class="field"><span class="lbl">Attendance Date</span><input class="ctl" type="date" [(ngModel)]="correction.attendanceDate" /></label>
            <label class="field"><span class="lbl">Punch Type</span>
              <select class="ctl" [(ngModel)]="correction.punchType">
                <option [ngValue]="1">Time In</option><option [ngValue]="2">Time Out</option>
                <option [ngValue]="3">Break In</option><option [ngValue]="4">Break Out</option>
              </select></label>
            <label class="field"><span class="lbl">Correct Time</span><input class="ctl" type="datetime-local" [(ngModel)]="correction.correctedTime" /></label>
            <label class="field"><span class="lbl">Reason *</span><textarea class="ctl" [(ngModel)]="correction.reason"></textarea></label>
            <label class="field"><span class="lbl">Supporting Document (optional)</span><input class="ctl" [(ngModel)]="correction.supportingDocument" placeholder="File name or reference" /></label>
            <div class="muted small mb">Approval: Supervisor/Dept Head → HR Officer → Payroll Officer (apply to payroll).</div>
            <div class="modal-actions">
              <button class="btn secondary" (click)="showCorrection.set(false)">Cancel</button>
              <button class="btn" (click)="saveCorrection()">Submit Request</button>
            </div>
          </div>
        </div>
      }

      @if (slip()) {
        <div class="modal-backdrop" (click)="slip.set(null)">
          <div class="modal printable" (click)="$event.stopPropagation()">
            <div class="modal-title">Payslip</div>
            <div style="font-size:13.5px">
              <div class="muted small mb">{{ slip()!.payrollCutoff?.name }} · Pay date {{ slip()!.payrollCutoff?.payDate }}</div>
              <div style="display:flex;justify-content:space-between;padding:3px 0"><span>Basic Pay</span><b>{{ slip()!.basicPay | number:'1.2-2' }}</b></div>
              <div style="display:flex;justify-content:space-between;padding:3px 0"><span>Overtime</span><b>{{ slip()!.overtimePay | number:'1.2-2' }}</b></div>
              <div style="display:flex;justify-content:space-between;padding:3px 0"><span>Allowances</span><b>{{ slip()!.allowances | number:'1.2-2' }}</b></div>
              <div style="display:flex;justify-content:space-between;padding:3px 0;border-top:1px solid var(--border)"><span>Gross</span><b>{{ slip()!.grossPay | number:'1.2-2' }}</b></div>
              <div style="display:flex;justify-content:space-between;padding:3px 0"><span>SSS / PhilHealth / Pag-IBIG</span><b>{{ slip()!.sssEmployee + slip()!.philHealthEmployee + slip()!.pagIbigEmployee | number:'1.2-2' }}</b></div>
              <div style="display:flex;justify-content:space-between;padding:3px 0"><span>Withholding Tax</span><b>{{ slip()!.withholdingTax | number:'1.2-2' }}</b></div>
              <div style="display:flex;justify-content:space-between;padding:3px 0"><span>Loans & Others</span><b>{{ slip()!.loanDeductions + slip()!.otherDeductions | number:'1.2-2' }}</b></div>
              @if (slip()!.undertimeHours > 0) {
                <div style="display:flex;justify-content:space-between;padding:3px 0"><span>Undertime ({{ slip()!.undertimeHours }} hr/s@if (slip()!.undertimeLeaveDays > 0) {, {{ slip()!.undertimeLeaveDays | number:'1.2-4' }} SIL day/s}@if (slip()!.undertimeElDays > 0) {, {{ slip()!.undertimeElDays | number:'1.2-4' }} EL day/s})</span><b>{{ slip()!.undertimeDeduction | number:'1.2-2' }}</b></div>
              }
              <div style="display:flex;justify-content:space-between;padding:8px 0;border-top:2px solid var(--text);font-weight:800;font-size:16px"><span>NET PAY</span><span>₱{{ slip()!.netPay | number:'1.2-2' }}</span></div>
            </div>
            <div class="modal-actions">
              <button class="btn secondary" (click)="slip.set(null)">Close</button>
              <button class="btn" onclick="window.print()">🖨 Print / PDF</button>
            </div>
          </div>
        </div>
      }
    </div>
  `
})
export class EssComponent implements OnInit {
  tab = signal('attendance');
  attTab = signal('logs');
  logFrom = new Date(Date.now() - 14 * 86400000).toISOString().slice(0, 10);
  logTo = new Date().toISOString().slice(0, 10);
  logDays = signal<any[]>([]);
  logPage = signal(1);
  logTotal = signal(0);
  readonly logPageSize = 25;
  corrections = signal<any[]>([]);
  correctionPage = signal(1);
  correctionTotal = signal(0);
  readonly correctionPageSize = 25;
  showCorrection = signal(false);
  attError = signal('');
  correction: any = { punchType: 1, issueType: 'MissingTimeIn' };
  issueTypes = [
    { v: 'MissingTimeIn', n: 'Missing Time In' },
    { v: 'MissingTimeOut', n: 'Missing Time Out' },
    { v: 'IncorrectRecord', n: 'Incorrect Attendance Record' },
    { v: 'ForgottenBiometric', n: 'Forgotten Biometrics Scan' },
    { v: 'DeviceFailure', n: 'Device Failure' },
    { v: 'Other', n: 'Other' }
  ];
  payslips = signal<any[]>([]);
  payslipPage = signal(1);
  payslipTotal = signal(0);
  readonly payslipPageSize = 25;
  balances = signal<any[]>([]);
  slip = signal<any | null>(null);
  saved = signal(false);
  pwMessage = signal('');
  pwError = signal(false);
  profile: any = {};
  pw = { current: '', next: '' };

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void {
    this.auth.refreshProfile().subscribe({ next: () => this.loadLogDays(), error: () => this.loadLogDays() });
  }

  loadLogDays(): void {
    if (!this.auth.user()?.employeeId) {
      this.logDays.set([]);
      this.logTotal.set(0);
      return;
    }
    this.api.get<{ total: number; items: any[] }>('attendance/logs/by-day', {
      from: this.logFrom, to: this.logTo, page: this.logPage(), pageSize: this.logPageSize
    }).subscribe(r => {
      this.logDays.set(r.items ?? []);
      this.logTotal.set(r.total ?? 0);
    });
  }

  logTotalPages(): number { return Math.max(1, Math.ceil(this.logTotal() / this.logPageSize)); }

  loadCorrections(): void {
    if (!this.auth.user()?.employeeId) {
      this.corrections.set([]);
      this.correctionTotal.set(0);
      return;
    }
    this.api.get<{ total: number; items: any[] }>('attendance/corrections', {
      page: this.correctionPage(), pageSize: this.correctionPageSize
    }).subscribe(r => {
      this.corrections.set(r.items ?? []);
      this.correctionTotal.set(r.total ?? 0);
    });
  }

  correctionTotalPages(): number { return Math.max(1, Math.ceil(this.correctionTotal() / this.correctionPageSize)); }

  openCorrection(): void {
    this.attError.set('');
    this.correction = {
      punchType: 1, issueType: 'MissingTimeIn',
      employeeId: this.auth.user()?.employeeId,
      attendanceDate: this.logTo
    };
    this.showCorrection.set(true);
  }

  formatIssue(v: string): string {
    return this.issueTypes.find(i => i.v === v)?.n ?? v?.replace(/([A-Z])/g, ' $1').trim() ?? '—';
  }

  saveCorrection(): void {
    if (!this.correction.reason) { this.attError.set('Reason is required.'); return; }
    this.api.post('attendance/corrections', this.correction).subscribe({
      next: () => {
        this.showCorrection.set(false);
        this.loadCorrections();
        this.attTab.set('corrections');
      },
      error: err => this.attError.set(err?.error?.message ?? 'Failed to submit.')
    });
  }

  loadPayslips(): void {
    this.api.get<{ total: number; items: any[] } | any[]>('payroll/my-payslips', {
      page: this.payslipPage(), pageSize: this.payslipPageSize
    }).subscribe(r => {
      const res = parsePagedResponse(r);
      this.payslips.set(res.items);
      this.payslipTotal.set(res.total);
    });
  }

  payslipTotalPages(): number { return Math.max(1, Math.ceil(this.payslipTotal() / this.payslipPageSize)); }

  loadBalances(): void {
    const employeeId = this.auth.user()?.employeeId;
    if (!employeeId) {
      this.balances.set([]);
      return;
    }

    const apply = (r: { total: number; items: any[] } | any[]) => {
      const items = parsePagedResponse(r).items.filter(b => b.employee?.id === employeeId);
      this.balances.set(this.uniqueByLeaveType(items));
    };

    this.api.get<{ total: number; items: any[] } | any[]>('leave/my-balances', { page: 1, pageSize: 10 })
      .subscribe({ next: apply, error: () => {
        this.api.get<{ total: number; items: any[] } | any[]>('leave/balances', { employeeId, mine: true, page: 1, pageSize: 10 })
          .subscribe(apply);
      }});
  }

  /** One card per leave type (guards against duplicate rows). */
  private uniqueByLeaveType(items: any[]): any[] {
    const byType = new Map<number, any>();
    for (const b of items) {
      const typeId = b.leaveType?.id;
      if (typeId == null) continue;
      if (!byType.has(typeId)) byType.set(typeId, b);
    }
    return [...byType.values()].sort((a, b) => (a.leaveType?.name ?? '').localeCompare(b.leaveType?.name ?? ''));
  }
  viewSlip(id: number): void { this.api.get<any>(`payroll/payslips/${id}`).subscribe(r => this.slip.set(r)); }

  saveProfile(): void {
    this.api.put('employees/me/personal', this.profile).subscribe(() => {
      this.saved.set(true);
      setTimeout(() => this.saved.set(false), 3000);
    });
  }

  changePassword(): void {
    this.pwMessage.set('');
    this.api.post('auth/change-password', { currentPassword: this.pw.current, newPassword: this.pw.next }).subscribe({
      next: () => { this.pwError.set(false); this.pwMessage.set('Password updated.'); this.pw = { current: '', next: '' }; },
      error: err => { this.pwError.set(true); this.pwMessage.set(err?.error?.message ?? 'Failed to update password.'); }
    });
  }
}
