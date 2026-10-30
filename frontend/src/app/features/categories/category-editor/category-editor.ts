import { ChangeDetectionStrategy, Component, ElementRef, afterNextRender, inject, input, output, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { CategoriesApi } from '../../../core/categories/categories-api';
import { CATEGORY_NAME_MAX_LENGTH, CATEGORY_PALETTE, CategorySpending } from '../../../core/categories/category.models';
import { describeHttpError } from '../../../core/http/describe-http-error';

@Component({
  selector: 'tr[app-category-editor]',
  imports: [ReactiveFormsModule],
  templateUrl: './category-editor.html',
  styleUrl: './category-editor.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'editor' },
})
export class CategoryEditor {
  private readonly api = inject(CategoriesApi);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  readonly category = input.required<CategorySpending | null>();
  readonly baseCurrency = input.required<string>();

  readonly saved = output<void>();
  readonly cancelled = output<void>();

  protected readonly palette = CATEGORY_PALETTE;
  protected readonly maxLength = CATEGORY_NAME_MAX_LENGTH;

  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(CATEGORY_NAME_MAX_LENGTH)]],
    color: [CATEGORY_PALETTE[6].color as string],
    monthlyBudget: this.fb.control<number | null>(null, Validators.min(0)),
  });

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly submitted = signal(false);

  constructor() {
    afterNextRender(() => {
      const category = this.category();
      if (category) {
        this.form.setValue({ name: category.name, color: category.color.toUpperCase(), monthlyBudget: category.monthlyBudget });
      }
      this.host.nativeElement.querySelector<HTMLInputElement>('input[formControlName="name"]')?.focus();
    });
  }

  protected nameInvalid(): boolean {
    const name = this.form.controls.name;
    return (name.invalid || !name.value.trim()) && (name.touched || this.submitted());
  }

  protected async save(): Promise<void> {
    this.submitted.set(true);
    const { name, color, monthlyBudget } = this.form.getRawValue();
    if (this.form.invalid || !name.trim()) {
      return;
    }
    await this.run(this.category()?.isArchived ?? false, { name: name.trim(), color, monthlyBudget });
  }

  protected async archive(): Promise<void> {
    const category = this.category();
    if (category) {
      await this.run(true, { name: category.name, color: category.color, monthlyBudget: category.monthlyBudget });
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && !this.busy()) {
      this.cancelled.emit();
    }
  }

  private async run(isArchived: boolean, values: { name: string; color: string; monthlyBudget: number | null }): Promise<void> {
    if (this.busy()) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      const category = this.category();
      await firstValueFrom(category ? this.api.update(category.id, { ...values, isArchived }) : this.api.create(values));
      this.saved.emit();
    } catch (error) {
      this.error.set(describeHttpError(error));
    } finally {
      this.busy.set(false);
    }
  }
}
