import { HttpClient, HttpEvent } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../config/api-base-url';
import { ReceiptDetail, ReceiptSummary, UpdateReceiptRequest } from './receipt.models';

@Injectable({ providedIn: 'root' })
export class ReceiptsApi {
  private readonly http = inject(HttpClient);

  readonly baseUrl = `${inject(API_BASE_URL)}/api/receipts`;
  readonly categoriesUrl = `${inject(API_BASE_URL)}/api/categories`;
  readonly exchangeRatesUrl = `${inject(API_BASE_URL)}/api/exchange-rates`;

  upload(files: readonly File[]): Observable<HttpEvent<ReceiptSummary[]>> {
    const form = new FormData();
    for (const file of files) {
      form.append('files', file, file.name);
    }
    return this.http.post<ReceiptSummary[]>(this.baseUrl, form, { observe: 'events', reportProgress: true });
  }

  get(id: string): Observable<ReceiptDetail> {
    return this.http.get<ReceiptDetail>(`${this.baseUrl}/${id}`);
  }

  previewUrl(id: string): string {
    return `${this.baseUrl}/${id}/preview`;
  }

  fileUrl(id: string): string {
    return `${this.baseUrl}/${id}/file`;
  }

  downloadFile(id: string): Observable<Blob> {
    return this.http.get(this.fileUrl(id), { responseType: 'blob' });
  }

  update(id: string, request: UpdateReceiptRequest): Observable<ReceiptDetail> {
    return this.http.put<ReceiptDetail>(`${this.baseUrl}/${id}`, request);
  }

  approve(id: string, amountCharged: number | null): Observable<ReceiptDetail> {
    return this.http.post<ReceiptDetail>(`${this.baseUrl}/${id}/approve`, { amountCharged });
  }

  reject(id: string): Observable<ReceiptDetail> {
    return this.http.post<ReceiptDetail>(`${this.baseUrl}/${id}/reject`, null);
  }

  reopen(id: string): Observable<ReceiptDetail> {
    return this.http.post<ReceiptDetail>(`${this.baseUrl}/${id}/reopen`, null);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }
}
