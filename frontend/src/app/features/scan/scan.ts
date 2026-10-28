import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RECEIPT_UPLOAD_LIMITS } from '../../core/receipts/receipt.models';
import { EntryNumberPipe } from '../../shared/formatting/entry-number.pipe';
import { FileSizePipe } from '../../shared/formatting/file-size.pipe';
import { StatusStamp } from '../../shared/ui/status-stamp/status-stamp';
import { UploadQueue } from './upload-queue';

/** The scanner bed: drop, choose or photograph receipts, and watch them arrive in the intake log. */
@Component({
  selector: 'app-scan',
  imports: [RouterLink, DecimalPipe, EntryNumberPipe, FileSizePipe, StatusStamp],
  templateUrl: './scan.html',
  styleUrl: './scan.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [UploadQueue],
})
export class Scan {
  protected readonly queue = inject(UploadQueue);
  protected readonly accept = RECEIPT_UPLOAD_LIMITS.accept;
  protected readonly maxFiles = RECEIPT_UPLOAD_LIMITS.maxFilesPerUpload;

  /** True while files are dragged over the bed, to light it up. */
  protected readonly dragging = signal(false);

  protected onFilesChosen(input: HTMLInputElement): void {
    this.queue.add(Array.from(input.files ?? []));
    input.value = ''; // Lets the same file be chosen again, e.g. after fixing it.
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault(); // Required, or the browser won't allow a drop here.
    this.dragging.set(true);
  }

  protected percent(progress: number): number {
    return Math.round(progress * 100);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault(); // Stop the browser from opening the file itself.
    this.dragging.set(false);
    this.queue.add(Array.from(event.dataTransfer?.files ?? []));
  }
}
