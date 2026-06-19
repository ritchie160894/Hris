import { Component, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-benefits',
  imports: [FormsModule, DecimalPipe],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Benefits</h1><div class="sub">HMO, allowances, incentives and bonuses</div></div>
        @if (auth.isHr()) { <button class="btn" (click)="showNew.set(true)">＋ New Benefit</button> }
      </div>

      <div class="grid grid-3 mb">
        @for (b of benefits(); track b.id) {
          <div class="card">
            <div class="card-title">{{ b.name }} <span class="badge muted">{{ typeName(b.type) }}</span></div>
            <div class="muted small">{{ b.provider || 'Internal' }}</div>
            @if (b.monthlyCost) { <div class="bold mt">₱{{ b.monthlyCost | number:'1.2-2' }}/month</div> }
            @if (auth.isHr()) { <button class="btn secondary sm mt" (click)="openAssign(b)">Assign to employee</button> }
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
              <select class="ctl" [(ngModel)]="form.type">
                <option [ngValue]="1">HMO</option><option [ngValue]="2">Allowance</option>
                <option [ngValue]="3">Incentive</option><option [ngValue]="4">Bonus</option><option [ngValue]="5">Other</option>
              </select></label>
            <label class="field"><span class="lbl">Provider</span><input class="ctl" [(ngModel)]="form.provider" /></label>
            <label class="field"><span class="lbl">Monthly Cost</span><input class="ctl" type="number" [(ngModel)]="form.monthlyCost" /></label>
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
  form: any = { type: 1 };
  assignForm: any = {};

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.get<any[]>('benefits').subscribe(r => this.benefits.set(r));
    this.api.get<any[]>('benefits/assignments').subscribe(r => this.assignments.set(r));
  }

  typeName(t: number): string { return ({ 1: 'HMO', 2: 'Allowance', 3: 'Incentive', 4: 'Bonus', 5: 'Other' } as any)[t] ?? t; }

  create(): void {
    this.api.post('benefits', this.form).subscribe(() => { this.showNew.set(false); this.form = { type: 1 }; this.load(); });
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
