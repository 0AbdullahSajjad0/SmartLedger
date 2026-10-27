import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, timeout } from 'rxjs';
import { SKIP_AUTH } from '../auth/auth-api';
import { API_BASE_URL } from '../config/api-base-url';

@Injectable({ providedIn: 'root' })
export class HealthApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /**
   * Emits true when the API reports "Healthy".
   * The timeout is generous because the API scales to zero and may need a cold start.
   */
  check(): Observable<boolean> {
    return this.http
      .get(`${this.baseUrl}/health`, { responseType: 'text', context: new HttpContext().set(SKIP_AUTH, true) })
      .pipe(
        timeout(60_000),
        map((body) => body === 'Healthy'),
      );
  }
}
