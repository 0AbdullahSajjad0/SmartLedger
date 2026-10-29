// Mirrors SmartLedger.Application.Reports contracts on the API.

import { CategorySpending } from '../categories/category.models';

/** A month in review. Approved receipts only, by receipt date, in the base currency. */
export interface MonthReport {
  /** "yyyy-MM". */
  month: string;
  baseCurrency: string;
  spent: number;
  approvedCount: number;
  previousMonthSpent: number;
  totalBudget: number;
  /** Six months ending with this one, oldest first. */
  trend: MonthTotal[];
  /** Up to five, biggest spend first. */
  topVendors: VendorSpending[];
  /** Every active category, and archived ones with spending. */
  categories: CategorySpending[];
  /** Null until the user asks AI to write it. */
  insight: Insight | null;
}

export interface MonthTotal {
  month: string;
  spent: number;
}

export interface VendorSpending {
  vendor: string;
  categoryName: string | null;
  receiptCount: number;
  spent: number;
}

/** AI's analysis of a month. */
export interface Insight {
  headline: string;
  paragraphs: string[];
  receiptCount: number;
  generatedAt: string;
  /** Receipts were approved or reopened since it was written. */
  isOutOfDate: boolean;
}
