export function monthOf(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`;
}

export function shiftMonth(month: string, by: number): string {
  const [year, monthNumber] = month.split('-').map(Number);
  return monthOf(new Date(year, monthNumber - 1 + by, 1));
}

export function isMonth(value: string | undefined | null): value is string {
  return !!value && /^\d{4}-(0[1-9]|1[0-2])$/.test(value);
}

const LONG = new Intl.DateTimeFormat('en', { month: 'long', year: 'numeric' });
const NAME = new Intl.DateTimeFormat('en', { month: 'long' });
const SHORT = new Intl.DateTimeFormat('en', { month: 'short' });

export function monthLabel(month: string, style: 'long' | 'name' | 'short' = 'long'): string {
  const [year, monthNumber] = month.split('-').map(Number);
  const formatter = style === 'long' ? LONG : style === 'name' ? NAME : SHORT;
  return formatter.format(new Date(year, monthNumber - 1, 1));
}
