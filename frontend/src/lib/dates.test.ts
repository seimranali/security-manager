import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest';
import * as D from './dates';

beforeAll(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 9, 19, 14, 30)); // Mon 19 Oct 2026, local
});
afterAll(() => vi.useRealTimers());

describe('dates', () => {
  it('parses and formats local ISO dates, rejecting impossible dates', () => {
    expect(D.toISODate(D.parseISODate('2026-02-28')!)).toBe('2026-02-28');
    expect(D.parseISODate('2026-02-30')).toBeNull();
    expect(D.parseISODate('19/10/2026')).toBeNull();
    expect(D.today()).toBe('2026-10-19');
  });

  it('adds days and months across boundaries, clamping month ends', () => {
    expect(D.addDays('2026-12-31', 1)).toBe('2027-01-01');
    expect(D.addDays('2026-03-01', -1)).toBe('2026-02-28');
    expect(D.addMonths('2026-01-31', 1)).toBe('2026-02-28');
    expect(D.addMonths('2028-01-31', 1)).toBe('2028-02-29'); // leap year
    expect(D.addMonths('2026-03-15', -12)).toBe('2025-03-15');
    expect(D.endOfMonth('2026-02-10')).toBe('2026-02-28');
    expect(D.sameDayInMonth('2026-02-01', '2026-01-31')).toBe('2026-02-28');
  });

  it('builds a 6x7 month grid starting on the configured weekday', () => {
    const monday = D.monthMatrix('2026-10-01', 1);
    expect(monday).toHaveLength(6);
    expect(monday[0][0]).toBe('2026-09-28'); // Monday before 1 Oct (a Thursday)
    expect(monday.flat()).toContain('2026-10-31');

    const sunday = D.monthMatrix('2026-10-01', 0);
    expect(sunday[0][0]).toBe('2026-09-27');
    expect(D.startOfWeek('2026-10-19', 1)).toBe('2026-10-19');
  });

  it('clamps and compares dates', () => {
    expect(D.clampDate('2026-01-01', '2026-02-01', '2026-03-01')).toBe('2026-02-01');
    expect(D.clampDate('2026-04-01', '2026-02-01', '2026-03-01')).toBe('2026-03-01');
    expect(D.compareDates('2026-01-02', '2026-01-10')).toBe(-1);
  });

  it('produces inclusive whole-day UTC bounds for a local date range', () => {
    const { from, to } = D.dayBoundsUtc('2026-10-01', '2026-10-01');
    expect(new Date(from).getTime()).toBe(new Date(2026, 9, 1).getTime());
    expect(new Date(to).getTime()).toBe(new Date(2026, 9, 2).getTime() - 1);
  });

  it('parses times and round-trips local/UTC conversion', () => {
    expect(D.parseTime('09:05')).toBe(545);
    expect(D.parseTime('24:00')).toBeNull();
    expect(D.minutesToTime(545)).toBe('09:05');
    expect(D.utcTimeToLocal(D.localTimeToUtc('02:15'))).toBe('02:15');
    expect(D.nowTime(15)).toBe('14:30');
  });
});
