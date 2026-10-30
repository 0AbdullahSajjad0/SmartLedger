import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ReceiptStatus } from '../../../core/receipts/receipt.models';

const LABELS: Record<ReceiptStatus, string> = {
  Processing: 'Reading',
  NeedsReview: 'Needs review',
  Approved: 'Approved',
  Rejected: 'Rejected',
  Failed: 'Unreadable',
};

@Component({
  selector: 'app-status-stamp',
  template: '{{ label() }}',
  styleUrl: './status-stamp.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[attr.data-status]': 'status()',
  },
})
export class StatusStamp {
  readonly status = input.required<ReceiptStatus>();

  protected readonly label = computed(() => LABELS[this.status()]);
}
