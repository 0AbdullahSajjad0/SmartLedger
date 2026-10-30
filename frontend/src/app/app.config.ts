import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, inject, provideAppInitializer, provideBrowserGlobalErrorListeners } from '@angular/core';
import { PreloadAllModules, provideRouter, withComponentInputBinding, withPreloading } from '@angular/router';
import { routes } from './app.routes';
import { ServerStatus } from './core/api/server-status';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthStore } from './core/auth/auth-store';
import { receiptImageCacheInterceptor } from './core/receipts/receipt-image-cache';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // Query parameters such as ?returnUrl= arrive as component inputs. Each page's code is still loaded on its own,
    // but once the first page is up the rest download in the background, so moving between pages doesn't wait.
    provideRouter(routes, withComponentInputBinding(), withPreloading(PreloadAllModules)),
    // The image cache comes first, so a cached preview is answered without even asking for an access token.
    provideHttpClient(withFetch(), withInterceptors([receiptImageCacheInterceptor, authInterceptor])),
    // Wake the API and restore a saved session at once, without blocking the first paint: on a cold start both wait
    // for the server, and the startup bar explains the wait. Route guards wait for the session.
    provideAppInitializer(() => {
      inject(ServerStatus).wake();
      void inject(AuthStore).restoreSession();
    }),
  ],
};
