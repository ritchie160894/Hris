import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-announcements',
  imports: [FormsModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Announcements</h1><div class="sub">Company announcements, memos, events and holiday notices</div></div>
        @if (auth.isHr()) { <button class="btn" (click)="openNew()">＋ Post Announcement</button> }
      </div>

      @for (a of items(); track a.id) {
        <div class="card mb">
          <div class="card-title">
            <span>@if (a.isPinned) { 📌 } {{ a.title }}</span>
            <span class="row" style="gap:8px">
              <span class="badge {{ badge(a.type) }}">{{ typeName(a.type) }}</span>
              @if (auth.isAdmin()) { <button class="btn ghost sm" (click)="remove(a)">Delete</button> }
            </span>
          </div>
          <p style="white-space:pre-wrap">{{ a.body }}</p>
          <div class="muted small">Posted {{ a.publishDate }} by {{ a.postedByName }}</div>
        </div>
      } @empty { <div class="card"><div class="empty">No announcements</div></div> }

      @if (showNew()) {
        <div class="modal-backdrop" (click)="showNew.set(false)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">Post Announcement</div>
            <label class="field"><span class="lbl">Title *</span><input class="ctl" [(ngModel)]="form.title" /></label>
            <label class="field"><span class="lbl">Type</span>
              <select class="ctl" [(ngModel)]="form.type">
                <option [ngValue]="1">Announcement</option><option [ngValue]="2">Memo</option>
                <option [ngValue]="3">Event</option><option [ngValue]="4">Holiday Notice</option>
              </select></label>
            <label class="field"><span class="lbl">Body *</span><textarea class="ctl" rows="5" [(ngModel)]="form.body"></textarea></label>
            <label class="field"><span class="lbl"><input type="checkbox" [(ngModel)]="form.isPinned" /> Pin to top</span></label>
            <div class="muted small mb">Posting sends an in-app notification to all active users.</div>
            <div class="modal-actions">
              <button class="btn secondary" (click)="showNew.set(false)">Cancel</button>
              <button class="btn" (click)="save()">Publish</button>
            </div>
          </div>
        </div>
      }
    </div>
  `
})
export class AnnouncementsComponent implements OnInit {
  items = signal<any[]>([]);
  showNew = signal(false);
  form: any = {};

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void { this.load(); }

  load(): void { this.api.get<any[]>('announcements').subscribe(r => this.items.set(r)); }

  typeName(t: number): string { return ({ 1: 'Announcement', 2: 'Memo', 3: 'Event', 4: 'Holiday Notice' } as any)[t] ?? t; }
  badge(t: number): string { return ({ 1: 'info', 2: 'muted', 3: 'success', 4: 'danger' } as any)[t] ?? 'muted'; }

  openNew(): void {
    this.form = { type: 1, publishDate: new Date().toISOString().slice(0, 10) };
    this.showNew.set(true);
  }

  save(): void {
    if (!this.form.title || !this.form.body) return;
    this.api.post('announcements', this.form).subscribe(() => { this.showNew.set(false); this.load(); });
  }

  remove(a: any): void {
    this.api.delete(`announcements/${a.id}`).subscribe(() => this.load());
  }
}
