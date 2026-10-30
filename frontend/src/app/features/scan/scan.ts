import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { RECEIPT_UPLOAD_LIMITS } from '../../core/receipts/receipt.models';
import { EntryNumberPipe } from '../../shared/formatting/entry-number.pipe';
import { FileSizePipe } from '../../shared/formatting/file-size.pipe';
import { StatusStamp } from '../../shared/ui/status-stamp/status-stamp';
import { UploadQueue } from './upload-queue';

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

  protected readonly dragging = signal(false);

  protected onFilesChosen(input: HTMLInputElement): void {
    this.queue.add(Array.from(input.files ?? []));
    input.value = '';
  }

  protected onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(true);
  }

  protected percent(progress: number): number {
    return Math.round(progress * 100);
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    this.queue.add(Array.from(event.dataTransfer?.files ?? []));
  }
}
