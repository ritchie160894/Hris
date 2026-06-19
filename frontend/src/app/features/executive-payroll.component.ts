import { Component, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService, saveBlob } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-executive-payroll',
  imports: [DatePipe, DecimalPipe, FormsModule],
  styles: [`
    .slip { font-size: 13.5px;
      .sec { font-weight: 700; margin: 14px 0 6px; color: var(--text-soft); text-transform: uppercase; font-size: 11.5px; letter-spacing: .05em; }
      .ln { display: flex; justify-content: space-between; padding: 3px 0; }
      .tot { border-top: 2px solid var(--text); margin-top: 8px; padding-top: 8px; font-weight: 800; font-size: 16px; }
    }
    .summary { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; margin-bottom: 16px; }
    @media (max-width: 720px) { .summary { grid-template-columns: 1fr; } }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1>Payroll Review</h1>
          <div class="sub">
            @if (auth.hasRole('VicePresidentHrHead')) { Review and approve payroll before CEO final sign-off. }
            @else if (auth.hasRole('PresidentCeo')) { View payroll registers and give final approval before release. }
            @else { Executive payroll review. }
          </div>
        </div>
        <button class="btn secondary" (click)="load()">↻ Refresh</button>
      </div>

      @if (message()) { <div class="alert success">{{ message() }}</div> }
      @if (error()) { <div class="alert error">{{ error() }}</div> }

      <div class="card mb">
        <div class="card-title">Payroll Cutoffs</div>
        <div class="table-wrap">
          <table class="data">
            <thead>
              <tr>
                <th>Cutoff</th><th>Period</th><th>Pay Date</th>
                <th class="num">Employees</th><th class="num">Total Gross</th><th class="num">Total Net</th>
                <th>Status</th><th style="min-width:220px"></th>
              </tr>
            </thead>
            <tbody>
              @for (c of cutoffs(); track c.id) {
                <tr>
                  <td class="bold">{{ c.name }}</td>
                  <td>{{ c.periodStart }} → {{ c.periodEnd }}</td>
                  <td>{{ c.payDate }}</td>
                  <td class="num">{{ c.payslipCount }}</td>
                  <td class="num">₱{{ c.totalGross | number:'1.2-2' }}</td>
                  <td class="num bold">₱{{ c.totalNet | number:'1.2-2' }}</td>
                  <td><span class="badge {{ statusClass(c.status) }}">{{ statusLabel(c.status) }}</span></td>
                  <td>
                    <div class="row" style="gap:6px;flex-wrap:wrap">
                      @if (c.payslipCount > 0) {
                        <button class="btn secondary sm" (click)="viewPayslips(c)">View</button>
                        <button class="btn ghost sm" (click)="exportCsv(c)">⬇ CSV</button>
                      }
                      @if (canVpApprove(c)) {
                        <button class="btn success sm" [disabled]="busy()" (click)="approve(c, true)">✓ VP Approve</button>
                        <button class="btn danger sm" [disabled]="busy()" (click)="approve(c, false)">✕ Reject</button>
                      }
                      @if (canCeoApprove(c)) {
                        <button class="btn success sm" [disabled]="busy()" (click)="approve(c, true)">✓ CEO Final Approve</button>
                        <button class="btn danger sm" [disabled]="busy()" (click)="approve(c, false)">✕ Reject</button>
                      }
                    </div>
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="8"><div class="empty">No payroll cutoffs to review yet</div></td></tr>
              }
            </tbody>
          </table>
        </div>
      </div>

      @if (selected(); as sel) {
        <div class="card">
          <div class="card-title">Payslip Register — {{ sel.name }}</div>
          <div class="summary">
            <div class="stat-card"><div class="label">Employees</div><div class="value">{{ sel.payslipCount }}</div></div>
            <div class="stat-card"><div class="label">Total Gross</div><div class="value">₱{{ sel.totalGross | number:'1.0-0' }}</div></div>
            <div class="stat-card accent"><div class="label">Total Net Pay</div><div class="value">₱{{ sel.totalNet | number:'1.0-0' }}</div></div>
          </div>
          @if (sel.vpApprovedByName) {
            <div class="muted small mb">VP approved by {{ sel.vpApprovedByName }} · {{ sel.vpApprovedAt | date:'MMM d, y h:mm a' }}</div>
          }
          <div class="table-wrap">
            <table class="data">
              <thead>
                <tr>
                  <th>Employee</th><th class="num">Basic</th><th class="num">OT</th><th class="num">Gross</th>
                  <th class="num">Deductions</th><th class="num">Net Pay</th><th></th>
                </tr>
              </thead>
              <tbody>
                @for (p of payslips(); track p.id) {
                  <tr>
                    <td><div class="bold">{{ p.employee.name }}</div><div class="muted small">{{ p.employee.employeeCode }} · {{ p.employee.department }}</div></td>
                    <td class="num">{{ p.basicPay | number:'1.2-2' }}</td>
                    <td class="num">{{ p.overtimePay | number:'1.2-2' }}</td>
                    <td class="num">{{ p.grossPay | number:'1.2-2' }}</td>
                    <td class="num">{{ p.totalDeductions | number:'1.2-2' }}</td>
                    <td class="num bold">₱{{ p.netPay | number:'1.2-2' }}</td>
                    <td><button class="btn ghost sm" (click)="viewSlip(p.id)">Detail</button></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (payslipTotal() > payslipPageSize) {
            <div class="pagination">
              Page {{ payslipPage() }} of {{ payslipPages() }} · {{ payslipTotal() }} employees
              <button class="btn secondary sm" [disabled]="payslipPage() <= 1" (click)="payslipPage.set(payslipPage() - 1); loadPayslips()">‹ Prev</button>
              <button class="btn secondary sm" [disabled]="payslipPage() >= payslipPages()" (click)="payslipPage.set(payslipPage() + 1); loadPayslips()">Next ›</button>
            </div>
          }
        </div>
      }

      @if (slip()) {
        <div class="modal-backdrop" (click)="slip.set(null)">
          <div class="modal printable wide" (click)="$event.stopPropagation()">
            <div class="modal-title">Payslip — {{ slip()!.employee?.firstName }} {{ slip()!.employee?.lastName }}</div>
            <div class="slip">
              <div class="muted small">{{ slip()!.payrollCutoff?.name }} · Pay date {{ slip()!.payrollCutoff?.payDate }}</div>
              <div class="sec">Earnings</div>
              <div class="ln"><span>Basic Pay</span><b>{{ slip()!.basicPay | number:'1.2-2' }}</b></div>
              <div class="ln"><span>Overtime</span><b>{{ slip()!.overtimePay | number:'1.2-2' }}</b></div>
              <div class="ln"><span>Allowances</span><b>{{ slip()!.allowances | number:'1.2-2' }}</b></div>
              <div class="ln bold"><span>Gross Pay</span><b>{{ slip()!.grossPay | number:'1.2-2' }}</b></div>
              <div class="sec">Deductions</div>
              <div class="ln"><span>SSS / PhilHealth / Pag-IBIG</span><b>{{ slip()!.sssEmployee + slip()!.philHealthEmployee + slip()!.pagIbigEmployee | number:'1.2-2' }}</b></div>
              <div class="ln"><span>Withholding Tax</span><b>{{ slip()!.withholdingTax | number:'1.2-2' }}</b></div>
              <div class="ln"><span>Loans & Others</span><b>{{ slip()!.loanDeductions + slip()!.otherDeductions | number:'1.2-2' }}</b></div>
              <div class="ln tot"><span>NET PAY</span><span>₱{{ slip()!.netPay | number:'1.2-2' }}</span></div>
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
export class ExecutivePayrollComponent implements OnInit {
  cutoffs = signal<any[]>([]);
  payslips = signal<any[]>([]);
  payslipPage = signal(1);
  payslipTotal = signal(0);
  payslipPageSize = 25;
  selected = signal<any | null>(null);
  slip = signal<any | null>(null);
  busy = signal(false);
  message = signal('');
  error = signal('');

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.get<{ items: any[] }>('payroll/cutoffs', { page: 1, pageSize: 100 }).subscribe({
      next: res => {
        const list = res.items ?? [];
        const visible = list.filter(c =>
          ['ForApproval', 'ForCeoApproval', 'Approved', 'Released', 'Closed'].includes(c.status));
        this.cutoffs.set(visible);
      },
      error: () => this.error.set('Unable to load payroll cutoffs.')
    });
  }

  canVpApprove(c: any): boolean {
    return c.status === 'ForApproval' && this.auth.hasRole('VicePresidentHrHead', 'SuperAdministrator');
  }

  canCeoApprove(c: any): boolean {
    return c.status === 'ForCeoApproval' && this.auth.hasRole('PresidentCeo', 'SuperAdministrator');
  }

  statusLabel(status: string): string {
    return ({ ForApproval: 'Awaiting VP', ForCeoApproval: 'Awaiting CEO', Approved: 'Approved', Released: 'Released' } as any)[status] ?? status;
  }

  statusClass(status: string): string {
    return ({ ForApproval: 'warning', ForCeoApproval: 'info', Approved: 'success', Released: 'success' } as any)[status] ?? 'muted';
  }

  approve(c: any, approve: boolean): void {
    if (!approve && !confirm(`Reject payroll cutoff "${c.name}"? It will return to draft for payroll to reprocess.`)) return;
    this.busy.set(true);
    this.message.set('');
    this.error.set('');
    this.api.post(`payroll/cutoffs/${c.id}/approve`, { approve, remarks: '' }).subscribe({
      next: (res: any) => {
        this.busy.set(false);
        this.message.set(res.message ?? (approve ? 'Payroll approved.' : 'Payroll rejected.'));
        this.selected.set(null);
        this.load();
      },
      error: err => {
        this.busy.set(false);
        this.error.set(err?.error?.message ?? 'Approval action failed.');
      }
    });
  }

  viewPayslips(c: any): void {
    this.selected.set(c);
    this.payslipPage.set(1);
    this.loadPayslips();
  }

  loadPayslips(): void {
    const c = this.selected();
    if (!c) return;
    this.api.get<{ total: number; items: any[] }>(`payroll/cutoffs/${c.id}/payslips`, {
      page: this.payslipPage(), pageSize: this.payslipPageSize
    }).subscribe(r => { this.payslips.set(r.items); this.payslipTotal.set(r.total); });
  }

  payslipPages(): number { return Math.max(1, Math.ceil(this.payslipTotal() / this.payslipPageSize)); }

  viewSlip(id: number): void {
    this.api.get<any>(`payroll/payslips/${id}`).subscribe(r => this.slip.set(r));
  }

  exportCsv(c: any): void {
    this.api.download('reports/payroll', { cutoffId: c.id, format: 'csv' }).subscribe(res => saveBlob(res, `${c.name.replace(/\s+/g, '-')}-payroll.csv`));
  }
}
