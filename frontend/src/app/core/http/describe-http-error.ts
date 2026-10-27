import { HttpErrorResponse } from '@angular/common/http';

interface ProblemDetails {
  title?: string;
  errors?: Record<string, string[]>;
}

/** Turns a failed API call into one sentence a person can act on. */
export function describeHttpError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Something went wrong. Please try again.';
  }

  if (error.status === 0) {
    return "Can't reach the server. Check your connection and try again.";
  }

  if (error.status === 429) {
    return 'Too many attempts. Wait a minute and try again.';
  }

  const problem = error.error as ProblemDetails | null;
  const validationMessages = problem?.errors ? Object.values(problem.errors).flat() : [];

  if (validationMessages.length > 0) {
    return validationMessages.join(' ');
  }

  return problem?.title ?? 'Something went wrong. Please try again.';
}
