import { DatePipe, DecimalPipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { describeHttpError } from '../../core/http/describe-http-error';
import { ReceiptDetail } from '../../core/receipts/receipt.models';
import { ReceiptsApi } from '../../core/receipts/receipts-api';
import { EntryNumberPipe } from '../../shared/formatting/entry-number.pipe';
import { FileSizePipe } from '../../shared/formatting/file-size.pipe';
import { StatusStamp } from '../../shared/ui/status-stamp/status-stamp';

/** Image types every browser can show inline. HEIC (iPhone) and PDF need opening separately. */
const PREVIEWABLE = new Set(['image/jpeg', 'image/png', 'image/webp']);

/**
 * One receipt: the original on the left, the record on the right. Becomes the review screen
 * once AI extraction fills in the details.
 */
@Component({
  selector: 'app-receipt-page',
  imports: [RouterLink, DatePipe, DecimalPipe, EntryNumberPipe, FileSizePipe, StatusStamp],
  templateUrl: './receipt-page.html',
  styleUrl: './receipt-page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReceiptPage {
  private readonly api = inject(ReceiptsApi);

  /** Route parameter :id. */
  readonly id = input.required<string>();

  protected readonly receipt = httpResource<ReceiptDetail>(() => `${this.api.baseUrl}/${this.id()}`);

  /**
   * The original file. It needs the access token, so it's fetched like any API call rather than
   * linked directly (an <img src> can't send an Authorization header), then shown via a blob: URL.
   */
  protected readonly file = httpResource.blob(() => `${this.api.baseUrl}/${this.id()}/file`);

  /** A temporary blob: address for the downloaded file, released when the file changes or the page closes. */
  protected readonly fileUrl = signal<string | null>(null);

  protected readonly canPreview = computed(() =>
    this.receipt.hasValue() ? PREVIEWABLE.has(this.receipt.value().contentType) : false,
  );

  /** Named by entry number, like the API does, never by the uploader's original file name. */
  protected readonly downloadName = computed(() => {
    if (!this.receipt.hasValue()) {
      return null;
    }
    const { number, fileName } = this.receipt.value();
    const extension = fileName.includes('.') ? fileName.slice(fileName.lastIndexOf('.')).toLowerCase() : '';
    return `receipt-${number.toString().padStart(4, '0')}${extension}`;
  });

  protected readonly errorMessage = computed(() =>
    this.receipt.error() ? describeHttpError(this.receipt.error()) : null,
  );

  constructor() {
    effect((onCleanup) => {
      // Check hasValue() first: reading value() of a resource in an error state throws.
      const blob = this.file.hasValue() ? this.file.value() : undefined;
      if (!blob) {
        this.fileUrl.set(null);
        return;
      }

      const url = URL.createObjectURL(blob);
      this.fileUrl.set(url);
      onCleanup(() => URL.revokeObjectURL(url));
    });
  }
}
