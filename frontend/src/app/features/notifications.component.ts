import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { ApiService } from '../core/api.service';

@Component({
  selector: 'app-notifications',
  imports: [DatePipe],
  styles: [`
    .btn.danger { color: var(--danger); }
    .btn.danger:hover { background: var(--danger-soft); }
    .pagination { display: flex; align-items: center; gap: 10px; padding: 12px 0 0; font-size: 13px; color: var(--text-soft); flex-wrap: wrap; }
  `],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Notifications</h1><div class="sub">{{ unread() }} unread · auto-removed after 7 days</div></div>
        <div class="row">
          <button class="btn secondary" (click)="markAll()">Mark all as read</button>
          <button class="btn secondary" (click)="deleteRead()">Clear read</button>
        </div>
      </div>

      <div class="card">
        @for (n of items(); track n.id) {
          <div class="row" style="padding:13px 4px;border-bottom:1px solid var(--border);align-items:flex-start"
               [style.background]="n.isRead ? '' : 'var(--primary-soft)'">
            <span style="font-size:18px">{{ icon(n.type) }}</span>
            <div style="flex:1">
              <div class="bold">{{ n.title }}</div>
              <div class="small" style="color:var(--text-soft)">{{ n.message }}</div>
              <div class="muted small">{{ n.createdAt | date:'EEE, MMM d, y · h:mm a' }}</div>
            </div>
            @if (!n.isRead) { <button class="btn ghost sm" (click)="read(n)">Mark read</button> }
            <button class="btn ghost sm danger" (click)="remove(n)">Delete</button>
          </div>
        } @empty { <div class="empty">No notifications</div> }
        @if (total() > pageSize) {
          <div class="pagination">
            <span>Page {{ page() }} of {{ totalPages() }} · {{ total() }} notification(s) · {{ pageSize }} rows/page</span>
            <button class="btn secondary sm" [disabled]="page() <= 1" (click)="page.set(page() - 1); load()">Previous</button>
            <button class="btn secondary sm" [disabled]="page() >= totalPages()" (click)="page.set(page() + 1); load()">Next</button>
          </div>
        }
      </div>
    </div>
  `
})
export class NotificationsComponent {
  items = signal<any[]>([]);
  unread = signal(0);
  page = signal(1);
  total = signal(0);
  readonly pageSize = 25;
  private readonly api = inject(ApiService);

  constructor() { this.load(); }

  load(): void {
    this.api.get<{ unread: number; total: number; items: any[] }>('notifications', {
      page: this.page(), pageSize: this.pageSize
    }).subscribe(r => {
      this.items.set(r.items);
      this.unread.set(r.unread);
      this.total.set(r.total);
    });
  }

  totalPages(): number { return Math.max(1, Math.ceil(this.total() / this.pageSize)); }

  icon(type: string): string {
    return ({
      ApprovalRequest: '✋', ApprovalResult: '✅', Attendance: '🕐', Payroll: '₱',
      Leave: '🌴', Overtime: '⏱', Announcement: '📣', Device: '📟', System: '⚙'
    } as any)[type] ?? '🔔';
  }

  read(n: any): void { this.api.post(`notifications/${n.id}/read`, {}).subscribe(() => this.load()); }
  markAll(): void { this.api.post('notifications/read-all', {}).subscribe(() => this.load()); }
  remove(n: any): void { this.api.delete(`notifications/${n.id}`).subscribe(() => this.load()); }
  deleteRead(): void {
    if (!confirm('Delete all read notifications?')) return;
    this.api.post('notifications/delete-read', {}).subscribe(() => { this.page.set(1); this.load(); });
  }
}
