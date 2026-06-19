import { Component, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-performance',
  imports: [FormsModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Performance Evaluation</h1><div class="sub">KPI-based reviews and scoring</div></div>
        @if (!auth.hasRole('Employee')) { <button class="btn" (click)="openNew()">＋ New Review</button> }
      </div>

      <div class="card">
        <div class="table-wrap">
          <table class="data">
            <thead><tr><th>Employee</th><th>Period</th><th>Reviewer</th><th class="num">Score</th><th>KPIs</th><th>Status</th></tr></thead>
            <tbody>
              @for (r of reviews(); track r.id) {
                <tr>
                  <td class="bold">{{ r.employee.name }}</td>
                  <td>{{ r.period }}</td>
                  <td>{{ r.reviewerName }}</td>
                  <td class="num bold" [style.color]="r.overallScore >= 3 ? 'var(--success)' : 'var(--warning)'">{{ r.overallScore }}</td>
                  <td>
                    @for (k of r.kpis; track k.id) { <span class="badge muted" style="margin:2px">{{ k.kpiName }}: {{ k.score }}</span> }
                  </td>
                  <td><span class="badge {{ r.isFinalized ? 'success' : 'pending' }}">{{ r.isFinalized ? 'Finalized' : 'Draft' }}</span></td>
                </tr>
              } @empty { <tr><td colspan="6"><div class="empty">No performance reviews</div></td></tr> }
            </tbody>
          </table>
        </div>
      </div>

      @if (showNew()) {
        <div class="modal-backdrop" (click)="showNew.set(false)">
          <div class="modal wide" (click)="$event.stopPropagation()">
            <div class="modal-title">New Performance Review</div>
            <div class="form-grid">
              <label class="field"><span class="lbl">Employee ID (numeric) *</span><input class="ctl" type="number" [(ngModel)]="form.employeeId" /></label>
              <label class="field"><span class="lbl">Period *</span><input class="ctl" [(ngModel)]="form.period" placeholder="2026 H1" /></label>
              <label class="field"><span class="lbl">Review Date</span><input class="ctl" type="date" [(ngModel)]="form.reviewDate" /></label>
              <label class="field"><span class="lbl">Reviewer</span><input class="ctl" [(ngModel)]="form.reviewerName" /></label>
            </div>
            <div class="card-title">KPI Scores (1–5)</div>
            @for (k of form.kpiScores; track $index) {
              <div class="row mb">
                <input class="ctl" style="flex:2" placeholder="KPI name" [(ngModel)]="k.kpiName" />
                <input class="ctl" style="max-width:110px" type="number" placeholder="Weight %" [(ngModel)]="k.weight" />
                <input class="ctl" style="max-width:100px" type="number" min="1" max="5" step="0.1" placeholder="Score" [(ngModel)]="k.score" />
                <button class="btn ghost sm" (click)="form.kpiScores.splice($index, 1)">✕</button>
              </div>
            }
            <button class="btn secondary sm" (click)="form.kpiScores.push({ weight: 20, score: 3 })">＋ Add KPI</button>
            <label class="field mt"><span class="lbl">Strengths</span><textarea class="ctl" [(ngModel)]="form.strengths"></textarea></label>
            <label class="field"><span class="lbl">Areas for Improvement</span><textarea class="ctl" [(ngModel)]="form.areasForImprovement"></textarea></label>
            <label class="field"><span class="lbl"><input type="checkbox" [(ngModel)]="form.isFinalized" /> Finalize review</span></label>
            <div class="modal-actions">
              <button class="btn secondary" (click)="showNew.set(false)">Cancel</button>
              <button class="btn" (click)="save()">Save Review</button>
            </div>
          </div>
        </div>
      }
    </div>
  `
})
export class PerformanceComponent implements OnInit {
  reviews = signal<any[]>([]);
  showNew = signal(false);
  form: any = {};

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.get<any[]>('performance/reviews').subscribe(r => this.reviews.set(r));
  }

  openNew(): void {
    this.form = {
      reviewDate: new Date().toISOString().slice(0, 10),
      reviewerName: this.auth.user()?.displayName,
      kpiScores: [
        { kpiName: 'Quality of Work', weight: 30, score: 3 },
        { kpiName: 'Productivity', weight: 30, score: 3 },
        { kpiName: 'Attendance & Punctuality', weight: 20, score: 3 },
        { kpiName: 'Teamwork', weight: 20, score: 3 }
      ]
    };
    this.showNew.set(true);
  }

  save(): void {
    this.api.post('performance/reviews', this.form).subscribe(() => { this.showNew.set(false); this.load(); });
  }
}
