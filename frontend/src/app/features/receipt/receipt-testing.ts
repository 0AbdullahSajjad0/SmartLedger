import { ReceiptDetail } from '../../core/receipts/receipt.models';

export function receiptDetail(changes: Partial<ReceiptDetail> = {}): ReceiptDetail {
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
    exchangeRate: null,
    exchangeRateDate: null,
    amountCharged: null,
    baseCurrencyTotal: null,
    approvedAt: null,
    createdAt: '2026-10-27T09:00:00Z',
    ...changes,
  };
}
