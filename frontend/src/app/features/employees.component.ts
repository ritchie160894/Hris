import { Component, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-employees',
  imports: [FormsModule, RouterLink, DecimalPipe],
  styles: [`
    .danger { color: var(--danger, #c0392b); }
    .btn.danger { color: var(--danger); }
    .btn.danger:hover { background: var(--danger-soft); }
    .btn.ghost.danger:hover { background: var(--danger-soft); }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Employees</h1><div class="sub">{{ total() }} record(s)</div></div>
        @if (auth.isHr()) { <button class="btn" (click)="openNew()">＋ New Employee</button> }
      </div>

      @if (message()) { <div class="alert success">{{ message() }}</div> }

      <div class="card mb">
        <div class="row">
          <input class="ctl" style="max-width:260px" placeholder="Search name or code…" [(ngModel)]="search" (input)="page.set(1); load()" />
          <select class="ctl" style="max-width:200px" [(ngModel)]="departmentId" (change)="page.set(1); load()">
            <option value="">All departments</option>
            @for (d of departments(); track d.id) { <option [value]="d.id">{{ d.name }}</option> }
          </select>
          <select class="ctl" style="max-width:180px" [(ngModel)]="status" (change)="page.set(1); load()">
            <option value="">Active employees</option>
            @for (s of statuses; track s) { <option [value]="s">{{ s }}</option> }
          </select>
          <label class="row muted small" style="gap:6px;cursor:pointer">
            <input type="checkbox" [(ngModel)]="showSeparated" (change)="page.set(1); load()" /> Include separated
          </label>
        </div>
      </div>

      <div class="card">
        <div class="table-wrap">
          <table class="data">
            <thead><tr><th>Employee</th><th>Department</th><th>Position</th><th>Branch / Site</th><th>Status</th><th class="num">Monthly Salary</th><th></th></tr></thead>
            <tbody>
              @for (e of items(); track e.id) {
                <tr>
                  <td>
                    <div class="row" style="gap:10px;flex-wrap:nowrap">
                      <div class="avatar">{{ initials(e.fullName) }}</div>
                      <div><div class="bold">{{ e.fullName }}</div><div class="muted small">{{ e.employeeCode }}</div></div>
                    </div>
                  </td>
                  <td>{{ e.department }}</td>
                  <td>{{ e.position }}</td>
                  <td>{{ e.branch }}<div class="muted small">{{ e.site }}</div></td>
                  <td><span class="badge {{ e.status.toLowerCase() }}">{{ e.status }}</span></td>
                  <td class="num">@if (auth.isHr() || auth.isPayroll()) { ₱{{ e.monthlySalary | number:'1.2-2' }} } @else { — }</td>
                  <td class="row" style="gap:6px;flex-wrap:nowrap">
                    <a class="btn ghost sm" [routerLink]="['/employees', e.id]">View</a>
                    @if (canRemove() && !isSeparated(e.status)) {
                      <button class="btn ghost sm danger" (click)="openRemove(e)">Remove</button>
                    }
                  </td>
                </tr>
              } @empty { <tr><td colspan="7"><div class="empty">No employees found</div></td></tr> }
            </tbody>
          </table>
        </div>
        <div class="pagination">
          Page {{ page() }} of {{ pages() }}
          <button class="btn secondary sm" [disabled]="page() <= 1" (click)="page.set(page() - 1); load()">‹ Prev</button>
          <button class="btn secondary sm" [disabled]="page() >= pages()" (click)="page.set(page() + 1); load()">Next ›</button>
        </div>
      </div>

      @if (showNew()) {
        <div class="modal-backdrop" (click)="showNew.set(false)">
          <div class="modal wide" (click)="$event.stopPropagation()">
            <div class="modal-title">New Employee</div>
            @if (error()) { <div class="alert error">{{ error() }}</div> }
            <div class="form-grid">
              <label class="field"><span class="lbl">Employee Code *</span><input class="ctl" [(ngModel)]="form.employeeCode" /></label>
              <label class="field"><span class="lbl">First Name *</span><input class="ctl" [(ngModel)]="form.firstName" /></label>
              <label class="field"><span class="lbl">Middle Name</span><input class="ctl" [(ngModel)]="form.middleName" /></label>
              <label class="field"><span class="lbl">Last Name *</span><input class="ctl" [(ngModel)]="form.lastName" /></label>
              <label class="field"><span class="lbl">Email</span><input class="ctl" [(ngModel)]="form.email" /></label>
              <label class="field"><span class="lbl">Contact Number</span><input class="ctl" [(ngModel)]="form.contactNumber" /></label>
              <label class="field"><span class="lbl">Hire Date *</span><input class="ctl" type="date" [(ngModel)]="form.hireDate" /></label>
              <div class="field" style="grid-column:1/-1"><span class="lbl">Employment Status</span><div class="muted small">New hires start as <b>Probationary</b> with <b>10 days Emergency Leave</b>. SIL (5 days) is granted when HR regularizes the employee.</div></div>
              <label class="field"><span class="lbl">Department</span>
                <select class="ctl" [(ngModel)]="form.departmentId"><option [ngValue]="null">—</option>@for (d of departments(); track d.id) { <option [ngValue]="d.id">{{ d.name }}</option> }</select></label>
              <label class="field"><span class="lbl">Position</span>
                <select class="ctl" [(ngModel)]="form.positionId"><option [ngValue]="null">—</option>@for (p of positions(); track p.id) { <option [ngValue]="p.id">{{ p.title }}</option> }</select></label>
              <label class="field"><span class="lbl">Branch</span>
                <select class="ctl" [(ngModel)]="form.branchId"><option [ngValue]="null">—</option>@for (b of branches(); track b.id) { <option [ngValue]="b.id">{{ b.name }}</option> }</select></label>
              <label class="field"><span class="lbl">Site</span>
                <select class="ctl" [(ngModel)]="form.siteId"><option [ngValue]="null">—</option>@for (s of sites(); track s.id) { <option [ngValue]="s.id">{{ s.name }}</option> }</select></label>
              <label class="field"><span class="lbl">Monthly Salary *</span><input class="ctl" type="number" [(ngModel)]="form.monthlySalary" /></label>
              <label class="field"><span class="lbl">SSS No.</span><input class="ctl" [(ngModel)]="form.sssNumber" /></label>
              <label class="field"><span class="lbl">PhilHealth No.</span><input class="ctl" [(ngModel)]="form.philHealthNumber" /></label>
              <label class="field"><span class="lbl">Pag-IBIG No.</span><input class="ctl" [(ngModel)]="form.pagIbigNumber" /></label>
              <label class="field"><span class="lbl">TIN</span><input class="ctl" [(ngModel)]="form.tin" /></label>
            </div>
            <div class="modal-actions">
              <button class="btn secondary" (click)="showNew.set(false)">Cancel</button>
              <button class="btn" [disabled]="busy()" (click)="save()">Create Employee</button>
            </div>
          </div>
        </div>
      }

      @if (removeTarget()) {
        <div class="modal-backdrop" (click)="removeTarget.set(null)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">Remove Employee</div>
            @if (error()) { <div class="alert error">{{ error() }}</div> }
            <p class="muted">This marks <b>{{ removeTarget()!.fullName }}</b> ({{ removeTarget()!.employeeCode }}) as no longer with the company. Their payroll and attendance history are kept; linked login accounts are deactivated.</p>
            <div class="form-grid">
              <label class="field"><span class="lbl">Separation Type *</span>
                <select class="ctl" [(ngModel)]="removeForm.status">
                  <option value="Resigned">Resigned</option>
                  <option value="Terminated">Terminated</option>
                </select></label>
              <label class="field"><span class="lbl">Separation Date *</span><input class="ctl" type="date" [(ngModel)]="removeForm.separationDate" /></label>
              <label class="field" style="grid-column:1/-1"><span class="lbl">Reason (optional)</span><input class="ctl" [(ngModel)]="removeForm.reason" placeholder="e.g. End of contract, voluntary resignation" /></label>
            </div>
            <div class="modal-actions">
              <button class="btn secondary" (click)="removeTarget.set(null)">Cancel</button>
              <button class="btn danger" [disabled]="busy()" (click)="confirmRemove()">Remove Employee</button>
            </div>
          </div>
        </div>
      }
    </div>
  `
})
export class EmployeesComponent implements OnInit {
  items = signal<any[]>([]);
  total = signal(0);
  page = signal(1);
  pageSize = 25;
  search = '';
  departmentId = '';
  status = '';
  departments = signal<any[]>([]);
  positions = signal<any[]>([]);
  branches = signal<any[]>([]);
  sites = signal<any[]>([]);
  showNew = signal(false);
  removeTarget = signal<any | null>(null);
  busy = signal(false);
  error = signal('');
  message = signal('');
  showSeparated = false;
  removeForm = { status: 'Resigned', separationDate: '', reason: '' };
  statuses = ['Probationary', 'Regular', 'Contractual', 'ProjectBased', 'Resigned', 'Terminated', 'Retired', 'OnLeave'];
  form: any = {};

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void {
    this.load();
    this.api.get<any[]>('organization/departments').subscribe(r => this.departments.set(r));
    this.api.get<any[]>('organization/positions').subscribe(r => this.positions.set(r));
    this.api.get<any[]>('organization/branches').subscribe(r => this.branches.set(r));
    this.api.get<any[]>('organization/sites').subscribe(r => this.sites.set(r));
  }

  load(): void {
    const params: Record<string, unknown> = {
      search: this.search, departmentId: this.departmentId, status: this.status,
      page: this.page(), pageSize: this.pageSize
    };
    if (!this.status && !this.showSeparated) params['activeOnly'] = true;
    this.api.get<{ total: number; items: any[] }>('employees', params).subscribe(res => { this.items.set(res.items); this.total.set(res.total); });
  }

  canRemove(): boolean {
    return this.auth.isAdmin() || this.auth.hasRole('HrOfficer');
  }

  isSeparated(status: string): boolean {
    return ['Resigned', 'Terminated', 'Retired'].includes(status);
  }

  openRemove(e: any): void {
    this.removeTarget.set(e);
    this.removeForm = { status: 'Resigned', separationDate: new Date().toISOString().slice(0, 10), reason: '' };
    this.error.set('');
  }

  confirmRemove(): void {
    const target = this.removeTarget();
    if (!target) return;
    if (!this.removeForm.separationDate) {
      this.error.set('Separation date is required.');
      return;
    }
    this.busy.set(true);
    this.api.post<{ message?: string }>(`employees/${target.id}/separate`, {
      status: this.removeForm.status,
      separationDate: this.removeForm.separationDate,
      reason: this.removeForm.reason || null
    }).subscribe({
      next: res => {
        this.busy.set(false);
        this.removeTarget.set(null);
        this.message.set(res.message ?? 'Employee removed.');
        this.load();
        setTimeout(() => this.message.set(''), 4000);
      },
      error: err => { this.busy.set(false); this.error.set(this.readError(err, 'Failed to remove employee.')); }
    });
  }

  pages(): number { return Math.max(1, Math.ceil(this.total() / this.pageSize)); }

  initials(name: string): string { return (name || '?').split(' ').map(p => p[0]).slice(0, 2).join('').toUpperCase(); }

  openNew(): void {
    this.form = { status: 'Probationary', monthlySalary: 0, hireDate: new Date().toISOString().slice(0, 10) };
    this.error.set('');
    this.showNew.set(true);
  }

  save(): void {
    if (!this.form.employeeCode || !this.form.firstName || !this.form.lastName) {
      this.error.set('Employee code, first name and last name are required.');
      return;
    }
    this.busy.set(true);
    const { status: _status, ...payload } = this.form;
    this.api.post('employees', payload).subscribe({
      next: () => { this.busy.set(false); this.showNew.set(false); this.load(); },
      error: err => { this.busy.set(false); this.error.set(this.readError(err)); }
    });
  }

  private readError(err: any, fallback = 'Failed to create employee.'): string {
    const body = err?.error;
    if (body?.message) return body.message;
    if (body?.errors) {
      const first = Object.values(body.errors as Record<string, string[]>)[0];
      if (Array.isArray(first) && first[0]) return first[0];
    }
    if (body?.title) return body.title;
    return fallback;
  }
}
