import { Component, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService, saveBlob } from '../core/api.service';
import { AuthService } from '../core/auth.service';

@Component({
  selector: 'app-documents',
  imports: [FormsModule, DatePipe],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Document Management</h1><div class="sub">Contracts, certificates and employee files with expiry monitoring</div></div>
        @if (auth.isHr()) { <button class="btn" (click)="showUpload.set(true)">⬆ Upload Document</button> }
      </div>

      @if (expiring().length > 0) {
        <div class="alert info">⚠ {{ expiring().length }} document(s) expiring within 60 days:
          @for (d of expiring(); track d.id) { <b>{{ d.title }}</b> ({{ d.employee.name }}, {{ d.expiryDate }}){{ $last ? '' : ', ' }} }
        </div>
      }

      <div class="card">
        <div class="row mb">
          <select class="ctl" style="max-width:200px" [(ngModel)]="category" (change)="load()">
            <option value="">All categories</option>
            @for (c of categories; track c) { <option [value]="c">{{ c }}</option> }
          </select>
        </div>
        <div class="table-wrap">
          <table class="data">
            <thead><tr><th>Title</th><th>Employee</th><th>Category</th><th>File</th><th>Expiry</th><th>Uploaded</th><th></th></tr></thead>
            <tbody>
              @for (d of docs(); track d.id) {
                <tr>
                  <td class="bold">{{ d.title }}</td>
                  <td>{{ d.employee.name }}</td>
                  <td><span class="badge muted">{{ d.category }}</span></td>
                  <td class="muted small">{{ d.fileName }} ({{ (d.fileSize / 1024).toFixed(0) }} KB)</td>
                  <td>{{ d.expiryDate || '—' }}</td>
                  <td class="muted small">{{ d.createdAt | date:'MMM d, y' }}</td>
                  <td class="row" style="gap:6px">
                    <button class="btn ghost sm" (click)="download(d)">⬇</button>
                    @if (auth.isAdmin()) { <button class="btn ghost sm" (click)="remove(d)">🗑</button> }
                  </td>
                </tr>
              } @empty { <tr><td colspan="7"><div class="empty">No documents</div></td></tr> }
            </tbody>
          </table>
        </div>
      </div>

      @if (showUpload()) {
        <div class="modal-backdrop" (click)="showUpload.set(false)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">Upload Document</div>
            @if (error()) { <div class="alert error">{{ error() }}</div> }
            <label class="field"><span class="lbl">Employee ID (numeric) *</span><input class="ctl" type="number" [(ngModel)]="form.employeeId" /></label>
            <label class="field"><span class="lbl">Title *</span><input class="ctl" [(ngModel)]="form.title" /></label>
            <label class="field"><span class="lbl">Category</span>
              <select class="ctl" [(ngModel)]="form.category">
                @for (c of categories; track c) { <option [value]="c">{{ c }}</option> }
              </select></label>
            <label class="field"><span class="lbl">Expiry Date (optional)</span><input class="ctl" type="date" [(ngModel)]="form.expiryDate" /></label>
            <label class="field"><span class="lbl">File *</span><input class="ctl" type="file" (change)="pickFile($event)" /></label>
            <div class="modal-actions">
              <button class="btn secondary" (click)="showUpload.set(false)">Cancel</button>
              <button class="btn" [disabled]="busy()" (click)="upload()">Upload</button>
            </div>
          </div>
        </div>
      }
    </div>
  `
})
export class DocumentsComponent implements OnInit {
  docs = signal<any[]>([]);
  expiring = signal<any[]>([]);
  showUpload = signal(false);
  busy = signal(false);
  error = signal('');
  category = '';
  categories = ['Contract', 'Certificate', 'GovernmentId', 'Memo', 'Policy', 'Other'];
  form: any = { category: 'Contract' };
  file: File | null = null;

  constructor(private api: ApiService, public auth: AuthService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.api.get<any[]>('documents', { category: this.category }).subscribe(r => this.docs.set(r));
    this.api.get<any[]>('documents/expiring').subscribe(r => this.expiring.set(r));
  }

  pickFile(ev: Event): void {
    this.file = (ev.target as HTMLInputElement).files?.[0] ?? null;
  }

  upload(): void {
    if (!this.form.employeeId || !this.form.title || !this.file) {
      this.error.set('Employee, title and file are required.');
      return;
    }
    this.busy.set(true);
    const fd = new FormData();
    fd.append('employeeId', this.form.employeeId);
    fd.append('title', this.form.title);
    fd.append('category', this.form.category);
    if (this.form.expiryDate) fd.append('expiryDate', this.form.expiryDate);
    fd.append('file', this.file);
    this.api.upload('documents/upload', fd).subscribe({
      next: () => { this.busy.set(false); this.showUpload.set(false); this.form = { category: 'Contract' }; this.file = null; this.load(); },
      error: err => { this.busy.set(false); this.error.set(err?.error?.message ?? 'Upload failed.'); }
    });
  }

  download(d: any): void {
    this.api.download(`documents/${d.id}/download`).subscribe(r => saveBlob(r, d.fileName));
  }

  remove(d: any): void {
    this.api.delete(`documents/${d.id}`).subscribe(() => this.load());
  }
}
