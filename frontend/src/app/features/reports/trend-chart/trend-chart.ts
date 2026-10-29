import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MonthTotal } from '../../../core/reports/report.models';
import { monthLabel } from '../../../shared/formatting/months';

const COMPACT = new Intl.NumberFormat('en', { notation: 'compact', maximumFractionDigits: 1 });

/**
 * Six months of spending as bars: one series, so no legend; the month under review in ink, the others muted.
 * Hovering a bar shows the exact amount; screen readers get a summary and the same figures as a table.
 */
@Component({
  selector: 'app-trend-chart',
  imports: [CurrencyPipe],
  templateUrl: './trend-chart.html',
  styleUrl: './trend-chart.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TrendChart {
  readonly months = input.required<MonthTotal[]>();
  /** The month under review, "yyyy-MM". */
  readonly selected = input.required<string>();
  readonly currency = input.required<string>();

  protected readonly label = monthLabel;

  private readonly largest = computed(() => Math.max(0, ...this.months().map((m) => m.spent)));

  protected readonly bars = computed(() =>
    this.months().map((m) => ({
      ...m,
      // Heights against the biggest month; a month with any spending stays visible as a sliver.
      height: this.largest() > 0 ? Math.max((m.spent / this.largest()) * 100, m.spent > 0 ? 2 : 0) : 0,
      short: m.spent > 0 ? COMPACT.format(m.spent).toLowerCase() : '0',
    })),
  );

  /** "Monthly spending, May to October 2026: May $3,410, …" */
  protected readonly summary = computed(() => {
    const months = this.months();
    if (months.length === 0) {
      return '';
    }
    const money = new Intl.NumberFormat('en', { style: 'currency', currency: this.currency(), maximumFractionDigits: 0 });
    const range = `${monthLabel(months[0].month, 'name')} to ${monthLabel(months[months.length - 1].month)}`;
    return `Monthly spending, ${range}: ${months.map((m) => `${monthLabel(m.month, 'name')} ${money.format(m.spent)}`).join(', ')}`;
  });
}
