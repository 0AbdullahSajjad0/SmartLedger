import { HttpRequest } from '@angular/common/http';
import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TEST_API, provideAuthTesting } from '../../core/auth/testing';
import { CategoryMonth, CategorySpending } from '../../core/categories/category.models';
import { CategoriesPage } from './categories-page';

const CATEGORIES_URL = `${TEST_API}/api/categories`;

function category(changes: Partial<CategorySpending>): CategorySpending {
  return { id: 'cat', name: 'Category', color: '#3D4A2F', monthlyBudget: null, isArchived: false, receiptCount: 0, spent: 0, ...changes };
}

const OCTOBER: CategoryMonth = {
  month: '2026-10',
  baseCurrency: 'USD',
  totalBudget: 2200,
  totalSpent: 2152.3,
  categories: [
    category({ id: 'travel', name: 'Travel', monthlyBudget: 1500, receiptCount: 7, spent: 1640 }),
    category({ id: 'food', name: 'Food & Dining', color: '#B08A2E', monthlyBudget: 600, receiptCount: 11, spent: 512.3 }),
    category({ id: 'office', name: 'Office Supplies', color: '#8C8270', monthlyBudget: 100, receiptCount: 0, spent: 0 }),
    category({ id: 'misc', name: 'Misc', monthlyBudget: null }),
    category({ id: 'old', name: 'Old projects', isArchived: true }),
  ],
};

describe('CategoriesPage', () => {
  let http: HttpTestingController;
  let fixture: ComponentFixture<CategoriesPage>;
  let page: HTMLElement;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ imports: [CategoriesPage], providers: provideAuthTesting() });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const isSpending = (month: string) => (r: HttpRequest<unknown>) =>
    r.url === `${CATEGORIES_URL}/spending` && r.params.get('month') === month;

  async function render(data: CategoryMonth = OCTOBER, month: string | undefined = '2026-10') {
    fixture = TestBed.createComponent(CategoriesPage);
    fixture.componentRef.setInput('month', month);
    page = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    (await vi.waitFor(() => http.expectOne(isSpending(data.month)))).flush(data);
    await settle();
  }

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  /** Each row as its cells' text: "Travel | 7 | 1,640.00 | …". */
  function rows(): string[] {
    return [...page.querySelectorAll('table.categories tbody tr')].map((row) =>
      [...row.querySelectorAll('td')].map((cell) => cell.textContent!.replace(/\s+/g, ' ').trim()).join(' | '),
    );
  }

  /** Each big figure as "label: value". */
  function figures(): string[] {
    return [...page.querySelectorAll('.figures__item')].map(
      (item) => `${item.querySelector('.label')!.textContent!.trim()}: ${item.querySelector('.figures__value')!.textContent!.trim()}`,
    );
  }

  function button(label: string): HTMLButtonElement {
    return [...page.querySelectorAll('button')].find((b) => b.textContent?.trim() === label || b.ariaLabel === label)!;
  }

  function type(selector: string, value: string): void {
    const input = page.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  async function answer(request: () => TestRequest, body: CategoryMonth): Promise<void> {
    (await vi.waitFor(request)).flush(body);
  }

  it('shows the month’s budget, spending and what is left', async () => {
    await render();

    expect(figures()).toEqual(['Monthly budget: $2,200.00', 'Spent in Oct: $2,152.30', 'Remaining: $47.70']);
    expect(page.querySelector('.months__current')?.textContent).toBe('October 2026');
  });

  it('lists active categories with spending against budget, coloured by how close they are', async () => {
    await render();

    expect(rows()).toEqual([
      'Travel | 7 | 1,640.00 | 1,500.00 | 109% | Edit',
      'Food & Dining | 11 | 512.30 | 600.00 | 85% | Edit',
      'Office Supplies | 0 | 0.00 | 100.00 | 0% | Edit',
      'Misc | 0 | 0.00 | — | No budget | Edit',
    ]);
    const tones = [...page.querySelectorAll('.usage')].map((u) => u.getAttribute('data-tone'));
    expect(tones).toEqual(['over', 'fine', 'fine']);
  });

  it('says when the month went over budget', async () => {
    await render({ ...OCTOBER, totalBudget: 2000 });

    expect(figures()[2]).toBe('Over budget by: $152.30');
    expect(page.querySelector('.figures__value--over')).not.toBeNull();
  });

  it('lists archived categories apart, and can bring one back', async () => {
    await render();
    expect(page.querySelector('.archived')?.textContent).toContain('Old projects');

    button('Unarchive Old projects').click();
    const put = await vi.waitFor(() => http.expectOne({ method: 'PUT', url: `${CATEGORIES_URL}/old` }));
    expect(put.request.body).toEqual({ name: 'Old projects', color: '#3D4A2F', monthlyBudget: null, isArchived: false });
    put.flush({});
    await answer(() => http.expectOne(isSpending('2026-10')), OCTOBER);
  });

  it('adds a category in place', async () => {
    await render();

    button('New category').click();
    await settle();
    type('#category-name-new', '  Marketing ');
    type('#category-budget-new', '250');
    page.querySelector<HTMLInputElement>('input[type="radio"][value="#6E4F6B"]')!.click();
    button('Add category').click();

    const post = await vi.waitFor(() => http.expectOne({ method: 'POST', url: CATEGORIES_URL }));
    expect(post.request.body).toEqual({ name: 'Marketing', color: '#6E4F6B', monthlyBudget: 250 });
    post.flush({ id: 'marketing', name: 'Marketing', color: '#6E4F6B', monthlyBudget: 250, isArchived: false });

    await answer(() => http.expectOne(isSpending('2026-10')), OCTOBER);
    await settle();
    expect(page.querySelector('[app-category-editor]')).toBeNull();
  });

  it("shows the API's reason when a category can't be added", async () => {
    await render();
    button('New category').click();
    await settle();
    type('#category-name-new', 'travel');

    button('Add category').click();
    (await vi.waitFor(() => http.expectOne({ method: 'POST', url: CATEGORIES_URL }))).flush(
      { title: 'You already have a category called "Travel".' },
      { status: 409, statusText: 'Conflict' },
    );

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.querySelector('[app-category-editor] [role="alert"]')?.textContent).toContain('already have a category');
    });
  });

  it('edits a category, filled in with its current values, and can archive it', async () => {
    await render();

    button('Edit Food & Dining').click();
    await settle();
    expect(page.querySelector<HTMLInputElement>('#category-name-food')?.value).toBe('Food & Dining');
    expect(page.querySelector<HTMLInputElement>('#category-budget-food')?.value).toBe('600');
    expect(page.querySelector<HTMLInputElement>('input[type="radio"]:checked')?.value).toBe('#B08A2E');

    button('Archive').click();
    const put = await vi.waitFor(() => http.expectOne({ method: 'PUT', url: `${CATEGORIES_URL}/food` }));
    expect(put.request.body).toEqual({ name: 'Food & Dining', color: '#B08A2E', monthlyBudget: 600, isArchived: true });
    put.flush({});
    await answer(() => http.expectOne(isSpending('2026-10')), OCTOBER);
  });

  it('pages back through months, but not into the future', async () => {
    const thisMonth = new Date().toISOString().slice(0, 7);
    await render({ ...OCTOBER, month: thisMonth }, undefined);

    const links = [...page.querySelectorAll<HTMLAnchorElement>('.months a')];
    expect(links).toHaveLength(1); // only "back"
    expect(links[0].getAttribute('href')).toContain('month=');
  });
});
