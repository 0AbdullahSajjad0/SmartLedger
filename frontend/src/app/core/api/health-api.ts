import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map, timeout } from 'rxjs';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class HealthApi {
  private readonly http = inject(HttpClient);

  /**
   * Emits true when the API reports "Healthy".
   * The timeout is generous because the API scales to zero and may need a cold start.
   */
  check(): Observable<boolean> {
    return this.http
      .get(`${environment.apiBaseUrl}/health`, { responseType: 'text' })
      .pipe(
        timeout(60_000),
        map((body) => body === 'Healthy'),
      );
  }
}
