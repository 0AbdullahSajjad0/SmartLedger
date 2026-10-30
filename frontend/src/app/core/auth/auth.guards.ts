import { inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { CanMatchFn, Router } from '@angular/router';
import { filter, firstValueFrom, map } from 'rxjs';
import { AuthStore } from './auth-store';

function sessionKnown(): Promise<boolean> {
  const auth = inject(AuthStore);
  return firstValueFrom(
    toObservable(auth.status).pipe(
      filter((status) => status !== 'restoring'),
      map((status) => status === 'signed-in'),
    ),
  );
}

export const signedInGuard: CanMatchFn = async (_route, segments) => {
  const router = inject(Router);
  const signedIn = await sessionKnown();
  const returnUrl = '/' + segments.map((segment) => segment.path).join('/');

  return signedIn || router.createUrlTree(['/sign-in'], { queryParams: returnUrl === '/' ? {} : { returnUrl } });
};

export const signedOutGuard: CanMatchFn = async () => {
  const router = inject(Router);
  return (await sessionKnown()) ? router.createUrlTree(['/overview']) : true;
};
