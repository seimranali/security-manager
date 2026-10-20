import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { createPortal } from 'react-dom';
import { CalendarDays, ChevronLeft, ChevronRight, X } from 'lucide-react';
import { cn } from '../../lib/format';
import { useFloating } from '../../hooks/useFloating';
import {
  addDays, addMonths, clampDate, endOfMonth, formatDateRange, formatDisplayDate, localeWeekStart, monthMatrix, monthNames,
  parseISODate, sameDayInMonth, startOfMonth, startOfWeek, today, weekdayNames, type ISODate, type Weekday,
} from '../../lib/dates';

export interface DateRange {
  start: ISODate;
  end: ISODate;
}

export interface RangePreset {
  label: string;
  range: () => DateRange;
}

export const DEFAULT_RANGE_PRESETS: RangePreset[] = [
  { label: 'Today', range: () => ({ start: today(), end: today() }) },
  { label: 'Yesterday', range: () => ({ start: addDays(today(), -1), end: addDays(today(), -1) }) },
  { label: 'Last 7 days', range: () => ({ start: addDays(today(), -6), end: today() }) },
  { label: 'Last 30 days', range: () => ({ start: addDays(today(), -29), end: today() }) },
  { label: 'This month', range: () => ({ start: startOfMonth(today()), end: today() }) },
  { label: 'Last month', range: () => ({ start: startOfMonth(addMonths(today(), -1)), end: endOfMonth(addMonths(today(), -1)) }) },
];

interface CommonProps {
  min?: ISODate;
  max?: ISODate;
  isDateDisabled?: (date: ISODate) => boolean;
  placeholder?: string;
  disabled?: boolean;
  required?: boolean;
  clearable?: boolean;
  size?: 'sm' | 'md';
  'aria-label'?: string;
  id?: string;
  className?: string;
  /** 0 = Sunday … 6 = Saturday. Defaults to the user's locale. */
  weekStartsOn?: Weekday;
}

interface SingleProps extends CommonProps {
  mode?: 'single';
  value: ISODate;
  onChange: (value: ISODate) => void;
}

interface RangeProps extends CommonProps {
  mode: 'range';
  value: DateRange;
  onChange: (value: DateRange) => void;
  /** Quick ranges shown beside the calendar; `false` hides them. */
  presets?: RangePreset[] | false;
}

export type DatePickerProps = SingleProps | RangeProps;

// ---------------------------------------------------------------------------------------------------------------------

interface CalendarProps {
  month: ISODate;
  onMonthChange: (month: ISODate) => void;
  focused: ISODate;
  onFocusedChange: (date: ISODate) => void;
  onSelect: (date: ISODate) => void;
  onHover?: (date: ISODate | null) => void;
  onEscape?: () => void;
  isDisabled: (date: ISODate) => boolean;
  isSelected: (date: ISODate) => boolean;
  rangeState?: (date: ISODate) => { inRange: boolean; start: boolean; end: boolean };
  weekStartsOn: Weekday;
  min?: ISODate;
  max?: ISODate;
  autoFocus?: boolean;
}

type View = 'days' | 'months' | 'years';

/** Accessible month grid with month/year drill-up views and full keyboard navigation. */
export function Calendar({
  month, onMonthChange, focused, onFocusedChange, onSelect, onHover, onEscape, isDisabled, isSelected, rangeState,
  weekStartsOn, min, max, autoFocus,
}: CalendarProps) {
  const [view, setView] = useState<View>('days');
  const gridRef = useRef<HTMLDivElement>(null);
  const keyboardFocus = useRef(!!autoFocus);
  const todayStr = today();
  const weeks = useMemo(() => monthMatrix(month, weekStartsOn), [month, weekStartsOn]);
  const weekdays = useMemo(() => weekdayNames(weekStartsOn, 'short'), [weekStartsOn]);
  const months = useMemo(() => monthNames('short'), []);
  const year = Number(month.slice(0, 4));
  const monthIndex = Number(month.slice(5, 7)) - 1;
  const title = formatDisplayDate(month, { month: 'long', year: 'numeric' });

  // Move DOM focus with the roving "focused" date while the user drives the grid from the keyboard.
  useEffect(() => {
    if (view === 'days' && keyboardFocus.current) {
      gridRef.current?.querySelector<HTMLButtonElement>(`[data-date="${focused}"]`)?.focus();
    }
  }, [focused, view, month]);

  const goTo = (date: ISODate) => {
    const next = clampDate(date, min, max);
    keyboardFocus.current = true;
    onFocusedChange(next);
    if (startOfMonth(next) !== month) onMonthChange(startOfMonth(next));
  };

  const onGridKey = (e: KeyboardEvent) => {
    const moves: Record<string, () => ISODate> = {
      ArrowLeft: () => addDays(focused, -1),
      ArrowRight: () => addDays(focused, 1),
      ArrowUp: () => addDays(focused, -7),
      ArrowDown: () => addDays(focused, 7),
      PageUp: () => addMonths(focused, e.shiftKey ? -12 : -1),
      PageDown: () => addMonths(focused, e.shiftKey ? 12 : 1),
      Home: () => startOfWeek(focused, weekStartsOn),
      End: () => addDays(startOfWeek(focused, weekStartsOn), 6),
    };
    if (moves[e.key]) {
      e.preventDefault();
      goTo(moves[e.key]());
    } else if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      if (!isDisabled(focused)) onSelect(focused);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      e.stopPropagation();
      onEscape?.();
    }
  };

  const shift = (delta: number) => {
    if (view === 'days') onMonthChange(addMonths(month, delta));
    else onMonthChange(`${year + delta * (view === 'years' ? 12 : 1)}-${month.slice(5)}`);
  };
  const prevDisabled = view === 'days' && !!min && month <= startOfMonth(min);
  const nextDisabled = view === 'days' && !!max && month >= startOfMonth(max);
  const yearPageStart = year - (year % 12);

  return (
    <div className="dp-calendar">
      <div className="dp-header">
        <button type="button" className="dp-nav" aria-label={view === 'days' ? 'Previous month' : view === 'months' ? 'Previous year' : 'Previous years'}
          disabled={prevDisabled} onClick={() => shift(-1)}><ChevronLeft size={16} /></button>
        <button type="button" className="dp-title" aria-live="polite"
          onClick={() => setView(view === 'days' ? 'months' : 'years')} disabled={view === 'years'}>
          {view === 'days' ? title : view === 'months' ? year : `${yearPageStart} – ${yearPageStart + 11}`}
        </button>
        <button type="button" className="dp-nav" aria-label={view === 'days' ? 'Next month' : view === 'months' ? 'Next year' : 'Next years'}
          disabled={nextDisabled} onClick={() => shift(1)}><ChevronRight size={16} /></button>
      </div>

      {view === 'days' && (
        <div ref={gridRef} role="grid" aria-label={title} className="dp-grid" onKeyDown={onGridKey} onMouseLeave={() => onHover?.(null)}>
          <div role="row" className="dp-row">
            {weekdays.map((w) => <span key={w} role="columnheader" className="dp-weekday" aria-label={w}>{w.slice(0, 2)}</span>)}
          </div>
          {weeks.map((week) => (
            <div key={week[0]} role="row" className="dp-row">
              {week.map((d) => {
                const disabled = isDisabled(d);
                const selected = isSelected(d);
                const range = rangeState?.(d);
                return (
                  <button
                    key={d}
                    type="button"
                    role="gridcell"
                    data-date={d}
                    tabIndex={d === focused ? 0 : -1}
                    aria-selected={selected}
                    aria-current={d === todayStr ? 'date' : undefined}
                    aria-disabled={disabled || undefined}
                    aria-label={formatDisplayDate(d, { dateStyle: 'full' })}
                    className={cn('dp-day',
                      startOfMonth(d) !== month && 'outside',
                      d === todayStr && 'today',
                      selected && 'selected',
                      range?.inRange && 'in-range',
                      range?.start && 'range-start',
                      range?.end && 'range-end',
                      disabled && 'disabled')}
                    onClick={() => {
                      if (disabled) return;
                      keyboardFocus.current = false;
                      onFocusedChange(d);
                      onSelect(d);
                    }}
                    onMouseEnter={() => onHover?.(d)}
                  >
                    {Number(d.slice(8))}
                  </button>
                );
              })}
            </div>
          ))}
        </div>
      )}

      {view === 'months' && (
        <div className="dp-picker-grid" role="listbox" aria-label={`Months of ${year}`}>
          {months.map((name, i) => {
            const value = `${year}-${String(i + 1).padStart(2, '0')}-01`;
            const disabled = (!!min && endOfMonth(value) < min) || (!!max && value > max);
            return (
              <button key={name} type="button" role="option" aria-selected={i === monthIndex} disabled={disabled}
                className={cn('dp-cell', i === monthIndex && 'selected')}
                onClick={() => { onMonthChange(value); onFocusedChange(clampDate(sameDayInMonth(value, focused), min, max)); setView('days'); }}>
                {name}
              </button>
            );
          })}
        </div>
      )}

      {view === 'years' && (
        <div className="dp-picker-grid" role="listbox" aria-label="Years">
          {Array.from({ length: 12 }, (_, i) => yearPageStart + i).map((y) => {
            const disabled = (!!min && y < Number(min.slice(0, 4))) || (!!max && y > Number(max.slice(0, 4)));
            return (
              <button key={y} type="button" role="option" aria-selected={y === year} disabled={disabled}
                className={cn('dp-cell', y === year && 'selected', y === new Date().getFullYear() && 'current')}
                onClick={() => { onMonthChange(`${y}-${month.slice(5)}`); setView('months'); }}>
                {y}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------------------------------------------------

/** Date (or date range) picker with a calendar popover. Values are local "YYYY-MM-DD" strings. */
export function DatePicker(props: DatePickerProps) {
  const { min, max, isDateDisabled, disabled, required, clearable = true, size = 'md', id, className } = props;
  const isRange = props.mode === 'range';
  const presets = props.mode === 'range' && props.presets !== false ? props.presets ?? DEFAULT_RANGE_PRESETS : [];
  const placeholder = props.placeholder ?? (isRange ? 'Any date' : 'Select date');
  const weekStartsOn = useMemo(() => props.weekStartsOn ?? localeWeekStart(), [props.weekStartsOn]);
  const autoId = useId();
  const baseId = id ?? `dp${autoId.replace(/:/g, '')}`;

  const anchor = clampDate((props.mode === 'range' ? props.value.start : props.value) || today(), min, max);
  const [open, setOpen] = useState(false);
  const [month, setMonth] = useState(startOfMonth(anchor));
  const [focused, setFocused] = useState(anchor);
  const [draftStart, setDraftStart] = useState<ISODate | null>(null);
  const [hover, setHover] = useState<ISODate | null>(null);

  const { triggerRef, panelRef, style, ready } = useFloating({
    open,
    fixedWidth: presets.length ? 448 : 304,
    preferredHeight: 360,
    maxHeight: 460,
    onOutside: () => setOpen(false),
  });

  const openPanel = () => {
    if (disabled) return;
    setMonth(startOfMonth(anchor));
    setFocused(anchor);
    setDraftStart(null);
    setHover(null);
    setOpen(true);
  };

  const close = (focusTrigger = true) => {
    setOpen(false);
    setDraftStart(null);
    if (focusTrigger) triggerRef.current?.focus();
  };

  const isDisabled = (d: ISODate) => (!!min && d < min) || (!!max && d > max) || !!isDateDisabled?.(d);

  // The range to highlight: a live preview (start → hovered day) while choosing, otherwise the current value.
  const effective = ((): DateRange | null => {
    if (props.mode !== 'range') return null;
    if (draftStart) {
      const other = hover ?? draftStart;
      return other < draftStart ? { start: other, end: draftStart } : { start: draftStart, end: other };
    }
    return props.value.start ? props.value : null;
  })();

  const select = (d: ISODate) => {
    if (props.mode === 'range') {
      if (!draftStart) {
        setDraftStart(d);
        return;
      }
      const [start, end] = d < draftStart ? [d, draftStart] : [draftStart, d];
      props.onChange({ start, end });
      close();
    } else {
      props.onChange(d);
      close();
    }
  };

  const clear = () => {
    if (props.mode === 'range') props.onChange({ start: '', end: '' });
    else props.onChange('');
  };

  const applyPreset = (preset: RangePreset) => {
    if (props.mode !== 'range') return;
    const r = preset.range();
    props.onChange({ start: clampDate(r.start, min, max), end: clampDate(r.end, min, max) });
    close();
  };

  const hasValue = props.mode === 'range' ? !!props.value.start : !!props.value;
  const display = props.mode === 'range'
    ? props.value.start ? formatDateRange(props.value.start, props.value.end) : ''
    : props.value ? formatDisplayDate(props.value) : '';
  const activePreset = props.mode === 'range' && props.value.start
    ? presets.find((p) => { const r = p.range(); return r.start === props.value.start && r.end === props.value.end; })
    : undefined;

  return (
    <div className={cn('dropdown', 'picker', `dropdown-${size}`, disabled && 'is-disabled', className)}>
      <button
        ref={triggerRef}
        id={baseId}
        type="button"
        className={cn('dd-trigger', open && 'is-open')}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-label={props['aria-label']}
        disabled={disabled}
        onClick={() => (open ? close() : openPanel())}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ') { e.preventDefault(); openPanel(); }
          if ((e.key === 'Backspace' || e.key === 'Delete') && clearable && hasValue) clear();
        }}
      >
        <CalendarDays size={15} className="picker-icon" />
        {display ? <span className="dd-value truncate">{activePreset ? activePreset.label : display}</span> : <span className="dd-placeholder">{placeholder}</span>}
        <span className="dd-icons">
          {clearable && hasValue && !disabled && (
            <span role="button" tabIndex={-1} className="dd-clear" aria-label="Clear date"
              onMouseDown={(e) => { e.preventDefault(); e.stopPropagation(); clear(); }}>
              <X size={13} />
            </span>
          )}
        </span>
      </button>
      {required && <input className="dd-native" tabIndex={-1} aria-hidden required value={hasValue ? 'set' : ''} onChange={() => {}}
        onFocus={() => triggerRef.current?.focus()} />}

      {ready && createPortal(
        <div ref={panelRef} className="dd-panel dp-panel" style={style} role="dialog" aria-modal="false"
          aria-label={isRange ? 'Choose date range' : 'Choose date'}
          onKeyDown={(e) => { if (e.key === 'Escape') { e.stopPropagation(); close(); } }}>
          <div className="dp-body">
            {presets.length > 0 && (
              <div className="dp-presets" role="group" aria-label="Quick ranges">
                {presets.map((p) => (
                  <button key={p.label} type="button" className={cn('dp-preset', activePreset === p && 'active')} onClick={() => applyPreset(p)}>
                    {p.label}
                  </button>
                ))}
              </div>
            )}
            <Calendar
              month={month}
              onMonthChange={setMonth}
              focused={focused}
              onFocusedChange={setFocused}
              onSelect={select}
              onHover={isRange ? setHover : undefined}
              onEscape={() => close()}
              isDisabled={isDisabled}
              isSelected={(d) => (props.mode === 'range' ? !!effective && (d === effective.start || d === effective.end) : d === props.value)}
              rangeState={isRange ? (d) => ({
                inRange: !!effective?.end && d > effective.start && d < effective.end,
                start: !!effective && d === effective.start,
                end: !!effective?.end && d === effective.end,
              }) : undefined}
              weekStartsOn={weekStartsOn}
              min={min}
              max={max}
              autoFocus
            />
          </div>
          <div className="dp-footer">
            {isRange ? (
              <span className="muted small">
                {draftStart ? `From ${formatDisplayDate(draftStart)} — pick an end date` : 'Pick a start date'}
              </span>
            ) : (
              <button type="button" className="dp-link" disabled={isDisabled(today())} onClick={() => select(today())}>Today</button>
            )}
            <span className="spacer" />
            {clearable && hasValue && <button type="button" className="dp-link" onClick={() => { clear(); close(); }}>Clear</button>}
          </div>
        </div>,
        document.body,
      )}
    </div>
  );
}

/** Narrow helper for callers that only need to know if a range is complete. */
export const isCompleteRange = (r: DateRange) => !!parseISODate(r.start) && !!parseISODate(r.end);
