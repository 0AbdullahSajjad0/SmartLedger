import { HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TEST_API, provideAuthTesting } from '../../core/auth/testing';
import { ReceiptDetail } from '../../core/receipts/receipt.models';
import { ReceiptPage } from './receipt-page';

const RECEIPT_URL = `${TEST_API}/api/receipts/r-1`;

function detail(changes: Partial<ReceiptDetail> = {}): ReceiptDetail {
  return {
    id: 'r-1',
    number: 1,
    status: 'NeedsReview',
    vendorName: null,
    receiptDate: null,
    currency: null,
    subtotal: null,
    tax: null,
    total: null,
    paymentMethod: null,
    categoryId: null,
    categorySuggestedByAi: false,
    notes: null,
    failureReason: null,
    fileName: 'IMG_2041.HEIC',
    contentType: 'image/heic',
    sizeBytes: 26_303,
    hasPreview: true,
    lineItems: [],
    extractionFlags: [],
    baseCurrency: null,
    baseCurrencyTotal: null,
    approvedAt: null,
    createdAt: '2026-10-27T09:00:00Z',
    ...changes,
  };
}

describe('ReceiptPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    // jsdom has no blob: URLs; the page only needs an address string back.
    URL.createObjectURL = vi.fn(() => 'blob:test-receipt');
    URL.revokeObjectURL = vi.fn();
    TestBed.configureTestingModule({ imports: [ReceiptPage], providers: provideAuthTesting() });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** Renders the page; `image` is which picture it's expected to fetch once the details arrive. */
  async function render(receipt: ReceiptDetail | null, image: 'preview' | 'file' | null = null) {
    const fixture = TestBed.createComponent(ReceiptPage);
    fixture.componentRef.setInput('id', 'r-1');
    fixture.detectChanges();

    const details = await vi.waitFor(() => http.expectOne(RECEIPT_URL));
    http.expectOne(`${TEST_API}/api/categories`).flush([
      { id: 'cat-office', name: 'Office Supplies', color: '#8C8270', monthlyBudget: null, isArchived: false },
    ]);

    if (receipt) {
      details.flush(receipt);
    } else {
      details.flush({ title: 'Receipt not found.' }, { status: 404, statusText: 'Not Found' });
    }

    // Answered before whenStable(), which would otherwise wait for this request forever.
    if (image) {
      const request = await vi.waitFor(() => http.expectOne(`${RECEIPT_URL}/${image}`));
      request.flush(new Blob(['jpeg'], { type: 'image/jpeg' }));
    }

    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, page: fixture.nativeElement as HTMLElement };
  }

  it('shows the cleaned preview next to the record', async () => {
    const { page } = await render(detail(), 'preview');

    expect(page.querySelector('img')?.getAttribute('src')).toBe('blob:test-receipt');
    expect(page.textContent).toContain('Entry 0001');
    expect(page.textContent).toContain('including any location your camera saved in it');
  });

  it("says the preview is being made while it isn't ready yet", async () => {
    const { page } = await render(detail({ status: 'Processing', hasPreview: false }));

    expect(page.querySelector('img')).toBeNull();
    expect(page.textContent).toContain('PREPARING THE PREVIEW');
    expect(page.textContent).toContain('AI is reading this receipt');
  });

  it("shows what AI read, with numbered notes on the fields it wasn't sure about", async () => {
    const { page } = await render(
      detail({
        vendorName: 'Paper & Co. Office Supply',
        paymentMethod: 'VISA ····4242',
        categoryId: 'cat-office',
        categorySuggestedByAi: true,
        total: 126.45,
        currency: 'USD',
        extractionFlags: [{ field: 'PaymentMethod', reason: 'One digit of the card number was faded.' }],
      }),
      'preview',
    );

    expect(page.querySelector('h1')?.textContent).toContain('Paper & Co. Office Supply');
    expect(page.textContent).toContain('Office Supplies');
    expect(page.textContent).toContain('suggested');
    expect(page.querySelector('.fields__item--flagged')?.textContent).toContain('Paid with');
    expect(page.querySelector('.footnotes')?.textContent).toContain('One digit of the card number was faded.');
  });

  it('shows the original for receipts uploaded before previews existed, if the browser can display it', async () => {
    const { page } = await render(detail({ hasPreview: false, fileName: 'old.png', contentType: 'image/png' }), 'file');

    expect(page.querySelector('img')?.getAttribute('src')).toBe('blob:test-receipt');
  });

  it('offers only the download for old files the browser cannot display', async () => {
    const { page } = await render(detail({ hasPreview: false }));

    expect(page.querySelector('img')).toBeNull();
    expect(page.textContent).toContain("There's no preview of this file");
  });

  it('downloads the original only when asked, named by entry number', async () => {
    const { fixture, page } = await render(
      detail({ number: 3, fileName: 'invoice.PDF', contentType: 'application/pdf' }),
      'preview',
    );
    let savedAs: string | null = null;
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      savedAs = this.download;
    });

    expect(page.querySelector('.download__note')?.textContent?.trim()).toBe('Exactly as uploaded.');
    page.querySelector<HTMLButtonElement>('.download button')!.click();
    // The auth interceptor adds the token asynchronously, so the request appears a moment after the click.
    const original = await vi.waitFor(() => http.expectOne(`${RECEIPT_URL}/file`));
    original.flush(new Blob(['%PDF'], { type: 'application/pdf' }));
    await fixture.whenStable();

    expect(savedAs).toBe('receipt-0003.pdf');
  });

  it('says so when the receipt does not exist or is not yours', async () => {
    const { page } = await render(null);

    expect(page.querySelector('[role="alert"]')?.textContent).toContain('Receipt not found.');
  });
});
