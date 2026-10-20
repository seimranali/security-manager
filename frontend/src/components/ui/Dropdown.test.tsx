import { useState } from 'react';
import { act, cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeAll, describe, expect, it, vi } from 'vitest';
import { Dropdown, type DropdownOption, type LoadOptionsArgs } from './Dropdown';

beforeAll(() => {
  // jsdom lacks layout APIs used for positioning/scrolling.
  Element.prototype.scrollIntoView = vi.fn();
});
afterEach(cleanup);

const fruits: DropdownOption[] = [
  { value: 'cherry', label: 'Cherry', group: 'Red' },
  { value: 'apple', label: 'Apple', group: 'Red' },
  { value: 'banana', label: 'Banana', group: 'Yellow' },
  { value: 'lemon', label: 'Lemon', group: 'Yellow', disabled: true },
];

function Single(props: Partial<Parameters<typeof Dropdown>[0]> & { initial?: string }) {
  const [value, setValue] = useState(props.initial ?? '');
  return <Dropdown aria-label="Fruit" options={fruits} {...props} multiple={false} value={value} onChange={setValue} />;
}

const optionLabels = () => screen.getAllByRole('option').map((o) => o.textContent?.trim());

describe('Dropdown', () => {
  it('opens, groups options under headers and selects with the mouse', async () => {
    const user = userEvent.setup();
    render(<Single />);
    await user.click(screen.getByRole('combobox', { name: 'Fruit' }));

    const list = screen.getByRole('listbox');
    expect(within(list).getByText('Red')).toBeTruthy();
    expect(within(list).getByText('Yellow')).toBeTruthy();
    expect(optionLabels()).toEqual(['Cherry', 'Apple', 'Banana', 'Lemon']);

    await user.click(screen.getByRole('option', { name: 'Banana' }));
    expect(screen.queryByRole('listbox')).toBeNull();
    expect(screen.getByRole('combobox').textContent).toContain('Banana');
  });

  it('filters by search text and highlights the match', async () => {
    const user = userEvent.setup();
    render(<Single searchable />);
    await user.click(screen.getByRole('combobox'));
    await user.keyboard('an');

    expect(optionLabels()).toEqual(['Banana']);
    expect(screen.getByRole('option').querySelector('mark')?.textContent).toBe('an');
  });

  it('sorts A→Z and Z→A with the sort toggle', async () => {
    const user = userEvent.setup();
    render(<Single options={fruits.map(({ group: _g, ...o }) => o)} sortToggle />);
    await user.click(screen.getByRole('combobox'));
    const toggle = screen.getByRole('button', { name: /sort/i });

    await user.click(toggle);
    expect(optionLabels()).toEqual(['Apple', 'Banana', 'Cherry', 'Lemon']);
    await user.click(toggle);
    expect(optionLabels()).toEqual(['Lemon', 'Cherry', 'Banana', 'Apple']);
  });

  it('supports keyboard navigation, skips disabled options and closes on Escape', async () => {
    const user = userEvent.setup();
    render(<Single />);
    screen.getByRole('combobox').focus();
    await user.keyboard('{ArrowDown}');          // open; first option active
    await user.keyboard('{ArrowDown}{ArrowDown}{ArrowDown}'); // Apple → Banana → (Lemon disabled) wraps to Cherry
    await user.keyboard('{Enter}');
    expect(screen.getByRole('combobox').textContent).toContain('Cherry');

    await user.keyboard('{ArrowDown}');
    expect(screen.getByRole('listbox')).toBeTruthy();
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('listbox')).toBeNull();
  });

  it('keeps pinned options first and lets them be chosen', async () => {
    const user = userEvent.setup();
    render(<Single initial="apple" options={[{ value: '', label: 'All fruit', pinned: true }, ...fruits]} sort="desc" />);
    await user.click(screen.getByRole('combobox'));
    expect(optionLabels()[0]).toBe('All fruit');
    await user.click(screen.getByRole('option', { name: 'All fruit' }));
    expect(screen.getByRole('combobox').textContent).toContain('All fruit');
  });

  it('multi-select toggles values, shows chips and supports select all / clear', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    function Multi() {
      const [value, setValue] = useState<string[]>([]);
      return <Dropdown multiple aria-label="Fruits" options={fruits} value={value} maxChips={1}
        onChange={(v) => { onChange(v); setValue(v); }} />;
    }
    render(<Multi />);
    await user.click(screen.getByRole('combobox'));
    await user.click(screen.getByRole('option', { name: 'Apple' }));
    await user.click(screen.getByRole('option', { name: 'Banana' }));
    expect(onChange).toHaveBeenLastCalledWith(['apple', 'banana']);
    expect(screen.getByRole('listbox')).toBeTruthy(); // stays open
    expect(screen.getByRole('combobox').textContent).toContain('+1');

    await user.click(screen.getByRole('button', { name: /select all/i }));
    expect(onChange).toHaveBeenLastCalledWith(['apple', 'banana', 'cherry']); // disabled Lemon excluded
    await user.click(screen.getByRole('button', { name: 'Clear' }));
    expect(onChange).toHaveBeenLastCalledWith([]);
  });

  it('loads options dynamically with debounced search, caching and load-more paging', async () => {
    const calls: LoadOptionsArgs[] = [];
    const all = Array.from({ length: 45 }, (_, i) => `10.0.0.${i + 1}`);
    const loadOptions = vi.fn(async (args: LoadOptionsArgs) => {
      calls.push(args);
      const matches = all.filter((ip) => ip.includes(args.search));
      const page = matches.slice((args.page - 1) * 20, args.page * 20);
      return { options: page.map((ip) => ({ value: ip, label: ip })), hasMore: args.page * 20 < matches.length };
    });
    const user = userEvent.setup();
    render(<Single options={undefined} loadOptions={loadOptions} debounceMs={50} />);

    await user.click(screen.getByRole('combobox'));
    await waitFor(() => expect(screen.getAllByRole('option')).toHaveLength(20));
    expect(calls[0]).toMatchObject({ search: '', page: 1 });

    await user.click(screen.getByRole('button', { name: 'Load more' }));
    await waitFor(() => expect(screen.getAllByRole('option')).toHaveLength(40));
    expect(calls.at(-1)).toMatchObject({ page: 2 });

    await user.keyboard('.4');
    await waitFor(() => expect(optionLabels()).toEqual(['10.0.0.4', '10.0.0.40', '10.0.0.41', '10.0.0.42', '10.0.0.43', '10.0.0.44', '10.0.0.45']));
    expect(calls.filter((c) => c.search === '.4')).toHaveLength(1); // debounced: one request for the final text

    // (jsdom splits the accessible name around the <mark> highlight, so match on text content)
    await user.click(screen.getAllByRole('option').find((o) => o.textContent === '10.0.0.42')!);
    expect(screen.getByRole('combobox').textContent).toContain('10.0.0.42'); // label remembered after close

    const before = loadOptions.mock.calls.length;
    await user.click(screen.getByRole('combobox'));
    await waitFor(() => expect(screen.getAllByRole('option').length).toBeGreaterThan(0));
    expect(loadOptions.mock.calls.length).toBe(before); // served from cache
  });

  it('shows an error with retry when dynamic loading fails', async () => {
    let fail = true;
    const loadOptions = vi.fn(async () => {
      if (fail) throw new Error('Network down');
      return { options: [{ value: 'x', label: 'Recovered' }], hasMore: false };
    });
    const user = userEvent.setup();
    render(<Single options={undefined} loadOptions={loadOptions} />);
    await user.click(screen.getByRole('combobox'));
    await screen.findByText('Network down');

    fail = false;
    await act(async () => { await user.click(screen.getByRole('button', { name: /retry/i })); });
    await screen.findByRole('option', { name: 'Recovered' });
  });

  it('clears the value with the clear button', async () => {
    const user = userEvent.setup();
    render(<Single initial="apple" clearable />);
    expect(screen.getByRole('combobox').textContent).toContain('Apple');
    await user.click(screen.getByRole('button', { name: 'Clear selection' }));
    expect(screen.getByRole('combobox').textContent).toContain('Select…');
  });
});
