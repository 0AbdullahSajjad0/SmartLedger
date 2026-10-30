import { CategorySpending } from '../categories/category.models';

export interface MonthReport {
  month: string;
  baseCurrency: string;
  spent: number;
  approvedCount: number;
  previousMonthSpent: number;
  totalBudget: number;
  trend: MonthTotal[];
  topVendors: VendorSpending[];
  categories: CategorySpending[];
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

export interface Insight {
  headline: string;
  paragraphs: string[];
  receiptCount: number;
  generatedAt: string;
  isOutOfDate: boolean;
}
