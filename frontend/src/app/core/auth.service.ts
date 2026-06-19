import { Injectable, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { tap } from 'rxjs';
import { environment } from '../../environments/environment';

export interface SessionUser {
  id: number;
  username: string;
  displayName: string;
  role: string;
  employeeId?: number | null;
  employeeName?: string | null;
  departmentId?: number | null;
}

const APPROVER_ROLES = ['SuperAdministrator', 'DepartmentHead', 'HrOfficer', 'VicePresidentHrHead', 'PresidentCeo', 'HrAdministrator', 'Supervisor'];
const HR_ROLES = ['SuperAdministrator', 'HrAdministrator', 'HrOfficer', 'VicePresidentHrHead'];

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly tokenKey = 'hris.token';
  private readonly userKey = 'hris.user';

  readonly user = signal<SessionUser | null>(this.restoreUser());
  readonly isLoggedIn = computed(() => !!this.user());
  readonly role = computed(() => this.user()?.role ?? '');
  readonly isApprover = computed(() => APPROVER_ROLES.includes(this.role()));
  readonly isHr = computed(() => HR_ROLES.includes(this.role()));
  readonly isAdmin = computed(() => ['SuperAdministrator', 'HrAdministrator'].includes(this.role()));
  readonly isPayroll = computed(() => ['SuperAdministrator', 'HrAdministrator', 'PayrollOfficer'].includes(this.role()));
  /// Executive portal users see a simplified approvals-only experience.
  readonly isExecutive = computed(() => ['VicePresidentHrHead', 'PresidentCeo'].includes(this.role()));
  /// Employee + executive portals get mobile-optimized layout (not full HR admin UI).
  readonly isPortalUser = computed(() => ['Employee', 'VicePresidentHrHead', 'PresidentCeo'].includes(this.role()));

  homeRoute(): string {
    if (this.isExecutive()) return '/approvals';
    if (this.role() === 'Employee') return '/me';
    return '/dashboard';
  }

  constructor(private http: HttpClient, private router: Router) {}

  login(username: string, password: string) {
    return this.http.post<{ token: string; user: SessionUser }>(`${environment.apiUrl}/auth/login`, { username, password })
      .pipe(tap(res => {
        localStorage.setItem(this.tokenKey, res.token);
        localStorage.setItem(this.userKey, JSON.stringify(res.user));
        this.user.set(res.user);
      }));
  }

  logout(): void {
    localStorage.removeItem(this.tokenKey);
    localStorage.removeItem(this.userKey);
    this.user.set(null);
    this.router.navigate(['/login']);
  }

  get token(): string | null {
    return localStorage.getItem(this.tokenKey);
  }

  hasRole(...roles: string[]): boolean {
    return roles.includes(this.role());
  }

  /** Sync employee link from server (fixes stale sessions missing employeeId). */
  refreshProfile() {
    return this.http.get<{ employeeId?: number | null; employee?: { fullName?: string } | null }>(`${environment.apiUrl}/auth/me`)
      .pipe(tap(me => {
        const current = this.user();
        if (!current) return;
        const next: SessionUser = {
          ...current,
          employeeId: me.employeeId ?? current.employeeId ?? null,
          employeeName: me.employee?.fullName ?? current.employeeName ?? null
        };
        localStorage.setItem(this.userKey, JSON.stringify(next));
        this.user.set(next);
      }));
  }

  private restoreUser(): SessionUser | null {
    try {
      const raw = localStorage.getItem(this.userKey);
      return raw ? JSON.parse(raw) : null;
    } catch {
      return null;
    }
  }
}
