export interface CategorySpending {
  id: string;
  name: string;
  color: string;
  monthlyBudget: number | null;
  isArchived: boolean;
  receiptCount: number;
  spent: number;
}

export interface CategoryMonth {
  month: string;
  baseCurrency: string;
  totalBudget: number;
  totalSpent: number;
  categories: CategorySpending[];
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

export const CATEGORY_NAME_MAX_LENGTH = 60;
