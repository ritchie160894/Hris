import { Component, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';

@Component({
  selector: 'app-recruitment',
  imports: [FormsModule, DatePipe],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Recruitment</h1><div class="sub">Job postings, applicant tracking and interviews</div></div>
        <button class="btn" (click)="openPosting()">＋ Job Posting</button>
      </div>

      <div class="tabs">
        <button [class.active]="tab() === 'postings'" (click)="tab.set('postings')">Job Postings</button>
        <button [class.active]="tab() === 'applicants'" (click)="tab.set('applicants')">Applicants</button>
      </div>

      @if (tab() === 'postings') {
        <div class="grid grid-3">
          @for (p of postings(); track p.id) {
            <div class="card">
              <div class="card-title">{{ p.title }} <span class="badge {{ p.status.toLowerCase() === 'open' ? 'success' : 'muted' }}">{{ p.status }}</span></div>
              <div class="muted small">{{ p.department }} · {{ p.position }} · {{ p.vacancies }} vacanc{{ p.vacancies > 1 ? 'ies' : 'y' }}</div>
              <p class="small">{{ p.description }}</p>
              <div class="row">
                <span class="badge muted">{{ p.applicantCount }} applicant(s)</span>
                <div class="spacer"></div>
                <button class="btn secondary sm" (click)="openApplicant(p)">＋ Applicant</button>
              </div>
            </div>
          } @empty { <div class="card"><div class="empty">No job postings</div></div> }
        </div>
      }

      @if (tab() === 'applicants') {
        <div class="card">
          <div class="table-wrap">
            <table class="data">
              <thead><tr><th>Applicant</th><th>Position</th><th>Contact</th><th>Status</th><th>Interviews</th><th></th></tr></thead>
              <tbody>
                @for (a of applicants(); track a.id) {
                  <tr>
                    <td class="bold">{{ a.firstName }} {{ a.lastName }}</td>
                    <td>{{ a.posting }}</td>
                    <td class="muted small">{{ a.email }}<br>{{ a.contactNumber }}</td>
                    <td>
                      <select class="ctl" style="max-width:140px;padding:4px 8px" [ngModel]="a.status" (ngModelChange)="setStatus(a, $event)">
                        @for (s of statuses; track s) { <option [value]="s">{{ s }}</option> }
                      </select>
                    </td>
                    <td>
                      @for (i of a.interviews; track i.id) {
                        <div class="small">{{ i.scheduledAt | date:'MMM d h:mm a' }} — {{ i.interviewerName }}
                          @if (i.result) { <span class="badge muted">{{ i.result }}</span> }</div>
                      } @empty { <span class="muted small">none</span> }
                    </td>
                    <td><button class="btn ghost sm" (click)="openInterview(a)">＋ Interview</button></td>
                  </tr>
                } @empty { <tr><td colspan="6"><div class="empty">No applicants</div></td></tr> }
              </tbody>
            </table>
          </div>
        </div>
      }

      @if (modal() === 'posting') {
        <div class="modal-backdrop" (click)="modal.set('')">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">New Job Posting</div>
            <label class="field"><span class="lbl">Title *</span><input class="ctl" [(ngModel)]="form.title" /></label>
            <label class="field"><span class="lbl">Description</span><textarea class="ctl" [(ngModel)]="form.description"></textarea></label>
            <label class="field"><span class="lbl">Requirements</span><textarea class="ctl" [(ngModel)]="form.requirements"></textarea></label>
            <div class="form-grid">
              <label class="field"><span class="lbl">Vacancies</span><input class="ctl" type="number" [(ngModel)]="form.vacancies" /></label>
              <label class="field"><span class="lbl">Status</span>
                <select class="ctl" [(ngModel)]="form.status"><option [ngValue]="1">Draft</option><option [ngValue]="2">Open</option></select></label>
            </div>
            <div class="modal-actions">
              <button class="btn secondary" (click)="modal.set('')">Cancel</button>
              <button class="btn" (click)="savePosting()">Save</button>
            </div>
          </div>
        </div>
      }

      @if (modal() === 'applicant') {
        <div class="modal-backdrop" (click)="modal.set('')">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">New Applicant</div>
            <div class="form-grid">
              <label class="field"><span class="lbl">First Name *</span><input class="ctl" [(ngModel)]="form.firstName" /></label>
              <label class="field"><span class="lbl">Last Name *</span><input class="ctl" [(ngModel)]="form.lastName" /></label>
              <label class="field"><span class="lbl">Email</span><input class="ctl" [(ngModel)]="form.email" /></label>
              <label class="field"><span class="lbl">Contact</span><input class="ctl" [(ngModel)]="form.contactNumber" /></label>
            </div>
            <div class="modal-actions">
              <button class="btn secondary" (click)="modal.set('')">Cancel</button>
              <button class="btn" (click)="saveApplicant()">Save</button>
            </div>
          </div>
        </div>
      }

      @if (modal() === 'interview') {
        <div class="modal-backdrop" (click)="modal.set('')">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">Schedule Interview</div>
            <label class="field"><span class="lbl">Date & Time *</span><input class="ctl" type="datetime-local" [(ngModel)]="form.scheduledAt" /></label>
            <label class="field"><span class="lbl">Interviewer</span><input class="ctl" [(ngModel)]="form.interviewerName" /></label>
            <label class="field"><span class="lbl">Location</span><input class="ctl" [(ngModel)]="form.location" /></label>
            <div class="modal-actions">
              <button class="btn secondary" (click)="modal.set('')">Cancel</button>
              <button class="btn" (click)="saveInterview()">Schedule</button>
            </div>
          </div>
        </div>
      }
    </div>
  `
})
export class RecruitmentComponent implements OnInit {
  tab = signal('postings');
  postings = signal<any[]>([]);
  applicants = signal<any[]>([]);
  modal = signal('');
  form: any = {};
  statuses = ['Applied', 'Screening', 'Interview', 'Offer', 'Hired', 'Rejected', 'Withdrawn'];

  constructor(private api: ApiService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.get<any[]>('recruitment/postings').subscribe(r => this.postings.set(r));
    this.api.get<any[]>('recruitment/applicants').subscribe(r => this.applicants.set(r));
  }

  openPosting(): void { this.form = { vacancies: 1, status: 2 }; this.modal.set('posting'); }
  savePosting(): void {
    this.api.post('recruitment/postings', this.form).subscribe(() => { this.modal.set(''); this.load(); });
  }

  openApplicant(p: any): void { this.form = { jobPostingId: p.id }; this.modal.set('applicant'); }
  saveApplicant(): void {
    this.api.post('recruitment/applicants', this.form).subscribe(() => { this.modal.set(''); this.tab.set('applicants'); this.load(); });
  }

  openInterview(a: any): void { this.form = { applicantId: a.id }; this.modal.set('interview'); }
  saveInterview(): void {
    this.api.post('recruitment/interviews', this.form).subscribe(() => { this.modal.set(''); this.load(); });
  }

  setStatus(a: any, status: string): void {
    this.api.put(`recruitment/applicants/${a.id}/status`, { status, notes: a.notes }).subscribe(() => this.load());
  }
}
