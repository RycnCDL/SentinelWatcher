// One number format for the whole app. Costs are in US dollars, so numbers use the matching
// en-US format (1,234.56) instead of mixing the browser locale with fixed-point strings.
const LOCALE = 'en-US';

const formatters = new Map<number, Intl.NumberFormat>();

function numberFormat(digits: number): Intl.NumberFormat {
  let f = formatters.get(digits);
  if (!f) {
    f = new Intl.NumberFormat(LOCALE, { minimumFractionDigits: digits, maximumFractionDigits: digits });
    formatters.set(digits, f);
  }
  return f;
}

const usd = new Intl.NumberFormat(LOCALE, { style: 'currency', currency: 'USD' });

/** 13027.853 -> "13,027.85" (two decimals by default). */
export function formatNumber(value: number | null | undefined, digits = 2): string {
  return value == null || Number.isNaN(value) ? '-' : numberFormat(digits).format(value);
}

/** 35.1 -> "$35.10". */
export function formatUsd(value: number | null | undefined): string {
  return value == null || Number.isNaN(value) ? '-' : usd.format(value);
}

/** 12.7 -> "12.70 GB". */
export function formatGb(value: number | null | undefined, digits = 2): string {
  return value == null || Number.isNaN(value) ? '-' : `${formatNumber(value, digits)} GB`;
}
