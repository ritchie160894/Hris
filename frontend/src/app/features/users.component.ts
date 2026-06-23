import { Component, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-users',
  imports: [FormsModule, DatePipe],
  styles: [`
    .pagination { display: flex; align-items: center; gap: 10px; padding: 12px 0 0; font-size: 13px; color: var(--text-soft); }
    .btn.danger { color: var(--danger); }
    .btn.danger:hover { background: var(--danger-soft); }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Users & Roles</h1><div class="sub">{{ total() }} account(s)</div></div>
        <button class="btn" (click)="openNew()">＋ New User</button>
      </div>

      @if (message()) { <div class="alert success">{{ message() }}</div> }
      @if (pageError()) { <div class="alert error">{{ pageError() }}</div> }

      <div class="card mb">
        <div class="row">
          <input class="ctl" style="max-width:260px" placeholder="Search username or name…" [(ngModel)]="search" (input)="page.set(1); load()" />
          <select class="ctl" style="max-width:220px" [(ngModel)]="roleFilter" (change)="page.set(1); load()">
            <option value="">All roles</option>
            @for (r of roles; track r.v) { <option [value]="r.v">{{ r.n }}</option> }
          </select>
          <select class="ctl" style="max-width:220px" [(ngModel)]="departmentFilter" (change)="page.set(1); load()">
            <option value="">All departments</option>
            @for (d of departments(); track d.id) { <option [value]="d.id">{{ d.name }}</option> }
          </select>
        </div>
      </div>

      <div class="card">
        <div class="table-wrap">
          <table class="data">
            <thead><tr><th>Username</th><th>Display Name</th><th>Role</th><th>Department</th><th>Linked Employee</th><th>Last Login</th><th>Status</th><th></th></tr></thead>
            <tbody>
              @for (u of users(); track u.id) {
                <tr>
                  <td class="bold">{{ u.username }}</td>
                  <td>{{ u.displayName }}</td>
                  <td><span class="badge">{{ roleLabel(u.role) }}</span></td>
                  <td>{{ u.department ?? '—' }}</td>
                  <td>{{ u.employee ? (u.employeeCode ? u.employeeCode + ' · ' : '') + u.employee : '—' }}</td>
                  <td class="muted small">{{ u.lastLoginAt ? (u.lastLoginAt | date:'MMM d, h:mm a') : 'never' }}</td>
                  <td>
                    @if (u.isLocked) { <span class="badge locked">Locked</span> }
                    @else { <span class="badge {{ u.isActive ? 'active' : 'muted' }}">{{ u.isActive ? 'Active' : 'Disabled' }}</span> }
                  </td>
                  <td class="row" style="gap:6px">
                    <button class="btn ghost sm" (click)="edit(u)">Edit</button>
                    @if (u.isLocked) { <button class="btn secondary sm" (click)="unlock(u)">Unlock</button> }
                    @if (canDelete(u)) { <button class="btn ghost sm danger" (click)="deleteUser(u)">Delete</button> }
                  </td>
                </tr>
              } @empty {
                <tr><td colspan="8"><div class="empty">No users found</div></td></tr>
              }
            </tbody>
          </table>
        </div>
        <div class="pagination">
          <span>Page {{ page() }} of {{ totalPages() }} · {{ total() }} account(s) · {{ pageSize }} rows/page</span>
          <button class="btn secondary sm" [disabled]="page() <= 1" (click)="page.set(page() - 1); load()">Previous</button>
          <button class="btn secondary sm" [disabled]="page() >= totalPages()" (click)="page.set(page() + 1); load()">Next</button>
        </div>
      </div>

      <div class="card mt">
        <div class="card-title">Role Capabilities</div>
        <table class="data">
          <thead><tr><th>Role</th><th>Access</th></tr></thead>
          <tbody>
            <tr><td><span class="badge">Super Administrator</span></td><td>Full system access including users, audit, devices and sync.</td></tr>
            <tr><td><span class="badge">HR Administrator</span></td><td>All HR modules, organization setup, reports, audit.</td></tr>
            <tr><td><span class="badge">Payroll Officer</span></td><td>Payroll processing, loans, government contributions, payroll reports. No approvals.</td></tr>
            <tr><td><span class="badge">HR Officer</span></td><td>Employee records, leave/OT review (level 2 approver), training.</td></tr>
            <tr><td><span class="badge">Department Head</span></td><td>Own department employees and requests; level 1 approver.</td></tr>
            <tr><td><span class="badge">Vice President & HR Head</span></td><td>Executive portal: approves leave/SIL/OT/loans/cash advances and payroll. No payroll processing.</td></tr>
            <tr><td><span class="badge">President & CEO</span></td><td>Executive portal: final approvals for leave and SIL only.</td></tr>
            <tr><td><span class="badge">Employee</span></td><td>Self-service portal: own attendance, payslips, requests.</td></tr>
          </tbody>
        </table>
      </div>

      @if (showForm()) {
        <div class="modal-backdrop" (click)="showForm.set(false)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">{{ form.id ? 'Edit User' : 'New User' }}</div>
            @if (error()) { <div class="alert error">{{ error() }}</div> }
            <div class="form-grid">
              @if (!form.id) { <label class="field"><span class="lbl">Username *</span><input class="ctl" [(ngModel)]="form.username" /></label> }
              <label class="field"><span class="lbl">Display Name *</span><input class="ctl" [(ngModel)]="form.displayName" /></label>
              <label class="field"><span class="lbl">Role *</span>
                <select class="ctl" [(ngModel)]="form.role">
                  @for (r of roles; track r.v) { <option [value]="r.v">{{ r.n }}</option> }
                </select></label>
              <label class="field"><span class="lbl">Department (filter employees)</span>
                <select class="ctl" [(ngModel)]="formDepartmentFilter" (change)="loadEmployeeOptions()">
                  <option value="">All departments</option>
                  @for (d of departments(); track d.id) { <option [value]="d.id">{{ d.name }}</option> }
                </select></label>
              <label class="field"><span class="lbl">Link to Employee (optional)</span>
                <select class="ctl" [(ngModel)]="form.employeeId">
                  <option [ngValue]="null">— No employee link —</option>
                  @for (e of employeeOptions(); track e.id) {
                    <option [ngValue]="e.id">{{ e.employeeCode }} · {{ e.name }}@if (e.department) { ({{ e.department }}) }</option>
                  }
                </select></label>
              <div class="muted small" style="grid-column:1/-1">Department comes from the linked employee record. Add departments under Organization → Departments, assign employees under Employees, then link them here.</div>
              <label class="field"><span class="lbl">Email</span><input class="ctl" [(ngModel)]="form.email" /></label>
              <label class="field"><span class="lbl">{{ form.id ? 'New Password (blank = keep)' : 'Password *' }}</span><input class="ctl" type="password" [(ngModel)]="form.password" /></label>
            </div>
            @if (form.id) { <label class="field"><span class="lbl"><input type="checkbox" [(ngModel)]="form.isActive" /> Active</span></label> }
            <div class="modal-actions">
              <button class="btn secondary" (click)="showForm.set(false)">Cancel</button>
              <button class="btn" (click)="save()">Save</button>
            </div>
          </div>
        </div>
      }
    </div>
  `
})
export class UsersComponent implements OnInit {
  users = signal<any[]>([]);
  departments = signal<any[]>([]);
  employeeOptions = signal<any[]>([]);
  total = signal(0);
  page = signal(1);
  readonly pageSize = 25;
  search = '';
  roleFilter = '';
  departmentFilter = '';
  formDepartmentFilter = '';
  showForm = signal(false);
  message = signal('');
  pageError = signal('');
  error = signal('');
  form: any = {};

  roles = [
    { v: 'SuperAdministrator', n: 'Super Administrator' },
    { v: 'HrAdministrator', n: 'HR Administrator' },
    { v: 'PayrollOfficer', n: 'Payroll Officer' },
    { v: 'HrOfficer', n: 'HR Officer' },
    { v: 'DepartmentHead', n: 'Department Head' },
    { v: 'Supervisor', n: 'Supervisor' },
    { v: 'VicePresidentHrHead', n: 'Vice President & HR Head' },
    { v: 'PresidentCeo', n: 'President & CEO' },
    { v: 'Employee', n: 'Employee' }
  ];

  constructor(private api: ApiService, private auth: AuthService) {}

  ngOnInit(): void {
    this.load();
    this.loadDepartments();
  }

  totalPages(): number {
    return Math.max(1, Math.ceil(this.total() / this.pageSize));
  }

  loadDepartments(): void {
    this.api.get<any[]>('organization/departments').subscribe({
      next: r => this.departments.set(r ?? []),
      error: () => this.departments.set([])
    });
  }

  loadEmployeeOptions(): void {
    const params: Record<string, unknown> = {};
    if (this.formDepartmentFilter) params['departmentId'] = this.formDepartmentFilter;
    if (this.form.id) params['forUserId'] = this.form.id;
    this.api.get<any[]>('users/employee-options', params).subscribe({
      next: r => this.employeeOptions.set(r ?? []),
      error: () => this.employeeOptions.set([])
    });
  }

  load(): void {
    const params: Record<string, unknown> = { page: this.page(), pageSize: this.pageSize };
    if (this.search.trim()) params['search'] = this.search.trim();
    if (this.roleFilter) params['role'] = this.roleFilter;
    if (this.departmentFilter) params['departmentId'] = this.departmentFilter;
    this.api.get<{ total: number; items: any[] }>('users', params).subscribe({
      next: res => {
        this.users.set(res.items ?? []);
        this.total.set(res.total ?? 0);
      },
      error: () => {
        this.users.set([]);
        this.total.set(0);
      }
    });
  }

  roleLabel(r: string): string { return this.roles.find(x => x.v === r)?.n ?? r; }

  openNew(): void {
    this.form = { role: 'Employee', isActive: true, employeeId: null };
    this.formDepartmentFilter = '';
    this.error.set('');
    this.showForm.set(true);
    this.loadEmployeeOptions();
  }

  edit(u: any): void {
    this.form = { ...u, password: '', employeeId: u.employeeId ?? null };
    this.formDepartmentFilter = u.departmentId ? String(u.departmentId) : '';
    this.error.set('');
    this.showForm.set(true);
    this.loadEmployeeOptions();
  }

  save(): void {
    const f = this.form;
    if (!f.displayName || (!f.id && (!f.username || !f.password))) {
      this.error.set('Username, display name and password are required.');
      return;
    }
    const req$ = f.id
      ? this.api.put(`users/${f.id}`, { displayName: f.displayName, role: f.role, employeeId: f.employeeId || null, email: f.email, isActive: f.isActive, newPassword: f.password || null })
      : this.api.post('users', { username: f.username, password: f.password, displayName: f.displayName, role: f.role, employeeId: f.employeeId || null, email: f.email });
    req$.subscribe({
      next: () => { this.showForm.set(false); this.message.set('User saved.'); this.load(); setTimeout(() => this.message.set(''), 3000); },
      error: err => this.error.set(err?.error?.message ?? 'Save failed.')
    });
  }

  unlock(u: any): void {
    this.api.post(`users/${u.id}/unlock`, {}).subscribe(() => this.load());
  }

  canDelete(u: any): boolean {
    return u.id !== this.auth.user()?.id;
  }

  deleteUser(u: any): void {
    if (!confirm(`Permanently delete user "${u.username}" (${u.displayName})?\n\nThis cannot be undone. The employee record is kept; only the login account is removed.`)) return;
    this.pageError.set('');
    this.api.delete<{ message?: string }>(`users/${u.id}`).subscribe({
      next: res => {
        this.message.set(res?.message ?? 'User deleted permanently.');
        if (this.users().length === 1 && this.page() > 1) this.page.set(this.page() - 1);
        this.load();
        setTimeout(() => this.message.set(''), 4000);
      },
      error: err => this.pageError.set(this.deleteErrorMessage(err))
    });
  }

  private deleteErrorMessage(err: any): string {
    const body = err?.error;
    if (body?.message) return body.message;
    if (err?.status === 405) return 'Delete is not available on the server. Restart the backend API, then try again.';
    if (err?.status === 404) return 'User not found, or the delete endpoint is missing. Restart the backend API.';
    if (err?.status === 0) return 'Cannot reach the API. Check that the backend is running on port 5000.';
    if (body?.title) return body.title;
    return 'Delete failed.';
  }
}
