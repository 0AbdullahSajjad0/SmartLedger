import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { from, switchMap, throwError, catchError } from 'rxjs';
import { API_BASE_URL } from '../config/api-base-url';
import { SKIP_AUTH } from './auth-api';
import { AuthStore } from './auth-store';

/**
 * Adds the access token to every request for our API, refreshing it first when it's about to expire.
 * If the API still answers 401 (e.g. the token was revoked), refreshes once and retries the request.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const apiBaseUrl = inject(API_BASE_URL);
  const auth = inject(AuthStore);

  if (!request.url.startsWith(apiBaseUrl) || request.context.get(SKIP_AUTH)) {
    return next(request);
  }

  const send = () => from(auth.getAccessToken()).pipe(switchMap((token) => next(withToken(request, token))));

  return send().pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401) {
        return throwError(() => error);
      }
      return from(auth.refresh()).pipe(switchMap((refreshed) => (refreshed ? send() : throwError(() => error))));
    }),
  );
};

function withToken(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
}
