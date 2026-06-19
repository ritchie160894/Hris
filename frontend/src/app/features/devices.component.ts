import { Component, OnInit, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';

@Component({
  selector: 'app-devices',
  imports: [FormsModule, DatePipe],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Biometric Devices</h1><div class="sub">SenseFace 2A registration, health and monitoring</div></div>
        <button class="btn" (click)="openNew()">＋ Register Device</button>
      </div>

      <div class="grid grid-3">
        @for (d of devices(); track d.id) {
          <div class="card">
            <div class="card-title">{{ d.name }} <span class="badge {{ d.status.toLowerCase() }}">{{ d.status }}</span></div>
            <div class="muted small">{{ d.model }} · SN {{ d.serialNumber }}</div>
            <div class="muted small">{{ d.site?.name ?? 'Unassigned' }} ({{ d.site?.branch }})</div>
            <div class="muted small">IP: {{ d.ipAddress ?? '—' }} · FW: {{ d.firmwareVersion ?? '—' }}</div>
            <div class="row mt" style="gap:6px">
              <span class="badge muted">👥 {{ d.userCount }}</span>
              <span class="badge muted">👤 {{ d.faceCount }} faces</span>
              <span class="badge muted">👆 {{ d.fingerprintCount }} prints</span>
              <span class="badge muted">📋 {{ d.logCount }} logs</span>
            </div>
            <div class="muted small mt">Last seen: {{ d.lastSeenAt ? (d.lastSeenAt | date:'MMM d, h:mm a') : 'never' }}</div>
            <div class="row mt">
              <button class="btn secondary sm" (click)="edit(d)">Edit</button>
              <button class="btn ghost sm" (click)="viewActivity(d)">Activity</button>
            </div>
          </div>
        } @empty { <div class="card"><div class="empty">No devices registered. Register your SenseFace 2A units here, then point them to a site gateway.</div></div> }
      </div>

      @if (activity()) {
        <div class="card mt">
          <div class="card-title">Device Activity Log</div>
          <table class="data">
            <thead><tr><th>Time</th><th>Activity</th><th>Details</th></tr></thead>
            <tbody>
              @for (a of activity(); track a.id) {
                <tr><td>{{ a.createdAt | date:'MMM d, h:mm a' }}</td>
                <td>@if (a.isError) { <span class="badge danger">{{ a.activity }}</span> } @else { {{ a.activity }} }</td>
                <td class="muted">{{ a.details }}</td></tr>
              } @empty { <tr><td colspan="3"><div class="empty">No activity recorded</div></td></tr> }
            </tbody>
          </table>
        </div>
      }

      @if (showForm()) {
        <div class="modal-backdrop" (click)="showForm.set(false)">
          <div class="modal" (click)="$event.stopPropagation()">
            <div class="modal-title">{{ form.id ? 'Edit Device' : 'Register SenseFace 2A' }}</div>
            @if (error()) { <div class="alert error">{{ error() }}</div> }
            <div class="form-grid">
              <label class="field"><span class="lbl">Serial Number *</span><input class="ctl" [(ngModel)]="form.serialNumber" [disabled]="!!form.id" /></label>
              <label class="field"><span class="lbl">Device Name *</span><input class="ctl" [(ngModel)]="form.name" /></label>
              <label class="field"><span class="lbl">Model</span><input class="ctl" [(ngModel)]="form.model" /></label>
              <label class="field"><span class="lbl">Site</span>
                <select class="ctl" [(ngModel)]="form.siteId"><option [ngValue]="null">Unassigned</option>@for (s of sites(); track s.id) { <option [ngValue]="s.id">{{ s.name }}</option> }</select></label>
              <label class="field"><span class="lbl">IP Address</span><input class="ctl" [(ngModel)]="form.ipAddress" /></label>
              <label class="field"><span class="lbl">Port</span><input class="ctl" type="number" [(ngModel)]="form.port" /></label>
            </div>
            <div class="muted small mb">Tip: on the device, set Comm. → Cloud Server to the site gateway's IP and port 8090 so it pushes attendance automatically.</div>
            <div class="modal-actions">
              <button class="btn secondary" (click)="showForm.set(false)">Cancel</button>
              <button class="btn" (click)="save()">Save</button>
            </div>
          </div>
        </div>
      }
    </div>
  `
})
export class DevicesComponent implements OnInit {
  devices = signal<any[]>([]);
  sites = signal<any[]>([]);
  activity = signal<any[] | null>(null);
  showForm = signal(false);
  error = signal('');
  form: any = {};

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.load();
    this.api.get<any[]>('organization/sites').subscribe(r => this.sites.set(r));
  }

  load(): void { this.api.get<any[]>('devices').subscribe(r => this.devices.set(r)); }

  openNew(): void {
    this.form = { model: 'SenseFace 2A', port: 4370, isActive: true };
    this.error.set('');
    this.showForm.set(true);
  }

  edit(d: any): void {
    this.form = { ...d, siteId: d.site?.id ?? null };
    delete this.form.site;
    this.showForm.set(true);
  }

  save(): void {
    if (!this.form.serialNumber || !this.form.name) { this.error.set('Serial number and name are required.'); return; }
    const req$ = this.form.id ? this.api.put(`devices/${this.form.id}`, this.form) : this.api.post('devices', this.form);
    req$.subscribe({
      next: () => { this.showForm.set(false); this.load(); },
      error: err => this.error.set(err?.error?.message ?? 'Save failed.')
    });
  }

  viewActivity(d: any): void {
    this.api.get<any[]>(`devices/${d.id}/activity`).subscribe(r => this.activity.set(r));
  }
}
