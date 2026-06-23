import { Component, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-benefits',
  imports: [FormsModule, DecimalPipe],
  styles: [`
    .benefit-card { position: relative; }
    .benefit-actions { display: flex; gap: 6px; flex-wrap: wrap; margin-top: 10px; }
    .btn.danger { color: var(--danger); }
    .btn.danger:hover { background: var(--danger-soft); }
    .thirteenth-note { margin-top: 8px; padding: 8px 10px; background: var(--info-soft, #eef6ff); border-radius: 8px; font-size: 12px; color: var(--text-soft); line-height: 1.4; }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Benefits</h1><div class="sub">HMO, allowances, incentives and bonuses</div></div>
        @if (auth.isHr()) { <button class="btn" (click)="openNew()">＋ New Benefit</button> }
      </div>

      @if (message()) { <div class="alert success">{{ message() }}</div> }
      @if (error()) { <div class="alert error">{{ error() }}</div> }

      <div class="grid grid-3 mb">
        @for (b of benefits(); track b.id) {
          <div class="card benefit-card">
            <div class="card-title">{{ b.name }} <span class="badge muted">{{ typeName(b.type) }}</span></div>
            <div class="muted small">{{ b.provider || 'Internal' }}</div>
            @if (b.monthlyCost) { <div class="bold mt">₱{{ b.monthlyCost | number:'1.2-2' }}/month</div> }
            @if (b.isThirteenthMonth) {
              <div class="thirteenth-note">
                <b>13th Month (PD 851):</b> Total basic salary earned in the year ÷ 12.
                Accrued each payroll as <b>basic pay ÷ 12</b> for assigned employees.
              </div>
            }
            @if (auth.isHr()) {
              <div class="benefit-actions">
                <button class="btn secondary sm" (click)="openAssign(b)">Assign to employee</button>
                <button class="btn ghost sm danger" (click)="removeBenefit(b)">Delete</button>
              </div>
            }
          </div>
        } @empty { <div class="card"><div class="empty">No benefits defined</div></div> }
      </div>

      <div class="card">
        <div class="card-title">Benefit Assignments</div>
        <div class="table-wrap">
          <table class="data">
            <thead><tr><th>Employee</th><th>Benefit</th><th>Effective</th><th>Policy #</th><th></th></tr></thead>
            <tbody>
              @for (a of assignments(); track a.id) {
                <tr>
                  <td class="bold">{{ a.employee.name }}</td>
                  <td>{{ a.benefit.name }} <span class="badge muted">{{ a.benefit.type }}</span></td>
                  <td>{{ a.effectiveDate }}@if (a.endDate) { → {{ a.endDate }} }</td>
                  <td class="muted">{{ a.policyNumber || '—' }}</td>
                  <td>@if (auth.isHr()) { <button class="btn ghost sm" (click)="unassign(a)">Remove</button> }</td>
                </tr>
              } @empty { <tr><td colspan="5"><div class="empty">No assignments</div></td></tr> }
            </tbody>
          </table>
        </div>
      </div>

      @if (showNew()) {
        <div class="modal-backdrop" (click)="showNew.set(false)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">New Benefit</div>
            <label class="field"><span class="lbl">Name *</span><input class="ctl" [(ngModel)]="form.name" /></label>
            <label class="field"><span class="lbl">Type</span>
              <select class="ctl" [(ngModel)]="form.type" (ngModelChange)="onTypeChange()">
                <option [ngValue]="1">HMO</option><option [ngValue]="2">Allowance</option>
                <option [ngValue]="3">Incentive</option><option [ngValue]="4">Bonus</option><option [ngValue]="5">Other</option>
              </select></label>
            <label class="field"><span class="lbl">Provider</span><input class="ctl" [(ngModel)]="form.provider" /></label>
            <label class="field"><span class="lbl">Monthly Cost</span><input class="ctl" type="number" [(ngModel)]="form.monthlyCost" /></label>
            @if (form.type === 4) {
              <label class="field row" style="gap:8px;align-items:center">
                <input type="checkbox" [(ngModel)]="form.isThirteenthMonth" />
                <span class="lbl" style="margin:0">Philippine 13th month pay (basic salary ÷ 12 per payroll)</span>
              </label>
            }
            <div class="modal-actions">
              <button class="btn secondary" (click)="showNew.set(false)">Cancel</button>
              <button class="btn" (click)="create()">Save</button>
            </div>
          </div>
        </div>
      }

      @if (assignTo()) {
        <div class="modal-backdrop" (click)="assignTo.set(null)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">Assign {{ assignTo()!.name }}</div>
            @if (assignTo()!.isThirteenthMonth) {
              <div class="thirteenth-note mb">Employee will accrue 13th month pay each payroll: <b>basic pay ÷ 12</b> (PD 851).</div>
            }
            <label class="field"><span class="lbl">Employee ID (numeric) *</span><input class="ctl" type="number" [(ngModel)]="assignForm.employeeId" /></label>
            <label class="field"><span class="lbl">Effective Date</span><input class="ctl" type="date" [(ngModel)]="assignForm.effectiveDate" /></label>
            <label class="field"><span class="lbl">Policy Number</span><input class="ctl" [(ngModel)]="assignForm.policyNumber" /></label>
            <div class="modal-actions">
              <button class="btn secondary" (click)="assignTo.set(null)">Cancel</button>
              <button class="btn" (click)="assign()">Assign</button>
            </div>
          </div>
        </div>
      }
    </div>
  `
})
export class BenefitsComponent implements OnInit {
  benefits = signal<any[]>([]);
  assignments = signal<any[]>([]);
  showNew = signal(false);
  assignTo = signal<any | null>(null);
  message = signal('');
  error = signal('');
  form: any = { type: 1, isThirteenthMonth: false };
  assignForm: any = {};

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.get<any[]>('benefits').subscribe(r => this.benefits.set(r));
    this.api.get<any[]>('benefits/assignments').subscribe(r => this.assignments.set(r));
  }

  typeName(t: number): string { return ({ 1: 'HMO', 2: 'Allowance', 3: 'Incentive', 4: 'Bonus', 5: 'Other' } as any)[t] ?? t; }

  openNew(): void {
    this.form = { type: 1, isThirteenthMonth: false };
    this.showNew.set(true);
  }

  onTypeChange(): void {
    if (this.form.type !== 4) this.form.isThirteenthMonth = false;
  }

  create(): void {
    if (!this.form.name?.trim()) { this.error.set('Name is required.'); return; }
    this.error.set('');
    this.api.post('benefits', this.form).subscribe({
      next: () => { this.showNew.set(false); this.form = { type: 1, isThirteenthMonth: false }; this.load(); },
      error: err => this.error.set(err?.error?.message ?? 'Could not save benefit.')
    });
  }

  removeBenefit(b: any): void {
    if (!confirm(`Delete benefit "${b.name}"?\n\nThis cannot be undone. Remove employee assignments first if any exist.`)) return;
    this.error.set('');
    this.api.delete(`benefits/${b.id}`).subscribe({
      next: () => { this.message.set('Benefit deleted.'); this.load(); setTimeout(() => this.message.set(''), 3000); },
      error: err => this.error.set(err?.error?.message ?? 'Could not delete benefit.')
    });
  }

  openAssign(b: any): void {
    this.assignForm = { benefitId: b.id, effectiveDate: new Date().toISOString().slice(0, 10) };
    this.assignTo.set(b);
  }

  assign(): void {
    this.api.post('benefits/assignments', this.assignForm).subscribe(() => { this.assignTo.set(null); this.load(); });
  }

  unassign(a: any): void {
    this.api.delete(`benefits/assignments/${a.id}`).subscribe(() => this.load());
  }
}
