import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';

@Component({
  selector: 'app-training',
  imports: [FormsModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Training Management</h1><div class="sub">Trainings, seminars and certification tracking</div></div>
        <button class="btn" (click)="openNew()">＋ New Training</button>
      </div>

      @if (expiring().length > 0) {
        <div class="alert info">⚠ {{ expiring().length }} certification(s) expiring within 90 days.</div>
      }

      <div class="grid grid-2">
        @for (t of trainings(); track t.id) {
          <div class="card">
            <div class="card-title">{{ t.title }} <span class="badge {{ t.status.toLowerCase() }}">{{ t.status }}</span></div>
            <div class="muted small">{{ t.provider || 'Internal' }} · {{ t.startDate }}@if (t.endDate) { → {{ t.endDate }} } · {{ t.location || 'TBD' }}</div>
            <p class="small">{{ t.description }}</p>
            <div class="card-title" style="font-size:13px">Participants ({{ t.participants.length }})</div>
            @for (p of t.participants; track p.id) {
              <div class="row" style="padding:5px 0;border-bottom:1px solid var(--border)">
                <span>{{ p.employee.name }}</span>
                <div class="spacer"></div>
                @if (p.completed) {
                  <span class="badge success">Completed</span>
                  @if (p.certificateNumber) { <span class="badge muted">Cert: {{ p.certificateNumber }}</span> }
                } @else {
                  <button class="btn ghost sm" (click)="markComplete(p)">Mark complete</button>
                }
              </div>
            } @empty { <div class="muted small">No participants yet</div> }
            <div class="row mt">
              <input class="ctl" style="max-width:160px" type="number" placeholder="Employee ID" [(ngModel)]="addIds[t.id]" />
              <button class="btn secondary sm" (click)="addParticipant(t)">＋ Add Participant</button>
            </div>
          </div>
        } @empty { <div class="card"><div class="empty">No trainings scheduled</div></div> }
      </div>

      @if (showNew()) {
        <div class="modal-backdrop" (click)="showNew.set(false)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">New Training</div>
            <label class="field"><span class="lbl">Title *</span><input class="ctl" [(ngModel)]="form.title" /></label>
            <label class="field"><span class="lbl">Provider</span><input class="ctl" [(ngModel)]="form.provider" /></label>
            <div class="form-grid">
              <label class="field"><span class="lbl">Start Date *</span><input class="ctl" type="date" [(ngModel)]="form.startDate" /></label>
              <label class="field"><span class="lbl">End Date</span><input class="ctl" type="date" [(ngModel)]="form.endDate" /></label>
              <label class="field"><span class="lbl">Location</span><input class="ctl" [(ngModel)]="form.location" /></label>
              <label class="field"><span class="lbl">Cost</span><input class="ctl" type="number" [(ngModel)]="form.cost" /></label>
            </div>
            <label class="field"><span class="lbl">Description</span><textarea class="ctl" [(ngModel)]="form.description"></textarea></label>
            <div class="modal-actions">
              <button class="btn secondary" (click)="showNew.set(false)">Cancel</button>
              <button class="btn" (click)="save()">Save</button>
            </div>
          </div>
        </div>
      }
    </div>
  `
})
export class TrainingComponent implements OnInit {
  trainings = signal<any[]>([]);
  expiring = signal<any[]>([]);
  showNew = signal(false);
  form: any = {};
  addIds: Record<number, number> = {};

  constructor(private api: ApiService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.get<any[]>('training').subscribe(r => this.trainings.set(r));
    this.api.get<any[]>('training/expiring-certifications').subscribe(r => this.expiring.set(r));
  }

  openNew(): void {
    this.form = { startDate: new Date().toISOString().slice(0, 10), status: 1 };
    this.showNew.set(true);
  }

  save(): void {
    this.api.post('training', this.form).subscribe(() => { this.showNew.set(false); this.load(); });
  }

  addParticipant(t: any): void {
    const employeeId = this.addIds[t.id];
    if (!employeeId) return;
    this.api.post(`training/${t.id}/participants`, { employeeId }).subscribe(() => { this.addIds[t.id] = 0; this.load(); });
  }

  markComplete(p: any): void {
    this.api.put(`training/participants/${p.id}`, { ...p, completed: true }).subscribe(() => this.load());
  }
}
