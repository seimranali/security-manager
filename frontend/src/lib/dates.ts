/**
 * Timezone-safe date/time helpers. Dates are exchanged as local calendar strings ("YYYY-MM-DD") and times as
 * 24-hour "HH:mm", so a picked day never shifts because of UTC conversion.
 */

export type ISODate = string; // YYYY-MM-DD
export type TimeString = string; // HH:mm (24h)
export type Weekday = 0 | 1 | 2 | 3 | 4 | 5 | 6;

const pad = (n: number) => String(n).padStart(2, '0');

export function toISODate(d: Date): ISODate {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/** Parses "YYYY-MM-DD" as a local date; returns null for invalid input (including 2026-02-30). */
export function parseISODate(value?: string | null): Date | null {
  const m = value?.match(/^(\d{4})-(\d{2})-(\d{2})$/);
  if (!m) return null;
  const d = new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]));
  return d.getFullYear() === Number(m[1]) && d.getMonth() === Number(m[2]) - 1 && d.getDate() === Number(m[3]) ? d : null;
}

export const today = (): ISODate => toISODate(new Date());

export function addDays(value: ISODate, days: number): ISODate {
  const d = parseISODate(value)!;
  d.setDate(d.getDate() + days);
  return toISODate(d);
}

/** Adds months, clamping the day (Jan 31 + 1 month = Feb 28/29). */
export function addMonths(value: ISODate, months: number): ISODate {
  const d = parseISODate(value)!;
  const target = new Date(d.getFullYear(), d.getMonth() + months, 1);
  const lastDay = new Date(target.getFullYear(), target.getMonth() + 1, 0).getDate();
  target.setDate(Math.min(d.getDate(), lastDay));
  return toISODate(target);
}

export const startOfMonth = (value: ISODate): ISODate => `${value.slice(0, 7)}-01`;

export function endOfMonth(value: ISODate): ISODate {
  const d = parseISODate(startOfMonth(value))!;
  return toISODate(new Date(d.getFullYear(), d.getMonth() + 1, 0));
}

/** The same day-of-month as `reference`, moved into the month starting at `monthStart` (clamped to its length). */
export function sameDayInMonth(monthStart: ISODate, reference: ISODate): ISODate {
  const lastDay = Number(endOfMonth(monthStart).slice(8));
  return `${monthStart.slice(0, 8)}${pad(Math.min(Number(reference.slice(8)), lastDay))}`;
}

/** ISO strings compare lexicographically in chronological order. */
export const compareDates = (a: ISODate, b: ISODate) => (a < b ? -1 : a > b ? 1 : 0);

export function clampDate(value: ISODate, min?: ISODate, max?: ISODate): ISODate {
  if (min && value < min) return min;
  if (max && value > max) return max;
  return value;
}

export function startOfWeek(value: ISODate, weekStartsOn: Weekday): ISODate {
  const d = parseISODate(value)!;
  return addDays(value, -((d.getDay() - weekStartsOn + 7) % 7));
}

/** 6×7 grid of dates covering the month of `value`, starting on `weekStartsOn`. */
export function monthMatrix(value: ISODate, weekStartsOn: Weekday): ISODate[][] {
  let cursor = startOfWeek(startOfMonth(value), weekStartsOn);
  return Array.from({ length: 6 }, () =>
    Array.from({ length: 7 }, () => {
      const day = cursor;
      cursor = addDays(cursor, 1);
      return day;
    }),
  );
}

/** First day of week for the user's locale (Intl weekInfo where supported), defaulting to Monday. */
export function localeWeekStart(): Weekday {
  try {
    const locale = new Intl.Locale(navigator.language) as Intl.Locale & { weekInfo?: { firstDay: number }; getWeekInfo?: () => { firstDay: number } };
    const firstDay = locale.getWeekInfo?.().firstDay ?? locale.weekInfo?.firstDay;
    if (firstDay) return (firstDay % 7) as Weekday;
  } catch {
    /* unsupported */
  }
  return 1;
}

export function weekdayNames(weekStartsOn: Weekday, style: 'narrow' | 'short' | 'long' = 'short'): string[] {
  const fmt = new Intl.DateTimeFormat(undefined, { weekday: style });
  // 2023-01-01 was a Sunday.
  return Array.from({ length: 7 }, (_, i) => fmt.format(new Date(2023, 0, 1 + ((i + weekStartsOn) % 7))));
}

export function monthNames(style: 'short' | 'long' = 'long'): string[] {
  const fmt = new Intl.DateTimeFormat(undefined, { month: style });
  return Array.from({ length: 12 }, (_, i) => fmt.format(new Date(2023, i, 1)));
}

export function formatDisplayDate(value: ISODate, opts: Intl.DateTimeFormatOptions = { dateStyle: 'medium' }): string {
  const d = parseISODate(value);
  return d ? new Intl.DateTimeFormat(undefined, opts).format(d) : '';
}

export function formatDateRange(start: ISODate, end: ISODate): string {
  const s = parseISODate(start);
  const e = parseISODate(end);
  if (!s) return '';
  if (!e) return `${formatDisplayDate(start)} – …`;
  const fmt = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }) as Intl.DateTimeFormat & { formatRange?: (a: Date, b: Date) => string };
  return fmt.formatRange ? fmt.formatRange(s, e) : `${fmt.format(s)} – ${fmt.format(e)}`;
}

/** Local start of `value` and the start of the following day, as UTC ISO timestamps (for API range filters). */
export function dayBoundsUtc(start: ISODate, end: ISODate): { from: string; to: string } {
  return {
    from: parseISODate(start)!.toISOString(),
    to: new Date(parseISODate(addDays(end, 1))!.getTime() - 1).toISOString(),
  };
}

// ---------- Time ----------

export function parseTime(value?: string | null): number | null {
  const m = value?.match(/^(\d{1,2}):(\d{2})$/);
  if (!m) return null;
  const h = Number(m[1]);
  const min = Number(m[2]);
  return h < 24 && min < 60 ? h * 60 + min : null;
}

export const minutesToTime = (minutes: number): TimeString => `${pad(Math.floor(minutes / 60) % 24)}:${pad(minutes % 60)}`;

export function nowTime(step = 1): TimeString {
  const d = new Date();
  const minutes = d.getHours() * 60 + d.getMinutes();
  return minutesToTime(Math.floor(minutes / step) * step);
}

export function localeUses12h(): boolean {
  try {
    const cycle = new Intl.DateTimeFormat(undefined, { hour: 'numeric' }).resolvedOptions().hourCycle;
    return cycle === 'h12' || cycle === 'h11';
  } catch {
    return false;
  }
}

export function formatDisplayTime(value: TimeString, hour12 = localeUses12h()): string {
  const minutes = parseTime(value);
  if (minutes == null) return '';
  return new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit', hour12 })
    .format(new Date(2023, 0, 1, Math.floor(minutes / 60), minutes % 60));
}

/** Converts a local wall-clock time to UTC "HH:mm" using today's offset. */
export function localTimeToUtc(value: TimeString): TimeString {
  const minutes = parseTime(value);
  if (minutes == null) return value;
  const d = new Date();
  d.setHours(Math.floor(minutes / 60), minutes % 60, 0, 0);
  return `${pad(d.getUTCHours())}:${pad(d.getUTCMinutes())}`;
}

/** Converts a UTC "HH:mm" to local wall-clock time using today's offset. */
export function utcTimeToLocal(value: TimeString): TimeString {
  const minutes = parseTime(value);
  if (minutes == null) return value;
  const d = new Date();
  d.setUTCHours(Math.floor(minutes / 60), minutes % 60, 0, 0);
  return `${pad(d.getHours())}:${pad(d.getMinutes())}`;
}
