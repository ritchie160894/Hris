import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, parsePagedResponse } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-leave',
  imports: [FormsModule],
  styles: [`
    .cal { display: grid; grid-template-columns: repeat(7, 1fr); gap: 4px;
      .dow { text-align: center; font-size: 11px; font-weight: 700; color: var(--text-faint); padding: 4px; text-transform: uppercase; }
      .day { min-height: 76px; border: 1px solid var(--border); border-radius: 8px; padding: 5px; font-size: 11.5px;
        .n { font-weight: 700; color: var(--text-soft); }
        &.empty-day { background: var(--bg); border-style: dashed; }
        .ev { background: var(--primary-soft); color: var(--primary-dark); border-radius: 4px; padding: 1px 5px; margin-top: 2px;
          overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .hol { background: var(--danger-soft); color: var(--danger); border-radius: 4px; padding: 1px 5px; margin-top: 2px; font-weight: 600; }
      }
    }
    .pagination { display: flex; align-items: center; gap: 10px; padding: 12px 0 0; font-size: 13px; color: var(--text-soft); flex-wrap: wrap; }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Leave Management</h1><div class="sub">Emergency Leave (10 days/year) and Service Incentive Leave — SIL (5 days/year, Regular employees)</div></div>
        <button class="btn" (click)="openNew()">＋ Apply for Leave</button>
      </div>

      <div class="tabs">
        <button [class.active]="tab() === 'requests'" (click)="tab.set('requests')">Requests</button>
        <button [class.active]="tab() === 'balances'" (click)="tab.set('balances'); loadBalances()">Credits & Balances</button>
        <button [class.active]="tab() === 'calendar'" (click)="tab.set('calendar'); loadCalendar()">Calendar</button>
      </div>

      @if (message()) { <div class="alert success">{{ message() }}</div> }

      @if (tab() === 'requests') {
        <div class="card">
          <div class="row mb">
            <select class="ctl" style="max-width:200px" [(ngModel)]="statusFilter" (change)="requestPage.set(1); loadRequests()">
              <option value="">All statuses</option>
              @for (s of ['Pending','InProgress','Approved','Rejected','ReturnedForRevision','Cancelled']; track s) { <option [value]="s">{{ s }}</option> }
            </select>
          </div>
          <div class="table-wrap">
            <table class="data">
              <thead><tr><th>Employee</th><th>Type</th><th>Dates</th><th class="num">Days</th><th>Reason</th><th>Status</th><th></th></tr></thead>
              <tbody>
                @for (l of requests(); track l.id) {
                  <tr>
                    <td><div class="bold">{{ l.employee.name }}</div><div class="muted small">{{ l.employee.department }}</div></td>
                    <td>
                      <span class="badge {{ l.leaveType.isSil ? 'info' : '' }}">{{ l.leaveType.code }}</span>
                      @if (l.isUndertime) { <div class="muted small">Undertime</div> }
                      @else if (l.isHalfDay) { <div class="muted small">Half day</div> }
                      @else { <div class="muted small">Full day</div> }
                    </td>
                    <td>{{ l.startDate }} → {{ l.endDate }}
                      @if (l.isUndertime) { <div class="muted small">{{ l.undertimeHours }} hr/s</div> }
                    </td>
                    <td class="num">{{ l.days }}</td>
                    <td class="muted" style="max-width:220px">{{ l.reason }}</td>
                    <td><span class="badge {{ l.status.toLowerCase() }}">{{ l.status }}</span>
                        @if (l.status === 'InProgress') { <div class="muted small">at level {{ l.currentApprovalLevel }}</div> }</td>
                    <td>@if (canCancel(l)) { <button class="btn ghost sm" (click)="cancel(l)">Cancel</button> }</td>
                  </tr>
                } @empty { <tr><td colspan="7"><div class="empty">No leave requests</div></td></tr> }
              </tbody>
            </table>
          </div>
          @if (requestTotal() > 0) {
            <div class="pagination">
              <span>{{ requestTotal() }} record(s)@if (requestTotal() > requestPageSize) { · Page {{ requestPage() }} of {{ requestTotalPages() }} · {{ requestPageSize }} rows/page }</span>
              @if (requestTotal() > requestPageSize) {
                <button class="btn secondary sm" [disabled]="requestPage() <= 1" (click)="requestPage.set(requestPage() - 1); loadRequests()">Previous</button>
                <button class="btn secondary sm" [disabled]="requestPage() >= requestTotalPages()" (click)="requestPage.set(requestPage() + 1); loadRequests()">Next</button>
              }
            </div>
          }
        </div>
      }

      @if (tab() === 'balances') {
        <div class="card">
          <div class="table-wrap">
            <table class="data">
              <thead><tr><th>Employee</th><th>Leave Type</th><th class="num">Credits</th><th class="num">Used</th><th class="num">Remaining</th></tr></thead>
              <tbody>
                @for (b of balances(); track b.id) {
                  <tr>
                    <td>{{ b.employee.name }}</td>
                    <td>{{ b.leaveType.name }}</td>
                    <td class="num">{{ b.credits }}</td>
                    <td class="num">{{ b.used }}</td>
                    <td class="num bold" [style.color]="b.remaining <= 0 ? 'var(--danger)' : 'var(--success)'">{{ b.remaining }}</td>
                  </tr>
                } @empty { <tr><td colspan="5"><div class="empty">No balances</div></td></tr> }
              </tbody>
            </table>
          </div>
          @if (balanceTotal() > balancePageSize) {
            <div class="pagination">
              <span>Page {{ balancePage() }} of {{ balanceTotalPages() }} · {{ balanceTotal() }} record(s) · {{ balancePageSize }} rows/page</span>
              <button class="btn secondary sm" [disabled]="balancePage() <= 1" (click)="balancePage.set(balancePage() - 1); loadBalances()">Previous</button>
              <button class="btn secondary sm" [disabled]="balancePage() >= balanceTotalPages()" (click)="balancePage.set(balancePage() + 1); loadBalances()">Next</button>
            </div>
          }
        </div>
      }

      @if (tab() === 'calendar') {
        <div class="card">
          <div class="row mb">
            <button class="btn secondary sm" (click)="moveMonth(-1)">‹</button>
            <b>{{ monthLabel() }}</b>
            <button class="btn secondary sm" (click)="moveMonth(1)">›</button>
          </div>
          <div class="cal">
            @for (d of ['Sun','Mon','Tue','Wed','Thu','Fri','Sat']; track d) { <div class="dow">{{ d }}</div> }
            @for (cell of calendarCells(); track $index) {
              <div class="day" [class.empty-day]="!cell">
                @if (cell) {
                  <div class="n">{{ cell.day }}</div>
                  @for (h of cell.holidays; track h.name) { <div class="hol">🎌 {{ h.name }}</div> }
                  @for (e of cell.events; track e.id) { <div class="ev">{{ e.leaveType }} · {{ e.employee }}</div> }
                }
              </div>
            }
          </div>
        </div>
      }

      @if (showNew()) {
        <div class="modal-backdrop" (click)="showNew.set(false)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">Apply for Leave</div>
            @if (error()) { <div class="alert error">{{ error() }}</div> }
            @if (!auth.hasRole('Employee')) {
              <label class="field"><span class="lbl">Employee ID (numeric; leave blank for self)</span><input class="ctl" type="number" [(ngModel)]="form.employeeId" /></label>
            }
            <label class="field"><span class="lbl">Leave Credit *</span>
              <select class="ctl" [(ngModel)]="form.leaveCategory" (ngModelChange)="onCategoryChange()">
                <option value="EL">Emergency Leave (EL) — 10 days/year</option>
                <option value="SIL">Service Incentive Leave (SIL) — 5 days/year, Regular only</option>
              </select>
            </label>
            <label class="field"><span class="lbl">Request Type *</span>
              <select class="ctl" [(ngModel)]="form.duration" (ngModelChange)="onDurationChange()">
                <option value="full">Leave — Full day(s)</option>
                <option value="halfDay">Leave — Half day</option>
                <option value="undertime">Undertime — Early departure</option>
              </select>
            </label>
            <div class="form-grid">
              <label class="field"><span class="lbl">Start Date *</span><input class="ctl" type="date" [(ngModel)]="form.startDate" (ngModelChange)="syncEndDate()" /></label>
              <label class="field"><span class="lbl">End Date *</span><input class="ctl" type="date" [(ngModel)]="form.endDate" [disabled]="form.duration !== 'full'" /></label>
            </div>
            @if (form.duration === 'undertime') {
              <label class="field"><span class="lbl">Undertime Hours *</span>
                <input class="ctl" type="number" min="0.25" step="0.25" [(ngModel)]="form.undertimeHours" />
                <div class="muted small">Charged as day-fraction (hours ÷ shift length).</div>
              </label>
            }
            <label class="field"><span class="lbl">Reason *</span><textarea class="ctl" [(ngModel)]="form.reason"></textarea></label>
            <div class="muted small mb">{{ workflowHint() }}</div>
            <div class="modal-actions">
              <button class="btn secondary" (click)="showNew.set(false)">Cancel</button>
              <button class="btn" [disabled]="busy()" (click)="submit()">Submit Application</button>
            </div>
          </div>
        </div>
      }
    </div>
  `
})
export class LeaveComponent implements OnInit {
  tab = signal('requests');
  requests = signal<any[]>([]);
  requestPage = signal(1);
  requestTotal = signal(0);
  readonly requestPageSize = 25;
  balances = signal<any[]>([]);
  balancePage = signal(1);
  balanceTotal = signal(0);
  readonly balancePageSize = 25;
  types = signal<any[]>([]);
  calendar = signal<{ leaves: any[]; holidays: any[] }>({ leaves: [], holidays: [] });
  showNew = signal(false);
  busy = signal(false);
  error = signal('');
  message = signal('');
  statusFilter = '';
  calYear = new Date().getFullYear();
  calMonth = new Date().getMonth() + 1;
  form: any = {};

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void {
    this.loadRequests();
    this.api.get<any[]>('leave/types').subscribe(r => this.types.set(r));
  }

  loadRequests(): void {
    this.api.get<{ total: number; items: any[] }>('leave/requests', {
      status: this.statusFilter, page: this.requestPage(), pageSize: this.requestPageSize
    }).subscribe(r => {
      this.requests.set(r.items);
      this.requestTotal.set(r.total);
    });
  }

  requestTotalPages(): number { return Math.max(1, Math.ceil(this.requestTotal() / this.requestPageSize)); }

  loadBalances(): void {
    this.api.get<{ total: number; items: any[] } | any[]>('leave/balances', {
      page: this.balancePage(), pageSize: this.balancePageSize
    }).subscribe(r => {
      const res = parsePagedResponse(r);
      this.balances.set(res.items);
      this.balanceTotal.set(res.total);
    });
  }

  balanceTotalPages(): number { return Math.max(1, Math.ceil(this.balanceTotal() / this.balancePageSize)); }

  loadCalendar(): void {
    this.api.get<{ leaves: any[]; holidays: any[] }>('leave/calendar', { year: this.calYear, month: this.calMonth })
      .subscribe(r => this.calendar.set(r));
  }

  moveMonth(delta: number): void {
    this.calMonth += delta;
    if (this.calMonth > 12) { this.calMonth = 1; this.calYear++; }
    if (this.calMonth < 1) { this.calMonth = 12; this.calYear--; }
    this.loadCalendar();
  }

  monthLabel(): string {
    return new Date(this.calYear, this.calMonth - 1, 1).toLocaleDateString('en-US', { month: 'long', year: 'numeric' });
  }

  calendarCells(): any[] {
    const firstDow = new Date(this.calYear, this.calMonth - 1, 1).getDay();
    const daysInMonth = new Date(this.calYear, this.calMonth, 0).getDate();
    const cells: any[] = Array(firstDow).fill(null);
    for (let d = 1; d <= daysInMonth; d++) {
      const dateStr = `${this.calYear}-${String(this.calMonth).padStart(2, '0')}-${String(d).padStart(2, '0')}`;
      cells.push({
        day: d,
        holidays: this.calendar().holidays.filter(h => h.date === dateStr),
        events: this.calendar().leaves.filter(l => l.startDate <= dateStr && l.endDate >= dateStr)
      });
    }
    return cells;
  }

  openNew(): void {
    this.error.set('');
    const today = new Date().toISOString().slice(0, 10);
    this.form = {
      leaveCategory: 'EL',
      employeeId: this.auth.user()?.employeeId,
      startDate: today,
      endDate: today,
      duration: 'full',
      undertimeHours: 1
    };
    this.applyLeaveTypeId();
    this.showNew.set(true);
  }

  elTypeId(): number | undefined {
    return this.types().find(t => t.code === 'EL')?.id;
  }

  silTypeId(): number | undefined {
    return this.types().find(t => t.code === 'SIL')?.id;
  }

  applyLeaveTypeId(): void {
    const id = this.form.leaveCategory === 'SIL' ? this.silTypeId() : this.elTypeId();
    if (id) this.form.leaveTypeId = id;
  }

  onCategoryChange(): void {
    this.applyLeaveTypeId();
  }

  workflowHint(): string {
    const el = this.form.leaveCategory === 'EL';
    const ut = this.form.duration === 'undertime';
    const half = this.form.duration === 'halfDay';
    if (el && ut) return 'EL undertime: Dept Head → HR → VP → CEO. EL credits deducted on approval; payroll deducts pay by undertime hours.';
    if (!el && ut) return 'SIL undertime: VP & HR Head → CEO (Regular only). SIL credits deducted on approval; payroll restores pay for covered hours.';
    if (el && half) return 'EL half day: Dept Head → HR → VP → CEO.';
    if (!el && half) return 'SIL half day: VP & HR Head → CEO (Regular only).';
    if (el) return 'EL leave: Dept Head → HR → VP → CEO.';
    return 'SIL leave: VP & HR Head → CEO (Regular employees only).';
  }

  onDurationChange(): void {
    if (this.form.duration === 'full') {
      this.form.isHalfDay = false;
      this.form.isUndertime = false;
    } else if (this.form.duration === 'halfDay') {
      this.form.isHalfDay = true;
      this.form.isUndertime = false;
      this.syncEndDate();
    } else if (this.form.duration === 'undertime') {
      this.form.isHalfDay = false;
      this.form.isUndertime = true;
      this.syncEndDate();
    }
  }

  syncEndDate(): void {
    if (this.form.duration !== 'full' && this.form.startDate)
      this.form.endDate = this.form.startDate;
  }

  submit(): void {
    if (!this.form.startDate || !this.form.endDate || !this.form.reason) {
      this.error.set('Dates and reason are required.');
      return;
    }
    if (this.form.duration === 'undertime' && (!this.form.undertimeHours || this.form.undertimeHours <= 0)) {
      this.error.set('Undertime hours are required.');
      return;
    }
    this.busy.set(true);
    this.applyLeaveTypeId();
    const payload = {
      ...this.form,
      isHalfDay: this.form.duration === 'halfDay',
      isUndertime: this.form.duration === 'undertime',
      undertimeHours: this.form.duration === 'undertime' ? Number(this.form.undertimeHours) : 0
    };
    delete payload.duration;
    delete payload.leaveCategory;
    this.api.post('leave/requests', payload).subscribe({
      next: () => {
        this.busy.set(false);
        this.showNew.set(false);
        this.message.set('Leave application submitted — it is now in the approval workflow.');
        this.loadRequests();
        setTimeout(() => this.message.set(''), 4000);
      },
      error: err => { this.busy.set(false); this.error.set(err?.error?.message ?? 'Submission failed.'); }
    });
  }

  canCancel(l: any): boolean {
    return ['Pending', 'InProgress'].includes(l.status);
  }

  cancel(l: any): void {
    this.api.post(`leave/requests/${l.id}/cancel`, {}).subscribe(() => this.loadRequests());
  }
}
