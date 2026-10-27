import { HttpClient, HttpEvent } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../config/api-base-url';
import { ReceiptSummary } from './receipt.models';

@Injectable({ providedIn: 'root' })
export class ReceiptsApi {
  private readonly http = inject(HttpClient);

  /** Also used by pages that load lists through `httpResource`. */
  readonly baseUrl = `${inject(API_BASE_URL)}/api/receipts`;

  /** Uploads files as one request and reports progress events along the way. */
  upload(files: readonly File[]): Observable<HttpEvent<ReceiptSummary[]>> {
    const form = new FormData();
    for (const file of files) {
      form.append('files', file, file.name);
    }
    return this.http.post<ReceiptSummary[]>(this.baseUrl, form, { observe: 'events', reportProgress: true });
  }
}
