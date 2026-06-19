import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DecimalPipe } from '@angular/common';
import { ApiService, saveBlob } from '../core/api.service';
import { AuthService } from '../core/auth.service';

type ReportCol = { key: string; label: string; money?: boolean };
type ReportDef = { key: string; name: string; icon: string; desc: string; needsRange?: boolean; roles: string[] };

const PAYROLL_ROLES = ['SuperAdministrator', 'HrAdministrator', 'PayrollOfficer'];
const HR_ROLES = ['SuperAdministrator', 'HrAdministrator', 'HrOfficer'];
const MANAGER_ROLES = ['SuperAdministrator', 'HrAdministrator', 'HrOfficer', 'PayrollOfficer', 'DepartmentHead'];

@Component({
  selector: 'app-reports',
  imports: [FormsModule, DecimalPipe],
  styles: [`
    .report-card { display: flex; flex-direction: column; gap: 10px;
      .desc { color: var(--text-soft); font-size: 13px; flex: 1; } }
    .preview-table { overflow: auto; }
    .att-filters, .report-filters { display: flex; flex-wrap: wrap; gap: 10px; margin-bottom: 14px; align-items: flex-end; }
    .att-filters label, .report-filters label { display: flex; flex-direction: column; gap: 4px; font-size: 12px; color: var(--text-soft); }
    .att-filters .ctl, .report-filters .ctl { min-width: 160px; }
    .punch-cell { font-variant-numeric: tabular-nums; white-space: nowrap; }
    .punch-missing { color: var(--text-faint); }
    .report-toolbar { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: center; gap: 10px; margin-bottom: 12px; }
    .pagination { display: flex; align-items: center; gap: 10px; font-size: 13px; color: var(--text-soft); }
    .num { text-align: right; font-variant-numeric: tabular-nums; }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Reports & Analytics</h1><div class="sub">Enterprise reports — view on screen, export CSV/Excel, or print to PDF</div></div>
      </div>

      @if (reports.length === 0) {
        <div class="card"><div class="empty">No reports are available for your role.</div></div>
      } @else {
      <div class="grid grid-3 mb">
        @for (r of reports; track r.key) {
          <div class="card report-card">
            <div class="card-title">{{ r.icon }} {{ r.name }}</div>
            <div class="desc">{{ r.desc }}</div>
            @if (r.needsRange) {
              <div class="row">
                <input class="ctl" type="date" [(ngModel)]="from" style="max-width:150px" />
                <input class="ctl" type="date" [(ngModel)]="to" style="max-width:150px" />
              </div>
            }
            @if (r.key === 'attendance') {
              <label class="field" style="margin:0">
                <span class="lbl">Department</span>
                <select class="ctl" [(ngModel)]="departmentId">
                  <option value="">All departments</option>
                  @for (d of departments(); track d.id) { <option [value]="d.id">{{ d.name }}</option> }
                </select>
              </label>
              <label class="field" style="margin:0">
                <span class="lbl">Employee name / code</span>
                <input class="ctl" [(ngModel)]="employeeName" placeholder="Search employee…" />
              </label>
            }
            @if (r.key === 'payroll') {
              <select class="ctl" [(ngModel)]="cutoffId" [disabled]="cutoffsLoading()">
                <option value="">Select cutoff…</option>
                @for (c of cutoffs(); track c.id) {
                  <option [value]="c.id">{{ c.name }}@if (c.status) { ({{ c.status }}) }</option>
                }
              </select>
              @if (cutoffsError()) {
                <div class="alert error sm">{{ cutoffsError() }}</div>
              } @else if (!cutoffsLoading() && cutoffs().length === 0) {
                <div class="muted small">No payroll cutoffs found — process payroll first.</div>
              }
            }
            @if (r.key === 'government') {
              <div class="row">
                <input class="ctl" type="number" [(ngModel)]="year" style="max-width:110px" />
                <select class="ctl" [(ngModel)]="month" style="max-width:140px">
                  @for (m of months; track m.v) { <option [ngValue]="m.v">{{ m.n }}</option> }
                </select>
              </div>
            }
            <div class="row">
              <button class="btn secondary sm" (click)="view(r.key)">👁 View</button>
              <button class="btn sm" (click)="exportCsv(r.key)">⬇ CSV / Excel</button>
            </div>
          </div>
        }
      </div>
      }

      @if (reportLoaded()) {
        <div class="card printable">
          <div class="report-toolbar">
            <div class="card-title" style="margin:0">{{ activeReport }} ({{ reportTotal() }} rows)</div>
            <button class="btn secondary sm" onclick="window.print()">🖨 Print / PDF</button>
          </div>

          @if (reportError()) {
            <div class="alert error">{{ reportError() }}</div>
          }

          @if (activeKey() === 'attendance') {
            <div class="att-filters">
              <label>Department
                <select class="ctl" [(ngModel)]="departmentId" (change)="reportPage.set(1); loadReport()">
                  <option value="">All departments</option>
                  @for (d of departments(); track d.id) { <option [value]="d.id">{{ d.name }}</option> }
                </select>
              </label>
              <label>Employee name / code
                <input class="ctl" [(ngModel)]="employeeName" placeholder="Search employee…" (keyup.enter)="reportPage.set(1); loadReport()" />
              </label>
              <button class="btn secondary sm" (click)="reportPage.set(1); loadReport()">Apply filters</button>
            </div>
            <div class="preview-table table-wrap">
              <table class="data">
                <thead>
                  <tr>
                    <th>Employee</th><th>Department</th><th>Date</th>
                    <th>AM In</th><th>Lunch Out</th><th>PM In</th><th>PM Out</th>
                    <th>Site</th><th>Source</th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of reportRows(); track row.employeeCode + row.date) {
                    <tr>
                      <td><div class="bold">{{ row.name }}</div><div class="muted small">{{ row.employeeCode }}</div></td>
                      <td>{{ row.department }}</td>
                      <td class="punch-cell">{{ row.date }}</td>
                      <td class="punch-cell" [class.punch-missing]="!row.morningIn">{{ row.morningIn ?? '—' }}</td>
                      <td class="punch-cell" [class.punch-missing]="!row.lunchOut">{{ row.lunchOut ?? '—' }}</td>
                      <td class="punch-cell" [class.punch-missing]="!row.afternoonIn">{{ row.afternoonIn ?? '—' }}</td>
                      <td class="punch-cell" [class.punch-missing]="!row.endOut">{{ row.endOut ?? '—' }}</td>
                      <td class="muted small">{{ row.site ?? '—' }}</td>
                      <td><span class="badge muted">{{ row.source ?? '—' }}</span></td>
                    </tr>
                  } @empty {
                    <tr><td colspan="9"><div class="empty">No attendance records for the selected filters</div></td></tr>
                  }
                </tbody>
              </table>
            </div>
          } @else {
            <div class="preview-table table-wrap">
              <table class="data">
                <thead>
                  <tr>@for (c of activeColumns(); track c.key) { <th [class.num]="c.money">{{ c.label }}</th> }</tr>
                </thead>
                <tbody>
                  @for (row of reportRows(); track $index) {
                    <tr>
                      @for (c of activeColumns(); track c.key) {
                        <td [class.num]="c.money">
                          @if (c.money && row[c.key] != null) { ₱{{ row[c.key] | number:'1.2-2' }} }
                          @else { {{ row[c.key] ?? '—' }} }
                        </td>
                      }
                    </tr>
                  } @empty {
                    <tr><td [attr.colspan]="activeColumns().length || 1"><div class="empty">No records for the selected filters</div></td></tr>
                  }
                </tbody>
              </table>
            </div>
          }

          @if (reportTotal() > pageSize) {
            <div class="pagination mt">
              <button class="btn secondary sm" [disabled]="reportPage() <= 1" (click)="reportPage.set(reportPage() - 1); loadReport()">Previous</button>
              <span>Page {{ reportPage() }} of {{ totalPages() }} · {{ pageSize }} rows/page</span>
              <button class="btn secondary sm" [disabled]="reportPage() >= totalPages()" (click)="reportPage.set(reportPage() + 1); loadReport()">Next</button>
            </div>
          }
        </div>
      }
    </div>
  `
})
export class ReportsComponent implements OnInit {
  reportRows = signal<any[]>([]);
  reportTotal = signal(0);
  reportPage = signal(1);
  reportLoaded = signal(false);
  reportError = signal('');
  activeColumns = signal<ReportCol[]>([]);
  cutoffs = signal<any[]>([]);
  cutoffsLoading = signal(true);
  cutoffsError = signal('');
  departments = signal<any[]>([]);
  readonly pageSize = 25;
  activeReport = '';
  activeKey = signal('');
  from = (() => { const d = new Date(); return new Date(d.getFullYear(), d.getMonth(), 1).toISOString().slice(0, 10); })();
  to = (() => { const d = new Date(); return new Date(d.getFullYear(), d.getMonth() + 1, 0).toISOString().slice(0, 10); })();
  cutoffId = '';
  departmentId = '';
  employeeName = '';
  year = new Date().getFullYear();
  month = new Date().getMonth() + 1;
  months = Array.from({ length: 12 }, (_, i) => ({ v: i + 1, n: new Date(2000, i, 1).toLocaleString('en-US', { month: 'long' }) }));

  private readonly columnMap: Record<string, ReportCol[]> = {
    payroll: [
      { key: 'employeeCode', label: 'Code' }, { key: 'name', label: 'Employee' }, { key: 'department', label: 'Department' },
      { key: 'basicPay', label: 'Basic', money: true }, { key: 'overtimePay', label: 'OT', money: true },
      { key: 'allowances', label: 'Allowances', money: true }, { key: 'grossPay', label: 'Gross', money: true },
      { key: 'sss', label: 'SSS', money: true }, { key: 'philHealth', label: 'PhilHealth', money: true },
      { key: 'pagIbig', label: 'Pag-IBIG', money: true }, { key: 'tax', label: 'Tax', money: true },
      { key: 'loans', label: 'Loans', money: true }, { key: 'netPay', label: 'Net Pay', money: true }
    ],
    leave: [
      { key: 'employeeCode', label: 'Code' }, { key: 'name', label: 'Employee' }, { key: 'department', label: 'Department' },
      { key: 'leaveType', label: 'Leave Type' }, { key: 'startDate', label: 'Start' }, { key: 'endDate', label: 'End' },
      { key: 'days', label: 'Days' }, { key: 'status', label: 'Status' }, { key: 'reason', label: 'Reason' }
    ],
    overtime: [
      { key: 'employeeCode', label: 'Code' }, { key: 'name', label: 'Employee' }, { key: 'department', label: 'Department' },
      { key: 'date', label: 'Date' }, { key: 'hours', label: 'Hours' }, { key: 'pay', label: 'Pay', money: true },
      { key: 'status', label: 'Status' }, { key: 'reason', label: 'Reason' }
    ],
    employees: [
      { key: 'employeeCode', label: 'Code' }, { key: 'name', label: 'Name' }, { key: 'department', label: 'Department' },
      { key: 'position', label: 'Position' }, { key: 'branch', label: 'Branch' }, { key: 'status', label: 'Status' },
      { key: 'hireDate', label: 'Hire Date' }, { key: 'monthlySalary', label: 'Salary', money: true }
    ],
    government: [
      { key: 'employeeCode', label: 'Code' }, { key: 'name', label: 'Name' },
      { key: 'sssEe', label: 'SSS EE', money: true }, { key: 'sssEr', label: 'SSS ER', money: true },
      { key: 'philHealth', label: 'PhilHealth', money: true }, { key: 'pagIbig', label: 'Pag-IBIG', money: true },
      { key: 'tax', label: 'Tax', money: true }
    ],
    branches: [
      { key: 'branch', label: 'Branch' }, { key: 'sites', label: 'Sites' }, { key: 'employees', label: 'Employees' },
      { key: 'devices', label: 'Devices' }, { key: 'active', label: 'Active' }
    ]
  };

  private readonly allReports: ReportDef[] = [
    { key: 'attendance', name: 'Attendance Report', icon: '🕐', desc: 'Daily attendance by employee with four punch slots — filter by department or name.', needsRange: true, roles: MANAGER_ROLES },
    { key: 'payroll', name: 'Payroll Register', icon: '₱', desc: 'Full payroll register for a cutoff: earnings, statutory deductions, net pay.', needsRange: false, roles: PAYROLL_ROLES },
    { key: 'leave', name: 'Leave Report', icon: '🌴', desc: 'Leave applications with type, dates, days and approval status.', needsRange: true, roles: MANAGER_ROLES },
    { key: 'overtime', name: 'Overtime Report', icon: '⏱', desc: 'Overtime requests with hours, computed pay and status.', needsRange: true, roles: MANAGER_ROLES },
    { key: 'employees', name: 'Employee Masterlist', icon: '👥', desc: 'Complete employee directory with government numbers and salary.', needsRange: false, roles: MANAGER_ROLES },
    { key: 'government', name: 'Government Remittance', icon: '🏛', desc: 'Monthly SSS, PhilHealth, Pag-IBIG and withholding tax remittance summary.', needsRange: false, roles: PAYROLL_ROLES },
    { key: 'branches', name: 'Branch Report', icon: '🏢', desc: 'Headcount, sites and devices per branch.', needsRange: false, roles: HR_ROLES }
  ];

  reports: ReportDef[] = [];

  constructor(private api: ApiService, private auth: AuthService) {}

  ngOnInit(): void {
    this.reports = this.allReports.filter(r => this.auth.hasRole(...r.roles));

    if (this.auth.hasRole(...PAYROLL_ROLES)) {
      this.api.get<{ items: any[] }>('payroll/cutoffs', { page: 1, pageSize: 100 }).subscribe({
        next: r => {
          this.cutoffs.set(Array.isArray(r.items) ? r.items : []);
          this.cutoffsError.set('');
          this.cutoffsLoading.set(false);
        },
        error: err => {
          this.cutoffs.set([]);
          this.cutoffsError.set(err?.error?.message ?? 'Unable to load payroll cutoffs.');
          this.cutoffsLoading.set(false);
        }
      });
    } else {
      this.cutoffsLoading.set(false);
    }

    if (this.auth.hasRole(...MANAGER_ROLES)) {
      this.api.get<any[]>('organization/departments').subscribe({ next: r => this.departments.set(r), error: () => {} });
    }
  }

  totalPages(): number {
    return Math.max(1, Math.ceil(this.reportTotal() / this.pageSize));
  }

  private params(key: string): Record<string, unknown> {
    const paged = ['attendance', 'payroll', 'leave', 'overtime', 'government'].includes(key);
    const p: Record<string, unknown> = paged ? { page: this.reportPage(), pageSize: this.pageSize } : {};
    if (key === 'payroll') p['cutoffId'] = this.cutoffId;
    if (key === 'government') { p['year'] = this.year; p['month'] = this.month; }
    if (key === 'attendance' || key === 'leave' || key === 'overtime') {
      p['from'] = this.from;
      p['to'] = this.to;
    }
    if (key === 'attendance') {
      if (this.departmentId) p['departmentId'] = this.departmentId;
      if (this.employeeName.trim()) p['employeeName'] = this.employeeName.trim();
    }
    return p;
  }

  view(key: string): void {
    if (!this.reports.some(r => r.key === key)) {
      this.reportError.set('You do not have access to this report.');
      return;
    }
    this.activeKey.set(key);
    this.activeReport = this.reports.find(r => r.key === key)?.name ?? key;
    this.reportPage.set(1);
    this.reportError.set('');
    this.activeColumns.set(this.columnMap[key] ?? []);
    this.loadReport();
  }

  loadReport(): void {
    const key = this.activeKey();
    if (key === 'payroll' && !this.cutoffId) {
      this.reportRows.set([]);
      this.reportTotal.set(0);
      this.reportLoaded.set(true);
      this.reportError.set('Select a payroll cutoff before viewing the register.');
      return;
    }

    this.api.get<any>(`reports/${key}`, this.params(key)).subscribe({
      next: res => {
        if (Array.isArray(res)) {
          this.reportRows.set(res);
          this.reportTotal.set(res.length);
        } else {
          this.reportRows.set(res.items ?? []);
          this.reportTotal.set(res.total ?? 0);
        }
        this.reportLoaded.set(true);
        this.reportError.set('');
      },
      error: err => {
        this.reportRows.set([]);
        this.reportTotal.set(0);
        this.reportLoaded.set(true);
        this.reportError.set(err?.error?.message ?? 'Unable to load report. Check your filters and try again.');
      }
    });
  }

  exportCsv(key: string): void {
    if (!this.reports.some(r => r.key === key)) {
      alert('You do not have access to this report.');
      return;
    }
    if (key === 'payroll' && !this.cutoffId) {
      alert('Select a payroll cutoff first.');
      return;
    }
    const p = { ...this.params(key) };
    delete p['page'];
    delete p['pageSize'];
    this.api.download(`reports/${key}`, p).subscribe(r => saveBlob(r, `${key}.csv`));
  }
}
