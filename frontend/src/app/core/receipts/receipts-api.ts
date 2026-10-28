import { HttpClient, HttpEvent } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../config/api-base-url';
import { ReceiptDetail, ReceiptSummary } from './receipt.models';

@Injectable({ providedIn: 'root' })
export class ReceiptsApi {
  private readonly http = inject(HttpClient);

  /** Also used by pages that load data through `httpResource`. */
  readonly baseUrl = `${inject(API_BASE_URL)}/api/receipts`;
  readonly categoriesUrl = `${inject(API_BASE_URL)}/api/categories`;

  /** Uploads files as one request and reports progress events along the way. */
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

  /** The cleaned JPEG the page shows: upright, size-limited, no metadata. */
  previewUrl(id: string): string {
    return `${this.baseUrl}/${id}/preview`;
  }

  /** The original upload, exactly as received. */
  fileUrl(id: string): string {
    return `${this.baseUrl}/${id}/file`;
  }

  /** Fetched with the access token like any API call, since a plain link can't send one. */
  downloadFile(id: string): Observable<Blob> {
    return this.http.get(this.fileUrl(id), { responseType: 'blob' });
  }
}
