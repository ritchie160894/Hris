import { Component, OnDestroy, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';

interface RetentionOption { days: number; label: string; }

@Component({
  selector: 'app-sync-monitor',
  imports: [DatePipe, FormsModule],
  styles: [`
    .pagination { display: flex; align-items: center; gap: 10px; padding: 12px 0 0; font-size: 13px; color: var(--text-soft); flex-wrap: wrap; }
    .retention-bar { display: flex; gap: 10px; align-items: center; flex-wrap: wrap; padding-bottom: 12px; margin-bottom: 12px; border-bottom: 1px solid var(--border); }
    .retention-bar select { max-width: 220px; }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Synchronization Monitor</h1><div class="sub">Offline-first sites queue punches locally and sync when internet is available — auto-refreshes every 15s</div></div>
        <button class="btn secondary" (click)="load()">↻ Refresh</button>
      </div>

      @if (message()) { <div class="alert success">{{ message() }}</div> }
      @if (error()) { <div class="alert error">{{ error() }}</div> }

      @if (data(); as d) {
        <div class="grid grid-4 mb">
          <div class="stat-card good"><div class="label">Sites Online</div><div class="value">{{ onlineCount(d.sites) }}/{{ d.sites.length }}</div></div>
          <div class="stat-card warn"><div class="label">Pending Records</div><div class="value">{{ totalPending(d.sites) }}</div><div class="hint">queued at sites</div></div>
          <div class="stat-card"><div class="label">Recent Batches</div><div class="value">{{ batchTotal() }}</div></div>
          <div class="stat-card bad"><div class="label">Unresolved Conflicts</div><div class="value">{{ d.unresolvedConflicts.length }}</div></div>
        </div>

        <div class="card mb">
          <div class="card-title">Site Status</div>
          <div class="table-wrap">
            <table class="data">
              <thead><tr><th>Site</th><th>Branch</th><th>Connection</th><th>Last Heartbeat</th><th>Last Sync</th><th class="num">Pending</th></tr></thead>
              <tbody>
                @for (s of d.sites; track s.id) {
                  <tr>
                    <td class="bold">{{ s.name }}</td>
                    <td>{{ s.branch }}</td>
                    <td><span class="badge {{ s.online ? 'online' : 'offline' }}">{{ s.online ? 'Online' : 'Offline' }}</span></td>
                    <td>{{ s.lastHeartbeatAt ? (s.lastHeartbeatAt | date:'MMM d, h:mm:ss a') : 'never' }}</td>
                    <td>{{ s.lastSyncAt ? (s.lastSyncAt | date:'MMM d, h:mm:ss a') : 'never' }}</td>
                    <td class="num">@if (s.pendingSyncCount > 0) { <span class="badge warning">{{ s.pendingSyncCount }}</span> } @else { 0 }</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>

        <div class="grid grid-2">
          <div class="card">
            <div class="card-title row" style="justify-content:space-between">
              <span>Recent Sync Batches</span>
              @if (batchTotal() > 0) {
                <span class="muted small">{{ batchTotal() }} batch(es)</span>
              }
            </div>
            <div class="retention-bar">
              <label class="field" style="margin:0">
                <span class="lbl">Auto-delete batches older than</span>
                <select class="ctl" [(ngModel)]="retentionDays" [disabled]="settingsBusy()">
                  @for (o of retentionOptions(); track o.days) {
                    <option [ngValue]="o.days">{{ o.label }}</option>
                  }
                </select>
              </label>
              <button class="btn secondary sm" [disabled]="settingsBusy() || retentionDays === savedRetentionDays()" (click)="saveRetention()">Save</button>
              <span class="muted small">Runs nightly; saving applies cleanup immediately. Pagination keeps the UI fast either way.</span>
            </div>
            <div class="table-wrap">
              <table class="data">
                <thead><tr><th>Site</th><th>Type</th><th class="num">Records</th><th class="num">Dups</th><th>Status</th><th>Time</th></tr></thead>
                <tbody>
                  @for (b of batches(); track b.id) {
                    <tr>
                      <td>{{ b.site }}</td>
                      <td>{{ b.dataType }} <span class="muted small">{{ b.direction === 'SiteToCentral' ? '↑' : '↓' }}</span></td>
                      <td class="num">{{ b.recordCount }}</td>
                      <td class="num">{{ b.duplicateCount }}</td>
                      <td><span class="badge {{ b.status.toLowerCase() }}">{{ b.status }}</span></td>
                      <td class="muted small">{{ b.createdAt | date:'h:mm:ss a' }}</td>
                    </tr>
                  } @empty { <tr><td colspan="6"><div class="empty">No sync batches yet</div></td></tr> }
                </tbody>
              </table>
            </div>
            @if (batchTotal() > batchPageSize) {
              <div class="pagination">
                <span>Page {{ batchPage() }} of {{ batchTotalPages() }} · {{ batchPageSize }} rows/page</span>
                <button class="btn secondary sm" [disabled]="batchPage() <= 1" (click)="batchPage.set(batchPage() - 1); loadBatches()">Previous</button>
                <button class="btn secondary sm" [disabled]="batchPage() >= batchTotalPages()" (click)="batchPage.set(batchPage() + 1); loadBatches()">Next</button>
              </div>
            }
          </div>

          <div class="card">
            <div class="card-title">Sync Conflicts</div>
            @for (c of d.unresolvedConflicts; track c.id) {
              <div style="padding:10px 0;border-bottom:1px solid var(--border)">
                <div class="bold small">{{ c.site }} · {{ c.dataType }}</div>
                <div class="small muted">{{ c.description }}</div>
                <div class="row mt" style="gap:6px">
                  <button class="btn secondary sm" (click)="resolve(c, 'KeptCentral')">Keep Central</button>
                  <button class="btn secondary sm" (click)="resolve(c, 'Merged')">Mark Merged</button>
                  <button class="btn ghost sm" (click)="resolve(c, 'Discarded')">Discard</button>
                </div>
              </div>
            } @empty { <div class="empty">✓ No unresolved conflicts</div> }
          </div>
        </div>
      } @else { <div class="card"><div class="empty">Loading…</div></div> }
    </div>
  `
})
export class SyncMonitorComponent implements OnInit, OnDestroy {
  data = signal<any | null>(null);
  batches = signal<any[]>([]);
  batchPage = signal(1);
  batchTotal = signal(0);
  retentionOptions = signal<RetentionOption[]>([]);
  savedRetentionDays = signal(30);
  retentionDays = 30;
  settingsBusy = signal(false);
  message = signal('');
  error = signal('');
  readonly batchPageSize = 25;
  private timer: ReturnType<typeof setInterval> | undefined;

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.loadSettings();
    this.load();
    this.timer = setInterval(() => this.load(), 15000);
  }

  ngOnDestroy(): void {
    if (this.timer) clearInterval(this.timer);
  }

  load(): void {
    this.api.get<any>('sync/status').subscribe(r => {
      this.data.set(r);
      this.batchTotal.set(r.batchTotal ?? 0);
    });
    this.loadBatches();
  }

  loadBatches(): void {
    this.api.get<{ total: number; items: any[] }>('sync/batches', {
      page: this.batchPage(),
      pageSize: this.batchPageSize
    }).subscribe(res => {
      this.batches.set(res.items);
      this.batchTotal.set(res.total);
    });
  }

  loadSettings(): void {
    this.api.get<{ batchRetentionDays: number; options: RetentionOption[] }>('sync/settings').subscribe(res => {
      this.retentionOptions.set(res.options);
      this.retentionDays = res.batchRetentionDays;
      this.savedRetentionDays.set(res.batchRetentionDays);
    });
  }

  saveRetention(): void {
    this.settingsBusy.set(true);
    this.message.set('');
    this.error.set('');
    this.api.put<{ message?: string; purged?: number }>('sync/settings', { batchRetentionDays: this.retentionDays }).subscribe({
      next: res => {
        this.settingsBusy.set(false);
        this.savedRetentionDays.set(this.retentionDays);
        const extra = res.purged ? ` ${res.purged} old batch(es) removed.` : '';
        this.message.set((res.message ?? 'Retention updated.') + extra);
        this.batchPage.set(1);
        this.load();
        setTimeout(() => this.message.set(''), 5000);
      },
      error: err => {
        this.settingsBusy.set(false);
        this.error.set(err?.error?.message ?? 'Could not save retention setting.');
      }
    });
  }

  batchTotalPages(): number {
    return Math.max(1, Math.ceil(this.batchTotal() / this.batchPageSize));
  }

  onlineCount(sites: any[]): number { return sites.filter(s => s.online).length; }
  totalPending(sites: any[]): number { return sites.reduce((sum, s) => sum + s.pendingSyncCount, 0); }

  resolve(c: any, resolution: string): void {
    this.api.post(`sync/conflicts/${c.id}/resolve`, { resolution }).subscribe(() => this.load());
  }
}
