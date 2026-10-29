import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, timeout } from 'rxjs';
import { SKIP_AUTH } from '../auth/auth-api';
import { API_BASE_URL } from '../config/api-base-url';

/** How long one readiness check may take before it's tried again. */
const ATTEMPT_TIMEOUT_MS = 25_000;

@Injectable({ providedIn: 'root' })
export class HealthApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /**
   * Emits true when the API and its database both answer ("Healthy" from /health/ready). Both sleep when idle:
   * the container scales to zero and the serverless database pauses, so the first check after a quiet spell can
   * take up to a minute; the caller retries.
   */
  ready(): Observable<boolean> {
    return this.http
      .get(`${this.baseUrl}/health/ready`, { responseType: 'text', context: new HttpContext().set(SKIP_AUTH, true) })
      .pipe(
        timeout(ATTEMPT_TIMEOUT_MS),
        map((body) => body === 'Healthy'),
      );
  }
}
