import { Component, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService, saveBlob } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-payroll',
  imports: [DecimalPipe, FormsModule],
  styles: [`
    .slip { font-size: 13.5px;
      .sec { font-weight: 700; margin: 14px 0 6px; color: var(--text-soft); text-transform: uppercase; font-size: 11.5px; letter-spacing: .05em; }
      .ln { display: flex; justify-content: space-between; padding: 3px 0; gap: 8px; }
      .ln span:first-child { flex: 1; }
      .tot { border-top: 2px solid var(--text); margin-top: 8px; padding-top: 8px; font-weight: 800; font-size: 16px; }
    }
    .modal.receipt {
      max-width: 300px; padding: 14px 12px; margin: 0 auto;
      .modal-title { font-size: 14px; text-align: center; margin-bottom: 10px; }
      .slip { font-size: 12px;
        .sec { margin: 10px 0 4px; font-size: 10.5px; }
        .tot { font-size: 14px; }
      }
      .modal-actions { flex-wrap: wrap; justify-content: center; }
    }
    @media print {
      .modal.receipt { max-width: 80mm; width: 80mm; padding: 8mm 4mm; box-shadow: none; }
    }
    .pagination { display: flex; align-items: center; gap: 10px; padding: 12px 0 0; font-size: 13px; color: var(--text-soft); flex-wrap: wrap; }
    .btn.danger { color: var(--danger); }
    .btn.danger:hover { background: var(--danger-soft); }
    .ded-check { display: grid; grid-template-columns: 24px 1fr auto; gap: 8px; align-items: center; padding: 6px 0; border-bottom: 1px solid var(--border); font-size: 13px; }
    .ded-group { margin-bottom: 16px; }
    .review-summary { display: grid; grid-template-columns: repeat(auto-fit, minmax(140px, 1fr)); gap: 12px; margin-bottom: 16px;
      .box { padding: 10px 12px; border: 1px solid var(--border); border-radius: 10px; background: var(--surface-soft, #fafafa);
        .lbl { font-size: 11px; color: var(--text-soft); text-transform: uppercase; letter-spacing: .04em; }
        .val { font-size: 18px; font-weight: 700; margin-top: 4px; } } }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Payroll</h1><div class="sub">Cutoffs, processing, approval and payslips</div></div>
        @if (auth.isPayroll()) { <button class="btn" (click)="openNewCutoff()">＋ New Cutoff</button> }
      </div>

      @if (message()) { <div class="alert success">{{ message() }}</div> }
      @if (error()) { <div class="alert error">{{ error() }}</div> }

      <div class="card mb">
        <div class="card-title row" style="justify-content:space-between">
          <span>Payroll Cutoffs</span>
          @if (cutoffTotal() > 0) { <span class="muted small">{{ cutoffTotal() }} cutoff(s)</span> }
        </div>
        <div class="table-wrap">
          <table class="data">
            <thead><tr><th>Cutoff</th><th>Period</th><th>Pay Date</th><th class="num">Payslips</th><th class="num">Total Net</th><th>Status</th><th style="width:320px"></th></tr></thead>
            <tbody>
              @for (c of cutoffs(); track c.id) {
                <tr>
                  <td class="bold">{{ c.name }}</td>
                  <td>{{ c.periodStart }} → {{ c.periodEnd }}</td>
                  <td>{{ c.payDate }}</td>
                  <td class="num">{{ c.payslipCount }}</td>
                  <td class="num">₱{{ c.totalNet | number:'1.2-2' }}</td>
                  <td><span class="badge {{ c.status.toLowerCase() }}">{{ c.status }}</span></td>
                  <td>
                    <div class="row" style="gap:6px">
                      @if (auth.isPayroll() && c.canProcess) {
                        <button class="btn sm" (click)="openDeductionChecklist(c)">☑ Deductions</button>
                        <button class="btn sm" (click)="process(c)">⚙ Process</button>
                      }
                      @if (c.status === 'ForApproval' && auth.hasRole('SuperAdministrator','VicePresidentHrHead')) {
                        <button class="btn success sm" (click)="approve(c, true)">✓ VP Approve</button>
                        <button class="btn danger sm" (click)="approve(c, false)">✕ Reject</button>
                      }
                      @if (c.status === 'ForCeoApproval') {
                        <span class="badge info">Awaiting CEO</span>
                      }
                      @if (c.status === 'Approved' && auth.isPayroll() && c.canRelease) {
                        <button class="btn sm" (click)="release(c)">💸 Release</button>
                      }
                      @if (c.canReset && auth.isPayroll()) {
                        <button class="btn danger sm" (click)="resetCutoff(c)">↩ Reset</button>
                      }
                      @if (c.blockReason && ['Draft','Approved','ForApproval','ForCeoApproval'].includes(c.status)) {
                        <span class="muted small" [title]="c.blockReason">{{ c.blockReason }}</span>
                      }
                      @if (auth.isAdmin()) {
                        <button class="btn ghost sm danger" (click)="deleteCutoff(c)">Delete</button>
                      }
                      @if (c.payslipCount > 0) {
                        <button class="btn secondary sm" (click)="openReview(c)">View</button>
                        <button class="btn ghost sm" (click)="exportCsv(c)">CSV</button>
                      }
                    </div>
                  </td>
                </tr>
              } @empty { <tr><td colspan="7"><div class="empty">No cutoffs yet — create one to start payroll processing</div></td></tr> }
            </tbody>
          </table>
        </div>
        @if (cutoffTotal() > cutoffPageSize) {
          <div class="pagination">
            <span>Page {{ cutoffPage() }} of {{ cutoffPages() }} · {{ cutoffPageSize }} rows/page</span>
            <button class="btn secondary sm" [disabled]="cutoffPage() <= 1" (click)="cutoffPage.set(cutoffPage() - 1); load()">Previous</button>
            <button class="btn secondary sm" [disabled]="cutoffPage() >= cutoffPages()" (click)="cutoffPage.set(cutoffPage() + 1); load()">Next</button>
          </div>
        }
      </div>

      @if (reviewModal()) {
        <div class="modal-backdrop" (click)="closeReview()">
          <div class="modal wide" (click)="$event.stopPropagation()">
            <div class="modal-title">Payroll Review — {{ reviewModal()!.name }}</div>
            <div class="muted small mb">Double-check amounts before approval or release. Open individual payslips for full breakdown.</div>
            <div class="review-summary">
              <div class="box"><div class="lbl">Status</div><div class="val"><span class="badge {{ reviewModal()!.status.toLowerCase() }}">{{ reviewModal()!.status }}</span></div></div>
              <div class="box"><div class="lbl">Pay Date</div><div class="val">{{ reviewModal()!.payDate }}</div></div>
              <div class="box"><div class="lbl">Employees</div><div class="val">{{ reviewModal()!.payslipCount }}</div></div>
              <div class="box"><div class="lbl">Total Gross</div><div class="val">₱{{ reviewModal()!.totalGross | number:'1.2-2' }}</div></div>
              <div class="box"><div class="lbl">Total Net</div><div class="val">₱{{ reviewModal()!.totalNet | number:'1.2-2' }}</div></div>
            </div>
            @if (reviewLoading()) { <div class="empty">Loading payslips…</div> }
            @else {
              <div class="table-wrap">
                <table class="data">
                  <thead><tr><th>Employee</th><th class="num">Basic</th><th class="num">Gross</th><th class="num">Deductions</th><th class="num">Net Pay</th><th></th></tr></thead>
                  <tbody>
                    @for (p of reviewPayslips(); track p.id) {
                      <tr>
                        <td><div class="bold">{{ p.employee.name }}</div><div class="muted small">{{ p.employee.department }}</div></td>
                        <td class="num">{{ p.basicPay | number:'1.2-2' }}</td>
                        <td class="num">{{ p.grossPay | number:'1.2-2' }}</td>
                        <td class="num">{{ p.totalDeductions | number:'1.2-2' }}</td>
                        <td class="num bold">₱{{ p.netPay | number:'1.2-2' }}</td>
                        <td><button class="btn ghost sm" (click)="viewSlip(p.id)">Payslip</button></td>
                      </tr>
                    } @empty { <tr><td colspan="6"><div class="empty">No payslips for this cutoff</div></td></tr> }
                  </tbody>
                </table>
              </div>
              @if (reviewPayslipTotal() > reviewPageSize) {
                <div class="pagination">
                  Page {{ reviewPage() }} of {{ reviewPages() }} · {{ reviewPayslipTotal() }} employees
                  <button class="btn secondary sm" [disabled]="reviewPage() <= 1" (click)="reviewPage.set(reviewPage() - 1); loadReviewPayslips()">‹ Prev</button>
                  <button class="btn secondary sm" [disabled]="reviewPage() >= reviewPages()" (click)="reviewPage.set(reviewPage() + 1); loadReviewPayslips()">Next ›</button>
                </div>
              }
            }
            <div class="modal-actions">
              <button class="btn secondary" (click)="closeReview()">Close</button>
              <button class="btn ghost" (click)="exportCsv(reviewModal()!)">Export CSV</button>
              @if (reviewModal()!.status === 'Approved' && auth.isPayroll() && reviewModal()!.canRelease) {
                <button class="btn" (click)="releaseFromReview()">💸 Release Payroll</button>
              }
            </div>
          </div>
        </div>
      }

      @if (showNewCutoff()) {
        <div class="modal-backdrop" (click)="showNewCutoff.set(false)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">New Payroll Cutoff</div>
            <div class="form-grid">
              <label class="field"><span class="lbl">Period Start *</span><input class="ctl" type="date" [(ngModel)]="cutoffForm.periodStart" /></label>
              <label class="field"><span class="lbl">Period End *</span><input class="ctl" type="date" [(ngModel)]="cutoffForm.periodEnd" /></label>
              <label class="field"><span class="lbl">Pay Date *</span><input class="ctl" type="date" [(ngModel)]="cutoffForm.payDate" /></label>
              <label class="field"><span class="lbl">Name (optional)</span><input class="ctl" [(ngModel)]="cutoffForm.name" /></label>
            </div>
            <div class="row">
              <label><input type="checkbox" [(ngModel)]="cutoffForm.deductSss" /> SSS</label>
              <label><input type="checkbox" [(ngModel)]="cutoffForm.deductPhilHealth" /> PhilHealth</label>
              <label><input type="checkbox" [(ngModel)]="cutoffForm.deductPagIbig" /> Pag-IBIG</label>
              <label><input type="checkbox" [(ngModel)]="cutoffForm.deductTax" /> Tax</label>
              <label><input type="checkbox" [(ngModel)]="cutoffForm.deductLoans" /> Loans</label>
              <label><input type="checkbox" [(ngModel)]="cutoffForm.deductOtherDeductions" /> Other Deductions</label>
            </div>
            <div class="muted small mt">Deductions apply only when enabled here <b>and</b> checked on the employee profile. Use <b>☑ Deductions</b> before processing to review per employee.</div>
            <div class="muted small">Pay schedule: 1–15 period → pay on the 20th · 16–EOM period → pay on the 5th of the next month. You can only create a cutoff after the period ends. Release is allowed within 5 days before pay day through pay day.</div>
            <div class="modal-actions">
              <button class="btn secondary" (click)="showNewCutoff.set(false)">Cancel</button>
              <button class="btn" (click)="createCutoff()">Create</button>
            </div>
          </div>
        </div>
      }

      @if (slip()) {
        <div class="modal-backdrop" (click)="slip.set(null)">
          <div class="modal printable receipt" (click)="$event.stopPropagation()">
            <div class="modal-title">Payslip — {{ slip()!.employee?.firstName }} {{ slip()!.employee?.lastName }}</div>
            <div class="slip">
              <div class="muted small">{{ slip()!.payrollCutoff?.name }} · Pay date {{ slip()!.payrollCutoff?.payDate }} · {{ slip()!.employee?.position?.title }}</div>
              <div class="sec">Earnings</div>
              <div class="ln"><span>Basic Pay</span><b>{{ slip()!.basicPay | number:'1.2-2' }}</b></div>
              <div class="ln"><span>Overtime Pay ({{ slip()!.overtimeHours }} hrs)</span><b>{{ slip()!.overtimePay | number:'1.2-2' }}</b></div>
              <div class="ln"><span>Allowances</span><b>{{ slip()!.allowances | number:'1.2-2' }}</b></div>
              <div class="ln"><span>Bonuses</span><b>{{ slip()!.bonuses | number:'1.2-2' }}</b></div>
              <div class="ln bold"><span>Gross Pay</span><b>{{ slip()!.grossPay | number:'1.2-2' }}</b></div>
              <div class="sec">Deductions</div>
              <div class="ln"><span>SSS</span><b>{{ slip()!.sssEmployee | number:'1.2-2' }}</b></div>
              <div class="ln"><span>PhilHealth</span><b>{{ slip()!.philHealthEmployee | number:'1.2-2' }}</b></div>
              <div class="ln"><span>Pag-IBIG</span><b>{{ slip()!.pagIbigEmployee | number:'1.2-2' }}</b></div>
              <div class="ln"><span>Withholding Tax</span><b>{{ slip()!.withholdingTax | number:'1.2-2' }}</b></div>
              <div class="ln"><span>Cash Advance</span><b>{{ slipDeductionAmount('CASH_ADVANCE') | number:'1.2-2' }}</b></div>
              <div class="ln"><span>Company Loan</span><b>{{ slipDeductionAmount('COMPANY_LOAN') | number:'1.2-2' }}</b></div>
              @if (slip()!.otherDeductions > 0) {
                <div class="ln"><span>Other Deductions</span><b>{{ slip()!.otherDeductions | number:'1.2-2' }}</b></div>
              }
              @for (ln of slipOtherDeductionLines(); track $index) {
                <div class="ln muted small"><span>{{ ln.name }}</span><b>{{ ln.amount | number:'1.2-2' }}</b></div>
              }
              <div class="ln"><span>Absences ({{ slip()!.daysAbsent }} day/s)</span><b>{{ slip()!.absenceDeduction | number:'1.2-2' }}</b></div>
              <div class="ln"><span>Tardiness ({{ slip()!.lateMinutes }} min)</span><b>{{ slip()!.lateDeduction | number:'1.2-2' }}</b></div>
              @if (slip()!.undertimeHours > 0) {
                <div class="ln"><span>Undertime ({{ slip()!.undertimeHours }} hr/s@if (slip()!.undertimeLeaveDays > 0) { · {{ slip()!.undertimeLeaveDays | number:'1.2-4' }} SIL day/s covered}@if (slip()!.undertimeElDays > 0) { · {{ slip()!.undertimeElDays | number:'1.2-4' }} EL day/s filed})</span><b>{{ slip()!.undertimeDeduction | number:'1.2-2' }}</b></div>
              }
              <div class="ln tot"><span>NET PAY</span><span>₱{{ slip()!.netPay | number:'1.2-2' }}</span></div>
            </div>
            <div class="modal-actions">
              <button class="btn secondary" (click)="slip.set(null)">Close</button>
              <button class="btn" onclick="window.print()">🖨 Print / PDF</button>
            </div>
          </div>
        </div>
      }

      @if (deductionModal()) {
        <div class="modal-backdrop" (click)="deductionModal.set(null)">
          <div class="modal wide" (click)="$event.stopPropagation()">
            <div class="modal-title">Deduction Checklist — {{ deductionModal()!.name }}</div>
            <div class="muted small mb">Cutoff {{ deductionCutoffHalfLabel() }} · Only checked deductions apply. Employee must also have the deduction enabled on their profile.</div>
            @if (deductionCutoffFlags(); as flags) {
              <div class="muted small mb row" style="gap:10px;flex-wrap:wrap">
                <span>Cutoff includes:</span>
                @if (flags.deductSss) { <span class="badge muted">SSS</span> }
                @if (flags.deductPhilHealth) { <span class="badge muted">PhilHealth</span> }
                @if (flags.deductPagIbig) { <span class="badge muted">Pag-IBIG</span> }
                @if (flags.deductTax) { <span class="badge muted">Tax</span> }
                @if (flags.deductLoans) { <span class="badge muted">Loans</span> }
                @if (flags.deductOtherDeductions) { <span class="badge muted">Other</span> }
              </div>
            }
            @if (deductionLoading()) { <div class="empty">Loading…</div> }
            @else {
              @for (grp of deductionGroups(); track grp.employeeId) {
                <div class="ded-group">
                  <div class="bold">{{ grp.employeeName }} <span class="muted small">({{ grp.employeeCode }})</span></div>
                  @for (item of grp.items; track item.employeeDeductionId) {
                    <label class="ded-check">
                      <input type="checkbox" [checked]="item.isApplied" (change)="toggleDeductionSelection(item, $event)" />
                      <span>{{ item.deduction.typeName }}</span>
                      <span class="muted">₱{{ item.deduction.amount | number:'1.2-2' }}</span>
                    </label>
                  }
                </div>
              } @empty { <div class="empty">No recurring deductions for this cutoff. Configure them on employee profiles first.</div> }
            }
            <div class="modal-actions">
              <button class="btn secondary" (click)="deductionModal.set(null)">Close</button>
              <button class="btn secondary" (click)="initDeductionChecklist()">Refresh checklist</button>
              <button class="btn" (click)="saveDeductionChecklist()">Save &amp; Process</button>
            </div>
          </div>
        </div>
      }
    </div>
  `
})
export class PayrollComponent implements OnInit {
  cutoffs = signal<any[]>([]);
  cutoffPage = signal(1);
  cutoffTotal = signal(0);
  readonly cutoffPageSize = 25;
  reviewModal = signal<any | null>(null);
  reviewPayslips = signal<any[]>([]);
  reviewPage = signal(1);
  reviewPayslipTotal = signal(0);
  reviewLoading = signal(false);
  readonly reviewPageSize = 25;
  slip = signal<any | null>(null);
  showNewCutoff = signal(false);
  deductionModal = signal<any | null>(null);
  deductionSelections = signal<any[]>([]);
  deductionCutoffHalfLabel = signal('');
  deductionCutoffFlags = signal<any | null>(null);
  deductionLoading = signal(false);
  message = signal('');
  error = signal('');
  cutoffForm: any = {};

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.get<{ total: number; items: any[] }>('payroll/cutoffs', {
      page: this.cutoffPage(),
      pageSize: this.cutoffPageSize
    }).subscribe(r => {
      this.cutoffs.set(r.items);
      this.cutoffTotal.set(r.total);
    });
  }

  cutoffPages(): number {
    return Math.max(1, Math.ceil(this.cutoffTotal() / this.cutoffPageSize));
  }

  openNewCutoff(): void {
    const now = new Date();
    const y = now.getFullYear(), m = now.getMonth(), d = now.getDate();
    const fmt = (dt: Date) => dt.toISOString().slice(0, 10);
    let periodStart: Date;
    let periodEnd: Date;
    let payDate: Date;

    if (d >= 16) {
      // 1–15 of current month just finished — next eligible cutoff
      periodStart = new Date(y, m, 1);
      periodEnd = new Date(y, m, 15);
      payDate = new Date(y, m, 20);
    } else {
      // Before the 16th — previous month's 16–EOM (if we're past the 5th pay day, that period is releasable)
      periodEnd = new Date(y, m, 0);
      periodStart = new Date(periodEnd.getFullYear(), periodEnd.getMonth(), 16);
      payDate = new Date(y, m, 5);
    }

    this.cutoffForm = {
      periodStart: fmt(periodStart), periodEnd: fmt(periodEnd), payDate: fmt(payDate),
      deductSss: true, deductPhilHealth: true, deductPagIbig: true, deductTax: true, deductLoans: true, deductOtherDeductions: true
    };
    this.showNewCutoff.set(true);
  }

  createCutoff(): void {
    this.api.post('payroll/cutoffs', this.cutoffForm).subscribe({
      next: () => { this.showNewCutoff.set(false); this.load(); },
      error: err => this.error.set(err?.error?.message ?? 'Failed to create cutoff.')
    });
  }

  process(c: any): void {
    this.message.set(''); this.error.set('');
    this.api.post(`payroll/cutoffs/${c.id}/process`, {}).subscribe({
      next: () => {
        this.message.set('Payroll queued for background processing. Refreshing…');
        this.pollProcessing(c.id);
      },
      error: err => this.error.set(err?.error?.message ?? 'Processing failed.')
    });
  }

  openDeductionChecklist(c: any): void {
    this.deductionModal.set(c);
    this.deductionLoading.set(true);
    this.api.post(`payroll/cutoffs/${c.id}/deduction-selections/init`, {}).subscribe({
      next: () => this.loadDeductionSelections(c.id),
      error: () => this.loadDeductionSelections(c.id)
    });
  }

  loadDeductionSelections(cutoffId: number): void {
    this.deductionLoading.set(true);
    this.api.get<{ items: any[]; cutoffHalf: string; cutoffFlags?: any }>(`payroll/cutoffs/${cutoffId}/deduction-selections`, { page: 1, pageSize: 500 })
      .subscribe({
        next: r => {
          this.deductionSelections.set(r.items);
          this.deductionCutoffHalfLabel.set(r.cutoffHalf ?? '');
          this.deductionCutoffFlags.set(r.cutoffFlags ?? null);
          this.deductionLoading.set(false);
        },
        error: () => this.deductionLoading.set(false)
      });
  }

  deductionGroups(): { employeeId: number; employeeName: string; employeeCode: string; items: any[] }[] {
    const map = new Map<number, { employeeId: number; employeeName: string; employeeCode: string; items: any[] }>();
    for (const s of this.deductionSelections()) {
      const g = map.get(s.employeeId) ?? { employeeId: s.employeeId, employeeName: s.employee.name, employeeCode: s.employee.employeeCode, items: [] as any[] };
      g.items.push(s);
      map.set(s.employeeId, g);
    }
    return [...map.values()];
  }

  toggleDeductionSelection(item: any, ev: Event): void {
    item.isApplied = (ev.target as HTMLInputElement).checked;
  }

  initDeductionChecklist(): void {
    const c = this.deductionModal();
    if (!c) return;
    this.api.post(`payroll/cutoffs/${c.id}/deduction-selections/init`, {}).subscribe(() => this.loadDeductionSelections(c.id));
  }

  saveDeductionChecklist(): void {
    const c = this.deductionModal();
    if (!c) return;
    const updates = this.deductionSelections().map(s => ({ employeeDeductionId: s.employeeDeductionId, isApplied: s.isApplied }));
    this.api.put(`payroll/cutoffs/${c.id}/deduction-selections`, updates).subscribe({
      next: () => {
        this.deductionModal.set(null);
        this.process(c);
      },
      error: err => this.error.set(err?.error?.message ?? 'Could not save deduction selections.')
    });
  }

  slipDeductionAmount(code: string): number {
    return this.slipDetailLines()
      .filter(x => x.type === 'deduction' && x.code === code)
      .reduce((s, x) => s + (x.amount ?? 0), 0);
  }

  slipOtherDeductionLines(): { name: string; amount: number }[] {
    return this.slipDetailLines()
      .filter(x => x.type === 'deduction' && x.code !== 'CASH_ADVANCE' && x.code !== 'COMPANY_LOAN')
      .map(x => ({ name: x.name, amount: x.amount }));
  }

  private slipDetailLines(): { type: string; code?: string; name: string; amount: number }[] {
    const slip = this.slip();
    if (!slip?.detailsJson) return [];
    try {
      const parsed = JSON.parse(slip.detailsJson);
      return (Array.isArray(parsed) ? parsed : []).map((x: any) => ({
        type: x.type, code: x.code, name: x.name, amount: x.amount ?? 0
      }));
    } catch { return []; }
  }

  private pollProcessing(cutoffId: number, attempts = 0): void {
    if (attempts > 40) { this.error.set('Payroll is still processing — refresh manually.'); return; }
    setTimeout(() => {
      this.api.get<{ total: number; items: any[] }>('payroll/cutoffs', {
        page: this.cutoffPage(),
        pageSize: this.cutoffPageSize
      }).subscribe(res => {
        this.cutoffs.set(res.items);
        this.cutoffTotal.set(res.total);
        const c = res.items.find(x => x.id === cutoffId);
        if (!c) return;
        if (c.status === 'Processing') { this.pollProcessing(cutoffId, attempts + 1); return; }
        if (c.status === 'ForApproval') this.message.set('Payroll processed — awaiting VP approval.');
        else if (c.status === 'ForCeoApproval') this.message.set('VP approved — awaiting CEO final approval.');
        else if (c.processingError) this.error.set(c.processingError);
        else this.message.set(`Payroll status: ${c.status}`);
      });
    }, 3000);
  }

  approve(c: any, approve: boolean): void {
    this.api.post(`payroll/cutoffs/${c.id}/approve`, { approve, remarks: '' }).subscribe({
      next: () => { this.message.set(approve ? 'Payroll approved.' : 'Payroll sent back to draft.'); this.load(); },
      error: err => this.error.set(err?.error?.message ?? 'Action failed.')
    });
  }

  release(c: any): void {
    this.api.post(`payroll/cutoffs/${c.id}/release`, {}).subscribe({
      next: () => { this.message.set('Payroll released — payslips are now visible to employees and loan payments posted.'); this.load(); },
      error: err => this.error.set(err?.error?.message ?? 'Release failed.')
    });
  }

  resetCutoff(c: any): void {
    if (!confirm(`Reset "${c.name}" to draft?\n\nThis removes payslips for this premature cutoff so you can re-process at the correct time.`)) return;
    this.api.post(`payroll/cutoffs/${c.id}/reset`, {}).subscribe({
      next: (res: any) => { this.message.set(res?.message ?? 'Cutoff reset.'); this.load(); },
      error: err => this.error.set(err?.error?.message ?? 'Reset failed.')
    });
  }

  deleteCutoff(c: any): void {
    if (!confirm(`Permanently delete payroll cutoff "${c.name}"?\n\nThis removes all payslips for this cutoff. This cannot be undone.`)) return;
    this.error.set('');
    this.api.delete<{ message?: string }>(`payroll/cutoffs/${c.id}`).subscribe({
      next: res => {
        this.message.set(res?.message ?? 'Cutoff deleted.');
        if (this.cutoffs().length === 1 && this.cutoffPage() > 1) this.cutoffPage.set(this.cutoffPage() - 1);
        if (this.reviewModal()?.id === c.id) this.closeReview();
        this.load();
        setTimeout(() => this.message.set(''), 4000);
      },
      error: err => this.error.set(err?.error?.message ?? 'Delete failed.')
    });
  }

  openReview(c: any): void {
    this.reviewModal.set(c);
    this.reviewPage.set(1);
    this.loadReviewPayslips();
  }

  closeReview(): void {
    this.reviewModal.set(null);
    this.reviewPayslips.set([]);
  }

  loadReviewPayslips(): void {
    const c = this.reviewModal();
    if (!c) return;
    this.reviewLoading.set(true);
    this.api.get<{ total: number; items: any[] }>(`payroll/cutoffs/${c.id}/payslips`, {
      page: this.reviewPage(), pageSize: this.reviewPageSize
    }).subscribe({
      next: r => {
        this.reviewPayslips.set(r.items);
        this.reviewPayslipTotal.set(r.total);
        this.reviewLoading.set(false);
      },
      error: () => this.reviewLoading.set(false)
    });
  }

  reviewPages(): number {
    return Math.max(1, Math.ceil(this.reviewPayslipTotal() / this.reviewPageSize));
  }

  releaseFromReview(): void {
    const c = this.reviewModal();
    if (!c) return;
    if (!confirm(`Release payroll "${c.name}"?\n\nPayslips will become visible to employees and loan payments will be posted.`)) return;
    this.api.post(`payroll/cutoffs/${c.id}/release`, {}).subscribe({
      next: () => {
        this.message.set('Payroll released — payslips are now visible to employees.');
        this.closeReview();
        this.load();
      },
      error: err => this.error.set(err?.error?.message ?? 'Release failed.')
    });
  }

  viewSlip(id: number): void {
    this.api.get<any>(`payroll/payslips/${id}`).subscribe(r => this.slip.set(r));
  }

  exportCsv(c: any): void {
    this.api.download('reports/payroll', { cutoffId: c.id }).subscribe(res => saveBlob(res, 'payroll.csv'));
  }
}
