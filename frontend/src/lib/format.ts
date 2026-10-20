export { clsx as cn } from 'clsx';

const dateTime = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' });
const time = new Intl.DateTimeFormat(undefined, { hour: '2-digit', minute: '2-digit' });
const day = new Intl.DateTimeFormat(undefined, { weekday: 'short', day: 'numeric' });
const number = new Intl.NumberFormat();

export const formatDateTime = (iso?: string | null) => (iso ? dateTime.format(new Date(iso)) : '—');
export const formatTime = (iso: string) => time.format(new Date(iso));
export const formatDay = (iso: string) => day.format(new Date(iso));
export const formatNumber = (n?: number | null) => (n == null ? '—' : number.format(n));

export function timeAgo(iso?: string | null): string {
  if (!iso) return 'never';
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000);
  const future = seconds < 0;
  const s = Math.abs(seconds);
  const [value, unit] =
    s < 60 ? [s, 's'] : s < 3600 ? [Math.floor(s / 60), 'm'] : s < 86400 ? [Math.floor(s / 3600), 'h'] : [Math.floor(s / 86400), 'd'];
  if (s < 10) return 'just now';
  return future ? `in ${value}${unit}` : `${value}${unit} ago`;
}

export function formatMinutes(minutes?: number | null): string {
  if (minutes == null) return '—';
  if (minutes < 60) return `${Math.round(minutes)}m`;
  if (minutes < 1440) return `${(minutes / 60).toFixed(1)}h`;
  return `${(minutes / 1440).toFixed(1)}d`;
}

export function formatInterval(minutes: number): string {
  if (minutes % 1440 === 0) return minutes === 1440 ? 'Daily' : `Every ${minutes / 1440} days`;
  if (minutes % 60 === 0) return minutes === 60 ? 'Hourly' : `Every ${minutes / 60} hours`;
  return `Every ${minutes} min`;
}

/** "PortScan, HttpHeaders" / "All" -> array of flags. */
export function parseScanTypes(value: string): string[] {
  if (value === 'All') return ['PortScan', 'HttpHeaders', 'SslCertificate'];
  return value.split(',').map((v) => v.trim()).filter(Boolean);
}

const SEVERITY_ORDER = ['Info', 'Low', 'Medium', 'High', 'Critical'];
/** Numeric rank so severities sort Info < Low < Medium < High < Critical. */
export const severityRank = (s: string) => SEVERITY_ORDER.indexOf(s);

export const toDate = (iso?: string | null) => (iso ? new Date(iso) : null);

export const splitCamel =(s: string) => s.replace(/([a-z])([A-Z])/g, '$1 $2');
