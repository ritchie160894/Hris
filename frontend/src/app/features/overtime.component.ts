import { Component, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-overtime',
  imports: [FormsModule, DecimalPipe],
  styles: [`
    .pagination { display: flex; align-items: center; gap: 10px; padding: 12px 0 0; font-size: 13px; color: var(--text-soft); flex-wrap: wrap; }
    .tabs { display: flex; gap: 4px; border-bottom: 2px solid var(--border);
      button { background: none; border: none; padding: 10px 16px; cursor: pointer; font-weight: 600; color: var(--text-soft);
        &.active { color: var(--primary); border-bottom: 2px solid var(--primary); margin-bottom: -2px; } } }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Overtime</h1><div class="sub">Requests, approvals and computed pay (125% hourly premium)</div></div>
        <button class="btn" (click)="openNew()">＋ File Overtime</button>
      </div>

      @if (message()) { <div class="alert success">{{ message() }}</div> }

      <div class="tabs mb">
        <button [class.active]="tab() === 'requests'" (click)="tab.set('requests'); load()">OT Requests</button>
        <button [class.active]="tab() === 'corrections'" (click)="tab.set('corrections'); loadCorrections()">OT Corrections</button>
      </div>

      @if (tab() === 'requests') {
      <div class="card">
        <div class="row mb">
          <select class="ctl" style="max-width:200px" [(ngModel)]="statusFilter" (change)="page.set(1); load()">
            <option value="">All statuses</option>
            @for (s of ['Pending','InProgress','Approved','Rejected','Cancelled']; track s) { <option [value]="s">{{ s }}</option> }
          </select>
        </div>
        <div class="table-wrap">
          <table class="data">
            <thead><tr><th>Employee</th><th>Date</th><th>Time</th><th class="num">Hours</th><th class="num">Computed Pay</th><th>Reason</th><th>Status</th><th></th></tr></thead>
            <tbody>
              @for (o of items(); track o.id) {
                <tr>
                  <td><div class="bold">{{ o.employee.name }}</div><div class="muted small">{{ o.employee.department }}</div></td>
                  <td>{{ o.overtimeDate }}</td>
                  <td>{{ o.startTime }} – {{ o.endTime }}</td>
                  <td class="num">{{ o.hours }}</td>
                  <td class="num">@if (o.computedPay) { ₱{{ o.computedPay | number:'1.2-2' }} } @else { — }</td>
                  <td class="muted" style="max-width:200px">{{ o.reason }}</td>
                  <td><span class="badge {{ o.status.toLowerCase() }}">{{ o.status }}</span>
                      @if (o.status === 'InProgress') { <div class="muted small">at level {{ o.currentApprovalLevel }}</div> }</td>
                  <td>@if (['Pending','InProgress'].includes(o.status)) { <button class="btn ghost sm" (click)="cancel(o)">Cancel</button> }</td>
                </tr>
              } @empty { <tr><td colspan="8"><div class="empty">No overtime requests</div></td></tr> }
            </tbody>
          </table>
        </div>
        @if (total() > pageSize) {
          <div class="pagination">
            <span>Page {{ page() }} of {{ totalPages() }} · {{ total() }} record(s) · {{ pageSize }} rows/page</span>
            <button class="btn secondary sm" [disabled]="page() <= 1" (click)="page.set(page() - 1); load()">Previous</button>
            <button class="btn secondary sm" [disabled]="page() >= totalPages()" (click)="page.set(page() + 1); load()">Next</button>
          </div>
        }
      </div>
      }

      @if (tab() === 'corrections') {
        <div class="card">
          <div class="row mb">
            <div class="spacer"></div>
            <button class="btn" (click)="openOtCorrection()">＋ Request OT Correction</button>
          </div>
          <div class="table-wrap">
            <table class="data">
              <thead><tr><th>Employee</th><th>Date</th><th>Time</th><th class="num">Hours</th><th>Issue</th><th>Reason</th><th>Status</th></tr></thead>
              <tbody>
                @for (c of corrections(); track c.id) {
                  <tr>
                    <td><div class="bold">{{ c.employee.name }}</div><div class="muted small">{{ c.employee.department }}</div></td>
                    <td>{{ c.overtimeDate }}</td>
                    <td>{{ c.startTime }} – {{ c.endTime }}</td>
                    <td class="num">{{ c.hours }}</td>
                    <td class="muted small">{{ formatOtIssue(c.issueType) }}</td>
                    <td class="muted">{{ c.reason }}</td>
                    <td>
                      <span class="badge {{ c.status.toLowerCase() }}">{{ c.status }}</span>
                      @if (c.payrollAppliedAt) { <div class="muted small">Applied to payroll</div> }
                    </td>
                  </tr>
                } @empty { <tr><td colspan="7"><div class="empty">No OT correction requests</div></td></tr> }
              </tbody>
            </table>
          </div>
          @if (corrTotal() > pageSize) {
            <div class="pagination">
              <span>Page {{ corrPage() }} of {{ corrTotalPages() }} · {{ corrTotal() }} record(s) · {{ pageSize }} rows/page</span>
              <button class="btn secondary sm" [disabled]="corrPage() <= 1" (click)="corrPage.set(corrPage() - 1); loadCorrections()">Previous</button>
              <button class="btn secondary sm" [disabled]="corrPage() >= corrTotalPages()" (click)="corrPage.set(corrPage() + 1); loadCorrections()">Next</button>
            </div>
          }
        </div>
      }

      @if (showOtCorrection()) {
        <div class="modal-backdrop" (click)="showOtCorrection.set(false)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">Request Overtime Correction</div>
            @if (error()) { <div class="alert error">{{ error() }}</div> }
            @if (!auth.hasRole('Employee')) {
              <label class="field"><span class="lbl">Employee ID</span><input class="ctl" type="number" [(ngModel)]="corrForm.employeeId" /></label>
            }
            <label class="field"><span class="lbl">Issue Type *</span>
              <select class="ctl" [(ngModel)]="corrForm.issueType">
                @for (i of otIssueTypes; track i.v) { <option [ngValue]="i.v">{{ i.n }}</option> }
              </select>
            </label>
            <label class="field"><span class="lbl">Overtime Date *</span><input class="ctl" type="date" [(ngModel)]="corrForm.overtimeDate" /></label>
            <div class="form-grid">
              <label class="field"><span class="lbl">Start *</span><input class="ctl" type="time" [(ngModel)]="corrForm.startTime" /></label>
              <label class="field"><span class="lbl">End *</span><input class="ctl" type="time" [(ngModel)]="corrForm.endTime" /></label>
            </div>
            <label class="field"><span class="lbl">Reason *</span><textarea class="ctl" [(ngModel)]="corrForm.reason"></textarea></label>
            <label class="field"><span class="lbl">Supporting Document (optional)</span><input class="ctl" [(ngModel)]="corrForm.supportingDocument" /></label>
            <div class="muted small mb">Approval: Department Head → HR Officer → Payroll Officer (apply to payroll).</div>
            <div class="modal-actions">
              <button class="btn secondary" (click)="showOtCorrection.set(false)">Cancel</button>
              <button class="btn" [disabled]="busy()" (click)="submitOtCorrection()">Submit</button>
            </div>
          </div>
        </div>
      }

      @if (showNew()) {
        <div class="modal-backdrop" (click)="showNew.set(false)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">File Overtime Request</div>
            @if (error()) { <div class="alert error">{{ error() }}</div> }
            @if (!auth.hasRole('Employee')) {
              <label class="field"><span class="lbl">Employee ID (numeric)</span><input class="ctl" type="number" [(ngModel)]="form.employeeId" /></label>
            }
            <label class="field"><span class="lbl">Overtime Date *</span><input class="ctl" type="date" [(ngModel)]="form.overtimeDate" /></label>
            <div class="form-grid">
              <label class="field"><span class="lbl">Start Time *</span><input class="ctl" type="time" [(ngModel)]="form.startTime" /></label>
              <label class="field"><span class="lbl">End Time *</span><input class="ctl" type="time" [(ngModel)]="form.endTime" /></label>
            </div>
            <label class="field"><span class="lbl">Reason *</span><textarea class="ctl" [(ngModel)]="form.reason"></textarea></label>
            <div class="muted small mb">Approval chain: Department Head → HR Officer → VP & HR Head.</div>
            <div class="modal-actions">
              <button class="btn secondary" (click)="showNew.set(false)">Cancel</button>
              <button class="btn" [disabled]="busy()" (click)="submit()">Submit Request</button>
            </div>
          </div>
        </div>
      }
    </div>
  `
})
export class OvertimeComponent implements OnInit {
  tab = signal('requests');
  items = signal<any[]>([]);
  corrections = signal<any[]>([]);
  corrPage = signal(1);
  corrTotal = signal(0);
  page = signal(1);
  total = signal(0);
  readonly pageSize = 25;
  showNew = signal(false);
  showOtCorrection = signal(false);
  busy = signal(false);
  error = signal('');
  message = signal('');
  statusFilter = '';
  form: any = {};
  corrForm: any = {};
  otIssueTypes = [
    { v: 'ApprovedOtNotEncoded', n: 'Approved OT not encoded' },
    { v: 'OtNotInLogs', n: 'OT not captured by attendance logs' },
    { v: 'IncorrectOtHours', n: 'Incorrect OT hours' },
    { v: 'Other', n: 'Other' }
  ];

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.get<{ total: number; items: any[] }>('overtime/requests', {
      status: this.statusFilter, page: this.page(), pageSize: this.pageSize
    }).subscribe(r => {
      this.items.set(r.items);
      this.total.set(r.total);
    });
  }

  totalPages(): number { return Math.max(1, Math.ceil(this.total() / this.pageSize)); }

  openNew(): void {
    this.error.set('');
    this.form = { employeeId: this.auth.user()?.employeeId, overtimeDate: new Date().toISOString().slice(0, 10) };
    this.showNew.set(true);
  }

  submit(): void {
    if (!this.form.overtimeDate || !this.form.startTime || !this.form.endTime || !this.form.reason) {
      this.error.set('All fields are required.');
      return;
    }
    this.busy.set(true);
    const payload = { ...this.form, startTime: this.form.startTime + ':00', endTime: this.form.endTime + ':00' };
    this.api.post('overtime/requests', payload).subscribe({
      next: () => {
        this.busy.set(false);
        this.showNew.set(false);
        this.message.set('Overtime request submitted for approval.');
        this.load();
        setTimeout(() => this.message.set(''), 4000);
      },
      error: err => { this.busy.set(false); this.error.set(err?.error?.message ?? 'Submission failed.'); }
    });
  }

  cancel(o: any): void {
    this.api.post(`overtime/requests/${o.id}/cancel`, {}).subscribe(() => this.load());
  }

  loadCorrections(): void {
    this.api.get<{ total: number; items: any[] }>('overtime/corrections', {
      page: this.corrPage(), pageSize: this.pageSize
    }).subscribe(r => {
      this.corrections.set(r.items);
      this.corrTotal.set(r.total);
    });
  }

  corrTotalPages(): number { return Math.max(1, Math.ceil(this.corrTotal() / this.pageSize)); }

  openOtCorrection(): void {
    this.error.set('');
    this.corrForm = { issueType: 'ApprovedOtNotEncoded', employeeId: this.auth.user()?.employeeId, overtimeDate: new Date().toISOString().slice(0, 10) };
    this.showOtCorrection.set(true);
  }

  formatOtIssue(v: string): string {
    return this.otIssueTypes.find(i => i.v === v)?.n ?? v;
  }

  submitOtCorrection(): void {
    if (!this.corrForm.overtimeDate || !this.corrForm.startTime || !this.corrForm.endTime || !this.corrForm.reason) {
      this.error.set('All required fields must be filled.');
      return;
    }
    this.busy.set(true);
    const payload = { ...this.corrForm, startTime: this.corrForm.startTime + ':00', endTime: this.corrForm.endTime + ':00' };
    this.api.post('overtime/corrections', payload).subscribe({
      next: () => {
        this.busy.set(false);
        this.showOtCorrection.set(false);
        this.message.set('OT correction submitted for approval.');
        this.tab.set('corrections');
        this.loadCorrections();
      },
      error: err => { this.busy.set(false); this.error.set(err?.error?.message ?? 'Submission failed.'); }
    });
  }
}
