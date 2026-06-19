import { Component, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';

@Component({
  selector: 'app-audit',
  imports: [FormsModule, DatePipe],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Audit Trail</h1><div class="sub">Login, activity, approval, payroll, device and sync logs</div></div>
      </div>

      <div class="card mb">
        <div class="row">
          <select class="ctl" style="max-width:180px" [(ngModel)]="category" (change)="page.set(1); load()">
            <option value="">All categories</option>
            @for (c of categories; track c) { <option [value]="c">{{ c }}</option> }
          </select>
          <input class="ctl" style="max-width:240px" placeholder="Search action or user…" [(ngModel)]="search" (input)="page.set(1); load()" />
          <input class="ctl" style="max-width:160px" type="date" [(ngModel)]="from" (change)="load()" />
          <input class="ctl" style="max-width:160px" type="date" [(ngModel)]="to" (change)="load()" />
        </div>
      </div>

      <div class="card">
        <div class="table-wrap">
          <table class="data">
            <thead><tr><th>Time</th><th>Category</th><th>User</th><th>Action</th><th>Entity</th><th>IP</th></tr></thead>
            <tbody>
              @for (a of items(); track a.id) {
                <tr>
                  <td class="muted small">{{ a.createdAt | date:'MMM d, y h:mm:ss a' }}</td>
                  <td><span class="badge {{ catBadge(a.category) }}">{{ a.category }}</span></td>
                  <td>{{ a.userName ?? 'system' }}</td>
                  <td>{{ a.action }}@if (a.details) { <div class="muted small">{{ a.details }}</div> }</td>
                  <td class="muted small">{{ a.entityType }}@if (a.entityId) { #{{ a.entityId }} }</td>
                  <td class="muted small">{{ a.ipAddress }}</td>
                </tr>
              } @empty { <tr><td colspan="6"><div class="empty">No audit entries</div></td></tr> }
            </tbody>
          </table>
        </div>
        <div class="pagination">
          {{ total() }} entries · Page {{ page() }}
          <button class="btn secondary sm" [disabled]="page() <= 1" (click)="page.set(page() - 1); load()">‹ Prev</button>
          <button class="btn secondary sm" [disabled]="page() * 50 >= total()" (click)="page.set(page() + 1); load()">Next ›</button>
        </div>
      </div>
    </div>
  `
})
export class AuditComponent implements OnInit {
  items = signal<any[]>([]);
  total = signal(0);
  page = signal(1);
  category = '';
  search = '';
  from = '';
  to = '';
  categories = ['Login', 'Activity', 'RecordChange', 'Approval', 'Payroll', 'Device', 'Sync', 'Security'];

  constructor(private api: ApiService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.get<{ total: number; items: any[] }>('audit', {
      category: this.category, search: this.search, from: this.from, to: this.to, page: this.page(), pageSize: 50
    }).subscribe(r => { this.items.set(r.items); this.total.set(r.total); });
  }

  catBadge(c: string): string {
    return ({ Login: 'info', Approval: 'success', Payroll: 'warning', Security: 'danger', Device: 'muted', Sync: 'muted' } as any)[c] ?? '';
  }
}
