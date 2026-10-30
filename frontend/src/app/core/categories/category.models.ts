// Mirrors SmartLedger.Application.Categories contracts on the API.

/** One category's month: the approved receipts filed under it, against its budget. */
export interface CategorySpending {
  id: string;
  name: string;
  color: string;
  monthlyBudget: number | null;
  isArchived: boolean;
  receiptCount: number;
  /** Sum of the receipts' base-currency totals. */
  spent: number;
}

/** Every category with what was spent in one month (by receipt date, in the base currency), and the totals. */
export interface CategoryMonth {
  /** "yyyy-MM". */
  month: string;
  baseCurrency: string;
  /** Sum of the budgets that month had (archiving ends a budget from its month). */
  totalBudget: number;
  /** Everything approved in the month, archived categories included. */
  totalSpent: number;
  categories: CategorySpending[];
  /** A past month: its budgets are as they were then and can't be changed. */
  isClosed: boolean;
}

export interface CategoryRequest {
  name: string;
  color: string;
  monthlyBudget: number | null;
}

export interface UpdateCategoryRequest extends CategoryRequest {
  isArchived: boolean;
}

/** The colours the API accepts: muted earth tones that sit with espresso, cream and terra cotta. */
export const CATEGORY_PALETTE = [
  { color: '#3D4A2F', name: 'Olive' },
  { color: '#5B6B73', name: 'Slate' },
  { color: '#7A5C45', name: 'Walnut' },
  { color: '#B08A2E', name: 'Mustard' },
  { color: '#8C8270', name: 'Taupe' },
  { color: '#4A4038', name: 'Umber' },
  { color: '#2F5D62', name: 'Deep teal' },
  { color: '#6E4F6B', name: 'Plum' },
  { color: '#556B5D', name: 'Sage' },
  { color: '#A07D5C', name: 'Sand' },
] as const;

/** Same limit as the API. */
export const CATEGORY_NAME_MAX_LENGTH = 60;
