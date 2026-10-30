import { HttpClient, HttpContext } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, timeout } from 'rxjs';
import { SKIP_AUTH } from '../auth/auth-api';
import { API_BASE_URL } from '../config/api-base-url';

const ATTEMPT_TIMEOUT_MS = 25_000;

@Injectable({ providedIn: 'root' })
export class HealthApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  ready(): Observable<boolean> {
    return this.http
      .get(`${this.baseUrl}/health/ready`, { responseType: 'text', context: new HttpContext().set(SKIP_AUTH, true) })
      .pipe(
        timeout(ATTEMPT_TIMEOUT_MS),
        map((body) => body === 'Healthy'),
      );
  }
}
