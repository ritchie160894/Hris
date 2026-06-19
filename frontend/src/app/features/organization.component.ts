import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-organization',
  imports: [FormsModule],
  styles: [`
    .btn.danger { color: var(--danger); }
    .btn.danger:hover { background: var(--danger-soft); }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Organization</h1><div class="sub">Company, branches, sites, departments, positions and holidays</div></div>
      </div>

      <div class="tabs">
        @for (t of ['Branches','Sites','Departments','Positions','Holidays','Company']; track t) {
          <button [class.active]="tab() === t" (click)="tab.set(t)">{{ t }}</button>
        }
      </div>

      @if (message()) { <div class="alert success">{{ message() }}</div> }
      @if (pageError()) { <div class="alert error">{{ pageError() }}</div> }

      @if (tab() === 'Branches') {
        <div class="card">
          <div class="row mb"><div class="spacer"></div>@if (auth.isAdmin()) { <button class="btn sm" (click)="newBranch()">＋ Branch</button> }</div>
          <table class="data">
            <thead><tr><th>Code</th><th>Name</th><th>Address</th><th class="num">Sites</th><th>Status</th><th></th></tr></thead>
            <tbody>
              @for (b of branches(); track b.id) {
                <tr>
                  <td class="bold">{{ b.code }}</td><td>{{ b.name }}</td><td class="muted">{{ b.address }}</td>
                  <td class="num">{{ b.sites?.length ?? 0 }}</td>
                  <td><span class="badge {{ b.isActive ? 'active' : 'muted' }}">{{ b.isActive ? 'Active' : 'Inactive' }}</span></td>
                  <td>@if (auth.isAdmin()) { <button class="btn ghost sm" (click)="editBranch(b)">Edit</button> }</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      @if (tab() === 'Sites') {
        <div class="card">
          <div class="row mb"><div class="spacer"></div>@if (auth.isAdmin()) { <button class="btn sm" (click)="newSite()">＋ Site</button> }</div>
          <table class="data">
            <thead><tr><th>Code</th><th>Name</th><th>Branch</th><th class="num">Devices</th><th>Gateway API Key</th><th></th></tr></thead>
            <tbody>
              @for (s of sites(); track s.id) {
                <tr>
                  <td class="bold">{{ s.code }}</td><td>{{ s.name }}</td><td>{{ s.branch?.name }}</td>
                  <td class="num">{{ s.devices?.length ?? 0 }}</td>
                  <td><code class="small">{{ auth.isAdmin() ? s.gatewayApiKey : '••••••••' }}</code></td>
                  <td class="row" style="gap:6px">
                    @if (auth.isAdmin()) {
                      <button class="btn ghost sm" (click)="editSite(s)">Edit</button>
                      <button class="btn ghost sm" (click)="regenKey(s)">↻ Key</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      @if (tab() === 'Departments') {
        <div class="card">
          <div class="row mb"><div class="spacer"></div>@if (auth.isAdmin()) { <button class="btn sm" (click)="newDept()">＋ Department</button> }</div>
          <table class="data">
            <thead><tr><th>Code</th><th>Name</th><th>Branch</th><th>Department Head</th><th></th></tr></thead>
            <tbody>
              @for (d of departments(); track d.id) {
                <tr>
                  <td class="bold">{{ d.code }}</td><td>{{ d.name }}</td><td>{{ d.branch?.name }}</td>
                  <td>{{ d.headEmployee ? d.headEmployee.firstName + ' ' + d.headEmployee.lastName : '—' }}</td>
                  <td class="row" style="gap:6px">
                    @if (auth.isAdmin()) {
                      <button class="btn ghost sm" (click)="editDept(d)">Edit</button>
                      <button class="btn ghost sm danger" (click)="deleteDept(d)">Delete</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      @if (tab() === 'Positions') {
        <div class="card">
          <div class="row mb"><div class="spacer"></div>@if (auth.isAdmin()) { <button class="btn sm" (click)="newPos()">＋ Position</button> }</div>
          <table class="data">
            <thead><tr><th>Code</th><th>Title</th><th>Department</th><th></th></tr></thead>
            <tbody>
              @for (p of positions(); track p.id) {
                <tr>
                  <td class="bold">{{ p.code }}</td><td>{{ p.title }}</td>                  <td>{{ p.department?.name ?? '—' }}</td>
                  <td class="row" style="gap:6px">
                    @if (auth.isAdmin()) {
                      <button class="btn ghost sm" (click)="editPos(p)">Edit</button>
                      <button class="btn ghost sm danger" (click)="deletePos(p)">Delete</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      @if (tab() === 'Holidays') {
        <div class="card">
          <div class="row mb"><div class="spacer"></div>@if (auth.isAdmin()) { <button class="btn sm" (click)="newHoliday()">＋ Holiday</button> }</div>
          <table class="data">
            <thead><tr><th>Date</th><th>Name</th><th>Type</th><th></th></tr></thead>
            <tbody>
              @for (h of holidays(); track h.id) {
                <tr>
                  <td class="bold">{{ h.date }}</td><td>{{ h.name }}</td>
                  <td><span class="badge {{ h.type === 1 ? 'danger' : 'info' }}">{{ h.type === 1 ? 'Regular' : 'Special' }}</span></td>
                  <td>@if (auth.isAdmin()) { <button class="btn ghost sm" (click)="deleteHoliday(h)">Delete</button> }</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      @if (tab() === 'Company') {
        <div class="card" style="max-width:600px">
          <div class="card-title">Company Profile</div>
          @if (company(); as c) {
            <label class="field"><span class="lbl">Name</span><input class="ctl" [(ngModel)]="c.name" [disabled]="!auth.isAdmin()" /></label>
            <label class="field"><span class="lbl">Legal Name</span><input class="ctl" [(ngModel)]="c.legalName" [disabled]="!auth.isAdmin()" /></label>
            <label class="field"><span class="lbl">TIN</span><input class="ctl" [(ngModel)]="c.tin" [disabled]="!auth.isAdmin()" /></label>
            <label class="field"><span class="lbl">Address</span><input class="ctl" [(ngModel)]="c.address" [disabled]="!auth.isAdmin()" /></label>
            <label class="field"><span class="lbl">Contact</span><input class="ctl" [(ngModel)]="c.contactNumber" [disabled]="!auth.isAdmin()" /></label>
            @if (auth.isAdmin()) { <button class="btn" (click)="saveCompany()">Save</button> }
          }
        </div>
      }

      @if (modal()) {
        <div class="modal-backdrop" (click)="modal.set('')">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">{{ form.id ? 'Edit' : 'New' }} {{ modal() }}</div>
            @if (error()) { <div class="alert error">{{ error() }}</div> }
            @if (modal() !== 'Holiday') {
              <div class="form-grid">
                <label class="field"><span class="lbl">Code *</span><input class="ctl" [(ngModel)]="form.code" /></label>
                <label class="field"><span class="lbl">{{ modal() === 'Position' ? 'Title *' : 'Name *' }}</span><input class="ctl" [(ngModel)]="form.name" /></label>
              </div>
              @if (modal() === 'Branch') { <label class="field"><span class="lbl">Address</span><input class="ctl" [(ngModel)]="form.address" /></label> }
              @if (modal() === 'Site' || modal() === 'Department') {
                <label class="field"><span class="lbl">Branch</span>
                  <select class="ctl" [(ngModel)]="form.branchId"><option [ngValue]="null">—</option>@for (b of branches(); track b.id) { <option [ngValue]="b.id">{{ b.name }}</option> }</select></label>
              }
              @if (modal() === 'Department') {
                <label class="field"><span class="lbl">Department Head</span>
                  <select class="ctl" [(ngModel)]="form.headEmployeeId">
                    <option [ngValue]="null">— Not assigned —</option>
                    @for (e of deptHeadCandidates(); track e.id) {
                      <option [ngValue]="e.id">{{ e.employeeCode }} · {{ e.fullName }}</option>
                    }
                  </select></label>
                <div class="muted small">Official head shown in this table. Pick an employee in this department, or assign a <b>Department Head</b> user under Users &amp; Roles (syncs automatically on save).</div>
              }
              @if (modal() === 'Site') { <label class="field"><span class="lbl">Address</span><input class="ctl" [(ngModel)]="form.address" /></label> }
              @if (modal() === 'Position') {
                <label class="field"><span class="lbl">Department</span>
                  <select class="ctl" [(ngModel)]="form.departmentId">
                    <option [ngValue]="null">— None (company-wide) —</option>
                    @for (d of departments(); track d.id) { <option [ngValue]="d.id">{{ d.name }}</option> }
                  </select></label>
                <div class="muted small">Link this position to a department (e.g. Finance Department Head → Finance). Leave empty only for company-wide roles like CEO.</div>
              }
            } @else {
              <label class="field"><span class="lbl">Date *</span><input class="ctl" type="date" [(ngModel)]="form.date" /></label>
              <label class="field"><span class="lbl">Name *</span><input class="ctl" [(ngModel)]="form.name" /></label>
              <label class="field"><span class="lbl">Type</span>
                <select class="ctl" [(ngModel)]="form.type"><option [ngValue]="1">Regular Holiday</option><option [ngValue]="2">Special Non-Working</option></select></label>
            }
            <div class="modal-actions">
              <button class="btn secondary" (click)="modal.set('')">Cancel</button>
              <button class="btn" (click)="save()">Save</button>
            </div>
          </div>
        </div>
      }
    </div>
  `
})
export class OrganizationComponent implements OnInit {
  tab = signal('Branches');
  branches = signal<any[]>([]);
  sites = signal<any[]>([]);
  departments = signal<any[]>([]);
  positions = signal<any[]>([]);
  holidays = signal<any[]>([]);
  deptHeadCandidates = signal<any[]>([]);
  company = signal<any | null>(null);
  modal = signal('');
  message = signal('');
  pageError = signal('');
  error = signal('');
  form: any = {};

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void { this.loadAll(); }

  loadAll(): void {
    this.api.get<any[]>('organization/branches').subscribe(r => this.branches.set(r));
    this.api.get<any[]>('organization/sites').subscribe(r => this.sites.set(r));
    this.api.get<any[]>('organization/departments').subscribe(r => this.departments.set(r));
    this.api.get<any[]>('organization/positions').subscribe(r => this.positions.set(r));
    this.api.get<any[]>('organization/holidays').subscribe(r => this.holidays.set(r));
    this.api.get<any>('organization/company').subscribe(r => this.company.set(r));
  }

  newBranch(): void { this.form = { isActive: true }; this.modal.set('Branch'); }
  editBranch(b: any): void { this.form = { ...b }; this.modal.set('Branch'); }
  newSite(): void { this.form = { isActive: true }; this.modal.set('Site'); }
  editSite(s: any): void { this.form = { ...s }; this.modal.set('Site'); }
  newDept(): void { this.form = { isActive: true, headEmployeeId: null }; this.deptHeadCandidates.set([]); this.modal.set('Department'); }
  editDept(d: any): void {
    this.form = { ...d, headEmployeeId: d.headEmployeeId ?? null };
    this.modal.set('Department');
    this.loadDeptHeadCandidates(d.id, d.headEmployeeId, d.headEmployee);
  }

  loadDeptHeadCandidates(departmentId: number, currentHeadId?: number | null, currentHead?: any): void {
    this.api.get<{ items: any[] }>('employees', { departmentId, activeOnly: true, pageSize: 100 }).subscribe({
      next: res => {
        let items = res.items ?? [];
        if (currentHeadId && !items.some(e => e.id === currentHeadId) && currentHead) {
          items = [{
            id: currentHeadId,
            employeeCode: currentHead.employeeCode,
            fullName: [currentHead.firstName, currentHead.lastName].filter(Boolean).join(' ')
          }, ...items];
        }
        this.deptHeadCandidates.set(items);
      },
      error: () => this.deptHeadCandidates.set([])
    });
  }
  newPos(): void { this.form = { isActive: true, departmentId: null }; this.modal.set('Position'); }
  editPos(p: any): void { this.form = { ...p, name: p.title, departmentId: p.departmentId ?? null }; this.modal.set('Position'); }
  newHoliday(): void { this.form = { type: 1 }; this.modal.set('Holiday'); }

  save(): void {
    this.error.set('');
    const kind = this.modal();
    let path = '';
    let body: any = { ...this.form };
    if (kind === 'Branch') path = 'organization/branches';
    if (kind === 'Site') path = 'organization/sites';
    if (kind === 'Department') path = 'organization/departments';
    if (kind === 'Position') { path = 'organization/positions'; body.title = body.name; }
    if (kind === 'Holiday') path = 'organization/holidays';
    // strip nav props to avoid serializer issues
    delete body.branch; delete body.sites; delete body.devices; delete body.headEmployee; delete body.department; delete body.company;

    const req$ = this.form.id && kind !== 'Holiday'
      ? this.api.put(`${path}/${this.form.id}`, body)
      : this.api.post(path, body);
    req$.subscribe({
      next: () => { this.modal.set(''); this.message.set('Saved.'); this.loadAll(); setTimeout(() => this.message.set(''), 3000); },
      error: err => this.error.set(err?.error?.message ?? 'Save failed.')
    });
  }

  saveCompany(): void {
    this.api.put('organization/company', this.company()).subscribe(() => {
      this.message.set('Company profile saved.');
      setTimeout(() => this.message.set(''), 3000);
    });
  }

  regenKey(s: any): void {
    this.api.post(`organization/sites/${s.id}/regenerate-key`, {}).subscribe(() => this.loadAll());
  }

  deleteHoliday(h: any): void {
    if (!confirm(`Permanently delete holiday "${h.name}" on ${h.date}?`)) return;
    this.pageError.set('');
    this.api.delete(`organization/holidays/${h.id}`).subscribe({
      next: () => { this.message.set('Holiday deleted.'); this.loadAll(); setTimeout(() => this.message.set(''), 3000); },
      error: err => { this.pageError.set(err?.error?.message ?? 'Delete failed.'); }
    });
  }

  deleteDept(d: any): void {
    if (!confirm(`Permanently delete department "${d.name}" (${d.code})?\n\nThis cannot be undone. Employees or positions still linked to it will block deletion.`)) return;
    this.pageError.set('');
    this.api.delete(`organization/departments/${d.id}`).subscribe({
      next: () => { this.message.set('Department deleted permanently.'); this.loadAll(); setTimeout(() => this.message.set(''), 3000); },
      error: err => { this.pageError.set(err?.error?.message ?? 'Delete failed.'); }
    });
  }

  deletePos(p: any): void {
    if (!confirm(`Permanently delete position "${p.title}" (${p.code})?\n\nThis cannot be undone. Employees still assigned to it will block deletion.`)) return;
    this.pageError.set('');
    this.api.delete(`organization/positions/${p.id}`).subscribe({
      next: () => { this.message.set('Position deleted permanently.'); this.loadAll(); setTimeout(() => this.message.set(''), 3000); },
      error: err => { this.pageError.set(err?.error?.message ?? 'Delete failed.'); }
    });
  }
}
