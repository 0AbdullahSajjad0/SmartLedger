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
    status: 'Processing',
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
    fileName: 'IMG_2041.png',
    contentType: 'image/png',
    sizeBytes: 26_303,
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

  async function render(receipt: ReceiptDetail | null) {
    const fixture = TestBed.createComponent(ReceiptPage);
    fixture.componentRef.setInput('id', 'r-1');
    fixture.detectChanges();

    const details = await vi.waitFor(() => http.expectOne(RECEIPT_URL));
    const file = http.expectOne(`${RECEIPT_URL}/file`);

    if (receipt) {
      details.flush(receipt);
      file.flush(new Blob(['png'], { type: receipt.contentType }));
    } else {
      details.flush({ title: 'Receipt not found.' }, { status: 404, statusText: 'Not Found' });
      file.flush(null, { status: 404, statusText: 'Not Found' });
    }

    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the original image next to the record', async () => {
    const page = await render(detail());

    expect(page.querySelector('img')?.getAttribute('src')).toBe('blob:test-receipt');
    expect(page.textContent).toContain('Entry 0001');
    expect(page.textContent).toContain("AI hasn't read this receipt yet");
    expect(page.querySelector('a[download]')?.getAttribute('download')).toBe('receipt-0001.png');
  });

  it('offers a download instead of a preview for PDFs', async () => {
    const page = await render(detail({ number: 3, fileName: 'invoice.pdf', contentType: 'application/pdf' }));

    expect(page.querySelector('img')).toBeNull();
    expect(page.textContent).toContain("PDF receipts can't be previewed here yet");
    expect(page.querySelector('a[download]')?.getAttribute('download')).toBe('receipt-0003.pdf');
  });

  it('says so when the receipt does not exist or is not yours', async () => {
    const page = await render(null);

    expect(page.querySelector('[role="alert"]')?.textContent).toContain('Receipt not found.');
  });
});
