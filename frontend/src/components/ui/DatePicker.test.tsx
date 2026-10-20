import { useState } from 'react';
import { cleanup, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { DatePicker, type DateRange } from './DatePicker';

beforeAll(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 9, 19, 12, 0)); // 19 Oct 2026
  Element.prototype.scrollIntoView = vi.fn();
});
afterAll(() => vi.useRealTimers());
afterEach(cleanup);

const day = (iso: string) => document.querySelector<HTMLButtonElement>(`[data-date="${iso}"]`)!;

function Single(props: { initial?: string; min?: string; max?: string; onValue?: (v: string) => void }) {
  const [value, setValue] = useState(props.initial ?? '');
  return <DatePicker aria-label="Date" value={value} min={props.min} max={props.max} weekStartsOn={1}
    onChange={(v) => { setValue(v); props.onValue?.(v); }} />;
}

function Range(props: { onValue: (r: DateRange) => void }) {
  const [value, setValue] = useState<DateRange>({ start: '', end: '' });
  return <DatePicker mode="range" aria-label="Range" value={value} weekStartsOn={1}
    onChange={(r) => { setValue(r); props.onValue(r); }} />;
}

describe('DatePicker', () => {
  it('opens on the current month, marks today and selects a day', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<Single onValue={onValue} />);
    await user.click(screen.getByRole('button', { name: 'Date' }));

    expect(screen.getByRole('grid', { name: 'October 2026' })).toBeTruthy();
    expect(day('2026-10-19').getAttribute('aria-current')).toBe('date');
    expect(day('2026-09-28').className).toContain('outside'); // leading days from September (Monday start)

    await user.click(day('2026-10-07'));
    expect(onValue).toHaveBeenCalledWith('2026-10-07');
    expect(screen.queryByRole('grid')).toBeNull();
  });

  it('navigates with the keyboard across months and selects with Enter', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<Single initial="2026-10-30" onValue={onValue} />);
    await user.click(screen.getByRole('button', { name: 'Date' }));

    expect(document.activeElement).toBe(day('2026-10-30'));
    await user.keyboard('{ArrowDown}'); // +7 days → 6 Nov, month flips
    expect(screen.getByRole('grid', { name: 'November 2026' })).toBeTruthy();
    expect(document.activeElement).toBe(day('2026-11-06'));
    await user.keyboard('{PageUp}{ArrowLeft}{Enter}'); // 6 Oct, then 5 Oct
    expect(onValue).toHaveBeenLastCalledWith('2026-10-05');
  });

  it('disables dates outside min/max and blocks navigation past them', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<Single initial="2026-10-15" min="2026-10-10" max="2026-10-20" onValue={onValue} />);
    await user.click(screen.getByRole('button', { name: 'Date' }));

    expect(day('2026-10-09').getAttribute('aria-disabled')).toBe('true');
    await user.click(day('2026-10-09'));
    expect(onValue).not.toHaveBeenCalled();
    expect((screen.getByRole('button', { name: 'Previous month' }) as HTMLButtonElement).disabled).toBe(true);
    expect((screen.getByRole('button', { name: 'Next month' }) as HTMLButtonElement).disabled).toBe(true);
  });

  it('drills up to months and years to jump far', async () => {
    const user = userEvent.setup();
    render(<Single initial="2026-10-15" />);
    await user.click(screen.getByRole('button', { name: 'Date' }));
    await user.click(screen.getByRole('button', { name: 'October 2026' }));
    await user.click(screen.getByRole('button', { name: '2026' }));
    await user.click(screen.getByRole('option', { name: '2024' }));
    await user.click(screen.getByRole('option', { name: 'Feb' }));
    expect(screen.getByRole('grid', { name: 'February 2024' })).toBeTruthy();
  });

  it('selects a range in either click order and previews it on hover', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<Range onValue={onValue} />);
    await user.click(screen.getByRole('button', { name: 'Range' }));

    await user.click(day('2026-10-12'));
    await user.hover(day('2026-10-08'));
    expect(day('2026-10-10').className).toContain('in-range');
    await user.click(day('2026-10-08')); // earlier than the start → swapped
    expect(onValue).toHaveBeenCalledWith({ start: '2026-10-08', end: '2026-10-12' });
  });

  it('applies range presets and shows the preset name', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<Range onValue={onValue} />);
    await user.click(screen.getByRole('button', { name: 'Range' }));
    await user.click(screen.getByRole('button', { name: 'Last 7 days' }));

    expect(onValue).toHaveBeenCalledWith({ start: '2026-10-13', end: '2026-10-19' });
    expect(screen.getByRole('button', { name: 'Range' }).textContent).toContain('Last 7 days');
  });

  it('closes on Escape and clears with the clear button', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<Single initial="2026-10-01" onValue={onValue} />);
    await user.click(screen.getByRole('button', { name: 'Date' }));
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('grid')).toBeNull();

    await user.click(screen.getByRole('button', { name: 'Clear date' }));
    expect(onValue).toHaveBeenCalledWith('');
  });
});
