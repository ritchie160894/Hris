import { Component, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-loans',
  imports: [FormsModule, DecimalPipe],
  styles: [`
    .row-actions { display: flex; flex-wrap: wrap; gap: 6px; justify-content: flex-end; }
    .btn.danger { color: var(--danger); }
    .btn.danger:hover { background: var(--danger-soft); }
    .pagination { display: flex; align-items: center; gap: 10px; padding: 12px 0 0; font-size: 13px; color: var(--text-soft); flex-wrap: wrap; }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Loans & Cash Advances</h1><div class="sub">Applications, balances and payroll-deducted repayments</div></div>
        <div class="row">
          @if (auth.isPayroll()) { <button class="btn secondary" (click)="openNew(true)">＋ Government Loan</button> }
          <button class="btn" (click)="openNew(false)">＋ Apply</button>
        </div>
      </div>

      @if (message()) { <div class="alert success">{{ message() }}</div> }

      <div class="card">
        <div class="row mb">
          <select class="ctl" style="max-width:200px" [(ngModel)]="typeFilter" (change)="page.set(1); load()">
            <option value="">All types</option>
            @for (t of types; track t.v) { <option [value]="t.v">{{ t.n }}</option> }
          </select>
        </div>
        <div class="table-wrap">
          <table class="data">
            <thead><tr><th>Employee</th><th>Type</th><th>Reference</th><th class="num">Principal</th><th class="num">Balance</th><th class="num">Per Cutoff</th><th>Status</th><th></th></tr></thead>
            <tbody>
              @for (l of items(); track l.id) {
                <tr>
                  <td class="bold">{{ l.employee.name }}</td>
                  <td><span class="badge muted">{{ typeName(l.type) }}</span></td>
                  <td class="muted">{{ l.reference }}</td>
                  <td class="num">₱{{ l.principal | number:'1.2-2' }}</td>
                  <td class="num bold">₱{{ l.balance | number:'1.2-2' }}</td>
                  <td class="num">₱{{ l.amortizationPerCutoff | number:'1.2-2' }}</td>
                  <td><span class="badge {{ statusKey(l.status) }}">{{ statusName(l.status) }}</span>
                      @if (l.status === 'PendingApproval') { <div class="muted small">{{ l.approvalStatus }}</div> }</td>
                  <td>
                    <div class="row-actions">
                      <button class="btn ghost sm" (click)="viewPayments(l)">Payments</button>
                      @if (canCancel(l)) {
                        <button class="btn ghost sm danger" [disabled]="busy()" (click)="cancel(l)">Cancel</button>
                      }
                    </div>
                  </td>
                </tr>
              } @empty { <tr><td colspan="8"><div class="empty">No loans or cash advances</div></td></tr> }
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

      @if (payments()) {
        <div class="card mt">
          <div class="card-title">Repayment History</div>
          <div class="table-wrap">
            <table class="data">
              <thead><tr><th>Date</th><th class="num">Amount</th><th>Remarks</th></tr></thead>
              <tbody>
                @for (p of payments(); track p.id) {
                  <tr><td>{{ p.paymentDate }}</td><td class="num">₱{{ p.amount | number:'1.2-2' }}</td><td class="muted">{{ p.remarks }}</td></tr>
                } @empty { <tr><td colspan="3"><div class="empty">No payments yet</div></td></tr> }
              </tbody>
            </table>
          </div>
        </div>
      }

      @if (showNew()) {
        <div class="modal-backdrop" (click)="showNew.set(false)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">{{ direct ? 'Add Government / Direct Loan' : 'Loan or Cash Advance Application' }}</div>
            @if (error()) { <div class="alert error">{{ error() }}</div> }
            @if (!auth.hasRole('Employee') || direct) {
              <label class="field"><span class="lbl">Employee ID (numeric)</span><input class="ctl" type="number" [(ngModel)]="form.employeeId" /></label>
            }
            <label class="field"><span class="lbl">Type</span>
              <select class="ctl" [(ngModel)]="form.type">
                @for (t of types; track t.v) { <option [ngValue]="t.v">{{ t.n }}</option> }
              </select></label>
            <div class="form-grid">
              <label class="field"><span class="lbl">Amount *</span><input class="ctl" type="number" [(ngModel)]="form.principal" /></label>
              <label class="field"><span class="lbl">Amortization / cutoff</span><input class="ctl" type="number" [(ngModel)]="form.amortizationPerCutoff" /></label>
              <label class="field"><span class="lbl">Start Date</span><input class="ctl" type="date" [(ngModel)]="form.startDate" /></label>
            </div>
            <label class="field"><span class="lbl">Purpose</span><textarea class="ctl" [(ngModel)]="form.purpose"></textarea></label>
            @if (!direct) { <div class="muted small mb">Applications follow the approval chain: Department Head → HR Officer → VP & HR Head.</div> }
            <div class="modal-actions">
              <button class="btn secondary" (click)="showNew.set(false)">Cancel</button>
              <button class="btn" [disabled]="busy()" (click)="submit()">{{ direct ? 'Add Loan' : 'Submit Application' }}</button>
            </div>
          </div>
        </div>
      }
    </div>
  `
})
export class LoansComponent implements OnInit {
  items = signal<any[]>([]);
  page = signal(1);
  total = signal(0);
  readonly pageSize = 25;
  payments = signal<any[] | null>(null);
  showNew = signal(false);
  busy = signal(false);
  error = signal('');
  message = signal('');
  typeFilter = '';
  direct = false;
  form: any = {};
  types = [
    { v: 'CompanyLoan', n: 'Company Loan' }, { v: 'CashAdvance', n: 'Cash Advance' },
    { v: 'SssLoan', n: 'SSS Loan' }, { v: 'PagIbigLoan', n: 'Pag-IBIG Loan' }, { v: 'Other', n: 'Other' }
  ];

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.get<{ total: number; items: any[] }>('loans', {
      type: this.typeFilter, page: this.page(), pageSize: this.pageSize
    }).subscribe(r => {
      this.items.set(r.items);
      this.total.set(r.total);
    });
  }

  totalPages(): number { return Math.max(1, Math.ceil(this.total() / this.pageSize)); }

  typeName(t: string): string { return this.types.find(x => x.v === t)?.n ?? t; }
  statusName(s: string): string { return s === 'PendingApproval' ? 'Pending Approval' : s === 'FullyPaid' ? 'Fully Paid' : s; }
  statusKey(s: string): string {
    return ({ Active: 'active', FullyPaid: 'success', PendingApproval: 'pending', Rejected: 'rejected', Cancelled: 'cancelled' } as any)[s] ?? 'muted';
  }

  openNew(direct: boolean): void {
    this.direct = direct;
    this.error.set('');
    this.form = {
      type: direct ? 'SssLoan' : 'CashAdvance',
      employeeId: this.auth.user()?.employeeId,
      startDate: new Date().toISOString().slice(0, 10)
    };
    this.showNew.set(true);
  }

  private readonly typeValues: Record<string, number> = { CompanyLoan: 1, CashAdvance: 2, SssLoan: 3, PagIbigLoan: 4, Other: 5 };

  submit(): void {
    if (!this.form.principal || this.form.principal <= 0) { this.error.set('Enter a valid amount.'); return; }
    this.busy.set(true);
    const url = this.direct ? 'loans/direct' : 'loans';
    const payload = { ...this.form, type: this.typeValues[this.form.type] ?? 5 };
    this.api.post(url, payload).subscribe({
      next: () => {
        this.busy.set(false);
        this.showNew.set(false);
        this.message.set(this.direct ? 'Loan added and active for payroll deduction.' : 'Application submitted for approval.');
        this.load();
        setTimeout(() => this.message.set(''), 4000);
      },
      error: err => { this.busy.set(false); this.error.set(err?.error?.message ?? 'Failed.'); }
    });
  }

  viewPayments(l: any): void {
    this.api.get<any[]>(`loans/${l.id}/payments`).subscribe(r => this.payments.set(r));
  }

  canCancel(l: any): boolean {
    const mine = this.auth.user()?.employeeId === l.employee?.id;
    return mine && l.status === 'PendingApproval'
      && ['Pending', 'InProgress'].includes(l.approvalStatus);
  }

  cancel(l: any): void {
    if (!confirm(`Cancel this ${this.typeName(l.type).toLowerCase()} application (${l.reference})?`)) return;
    this.busy.set(true);
    this.api.post(`loans/${l.id}/cancel`, {}).subscribe({
      next: () => {
        this.busy.set(false);
        this.message.set('Application cancelled.');
        this.load();
        setTimeout(() => this.message.set(''), 4000);
      },
      error: err => {
        this.busy.set(false);
        this.message.set('');
        alert(err?.error?.message ?? 'Unable to cancel this application.');
      }
    });
  }
}
