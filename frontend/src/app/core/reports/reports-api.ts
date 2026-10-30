import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../config/api-base-url';
import { Insight } from './report.models';

@Injectable({ providedIn: 'root' })
export class ReportsApi {
  private readonly http = inject(HttpClient);

  readonly url = `${inject(API_BASE_URL)}/api/reports`;

  writeInsight(month: string): Observable<Insight> {
    return this.http.post<Insight>(`${this.url}/insight`, null, { params: { month } });
  }

  exportCsv(month: string): Observable<Blob> {
    return this.http.get(`${this.url}/export`, { params: { month }, responseType: 'blob' });
  }
}
