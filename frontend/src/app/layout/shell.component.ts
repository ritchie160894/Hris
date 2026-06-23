import { Component, HostListener, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { DatePipe } from '@angular/common';
import { fromEvent, merge, timer } from 'rxjs';
import { filter, switchMap } from 'rxjs/operators';
import { AuthService } from '../core/auth.service';
import { ApiService } from '../core/api.service';

interface NavItem { label: string; icon: string; link: string; roles?: string[]; }
interface NavGroup { title: string; items: NavItem[]; }

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, DatePipe],
  styles: [`
    :host { display: block; height: 100vh; }
    .layout { display: flex; height: 100%; }
    aside {
      width: 248px; background: var(--sidebar-bg); color: #c6cede; display: flex; flex-direction: column;
      flex-shrink: 0; transition: margin-left .2s;
      &.closed { margin-left: -248px; }
    }
    .brand {
      padding: 14px 16px; border-bottom: 1px solid rgba(255,255,255,.08);
      display: flex; align-items: center; gap: 11px;
    }
    nav { flex: 1; overflow-y: auto; padding: 10px 0 24px; }
    .group-title { padding: 16px 20px 6px; font-size: 10.5px; font-weight: 700; letter-spacing: .1em; text-transform: uppercase; color: #6b7693; }
    nav a {
      display: flex; align-items: center; gap: 11px; padding: 9px 20px; color: #c6cede;
      font-size: 13.5px; font-weight: 500;
      .ic { width: 18px; text-align: center; opacity: .85; }
      &:hover { background: rgba(255,255,255,.05); color: #fff; }
      &.active { background: var(--sidebar-active); color: #fff; box-shadow: inset 3px 0 0 var(--primary); }
    }
    main { flex: 1; display: flex; flex-direction: column; min-width: 0; }
    header {
      background: #fff; border-bottom: 1px solid var(--border); padding: 0 20px;
      height: 58px; display: flex; align-items: center; gap: 14px; flex-shrink: 0;
    }
    .burger { background: none; border: none; font-size: 19px; cursor: pointer; color: var(--text-soft); padding: 6px; }
    .who { text-align: right; .nm { font-weight: 600; font-size: 13.5px; } .rl { font-size: 11.5px; color: var(--text-faint); } }
    .bell { position: relative; background: none; border: none; cursor: pointer; font-size: 18px; padding: 6px; color: var(--text-soft);
      .dot { position: absolute; top: 0; right: 0; background: var(--danger); color: #fff; font-size: 10px; font-weight: 700;
        border-radius: 999px; padding: 1px 5px; min-width: 16px; }
    }
    .notif-panel {
      position: absolute; top: 54px; right: 16px; width: 360px; max-height: 480px; overflow-y: auto;
      background: #fff; border: 1px solid var(--border); border-radius: 12px; box-shadow: 0 12px 40px rgba(0,0,0,.16); z-index: 50;
      .n-head { display: flex; justify-content: space-between; align-items: center; padding: 12px 16px; border-bottom: 1px solid var(--border); font-weight: 700; }
      .n-item {
        display: flex; align-items: flex-start; gap: 8px; padding: 11px 12px 11px 16px;
        border-bottom: 1px solid var(--border);
        &.unread { background: var(--primary-soft); }
        .n-body { flex: 1; min-width: 0; cursor: pointer;
          .t { font-weight: 600; font-size: 13px; } .m { font-size: 12.5px; color: var(--text-soft); }
          .d { font-size: 11px; color: var(--text-faint); margin-top: 2px; }
          &:hover { opacity: .92; }
        }
        .n-del { flex-shrink: 0; color: var(--text-faint); font-size: 12px; padding: 4px 8px;
          &:hover { color: var(--danger); background: var(--danger-soft); }
        }
      }
    }
    .content { flex: 1; overflow-y: auto; }
    .sidebar-backdrop {
      position: fixed; inset: 0; background: rgba(15, 23, 40, .45); z-index: 55;
    }
    @media (max-width: 900px) {
      aside { position: fixed; z-index: 60; height: 100%; box-shadow: 4px 0 24px rgba(0,0,0,.18); }
      header { position: relative; }
    }
  `],
  template: `
    <div class="layout" [class.portal-shell]="auth.isPortalUser()">
      @if (auth.isPortalUser() && sidebarOpen() && isMobile()) {
        <div class="sidebar-backdrop" (click)="sidebarOpen.set(false)"></div>
      }
      <aside [class.closed]="!sidebarOpen()">
        <div class="brand">
          <img src="logo-mark.svg" alt="" class="brand-mark" />
          <div class="brand-copy">
            <strong>Qcon</strong>
            <span>HriSystem</span>
          </div>
        </div>
        <nav>
          @for (group of visibleGroups(); track group.title) {
            <div class="group-title">{{ group.title }}</div>
            @for (item of group.items; track item.link) {
              <a [routerLink]="item.link" routerLinkActive="active" (click)="closeSidebarIfMobile()"><span class="ic">{{ item.icon }}</span>{{ item.label }}</a>
            }
          }
        </nav>
      </aside>
      <main>
        <header>
          <button class="burger" (click)="sidebarOpen.set(!sidebarOpen())">☰</button>
          <div class="spacer"></div>
          <button class="bell" (click)="toggleNotifs()">
            🔔 @if (unread() > 0) { <span class="dot">{{ unread() }}</span> }
          </button>
          <div class="who">
            <div class="nm">{{ auth.user()?.displayName }}</div>
            <div class="rl">{{ roleLabel() }}</div>
          </div>
          <div class="avatar">{{ initials() }}</div>
          <button class="btn secondary sm" (click)="auth.logout()">Logout</button>
          @if (showNotifs()) {
            <div class="notif-panel">
              <div class="n-head">
                Notifications
                <button class="btn ghost sm" (click)="markAllRead()">Mark all read</button>
              </div>
              @for (n of notifs(); track n.id) {
                <div class="n-item" [class.unread]="!n.isRead">
                  <div class="n-body" (click)="readNotif(n)">
                    <div class="t">{{ n.title }}</div>
                    <div class="m">{{ n.message }}</div>
                    <div class="d">{{ n.createdAt | date:'MMM d, h:mm a' }}</div>
                  </div>
                  <button type="button" class="btn ghost sm n-del" title="Delete" (click)="deleteNotif(n, $event)">Delete</button>
                </div>
              } @empty {
                <div class="empty">No notifications</div>
              }
            </div>
          }
        </header>
        <div class="content" (click)="showNotifs.set(false)">
          <router-outlet />
        </div>
      </main>
    </div>
  `
})
export class ShellComponent {
  sidebarOpen = signal(window.innerWidth > 900);
  isMobile = signal(window.innerWidth <= 900);
  showNotifs = signal(false);
  unread = signal(0);
  notifs = signal<any[]>([]);
  private readonly api = inject(ApiService);

  // Executives (VP & HR Head, President & CEO) are owners: their portal is approval-only
  // plus announcements — no timekeeping, compensation, or admin pages.
  private static readonly STAFF = ['SuperAdministrator', 'HrAdministrator', 'HrOfficer', 'PayrollOfficer', 'DepartmentHead', 'Supervisor', 'Employee'];

  private readonly groups: NavGroup[] = [
    {
      title: 'Overview',
      items: [
        { label: 'Dashboard', icon: '▦', link: '/dashboard', roles: ShellComponent.STAFF },
        { label: 'Approvals', icon: '✓', link: '/approvals', roles: ['SuperAdministrator', 'HrAdministrator', 'HrOfficer', 'DepartmentHead', 'Supervisor', 'VicePresidentHrHead', 'PresidentCeo'] },
        { label: 'Payroll Review', icon: '₱', link: '/executive-payroll', roles: ['VicePresidentHrHead', 'PresidentCeo'] },
        { label: 'My Portal', icon: '☺', link: '/me', roles: ShellComponent.STAFF },
        { label: 'Announcements', icon: '📣', link: '/announcements' }
      ]
    },
    {
      title: 'Workforce',
      items: [
        { label: 'Employees', icon: '👥', link: '/employees', roles: ['SuperAdministrator', 'HrAdministrator', 'HrOfficer', 'DepartmentHead', 'Supervisor', 'PayrollOfficer'] },
        { label: 'Organization', icon: '🏢', link: '/organization', roles: ['SuperAdministrator', 'HrAdministrator', 'HrOfficer'] },
        { label: 'Attendance', icon: '🕐', link: '/attendance', roles: ShellComponent.STAFF },
        { label: 'Leave', icon: '🌴', link: '/leave', roles: ShellComponent.STAFF },
        { label: 'Overtime', icon: '⏱', link: '/overtime', roles: ShellComponent.STAFF }
      ]
    },
    {
      title: 'Compensation',
      items: [
        { label: 'Payroll', icon: '₱', link: '/payroll', roles: ['SuperAdministrator', 'HrAdministrator', 'PayrollOfficer'] },
        { label: 'Loans & Advances', icon: '💳', link: '/loans', roles: ShellComponent.STAFF },
        { label: 'Government', icon: '🏛', link: '/government', roles: ['SuperAdministrator', 'HrAdministrator', 'PayrollOfficer'] },
        { label: 'Benefits', icon: '🎁', link: '/benefits', roles: ShellComponent.STAFF }
      ]
    },
    {
      title: 'Talent',
      items: [
        { label: 'Performance', icon: '📈', link: '/performance', roles: ['SuperAdministrator', 'HrAdministrator', 'HrOfficer', 'DepartmentHead', 'Supervisor', 'PayrollOfficer'] },
        { label: 'Training', icon: '🎓', link: '/training', roles: ['SuperAdministrator', 'HrAdministrator', 'HrOfficer'] },
        { label: 'Documents', icon: '📁', link: '/documents', roles: ShellComponent.STAFF }
      ]
    },
    {
      title: 'System',
      items: [
        { label: 'Reports', icon: '📊', link: '/reports', roles: ['SuperAdministrator', 'HrAdministrator', 'HrOfficer', 'PayrollOfficer', 'DepartmentHead'] },
        { label: 'Devices', icon: '📟', link: '/devices', roles: ['SuperAdministrator', 'HrAdministrator'] },
        { label: 'Sync Monitor', icon: '🔄', link: '/sync', roles: ['SuperAdministrator', 'HrAdministrator'] },
        { label: 'Users & Roles', icon: '🔐', link: '/users', roles: ['SuperAdministrator', 'HrAdministrator'] },
        { label: 'Audit Trail', icon: '📜', link: '/audit', roles: ['SuperAdministrator', 'HrAdministrator'] }
      ]
    }
  ];

  visibleGroups = computed(() => {
    const role = this.auth.role();
    return this.groups
      .map(g => ({ ...g, items: g.items.filter(i => !i.roles || i.roles.includes(role)) }))
      .filter(g => g.items.length > 0);
  });

  constructor(public auth: AuthService) {
    merge(
      timer(0, 15000),
      fromEvent(document, 'visibilitychange').pipe(filter(() => document.visibilityState === 'visible')),
      fromEvent(window, 'focus')
    ).pipe(
      filter(() => document.visibilityState === 'visible'),
      switchMap(() => this.api.get<{ unread: number; items: any[] }>('notifications', { take: 15 })),
      takeUntilDestroyed()
    ).subscribe({
      next: res => { this.unread.set(res.unread); this.notifs.set(res.items); },
      error: () => {}
    });
  }

  @HostListener('window:resize')
  onResize(): void {
    const mobile = window.innerWidth <= 900;
    this.isMobile.set(mobile);
    if (!mobile) this.sidebarOpen.set(true);
  }

  closeSidebarIfMobile(): void {
    if (this.isMobile()) this.sidebarOpen.set(false);
  }

  initials(): string {
    const n = this.auth.user()?.displayName ?? '?';
    return n.split(' ').map(p => p[0]).slice(0, 2).join('').toUpperCase();
  }

  roleLabel(): string {
    return (this.auth.role() || '').replace(/([A-Z])/g, ' $1').trim()
      .replace('Hr ', 'HR ').replace('Ceo', 'CEO');
  }

  toggleNotifs(): void {
    this.showNotifs.set(!this.showNotifs());
    if (this.showNotifs()) this.loadNotifs();
  }

  loadNotifs(): void {
    this.api.get<{ unread: number; items: any[] }>('notifications', { take: 15 }).subscribe({
      next: res => { this.unread.set(res.unread); this.notifs.set(res.items); },
      error: () => {}
    });
  }

  readNotif(n: any): void {
    if (!n.isRead) this.api.post(`notifications/${n.id}/read`, {}).subscribe(() => this.loadNotifs());
  }

  markAllRead(): void {
    this.api.post('notifications/read-all', {}).subscribe(() => this.loadNotifs());
  }

  deleteNotif(n: any, event: Event): void {
    event.stopPropagation();
    this.api.delete(`notifications/${n.id}`).subscribe(() => this.loadNotifs());
  }
}
