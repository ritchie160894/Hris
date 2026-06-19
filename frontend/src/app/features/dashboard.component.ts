import { Component, OnInit, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-dashboard',
  imports: [DatePipe, DecimalPipe, RouterLink],
  styles: [`
    .bar { display: flex; gap: 6px; align-items: center; margin-bottom: 8px; font-size: 13px;
      .name { width: 140px; color: var(--text-soft); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
      .track { flex: 1; height: 8px; border-radius: 99px; background: var(--border); overflow: hidden; }
      .fill { height: 100%; background: var(--primary); border-radius: 99px; }
      .cnt { width: 32px; text-align: right; font-weight: 600; }
    }
    .site { display: flex; align-items: center; gap: 10px; padding: 10px 0; border-bottom: 1px solid var(--border);
      &:last-child { border-bottom: 0; }
      .nm { font-weight: 600; font-size: 13.5px; } .det { font-size: 12px; color: var(--text-faint); }
    }
    .stat-dot { width: 9px; height: 9px; border-radius: 50%; flex-shrink: 0;
      &.on { background: var(--success); } &.off { background: var(--danger); } }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div>
          <h1>Dashboard</h1>
          <div class="sub">Welcome back, {{ auth.user()?.displayName }}</div>
        </div>
        @if (d()?.pendingApprovals?.total > 0 && auth.isApprover()) {
          <a routerLink="/approvals" class="btn">✓ {{ d()!.pendingApprovals.total }} pending approval(s)</a>
        }
      </div>

      @if (d(); as data) {
        <div class="grid grid-4 mb">
          <div class="stat-card accent"><div class="label">Total Employees</div><div class="value">{{ data.employees.total }}</div><div class="hint">active workforce</div></div>
          <div class="stat-card good"><div class="label">Present Today</div><div class="value">{{ data.employees.presentToday }}</div><div class="hint">{{ data.employees.onLeaveToday }} on leave</div></div>
          <div class="stat-card warn"><div class="label">Pending Approvals</div><div class="value">{{ data.pendingApprovals.total }}</div><div class="hint">{{ data.pendingApprovals.leaves }} leave · {{ data.pendingApprovals.overtime }} OT · {{ data.pendingApprovals.loans }} loans</div></div>
          <div class="stat-card"><div class="label">Payroll (month)</div><div class="value">₱{{ data.payroll.monthToDateNet | number:'1.0-0' }}</div>
            <div class="hint">@if (data.payroll.latestCutoff) { {{ data.payroll.latestCutoff.name }} · <span class="badge {{ data.payroll.latestCutoff.status.toLowerCase() }}">{{ data.payroll.latestCutoff.status }}</span> } @else { no cutoffs yet }</div></div>
        </div>

        <div class="grid grid-3">
          <div class="card">
            <div class="card-title">Headcount by Department</div>
            @for (r of data.byDepartment; track r.department) {
              <div class="bar">
                <div class="name">{{ r.department }}</div>
                <div class="track"><div class="fill" [style.width.%]="barWidth(r.count, data.byDepartment)"></div></div>
                <div class="cnt">{{ r.count }}</div>
              </div>
            } @empty { <div class="empty">No data</div> }
            <div class="card-title mt">By Branch</div>
            @for (r of data.byBranch; track r.branch) {
              <div class="bar">
                <div class="name">{{ r.branch }}</div>
                <div class="track"><div class="fill" [style.width.%]="barWidth(r.count, data.byBranch)" style="background: var(--success)"></div></div>
                <div class="cnt">{{ r.count }}</div>
              </div>
            }
          </div>

          <div class="card">
            <div class="card-title">Sites & Sync Status</div>
            @for (s of data.sites; track s.id) {
              <div class="site">
                <span class="stat-dot" [class.on]="s.online" [class.off]="!s.online"></span>
                <div style="flex:1">
                  <div class="nm">{{ s.name }}</div>
                  <div class="det">{{ s.branch }} · last sync: {{ s.lastSyncAt ? (s.lastSyncAt | date:'MMM d, h:mm a') : 'never' }}</div>
                </div>
                @if (s.pendingSyncCount > 0) { <span class="badge warning">{{ s.pendingSyncCount }} queued</span> }
                @else { <span class="badge" [class]="s.online ? 'badge online' : 'badge offline'">{{ s.online ? 'Online' : 'Offline' }}</span> }
              </div>
            } @empty { <div class="empty">No sites registered</div> }
            <div class="card-title mt">Devices</div>
            <div class="row">
              @for (dv of data.devices; track dv.status) {
                <span class="badge {{ dv.status.toLowerCase() }}">{{ dv.status }}: {{ dv.count }}</span>
              } @empty { <span class="muted small">No devices registered</span> }
            </div>
          </div>

          <div class="card">
            <div class="card-title">Announcements <a routerLink="/announcements" class="small">View all</a></div>
            @for (a of data.recentAnnouncements; track a.id) {
              <div class="site">
                <div style="flex:1">
                  <div class="nm">@if (a.isPinned) { 📌 } {{ a.title }}</div>
                  <div class="det">{{ a.type }} · {{ a.publishDate | date:'MMM d, y' }}</div>
                </div>
              </div>
            } @empty { <div class="empty">No announcements</div> }
          </div>
        </div>
      } @else {
        <div class="card"><div class="empty">Loading dashboard…</div></div>
      }
    </div>
  `
})
export class DashboardComponent implements OnInit {
  d = signal<any | null>(null);

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void {
    this.api.get<any>('dashboard').subscribe(res => this.d.set(res));
  }

  barWidth(count: number, rows: { count: number }[]): number {
    const max = Math.max(...rows.map(r => r.count), 1);
    return Math.round((count / max) * 100);
  }
}
