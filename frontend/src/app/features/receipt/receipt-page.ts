import { DatePipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthStore } from '../../core/auth/auth-store';
import { describeHttpError } from '../../core/http/describe-http-error';
import { Category, ReceiptDetail } from '../../core/receipts/receipt.models';
import { ReceiptsApi } from '../../core/receipts/receipts-api';
import { EntryNumberPipe } from '../../shared/formatting/entry-number.pipe';
import { FileSizePipe } from '../../shared/formatting/file-size.pipe';
import { saveFile } from '../../shared/files/save-file';
import { StatusStamp } from '../../shared/ui/status-stamp/status-stamp';
import { numberFootnotes } from './footnotes';
import { ReceiptReviewForm } from './review-form/review-form';

/** Originals every browser can show inline, for receipts uploaded before the server made previews. */
const PREVIEWABLE = new Set(['image/jpeg', 'image/png', 'image/webp']);

const REFRESH_WHILE_READING_MS = 3_000;

/**
 * One receipt: the original on the left, the record on the right. Becomes the review screen
 * once AI extraction fills in the details.
 */
@Component({
  selector: 'app-receipt-page',
  imports: [RouterLink, DatePipe, DecimalPipe, NgTemplateOutlet, EntryNumberPipe, FileSizePipe, StatusStamp, ReceiptReviewForm],
  templateUrl: './receipt-page.html',
  styleUrl: './receipt-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReceiptPage {
  private readonly api = inject(ReceiptsApi);
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);

  /** Route parameter :id. */
  readonly id = input.required<string>();

  protected readonly receipt = httpResource<ReceiptDetail>(() => `${this.api.baseUrl}/${this.id()}`);

  /** For the category's name rather than its id, and the review form's category picker. */
  protected readonly categories = httpResource<Category[]>(() => this.api.categoriesUrl, { defaultValue: [] });

  protected readonly categoryName = computed(() => {
    const categoryId = this.receipt.hasValue() ? this.receipt.value().categoryId : null;
    return categoryId ? (this.categories.value().find((c) => c.id === categoryId)?.name ?? null) : null;
  });

  /** Fields the AI wasn't sure about, numbered like footnotes: "Paid with ¹" → "¹ One digit was faded." */
  protected readonly footnotes = computed(() =>
    this.receipt.hasValue() ? numberFootnotes(this.receipt.value().extractionFlags) : [],
  );

  /** What approving converts to. */
  protected readonly baseCurrency = computed(() => this.auth.user()?.baseCurrency ?? 'USD');

  /** Waiting for review, or AI couldn't read it: the record is a form. */
  protected readonly editable = computed(() => {
    const status = this.receipt.hasValue() ? this.receipt.value().status : null;
    return status === 'NeedsReview' || status === 'Failed';
  });

  protected readonly reopening = signal(false);
  protected readonly reopenError = signal<string | null>(null);

  protected readonly confirmingDelete = signal(false);
  protected readonly deleting = signal(false);
  protected readonly deleteError = signal<string | null>(null);

  /** Permanently removes the receipt and its files, then goes back to the ledger. */
  protected deleteReceipt(): void {
    if (!this.receipt.hasValue() || this.deleting()) {
      return;
    }
    this.deleting.set(true);
    this.deleteError.set(null);
    this.api
      .delete(this.receipt.value().id)
      .pipe(
        finalize(() => this.deleting.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => void this.router.navigateByUrl('/receipts'),
        error: (error: unknown) => this.deleteError.set(describeHttpError(error)),
      });
  }

  /** Marks are only for review; once approved or rejected, the notes have been answered. */
  protected footnoteFor(field: string): number | null {
    return this.editable() ? (this.footnotes().find((note) => note.field === field)?.number ?? null) : null;
  }

  /**
   * The picture beside the record: the cleaned preview once the server has made it. Receipts from before previews
   * existed fall back to the original, if it's a format browsers can show. It needs the access token, so it's
   * fetched like any API call (an <img src> can't send an Authorization header), then shown via a blob: URL.
   */
  protected readonly image = httpResource.blob(() => this.imageSource());

  // A plain string, so saving or reopening the receipt (a new object, same picture) doesn't download it again.
  private readonly imageSource = computed(() => {
    if (!this.receipt.hasValue()) {
      return undefined;
    }
    const { id, hasPreview, status, contentType } = this.receipt.value();
    if (hasPreview) {
      return this.api.previewUrl(id);
    }
    return status !== 'Processing' && PREVIEWABLE.has(contentType) ? this.api.fileUrl(id) : undefined;
  });

  /** A temporary blob: address for the image, released when the image changes or the page closes. */
  protected readonly imageUrl = signal<string | null>(null);

  /** Photos keep whatever the camera saved in them, which can include where they were taken. */
  protected readonly originalIsPhoto = computed(() =>
    this.receipt.hasValue() ? this.receipt.value().contentType.startsWith('image/') : false,
  );

  protected readonly downloading = signal(false);
  protected readonly downloadError = signal<string | null>(null);

  protected readonly errorMessage = computed(() =>
    this.receipt.error() ? describeHttpError(this.receipt.error()) : null,
  );

  /** The review form saved, approved or rejected the receipt: show it as the API now has it. */
  protected onChanged(receipt: ReceiptDetail): void {
    this.receipt.set(receipt);
  }

  /** Takes back an approval or rejection, so the record becomes a form again. */
  protected reopen(): void {
    if (!this.receipt.hasValue() || this.reopening()) {
      return;
    }
    this.reopening.set(true);
    this.reopenError.set(null);
    this.api
      .reopen(this.receipt.value().id)
      .pipe(
        finalize(() => this.reopening.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (receipt) => this.receipt.set(receipt),
        error: (error: unknown) => this.reopenError.set(describeHttpError(error)),
      });
  }

  /** Fetched only when asked for: originals can be 10 MB, and the page itself shows the small preview. */
  protected downloadOriginal(): void {
    if (!this.receipt.hasValue() || this.downloading()) {
      return;
    }
    const { id, number, fileName } = this.receipt.value();

    this.downloading.set(true);
    this.downloadError.set(null);
    this.api
      .downloadFile(id)
      .pipe(
        finalize(() => this.downloading.set(false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (blob) => saveFile(blob, downloadName(number, fileName)),
        error: (error: unknown) => this.downloadError.set(describeHttpError(error)),
      });
  }

  constructor() {
    // While AI is still reading this receipt, check again every few seconds.
    effect((onCleanup) => {
      const reading = this.receipt.hasValue() && this.receipt.value().status === 'Processing';
      if (!reading) {
        return;
      }
      const timer = setInterval(() => this.receipt.reload(), REFRESH_WHILE_READING_MS);
      onCleanup(() => clearInterval(timer));
    });

    effect((onCleanup) => {
      // Check hasValue() first: reading value() of a resource in an error state throws.
      const blob = this.image.hasValue() ? this.image.value() : undefined;
      if (!blob) {
        this.imageUrl.set(null);
        return;
      }

      const url = URL.createObjectURL(blob);
      this.imageUrl.set(url);
      onCleanup(() => URL.revokeObjectURL(url));
    });
  }
}

/** Named by entry number, like the API does, never by the uploader's original file name. */
function downloadName(number: number, fileName: string): string {
  const extension = fileName.includes('.') ? fileName.slice(fileName.lastIndexOf('.')).toLowerCase() : '';
  return `receipt-${number.toString().padStart(4, '0')}${extension}`;
}
