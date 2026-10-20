import { useState } from 'react';
import { cleanup, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterAll, afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { TimePicker } from './TimePicker';

beforeAll(() => {
  vi.useFakeTimers({ toFake: ['Date'] });
  vi.setSystemTime(new Date(2026, 9, 19, 14, 37));
  Element.prototype.scrollIntoView = vi.fn();
});
afterAll(() => vi.useRealTimers());
afterEach(cleanup);

function Harness(props: { initial?: string; hour12?: boolean; min?: string; max?: string; onValue?: (v: string) => void }) {
  const [value, setValue] = useState(props.initial ?? '');
  return <TimePicker aria-label="Time" value={value} hour12={props.hour12 ?? false} minuteStep={15} min={props.min} max={props.max}
    onChange={(v) => { setValue(v); props.onValue?.(v); }} />;
}

const column = (name: string) => screen.getByRole('listbox', { name });
const option = (col: string, label: string) => within(column(col)).getByRole('option', { name: label });

describe('TimePicker', () => {
  it('builds hour and minute columns using the minute step and composes the value', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<Harness onValue={onValue} />);
    await user.click(screen.getByRole('button', { name: 'Time' }));

    expect(within(column('Hours')).getAllByRole('option')).toHaveLength(24);
    expect(within(column('Minutes')).getAllByRole('option').map((o) => o.textContent)).toEqual(['00', '15', '30', '45']);

    await user.click(option('Hours', '09'));
    expect(onValue).toHaveBeenLastCalledWith('09:00');
    await user.click(option('Minutes', '45'));
    expect(onValue).toHaveBeenLastCalledWith('09:45');
    expect(screen.getByRole('button', { name: 'Time' }).textContent).toContain('09:45');
  });

  it('handles 12-hour mode with AM/PM', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<Harness initial="09:30" hour12 onValue={onValue} />);
    expect(screen.getByRole('button', { name: 'Time' }).textContent).toMatch(/9:30\s?AM/i);
    await user.click(screen.getByRole('button', { name: 'Time' }));

    await user.click(option('AM/PM', 'PM'));
    expect(onValue).toHaveBeenLastCalledWith('21:30');
    await user.click(option('Hours', '12'));
    expect(onValue).toHaveBeenLastCalledWith('12:30'); // 12 PM is noon
  });

  it('disables times outside min/max', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<Harness initial="09:00" min="08:30" max="17:00" onValue={onValue} />);
    await user.click(screen.getByRole('button', { name: 'Time' }));

    expect(option('Hours', '07').getAttribute('aria-disabled')).toBe('true');
    expect(option('Hours', '18').getAttribute('aria-disabled')).toBe('true');
    await user.click(option('Hours', '07'));
    expect(onValue).not.toHaveBeenCalled();

    await user.click(option('Hours', '08')); // only 08:30 and 08:45 are valid → snaps to 08:30
    expect(onValue).toHaveBeenLastCalledWith('08:30');
    expect(option('Minutes', '00').getAttribute('aria-disabled')).toBe('true');
  });

  it('supports keyboard: arrows change the value, Left/Right switch columns, Enter closes', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<Harness initial="10:15" onValue={onValue} />);
    await user.click(screen.getByRole('button', { name: 'Time' }));

    expect(document.activeElement).toBe(column('Hours'));
    await user.keyboard('{ArrowDown}');
    expect(onValue).toHaveBeenLastCalledWith('11:15');
    await user.keyboard('{ArrowRight}{ArrowUp}');
    expect(onValue).toHaveBeenLastCalledWith('11:00');
    await user.keyboard('{Enter}');
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('"Now" picks the current time rounded down to the step', async () => {
    const user = userEvent.setup();
    const onValue = vi.fn();
    render(<Harness onValue={onValue} />);
    await user.click(screen.getByRole('button', { name: 'Time' }));
    await user.click(screen.getByRole('button', { name: 'Now' }));
    expect(onValue).toHaveBeenLastCalledWith('14:30');
  });

  it('keeps an off-step value visible and selected', async () => {
    const user = userEvent.setup();
    render(<Harness initial="10:07" />);
    await user.click(screen.getByRole('button', { name: 'Time' }));
    expect(option('Minutes', '07').getAttribute('aria-selected')).toBe('true');
  });
});
