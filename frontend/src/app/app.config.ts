import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { ServerStatus } from './core/api/server-status';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthStore } from './core/auth/auth-store';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Query parameters such as ?returnUrl= arrive as component inputs.
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    // Wake the API and restore a saved session at once, without blocking the first paint: on a cold start both wait
    // for the server, and the startup bar explains the wait. Route guards wait for the session.
    provideAppInitializer(() => {
      inject(ServerStatus).wake();
      void inject(AuthStore).restoreSession();
    }),
  ],
};
