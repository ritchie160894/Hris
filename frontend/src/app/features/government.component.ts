import { Component, OnInit, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService, saveBlob } from '../core/api.service';

@Component({
  selector: 'app-government',
  imports: [DecimalPipe, FormsModule],
  template: `
    <div class="page">
      <div class="page-header">
        <div><h1>Government Contributions</h1><div class="sub">SSS, PhilHealth, Pag-IBIG, withholding tax tables and remittances</div></div>
      </div>

      <div class="tabs">
        @for (t of ['Remittance','SSS Table','PhilHealth','Pag-IBIG','Tax Table']; track t) {
          <button [class.active]="tab() === t" (click)="tab.set(t)">{{ t }}</button>
        }
      </div>

      @if (tab() === 'Remittance') {
        <div class="card">
          <div class="row mb">
            <input class="ctl" style="max-width:110px" type="number" [(ngModel)]="year" />
            <select class="ctl" style="max-width:150px" [(ngModel)]="month">
              @for (m of months; track m.v) { <option [ngValue]="m.v">{{ m.n }}</option> }
            </select>
            <button class="btn sm" (click)="loadRemittance()">Load</button>
            <div class="spacer"></div>
            <button class="btn secondary sm" (click)="exportRemit()">⬇ Export CSV</button>
          </div>
          <div class="table-wrap">
            <table class="data">
              <thead><tr><th>Employee</th><th class="num">SSS EE</th><th class="num">SSS ER</th><th class="num">PhilHealth</th><th class="num">Pag-IBIG</th><th class="num">W/Tax</th></tr></thead>
              <tbody>
                @for (r of remittance(); track r.employeeCode) {
                  <tr>
                    <td><div class="bold">{{ r.name }}</div><div class="muted small">{{ r.employeeCode }}</div></td>
                    <td class="num">{{ r.sssEe | number:'1.2-2' }}</td><td class="num">{{ r.sssEr | number:'1.2-2' }}</td>
                    <td class="num">{{ r.philHealth | number:'1.2-2' }}</td><td class="num">{{ r.pagIbig | number:'1.2-2' }}</td>
                    <td class="num">{{ r.tax | number:'1.2-2' }}</td>
                  </tr>
                } @empty { <tr><td colspan="6"><div class="empty">No remittance data for this month (run payroll first)</div></td></tr> }
              </tbody>
            </table>
          </div>
        </div>
      }

      @if (tab() === 'SSS Table') {
        <div class="card">
          <div class="table-wrap">
            <table class="data">
              <thead><tr><th class="num">Range From</th><th class="num">Range To</th><th class="num">MSC</th><th class="num">EE Share</th><th class="num">ER Share</th></tr></thead>
              <tbody>
                @for (s of sss(); track s.id) {
                  <tr><td class="num">{{ s.rangeFrom | number:'1.2-2' }}</td><td class="num">{{ s.rangeTo | number:'1.2-2' }}</td>
                  <td class="num">{{ s.monthlySalaryCredit | number:'1.0-0' }}</td><td class="num">{{ s.employeeShare | number:'1.2-2' }}</td><td class="num">{{ s.employerShare | number:'1.2-2' }}</td></tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      }

      @if (tab() === 'PhilHealth') {
        <div class="card">
          <table class="data">
            <thead><tr><th>Year</th><th class="num">Premium Rate %</th><th class="num">Min Salary</th><th class="num">Max Salary</th><th class="num">EE Share %</th></tr></thead>
            <tbody>
              @for (p of philhealth(); track p.id) {
                <tr><td>{{ p.effectiveYear }}</td><td class="num">{{ p.ratePercent }}</td><td class="num">{{ p.minSalary | number }}</td><td class="num">{{ p.maxSalary | number }}</td><td class="num">{{ p.employeeSharePercent }}</td></tr>
              }
            </tbody>
          </table>
        </div>
      }

      @if (tab() === 'Pag-IBIG') {
        <div class="card">
          <table class="data">
            <thead><tr><th>Year</th><th class="num">EE Rate %</th><th class="num">ER Rate %</th><th class="num">Max Compensation</th></tr></thead>
            <tbody>
              @for (p of pagibig(); track p.id) {
                <tr><td>{{ p.effectiveYear }}</td><td class="num">{{ p.employeeRatePercent }}</td><td class="num">{{ p.employerRatePercent }}</td><td class="num">{{ p.maxCompensation | number }}</td></tr>
              }
            </tbody>
          </table>
        </div>
      }

      @if (tab() === 'Tax Table') {
        <div class="card">
          <table class="data">
            <thead><tr><th class="num">Monthly From</th><th class="num">Monthly To</th><th class="num">Base Tax</th><th class="num">% Over Excess</th></tr></thead>
            <tbody>
              @for (t of tax(); track t.id) {
                <tr><td class="num">{{ t.rangeFrom | number:'1.2-2' }}</td><td class="num">{{ t.rangeTo | number:'1.2-2' }}</td>
                <td class="num">{{ t.baseTax | number:'1.2-2' }}</td><td class="num">{{ t.ratePercentOverExcess }}%</td></tr>
              }
            </tbody>
          </table>
        </div>
      }
    </div>
  `
})
export class GovernmentComponent implements OnInit {
  tab = signal('Remittance');
  sss = signal<any[]>([]);
  philhealth = signal<any[]>([]);
  pagibig = signal<any[]>([]);
  tax = signal<any[]>([]);
  remittance = signal<any[]>([]);
  year = new Date().getFullYear();
  month = new Date().getMonth() + 1;
  months = Array.from({ length: 12 }, (_, i) => ({ v: i + 1, n: new Date(2000, i, 1).toLocaleString('en-US', { month: 'long' }) }));

  constructor(private api: ApiService) {}

  ngOnInit(): void {
    this.api.get<any[]>('government/sss').subscribe(r => this.sss.set(r));
    this.api.get<any[]>('government/philhealth').subscribe(r => this.philhealth.set(r));
    this.api.get<any[]>('government/pagibig').subscribe(r => this.pagibig.set(r));
    this.api.get<any[]>('government/tax').subscribe(r => this.tax.set(r));
    this.loadRemittance();
  }

  loadRemittance(): void {
    this.api.get<any[]>('government/remittance', { year: this.year, month: this.month }).subscribe(r => this.remittance.set(r));
  }

  exportRemit(): void {
    this.api.download('reports/government', { year: this.year, month: this.month }).subscribe(r => saveBlob(r, 'remittance.csv'));
  }
}
