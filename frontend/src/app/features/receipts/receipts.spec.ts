import { HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TEST_API, provideAuthTesting } from '../../core/auth/testing';
import { PagedResult, ReceiptSummary } from '../../core/receipts/receipt.models';
import { Receipts } from './receipts';

const RECEIPTS_URL = `${TEST_API}/api/receipts`;

function page(items: Partial<ReceiptSummary>[], totalCount = items.length): PagedResult<ReceiptSummary> {
  return {
    page: 1,
    pageSize: 25,
    totalCount,
    items: items.map((item, index) => ({
      id: `r-${index}`,
      number: index + 1,
      status: 'Processing',
      vendorName: null,
      receiptDate: null,
      currency: null,
      total: null,
      baseCurrencyTotal: null,
      categoryId: null,
      fileName: `file-${index}.jpg`,
      createdAt: '2026-10-27T09:00:00Z',
      ...item,
    })),
  };
}

describe('Receipts', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({ imports: [Receipts], providers: provideAuthTesting() });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(inputs: { status?: string; page?: string } = {}) {
    const fixture = TestBed.createComponent(Receipts);
    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }
    fixture.detectChanges();
    const request: TestRequest = await vi.waitFor(() =>
      http.expectOne((req) => req.url === RECEIPTS_URL),
    );
    return { fixture, request };
  }

  it('lists receipts with entry numbers, amounts and status stamps', async () => {
    const { fixture, request } = await render();
    request.flush(
      page([
        { number: 12, vendorName: 'Paper & Co.', total: 126.45, currency: 'USD', status: 'NeedsReview' },
        { number: 11, fileName: 'IMG_2041.jpg' },
      ]),
    );
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent!;
    expect(text).toContain('0012');
    expect(text).toContain('Paper & Co.');
    expect(text).toContain('126.45');
    expect(text).toContain('Needs review');
    // Not read by AI yet: the file name stands in for the vendor.
    expect(text).toContain('IMG_2041.jpg');
  });

  it('asks the API for the status and page in the address', async () => {
    const { request } = await render({ status: 'NeedsReview', page: '2' });

    expect(request.request.params.get('status')).toBe('NeedsReview');
    expect(request.request.params.get('page')).toBe('2');
    request.flush(page([]));
  });

  it('ignores an unknown status instead of sending it', async () => {
    const { request } = await render({ status: 'Hacked' });

    expect(request.request.params.has('status')).toBe(false);
    request.flush(page([]));
  });

  it('invites a first scan when the ledger is empty', async () => {
    const { fixture, request } = await render();
    request.flush(page([]));
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Scan your first receipt');
  });
});
