import { DatePipe, DecimalPipe, NgTemplateOutlet } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AbstractControl, FormControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom, map } from 'rxjs';
import { describeHttpError } from '../../../core/http/describe-http-error';
import {
  Category,
  ExchangeRateQuote,
  LineItem,
  ReceiptDetail,
  UpdateReceiptRequest,
} from '../../../core/receipts/receipt.models';
import { ReceiptsApi } from '../../../core/receipts/receipts-api';
import { numberFootnotes } from '../footnotes';

interface LineItemForm {
  description: FormControl<string>;
  quantity: FormControl<number | null>;
  unitPrice: FormControl<number | null>;
  amount: FormControl<number | null>;
}

const TOLERANCE = 0.01;

const CURRENCY_NAMES = new Intl.DisplayNames(['en'], { type: 'currency' });
const CURRENCIES = Intl.supportedValuesOf('currency').map((code) => ({ code, name: CURRENCY_NAMES.of(code) ?? code }));

const MONEY = new Intl.NumberFormat('en', { minimumFractionDigits: 2, maximumFractionDigits: 2 });

@Component({
  selector: 'app-receipt-review-form',
  imports: [ReactiveFormsModule, DatePipe, DecimalPipe, NgTemplateOutlet],
  templateUrl: './review-form.html',
  styleUrl: './review-form.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReceiptReviewForm {
  private readonly api = inject(ReceiptsApi);
  private readonly fb = inject(NonNullableFormBuilder);

  readonly receipt = input.required<ReceiptDetail>();
  readonly categories = input.required<Category[]>();
  readonly baseCurrency = input.required<string>();

  readonly changed = output<ReceiptDetail>();

  protected readonly currencies = CURRENCIES;
  protected readonly today = new Date().toISOString().slice(0, 10);

  protected readonly form = this.fb.group({
    vendorName: ['', Validators.maxLength(200)],
    receiptDate: [''],
    currency: ['', Validators.pattern(/^[A-Za-z]{3}$/)],
    categoryId: [''],
    paymentMethod: ['', Validators.maxLength(60)],
    notes: ['', Validators.maxLength(500)],
    subtotal: this.fb.control<number | null>(null, Validators.min(0)),
    tax: this.fb.control<number | null>(null, Validators.min(0)),
    total: this.fb.control<number | null>(null, Validators.min(0)),
    lineItems: this.fb.array<FormGroup<LineItemForm>>([]),
    amountCharged: this.fb.control<number | null>(null, Validators.min(0.01)),
  });

  protected readonly value = toSignal(this.form.valueChanges.pipe(map(() => this.form.getRawValue())), {
    initialValue: this.form.getRawValue(),
  });

  protected readonly busy = signal<'save' | 'approve' | 'reject' | null>(null);
  protected readonly serverError = signal<string | null>(null);
  protected readonly savedMessage = signal<string | null>(null);
  private readonly submitted = signal(false);

  protected readonly footnotes = computed(() => numberFootnotes(this.receipt().extractionFlags));

  protected readonly categoryOptions = computed(() =>
    this.categories().filter((c) => !c.isArchived || c.id === this.receipt().categoryId),
  );

  protected readonly checks = computed(() => {
    const { lineItems, subtotal, tax, total } = this.value();
    const warnings: string[] = [];

    if (lineItems.length > 0) {
      const lines = lineItems.reduce((sum, line) => sum + (line.amount ?? 0), 0);
      const target = subtotal ?? (tax === null ? total : null);
      if (target !== null && Math.abs(lines - target) > TOLERANCE) {
        warnings.push(
          `The line items add up to ${MONEY.format(lines)}, but the ${subtotal !== null ? 'subtotal' : 'total'} is ${MONEY.format(target)}.`,
        );
      }
    }

    if (subtotal !== null && tax !== null && total !== null && Math.abs(subtotal + tax - total) > TOLERANCE) {
      warnings.push(`Subtotal plus tax is ${MONEY.format(subtotal + tax)}, but the total is ${MONEY.format(total)}. A tip or service charge?`);
    }

    return warnings;
  });

  protected readonly missing = computed(() => {
    const v = this.value();
    return [
      v.vendorName.trim() ? null : 'vendor',
      v.receiptDate ? null : 'date',
      /^[A-Za-z]{3}$/.test(v.currency.trim()) ? null : 'currency',
      v.total !== null ? null : 'total',
      v.categoryId ? null : 'category',
    ].filter((field): field is string => field !== null);
  });

  protected readonly fromCurrency = computed(() => this.value().currency.trim().toUpperCase());
  protected readonly sameCurrency = computed(() => this.fromCurrency() === this.baseCurrency());

  private readonly rateKey = computed(() => {
    const from = this.fromCurrency();
    const date = this.value().receiptDate;
    return /^[A-Z]{3}$/.test(from) && date && !this.sameCurrency() ? `${from}|${date}` : null;
  });

  protected readonly quote = httpResource<ExchangeRateQuote>(() => {
    const key = this.rateKey();
    if (!key) {
      return undefined;
    }
    const [from, date] = key.split('|');
    return { url: this.api.exchangeRatesUrl, params: { from, to: this.baseCurrency(), date } };
  });

  protected readonly quoteError = computed(() => (this.quote.error() ? describeHttpError(this.quote.error()) : null));

  protected readonly converted = computed(() => {
    const { total, amountCharged } = this.value();
    if (amountCharged !== null) {
      return amountCharged;
    }
    if (total === null) {
      return null;
    }
    if (this.sameCurrency()) {
      return total;
    }
    return this.quote.hasValue() ? Math.round(total * this.quote.value().rate * 100) / 100 : null;
  });

  constructor() {
    effect(() => {
      const receipt = this.receipt();
      untracked(() => this.load(receipt));
    });
  }

  protected footnoteFor(field: string): number | null {
    return this.footnotes().find((note) => note.field === field)?.number ?? null;
  }

  protected showError(control: AbstractControl): boolean {
    return control.invalid && (control.touched || this.submitted());
  }

  protected addLine(): void {
    this.form.controls.lineItems.push(this.lineGroup({ description: '', quantity: 1, unitPrice: null, amount: null }));
    this.form.controls.lineItems.markAsDirty();
  }

  protected removeLine(index: number): void {
    this.form.controls.lineItems.removeAt(index);
    this.form.controls.lineItems.markAsDirty();
  }

  protected recalculate(index: number): void {
    const { quantity, unitPrice, amount } = this.form.controls.lineItems.at(index).controls;
    if (quantity.value !== null && unitPrice.value !== null) {
      amount.setValue(Math.round(quantity.value * unitPrice.value * 100) / 100);
    }
  }

  protected async save(): Promise<void> {
    this.submitted.set(true);
    if (this.form.invalid) {
      return;
    }
    await this.run('save', async () => {
      this.changed.emit(await firstValueFrom(this.api.update(this.receipt().id, this.request())));
      this.savedMessage.set('Changes saved.');
    });
  }

  protected async approve(): Promise<void> {
    this.submitted.set(true);
    if (this.form.invalid) {
      return;
    }
    const missing = this.missing();
    if (missing.length > 0) {
      this.serverError.set(`Add the ${listOf(missing)} before approving.`);
      return;
    }

    await this.run('approve', async () => {
      const id = this.receipt().id;
      if (this.detailsChanged()) {
        await firstValueFrom(this.api.update(id, this.request()));
      }
      this.changed.emit(await firstValueFrom(this.api.approve(id, this.form.controls.amountCharged.value)));
    });
  }

  protected async reject(): Promise<void> {
    await this.run('reject', async () => this.changed.emit(await firstValueFrom(this.api.reject(this.receipt().id))));
  }

  private async run(kind: 'save' | 'approve' | 'reject', work: () => Promise<void>): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(kind);
    this.serverError.set(null);
    this.savedMessage.set(null);
    try {
      await work();
    } catch (error) {
      this.serverError.set(describeHttpError(error));
    } finally {
      this.busy.set(null);
    }
  }

  private detailsChanged(): boolean {
    return Object.entries(this.form.controls).some(([name, control]) => name !== 'amountCharged' && control.dirty);
  }

  private load(receipt: ReceiptDetail): void {
    const lines = this.form.controls.lineItems;
    lines.clear({ emitEvent: false });
    for (const line of receipt.lineItems) {
      lines.push(this.lineGroup(line), { emitEvent: false });
    }

    this.form.reset({
      vendorName: receipt.vendorName ?? '',
      receiptDate: receipt.receiptDate ?? '',
      currency: receipt.currency ?? '',
      categoryId: receipt.categoryId ?? '',
      paymentMethod: receipt.paymentMethod ?? '',
      notes: receipt.notes ?? '',
      subtotal: receipt.subtotal,
      tax: receipt.tax,
      total: receipt.total,
      amountCharged: null,
    });
    this.submitted.set(false);
  }

  private lineGroup(line: Pick<LineItem, 'description'> & { quantity: number | null; unitPrice: number | null; amount: number | null }) {
    return this.fb.group<LineItemForm>({
      description: this.fb.control(line.description, [Validators.required, Validators.maxLength(200)]),
      quantity: this.fb.control<number | null>(line.quantity, [Validators.required, Validators.min(0.001)]),
      unitPrice: this.fb.control<number | null>(line.unitPrice, Validators.required),
      amount: this.fb.control<number | null>(line.amount, Validators.required),
    });
  }

  private request(): UpdateReceiptRequest {
    const v = this.form.getRawValue();
    return {
      vendorName: text(v.vendorName),
      receiptDate: v.receiptDate || null,
      currency: text(v.currency)?.toUpperCase() ?? null,
      subtotal: v.subtotal,
      tax: v.tax,
      total: v.total,
      paymentMethod: text(v.paymentMethod),
      categoryId: v.categoryId || null,
      notes: text(v.notes),
      lineItems: v.lineItems.map((line) => ({
        description: line.description.trim(),
        quantity: line.quantity ?? 1,
        unitPrice: line.unitPrice ?? 0,
        amount: line.amount ?? 0,
      })),
    };
  }
}

function text(value: string): string | null {
  const trimmed = value.trim();
  return trimmed ? trimmed : null;
}

function listOf(items: string[]): string {
  return items.length === 1 ? items[0] : `${items.slice(0, -1).join(', ')} and ${items[items.length - 1]}`;
}
