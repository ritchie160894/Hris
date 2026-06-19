import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { environment } from '../../environments/environment';

/** Thin wrapper over HttpClient that prefixes the API base URL and builds query params. */
@Injectable({ providedIn: 'root' })
export class ApiService {
  constructor(private http: HttpClient) {}

  get<T>(path: string, params?: Record<string, unknown>) {
    return this.http.get<T>(`${environment.apiUrl}/${path}`, { params: this.toParams(params) });
  }

  post<T>(path: string, body: unknown) {
    return this.http.post<T>(`${environment.apiUrl}/${path}`, body);
  }

  put<T>(path: string, body: unknown) {
    return this.http.put<T>(`${environment.apiUrl}/${path}`, body);
  }

  delete<T>(path: string) {
    return this.http.delete<T>(`${environment.apiUrl}/${path}`);
  }

  upload<T>(path: string, form: FormData) {
    return this.http.post<T>(`${environment.apiUrl}/${path}`, form);
  }

  /** Triggers a CSV download with the auth token attached. */
  download(path: string, params?: Record<string, unknown>) {
    return this.http.get(`${environment.apiUrl}/${path}`, {
      params: this.toParams({ ...params, format: 'csv' }),
      responseType: 'blob',
      observe: 'response'
    });
  }

  private toParams(params?: Record<string, unknown>): HttpParams | undefined {
    if (!params) return undefined;
    let hp = new HttpParams();
    for (const [k, v] of Object.entries(params)) {
      if (v !== undefined && v !== null && v !== '') hp = hp.set(k, String(v));
    }
    return hp;
  }
}

/** Normalizes list endpoints that return either `{ items, total }` or a plain array. */
export function parsePagedResponse<T>(r: { items?: T[]; total?: number } | T[] | null | undefined): { items: T[]; total: number } {
  if (Array.isArray(r)) return { items: r, total: r.length };
  const items = r?.items ?? [];
  return { items, total: r?.total ?? items.length };
}

export function saveBlob(response: { body: Blob | null; headers: { get(h: string): string | null } }, fallbackName: string): void {
  const blob = response.body;
  if (!blob) return;
  const cd = response.headers.get('content-disposition') ?? '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)/i.exec(cd);
  const name = match ? decodeURIComponent(match[1]) : fallbackName;
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  a.click();
  URL.revokeObjectURL(url);
}
