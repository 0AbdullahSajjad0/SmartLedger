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
    provideRouter(routes, withComponentInputBinding(), withPreloading(PreloadAllModules)),
    provideHttpClient(withFetch(), withInterceptors([receiptImageCacheInterceptor, authInterceptor])),
    provideAppInitializer(() => {
      inject(ServerStatus).wake();
      void inject(AuthStore).restoreSession();
    }),
  ],
};
