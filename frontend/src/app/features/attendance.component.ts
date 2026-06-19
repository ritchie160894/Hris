import { Component, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-attendance',
  imports: [DatePipe, FormsModule],
  styles: [`
    .day-punches { display: flex; flex-direction: column; gap: 6px; }
    .day-punch { display: flex; flex-direction: column; gap: 1px; font-size: 13px; line-height: 1.35; }
    .day-punch.missing { opacity: .55; }
    .day-punch .time { font-variant-numeric: tabular-nums; }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Attendance</h1><div class="sub">Monitoring, logs and corrections</div></div>
        @if (auth.isHr()) { <button class="btn secondary" (click)="showManual.set(true)">＋ Manual Entry</button> }
      </div>

      <div class="tabs">
        <button [class.active]="tab() === 'daily'" (click)="tab.set('daily')">Daily Monitoring</button>
        <button [class.active]="tab() === 'logs'" (click)="tab.set('logs'); loadLogs()">Raw Logs</button>
        <button [class.active]="tab() === 'corrections'" (click)="tab.set('corrections'); loadCorrections()">Corrections</button>
      </div>

      @if (tab() === 'daily') {
        <div class="card">
          <div class="row mb">
            <input class="ctl" style="max-width:180px" type="date" [(ngModel)]="date" (change)="loadDaily()" />
            <div class="spacer"></div>
            <span class="badge present">Present: {{ countStatus('Present') }}</span>
            <span class="badge late">Late: {{ countStatus('Late') }}</span>
            <span class="badge onleave">On Leave: {{ countStatus('On Leave') }}</span>
            <span class="badge absent">Absent: {{ countStatus('Absent') }}</span>
          </div>
          <div class="table-wrap">
            <table class="data">
              <thead><tr><th>Employee</th><th>Department</th><th>Time In</th><th>Time Out</th><th class="num">Late (min)</th><th class="num">Hours</th><th>Status</th></tr></thead>
              <tbody>
                @for (r of daily(); track r.id) {
                  <tr>
                    <td><div class="bold">{{ r.name }}</div><div class="muted small">{{ r.employeeCode }}</div></td>
                    <td>{{ r.department }}</td>
                    <td>{{ r.timeIn ? (r.timeIn | date:'h:mm a') : '—' }}</td>
                    <td>{{ r.timeOut ? (r.timeOut | date:'h:mm a') : '—' }}</td>
                    <td class="num">{{ r.lateMins ?? '—' }}</td>
                    <td class="num">{{ r.hours ?? '—' }}</td>
                    <td><span class="badge {{ r.status.toLowerCase().replace(' ', '') }}">{{ r.status }}</span></td>
                  </tr>
                } @empty { <tr><td colspan="7"><div class="empty">No data for this date</div></td></tr> }
              </tbody>
            </table>
          </div>
        </div>
      }

      @if (tab() === 'logs') {
        <div class="card">
          <div class="row mb">
            <input class="ctl" style="max-width:170px" type="date" [(ngModel)]="logFrom" (change)="logPage.set(1); loadLogs()" />
            <span class="muted">to</span>
            <input class="ctl" style="max-width:170px" type="date" [(ngModel)]="logTo" (change)="logPage.set(1); loadLogs()" />
            <div class="spacer"></div>
            <span class="muted small">{{ logTotal() }} day(s)</span>
          </div>
          <div class="table-wrap">
            <table class="data logs-by-day">
              <thead><tr><th>Employee</th><th>Punch</th><th>Date & Time</th><th>Source</th><th>Verify</th><th>Site / Device</th></tr></thead>
              <tbody>
                @for (row of logDays(); track row.date + row.employee?.id) {
                  <tr>
                    <td rowspan="1" style="vertical-align:top">
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
              <button class="btn secondary sm" [disabled]="logPage() <= 1" (click)="logPage.set(logPage() - 1); loadLogs()">← Prev</button>
              <span class="muted small">Page {{ logPage() }} of {{ logTotalPages() }}</span>
              <button class="btn secondary sm" [disabled]="logPage() >= logTotalPages()" (click)="logPage.set(logPage() + 1); loadLogs()">Next →</button>
            </div>
          }
        </div>
      }

      @if (tab() === 'corrections') {
        <div class="card">
          <div class="row mb">
            <div class="spacer"></div>
            <button class="btn" (click)="openCorrection()">＋ Request Correction</button>
          </div>
          <div class="table-wrap">
            <table class="data">
              <thead><tr><th>Employee</th><th>Date</th><th>Issue</th><th>Punch</th><th>Corrected Time</th><th>Reason</th><th>Status</th></tr></thead>
              <tbody>
                @for (c of corrections(); track c.id) {
                  <tr>
                    <td>{{ c.employee.name }}</td>
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
                } @empty { <tr><td colspan="7"><div class="empty">No correction requests</div></td></tr> }
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

      @if (showManual()) {
        <div class="modal-backdrop" (click)="showManual.set(false)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">Manual Attendance Entry</div>
            @if (error()) { <div class="alert error">{{ error() }}</div> }
            <label class="field"><span class="lbl">Employee ID (numeric)</span><input class="ctl" type="number" [(ngModel)]="manual.employeeId" /></label>
            <label class="field"><span class="lbl">Punch Type</span>
              <select class="ctl" [(ngModel)]="manual.punchType">
                <option [ngValue]="1">Time In</option><option [ngValue]="2">Time Out</option>
                <option [ngValue]="3">Break In</option><option [ngValue]="4">Break Out</option>
              </select></label>
            <label class="field"><span class="lbl">Date & Time</span><input class="ctl" type="datetime-local" [(ngModel)]="manual.punchTime" /></label>
            <label class="field"><span class="lbl">Remarks</span><input class="ctl" [(ngModel)]="manual.remarks" /></label>
            <div class="modal-actions">
              <button class="btn secondary" (click)="showManual.set(false)">Cancel</button>
              <button class="btn" (click)="saveManual()">Save Entry</button>
            </div>
          </div>
        </div>
      }

      @if (showCorrection()) {
        <div class="modal-backdrop" (click)="showCorrection.set(false)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">Request Attendance Correction</div>
            @if (error()) { <div class="alert error">{{ error() }}</div> }
            @if (!auth.hasRole('Employee')) {
              <label class="field"><span class="lbl">Employee ID (numeric)</span><input class="ctl" type="number" [(ngModel)]="correction.employeeId" /></label>
            }
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
    </div>
  `
})
export class AttendanceComponent implements OnInit {
  tab = signal('daily');
  date = new Date().toISOString().slice(0, 10);
  logFrom = new Date(Date.now() - 7 * 86400000).toISOString().slice(0, 10);
  logTo = new Date().toISOString().slice(0, 10);
  daily = signal<any[]>([]);
  logDays = signal<any[]>([]);
  logTotal = signal(0);
  logPage = signal(1);
  logPageSize = 25;
  corrections = signal<any[]>([]);
  correctionPage = signal(1);
  correctionTotal = signal(0);
  readonly correctionPageSize = 25;
  showManual = signal(false);
  showCorrection = signal(false);
  error = signal('');
  manual: any = { punchType: 1 };
  correction: any = { punchType: 1, issueType: 'MissingTimeIn' };
  issueTypes = [
    { v: 'MissingTimeIn', n: 'Missing Time In' },
    { v: 'MissingTimeOut', n: 'Missing Time Out' },
    { v: 'IncorrectRecord', n: 'Incorrect Attendance Record' },
    { v: 'ForgottenBiometric', n: 'Forgotten Biometrics Scan' },
    { v: 'DeviceFailure', n: 'Device Failure' },
    { v: 'Other', n: 'Other' }
  ];

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void { this.loadDaily(); }

  loadDaily(): void {
    this.api.get<any[]>('attendance/daily-summary', { date: this.date }).subscribe(r => this.daily.set(r));
  }

  loadLogs(): void {
    this.api.get<{ total: number; items: any[] }>('attendance/logs/by-day', {
      from: this.logFrom, to: this.logTo, page: this.logPage(), pageSize: this.logPageSize
    }).subscribe(r => { this.logDays.set(r.items); this.logTotal.set(r.total); });
  }

  logTotalPages(): number { return Math.max(1, Math.ceil(this.logTotal() / this.logPageSize)); }

  loadCorrections(): void {
    this.api.get<{ total: number; items: any[] }>('attendance/corrections', {
      page: this.correctionPage(), pageSize: this.correctionPageSize
    }).subscribe(r => {
      this.corrections.set(r.items);
      this.correctionTotal.set(r.total);
    });
  }

  correctionTotalPages(): number { return Math.max(1, Math.ceil(this.correctionTotal() / this.correctionPageSize)); }

  countStatus(s: string): number { return this.daily().filter(r => r.status === s).length; }

  saveManual(): void {
    this.error.set('');
    this.api.post('attendance/logs', this.manual).subscribe({
      next: () => { this.showManual.set(false); this.manual = { punchType: 1 }; this.loadDaily(); },
      error: err => this.error.set(err?.error?.message ?? 'Failed to save.')
    });
  }

  openCorrection(): void {
    this.error.set('');
    this.correction = { punchType: 1, issueType: 'MissingTimeIn', employeeId: this.auth.user()?.employeeId, attendanceDate: this.date };
    this.showCorrection.set(true);
  }

  formatIssue(v: string): string {
    return this.issueTypes.find(i => i.v === v)?.n ?? v?.replace(/([A-Z])/g, ' $1').trim() ?? '—';
  }

  saveCorrection(): void {
    if (!this.correction.reason) { this.error.set('Reason is required.'); return; }
    this.api.post('attendance/corrections', this.correction).subscribe({
      next: () => { this.showCorrection.set(false); this.loadCorrections(); this.tab.set('corrections'); },
      error: err => this.error.set(err?.error?.message ?? 'Failed to submit.')
    });
  }
}
