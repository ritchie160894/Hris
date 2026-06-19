import { Component, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';

interface PendingRequest {
  requestType: string;
  typeLabel: string;
  requestId: number;
  employee: string;
  employeeCode: string;
  department: string;
  summary: string;
  details: string;
  date: string;
  level: number;
  stepName: string;
  isApplyOnly?: boolean;
}

@Component({
  selector: 'app-approvals',
  imports: [DatePipe, DecimalPipe, FormsModule, RouterLink],
  styles: [`
    .req-card { display: flex; gap: 14px; padding: 16px; border: 1px solid var(--border); border-radius: var(--radius);
      background: #fff; margin-bottom: 12px; align-items: flex-start; flex-wrap: wrap; box-shadow: var(--shadow); }
    .req-main { flex: 1; min-width: 240px; }
    .req-emp { font-weight: 700; font-size: 14.5px; }
    .req-sum { margin: 4px 0; font-size: 13.5px; }
    .req-det { color: var(--text-soft); font-size: 13px; font-style: italic; }
    .req-meta { font-size: 12px; color: var(--text-faint); margin-top: 6px; }
    .req-actions { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
    .chain { margin: 10px 0; padding: 10px 14px; background: var(--bg); border-radius: 8px;
      .step { display: flex; gap: 8px; align-items: center; padding: 4px 0; font-size: 12.5px; } }
    .filter-pill { border: 1px solid var(--border); background: #fff; border-radius: 999px; padding: 6px 14px;
      cursor: pointer; font-size: 13px; font-weight: 600; font-family: inherit; color: var(--text-soft);
      &.on { background: var(--primary); color: #fff; border-color: var(--primary); } }
    @media (max-width: 720px) {
      .req-card { flex-direction: column; }
      .req-main { min-width: 0; width: 100%; }
      .req-emp .muted.small { display: block; margin-top: 2px; }
      .req-actions { width: 100%; }
      .req-actions .btn { flex: 1; justify-content: center; }
    }
    .pagination { display: flex; align-items: center; gap: 10px; padding: 12px 0 0; font-size: 13px; color: var(--text-soft); flex-wrap: wrap; }
    .btn.danger { color: var(--danger); }
    .btn.danger:hover { background: var(--danger-soft); }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1>Approval Portal</h1>
          <div class="sub">Acting as <b>{{ stepLabel() }}</b> — review and act on requests awaiting your approval</div>
        </div>
        <button class="btn secondary" (click)="load()">↻ Refresh</button>
      </div>

      @if (message()) { <div class="alert success">{{ message() }}</div> }
      @if (error()) { <div class="alert error">{{ error() }}</div> }

      <div class="filter-pills row mb">
        @for (f of filters; track f) {
          <button class="filter-pill" [class.on]="filter() === f" (click)="setFilter(f)">
            {{ f }} @if (f !== 'All') { ({{ countOf(f) }}) } @else { ({{ pendingCounts()['All'] || pendingTotal() }}) }
          </button>
        }
      </div>

      @for (r of pending(); track r.requestType + '-' + r.requestId) {
        <div class="req-card">
          <div class="avatar">{{ initials(r.employee) }}</div>
          <div class="req-main">
            <div class="req-emp">{{ r.employee }} <span class="muted small">· {{ r.employeeCode }} · {{ r.department }}</span></div>
            <div class="req-sum"><span class="badge">{{ r.typeLabel }}</span> {{ r.summary }}</div>
            @if (r.details) { <div class="req-det">"{{ r.details }}"</div> }
            <div class="req-meta">Submitted {{ r.date | date:'MMM d, y h:mm a' }} · Approval level {{ r.level }} ({{ r.stepName }})</div>
            @if (expanded() === key(r)) {
              <div class="chain">
                @for (s of chain(); track s.level) {
                  <div class="step">
                    <span class="badge {{ s.status.toLowerCase() }}">L{{ s.level }}</span>
                    <b>{{ s.stepName }}</b>
                    <span class="muted">{{ s.status }}@if (s.actedByName) { · {{ s.actedByName }} } @if (s.remarks) { · "{{ s.remarks }}" }</span>
                  </div>
                }
              </div>
              <label class="field" style="margin-top:8px">
                <span class="lbl">Remarks (optional)</span>
                <textarea class="ctl" [(ngModel)]="remarks" rows="2" placeholder="Add comments for this decision…"></textarea>
              </label>
            }
          </div>
          <div class="req-actions">
            @if (expanded() !== key(r)) {
              <button class="btn secondary sm" (click)="expand(r)">Review</button>
            } @else {
              @if (r.isApplyOnly) {
                <button class="btn sm" [disabled]="busy()" (click)="act(r, 'approve')">✓ Apply to Payroll</button>
                <span class="muted small">Payroll applies — cannot reject</span>
              } @else {
                <button class="btn success sm" [disabled]="busy()" (click)="act(r, 'approve')">✓ Approve</button>
                <button class="btn danger sm" [disabled]="busy()" (click)="act(r, 'reject')">✕ Reject</button>
                <button class="btn secondary sm" [disabled]="busy()" (click)="act(r, 'return')">↩ Return for Revision</button>
              }
              <button class="btn ghost sm" (click)="expanded.set('')">Close</button>
            }
          </div>
        </div>
      } @empty {
        @if (filter() !== 'Payroll') {
          <div class="card"><div class="empty">🎉 Nothing pending — you're all caught up.</div></div>
        }
      }

      @if (filter() !== 'Payroll' && pendingTotal() > pendingPageSize) {
        <div class="pagination">
          <span>Page {{ pendingPage() }} of {{ pendingTotalPages() }} · {{ pendingTotal() }} request(s)</span>
          <button class="btn secondary sm" [disabled]="pendingPage() <= 1" (click)="pendingPage.set(pendingPage() - 1); loadPending()">Previous</button>
          <button class="btn secondary sm" [disabled]="pendingPage() >= pendingTotalPages()" (click)="pendingPage.set(pendingPage() + 1); loadPending()">Next</button>
        </div>
      }

      @if (filter() === 'Payroll' && !showPayrollSection()) {
        <div class="card"><div class="empty">No payroll cutoffs awaiting your approval.</div></div>
      }

      @if (showPayrollSection()) {
        <div class="card mt">
          <div class="card-title row" style="justify-content:space-between">
            <span>Payroll Approval</span>
            <a class="btn ghost sm" routerLink="/executive-payroll">Open Payroll Review →</a>
          </div>
          <div class="muted small mb">Two-step approval: VP first, then CEO final sign-off.</div>
          @for (c of payrollCutoffs(); track c.id) {
            <div class="req-card" style="box-shadow:none">
              <div class="avatar">₱</div>
              <div class="req-main">
                <div class="req-emp">{{ c.name }}</div>
                <div class="req-sum">
                  <span class="badge {{ payrollStatusClass(c.status) }}">{{ payrollStatusLabel(c.status) }}</span>
                  {{ c.periodStart }} → {{ c.periodEnd }} · Pay date {{ c.payDate }}
                </div>
                <div class="req-meta">{{ c.payslipCount }} payslip/s · Total net ₱{{ c.totalNet | number:'1.2-2' }}</div>
                @if (c.vpApprovedByName) {
                  <div class="req-meta">VP approved by {{ c.vpApprovedByName }}</div>
                }
                @if (expanded() === 'payroll-' + c.id) {
                  <label class="field" style="margin-top:8px">
                    <span class="lbl">Remarks (optional)</span>
                    <textarea class="ctl" [(ngModel)]="remarks" rows="2" placeholder="Add comments for this decision…"></textarea>
                  </label>
                }
              </div>
              <div class="req-actions">
                @if (expanded() !== 'payroll-' + c.id) {
                  <button class="btn secondary sm" (click)="expanded.set('payroll-' + c.id); remarks = ''">Review</button>
                } @else {
                  @if (canVpApprovePayroll(c)) {
                    <button class="btn success sm" [disabled]="busy()" (click)="actPayroll(c, true)">✓ VP Approve</button>
                  }
                  @if (canCeoApprovePayroll(c)) {
                    <button class="btn success sm" [disabled]="busy()" (click)="actPayroll(c, true)">✓ CEO Final Approve</button>
                  }
                  @if (canVpApprovePayroll(c) || canCeoApprovePayroll(c)) {
                    <button class="btn danger sm" [disabled]="busy()" (click)="actPayroll(c, false)">✕ Reject</button>
                  }
                  <button class="btn ghost sm" (click)="expanded.set('')">Close</button>
                }
              </div>
            </div>
          } @empty { <div class="empty">No payroll cutoffs awaiting your approval</div> }
        </div>
      }

      <div class="card mt">
        <div class="card-title row" style="justify-content:space-between">
          <span>My Approval History</span>
          @if (historyTotal() > 0) {
            <span class="muted small">{{ historyTotal() }} record(s)</span>
          }
        </div>
        <div class="table-wrap">
          <table class="data">
            <thead><tr><th>Type</th><th>Request</th><th>Step</th><th>Decision</th><th>Remarks</th><th>Date</th><th></th></tr></thead>
            <tbody>
              @for (h of history(); track h.id) {
                <tr>
                  <td>{{ h.requestType }}</td>
                  <td>#{{ h.requestId }}</td>
                  <td>{{ h.stepName }}</td>
                  <td><span class="badge {{ h.status.toLowerCase() }}">{{ h.status }}</span></td>
                  <td class="muted">{{ h.remarks || '—' }}</td>
                  <td>{{ h.actedAt | date:'MMM d, h:mm a' }}</td>
                  <td>
                    @if (canDeleteHistory(h)) {
                      <button class="btn ghost sm danger" [disabled]="historyBusy()" (click)="deleteHistory(h)">Delete</button>
                    }
                  </td>
                </tr>
              } @empty { <tr><td colspan="7"><div class="empty">No approval history yet</div></td></tr> }
            </tbody>
          </table>
        </div>
        @if (historyTotal() > historyPageSize) {
          <div class="pagination">
            <span>Page {{ historyPage() }} of {{ historyTotalPages() }} · {{ historyPageSize }} rows/page</span>
            <button class="btn secondary sm" [disabled]="historyPage() <= 1" (click)="historyPage.set(historyPage() - 1); loadHistory()">Previous</button>
            <button class="btn secondary sm" [disabled]="historyPage() >= historyTotalPages()" (click)="historyPage.set(historyPage() + 1); loadHistory()">Next</button>
          </div>
        }
      </div>
    </div>
  `
})
export class ApprovalsComponent implements OnInit {
  pending = signal<PendingRequest[]>([]);
  pendingPage = signal(1);
  pendingTotal = signal(0);
  pendingCounts = signal<Record<string, number>>({});
  readonly pendingPageSize = 25;
  history = signal<any[]>([]);
  historyPage = signal(1);
  historyTotal = signal(0);
  historyBusy = signal(false);
  readonly historyPageSize = 25;
  chain = signal<any[]>([]);
  expanded = signal('');
  filter = signal('All');
  busy = signal(false);
  message = signal('');
  error = signal('');
  remarks = '';
  filters = ['All', 'Leave', 'SIL', 'Overtime', 'Overtime Correction', 'Cash Advance', 'Loan', 'Attendance Correction', 'Payroll'];

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void { this.load(); }

  payrollCutoffs = signal<any[]>([]);

  isPayrollApprover(): boolean {
    return this.auth.hasRole('VicePresidentHrHead', 'PresidentCeo', 'SuperAdministrator');
  }

  showPayrollSection(): boolean {
    return this.isPayrollApprover() && this.payrollCutoffs().length > 0;
  }

  canVpApprovePayroll(c: any): boolean {
    return c.status === 'ForApproval' && this.auth.hasRole('VicePresidentHrHead', 'SuperAdministrator');
  }

  canCeoApprovePayroll(c: any): boolean {
    return c.status === 'ForCeoApproval' && this.auth.hasRole('PresidentCeo', 'SuperAdministrator');
  }

  payrollStatusLabel(status: string): string {
    return ({ ForApproval: 'Awaiting VP', ForCeoApproval: 'Awaiting CEO' } as any)[status] ?? status;
  }

  payrollStatusClass(status: string): string {
    return ({ ForApproval: 'forapproval', ForCeoApproval: 'forceoapproval' } as any)[status] ?? 'muted';
  }

  load(): void {
    this.loadPending();
    this.loadHistory();
    if (this.isPayrollApprover())
      this.api.get<{ items: any[] }>('payroll/cutoffs', { page: 1, pageSize: 100 }).subscribe(res => {
        const list = res.items ?? [];
        const pending = list.filter(c => {
          if (c.status === 'ForApproval' && this.auth.hasRole('VicePresidentHrHead', 'SuperAdministrator')) return true;
          if (c.status === 'ForCeoApproval' && this.auth.hasRole('PresidentCeo', 'SuperAdministrator')) return true;
          return false;
        });
        this.payrollCutoffs.set(pending);
      });
  }

  loadPending(): void {
    const type = this.filter();
    this.api.get<{ total: number; items: PendingRequest[]; counts?: Record<string, number> }>('approvals/pending', {
      page: this.pendingPage(),
      pageSize: this.pendingPageSize,
      type: type === 'All' || type === 'Payroll' ? undefined : type
    }).subscribe(res => {
      this.pending.set(res.items ?? []);
      this.pendingTotal.set(res.total ?? 0);
      if (res.counts) this.pendingCounts.set(res.counts);
    });
  }

  setFilter(f: string): void {
    this.filter.set(f);
    this.pendingPage.set(1);
    if (f !== 'Payroll') this.loadPending();
  }

  pendingTotalPages(): number {
    return Math.max(1, Math.ceil(this.pendingTotal() / this.pendingPageSize));
  }

  loadHistory(): void {
    this.api.get<{ total: number; items: any[] }>('approvals/history', {
      page: this.historyPage(),
      pageSize: this.historyPageSize
    }).subscribe(res => {
      this.history.set(res.items);
      this.historyTotal.set(res.total);
    });
  }

  historyTotalPages(): number {
    return Math.max(1, Math.ceil(this.historyTotal() / this.historyPageSize));
  }

  canDeleteHistory(h: any): boolean {
    const uid = this.auth.user()?.id;
    return this.auth.hasRole('SuperAdministrator') || h.actedByUserId === uid;
  }

  deleteHistory(h: any): void {
    const label = `${h.requestType} #${h.requestId} · ${h.stepName}`;
    if (!confirm(`Remove "${label}" from your approval history?\n\nThe underlying request and approval chain are kept; only this history row is hidden.`)) return;
    this.historyBusy.set(true);
    this.error.set('');
    this.api.delete<{ message?: string }>(`approvals/history/${h.id}`).subscribe({
      next: res => {
        this.historyBusy.set(false);
        this.message.set(res?.message ?? 'History entry removed.');
        if (this.history().length === 1 && this.historyPage() > 1) this.historyPage.set(this.historyPage() - 1);
        this.loadHistory();
        setTimeout(() => this.message.set(''), 4000);
      },
      error: err => {
        this.historyBusy.set(false);
        this.error.set(err?.error?.message ?? 'Could not remove history entry.');
      }
    });
  }

  actPayroll(c: any, approve: boolean): void {
    this.busy.set(true);
    this.message.set('');
    this.error.set('');
    this.api.post(`payroll/cutoffs/${c.id}/approve`, { approve, remarks: this.remarks }).subscribe({
      next: (res: any) => {
        this.busy.set(false);
        this.expanded.set('');
        this.message.set(res?.message ?? `Payroll cutoff "${c.name}" ${approve ? 'approved' : 'rejected'}.`);
        this.load();
      },
      error: err => {
        this.busy.set(false);
        this.error.set(err?.error?.message ?? 'Action failed.');
      }
    });
  }

  countOf(label: string): number {
    if (label === 'Payroll') return this.payrollCutoffs().length;
    return this.pendingCounts()[label] ?? 0;
  }

  key(r: PendingRequest): string { return `${r.requestType}-${r.requestId}`; }

  expand(r: PendingRequest): void {
    this.expanded.set(this.key(r));
    this.remarks = '';
    this.chain.set([]);
    this.api.get<any[]>('approvals/chain', { requestType: r.requestType, requestId: r.requestId })
      .subscribe(res => this.chain.set(res));
  }

  act(r: PendingRequest, decision: 'approve' | 'reject' | 'return'): void {
    this.busy.set(true);
    this.message.set('');
    this.error.set('');
    const isCorrection = r.requestType === 'AttendanceCorrection';
    const isOtCorrection = r.requestType === 'OvertimeCorrection';
    const req$ = isCorrection
      ? this.api.post(`attendance/corrections/${r.requestId}/act`, { approve: decision === 'approve', remarks: this.remarks, return: decision === 'return' })
      : isOtCorrection
        ? this.api.post(`overtime/corrections/${r.requestId}/act`, { approve: decision === 'approve', remarks: this.remarks, return: decision === 'return' })
        : this.api.post('approvals/act', { requestType: r.requestType, requestId: r.requestId, approve: decision === 'approve', remarks: this.remarks, returnForRevision: decision === 'return' });
    req$.subscribe({
      next: () => {
        this.busy.set(false);
        this.expanded.set('');
        this.message.set(`${r.typeLabel} request for ${r.employee} — ${decision === 'approve' ? 'approved' : decision === 'reject' ? 'rejected' : 'returned for revision'}.`);
        if (this.pending().length === 1 && this.pendingPage() > 1) this.pendingPage.set(this.pendingPage() - 1);
        this.load();
      },
      error: err => {
        this.busy.set(false);
        this.error.set(err?.error?.message ?? 'Action failed.');
      }
    });
  }

  initials(name: string): string {
    return (name || '?').split(' ').map(p => p[0]).slice(0, 2).join('').toUpperCase();
  }

  stepLabel(): string {
    return (this.auth.role() || '').replace(/([A-Z])/g, ' $1').trim().replace('Hr ', 'HR ').replace('Ceo', 'CEO');
  }
}
