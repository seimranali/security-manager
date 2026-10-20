import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import { createPortal } from 'react-dom';
import { Clock, X } from 'lucide-react';
import { cn } from '../../lib/format';
import { useFloating } from '../../hooks/useFloating';
import { formatDisplayTime, localeUses12h, minutesToTime, nowTime, parseTime, type TimeString } from '../../lib/dates';

export interface TimePickerProps {
  /** 24-hour "HH:mm", or '' for no value. */
  value: TimeString;
  onChange: (value: TimeString) => void;
  /** Minute granularity in the list (default 5). */
  minuteStep?: number;
  /** 12-hour display with AM/PM. Defaults to the user's locale. */
  hour12?: boolean;
  min?: TimeString;
  max?: TimeString;
  placeholder?: string;
  clearable?: boolean;
  disabled?: boolean;
  required?: boolean;
  showNow?: boolean;
  size?: 'sm' | 'md';
  'aria-label'?: string;
  id?: string;
  className?: string;
}

type ColumnKey = 'hour' | 'minute' | 'period';

interface ColumnItem {
  key: string;
  label: string;
  minutes: number; // resulting time (minutes since midnight) when chosen
  selected: boolean;
  disabled: boolean;
}

const pad = (n: number) => String(n).padStart(2, '0');

/** Time picker with hour / minute (/ AM-PM) columns. Changes apply live; Enter or Done closes. */
export function TimePicker({
  value, onChange, minuteStep = 5, hour12: hour12Prop, min, max, placeholder = 'Select time', clearable = true,
  disabled, required, showNow = true, size = 'md', id, className, ...rest
}: TimePickerProps) {
  const hour12 = useMemo(() => hour12Prop ?? localeUses12h(), [hour12Prop]);
  const autoId = useId();
  const baseId = id ?? `tp${autoId.replace(/:/g, '')}`;
  const [open, setOpen] = useState(false);
  const columnRefs = useRef<Record<ColumnKey, HTMLDivElement | null>>({ hour: null, minute: null, period: null });

  const { triggerRef, panelRef, style, ready } = useFloating({
    open, fixedWidth: hour12 ? 248 : 196, preferredHeight: 300, onOutside: () => setOpen(false),
  });

  const current = parseTime(value);
  const minM = parseTime(min) ?? 0;
  const maxM = parseTime(max) ?? 24 * 60 - 1;
  const outOfRange = (m: number) => m < minM || m > maxM;

  const hour24 = current == null ? null : Math.floor(current / 60);
  const minute = current == null ? null : current % 60;
  const isPm = hour24 != null && hour24 >= 12;

  const minuteValues = useMemo(() => {
    const values = Array.from({ length: Math.ceil(60 / minuteStep) }, (_, i) => i * minuteStep).filter((m) => m < 60);
    if (minute != null && !values.includes(minute)) values.push(minute); // keep an off-step value visible
    return values.sort((a, b) => a - b);
  }, [minuteStep, minute]);

  const firstValidMinute = (h: number) => minuteValues.find((m) => !outOfRange(h * 60 + m));

  // ---------- Column models ----------
  const hours: ColumnItem[] = (hour12 ? [12, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11] : Array.from({ length: 24 }, (_, h) => h)).map((h) => {
    const h24 = hour12 ? (h % 12) + (isPm ? 12 : 0) : h;
    const m = minute != null && !outOfRange(h24 * 60 + minute) ? minute : firstValidMinute(h24);
    return {
      key: String(h),
      label: hour12 ? String(h) : pad(h),
      minutes: h24 * 60 + (m ?? 0),
      selected: hour24 === h24,
      disabled: m === undefined,
    };
  });

  const baseHour = hour24 ?? (hours.find((h) => !h.disabled) ? Math.floor(hours.find((h) => !h.disabled)!.minutes / 60) : 0);
  const minutes: ColumnItem[] = minuteValues.map((m) => ({
    key: String(m),
    label: pad(m),
    minutes: baseHour * 60 + m,
    selected: minute === m,
    disabled: outOfRange(baseHour * 60 + m),
  }));

  const periods: ColumnItem[] = hour12 ? (['AM', 'PM'] as const).map((p) => {
    const h = hour24 ?? 0;
    const target = ((h % 12) + (p === 'PM' ? 12 : 0)) * 60 + (minute ?? 0);
    return { key: p, label: p, minutes: target, selected: hour24 != null && (p === 'PM') === isPm, disabled: outOfRange(target) };
  }) : [];

  const columns: { key: ColumnKey; label: string; items: ColumnItem[] }[] = [
    { key: 'hour', label: 'Hours', items: hours },
    { key: 'minute', label: 'Minutes', items: minutes },
    ...(hour12 ? [{ key: 'period' as const, label: 'AM/PM', items: periods }] : []),
  ];

  // Centre the selected entries when the panel opens.
  useEffect(() => {
    if (!ready) return;
    Object.values(columnRefs.current).forEach((col) => col?.querySelector('[aria-selected="true"]')?.scrollIntoView({ block: 'center' }));
    columnRefs.current.hour?.focus();
  }, [ready]);

  const close = (focusTrigger = true) => {
    setOpen(false);
    if (focusTrigger) triggerRef.current?.focus();
  };

  const choose = (item: ColumnItem) => {
    if (!item.disabled) onChange(minutesToTime(item.minutes));
  };

  const onColumnKey = (e: KeyboardEvent, columnIndex: number) => {
    const { items } = columns[columnIndex];
    const at = Math.max(0, items.findIndex((i) => i.selected));
    const step = (delta: number) => {
      for (let i = 1; i <= items.length; i++) {
        const candidate = items[(at + delta * i + items.length * i) % items.length];
        if (!candidate.disabled) return choose(candidate);
      }
    };
    switch (e.key) {
      case 'ArrowDown': e.preventDefault(); step(1); break;
      case 'ArrowUp': e.preventDefault(); step(-1); break;
      case 'Home': e.preventDefault(); { const f = items.find((i) => !i.disabled); if (f) choose(f); } break;
      case 'End': e.preventDefault(); { const l = [...items].reverse().find((i) => !i.disabled); if (l) choose(l); } break;
      case 'ArrowRight': e.preventDefault(); columnRefs.current[columns[Math.min(columnIndex + 1, columns.length - 1)].key]?.focus(); break;
      case 'ArrowLeft': e.preventDefault(); columnRefs.current[columns[Math.max(columnIndex - 1, 0)].key]?.focus(); break;
      case 'Enter': e.preventDefault(); close(); break;
      case 'Escape': e.preventDefault(); e.stopPropagation(); close(); break;
    }
  };

  // Keep the selected item visible as it changes via keyboard.
  useEffect(() => {
    if (!ready) return;
    Object.values(columnRefs.current).forEach((col) => col?.querySelector('[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' }));
  }, [value, ready]);

  const now = nowTime(minuteStep);
  const nowMinutes = parseTime(now)!;

  return (
    <div className={cn('dropdown', 'picker', `dropdown-${size}`, disabled && 'is-disabled', className)}>
      <button
        ref={triggerRef}
        id={baseId}
        type="button"
        className={cn('dd-trigger', open && 'is-open')}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-label={rest['aria-label']}
        disabled={disabled}
        onClick={() => (open ? close() : setOpen(true))}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown' || e.key === 'Enter' || e.key === ' ') { e.preventDefault(); setOpen(true); }
          if ((e.key === 'Backspace' || e.key === 'Delete') && clearable && value) onChange('');
        }}
      >
        <Clock size={15} className="picker-icon" />
        {value ? <span className="dd-value">{formatDisplayTime(value, hour12)}</span> : <span className="dd-placeholder">{placeholder}</span>}
        <span className="dd-icons">
          {clearable && value && !disabled && (
            <span role="button" tabIndex={-1} className="dd-clear" aria-label="Clear time"
              onMouseDown={(e) => { e.preventDefault(); e.stopPropagation(); onChange(''); }}>
              <X size={13} />
            </span>
          )}
        </span>
      </button>
      {required && <input className="dd-native" tabIndex={-1} aria-hidden required value={value ? 'set' : ''} onChange={() => {}}
        onFocus={() => triggerRef.current?.focus()} />}

      {ready && createPortal(
        <div ref={panelRef} className="dd-panel tp-panel" style={style} role="dialog" aria-label="Choose time">
          <div className="tp-columns">
            {columns.map((col, ci) => (
              <div key={col.key} className="tp-column">
                <div className="tp-column-label">{col.label}</div>
                <div
                  ref={(el) => { columnRefs.current[col.key] = el; }}
                  className="tp-list"
                  role="listbox"
                  aria-label={col.label}
                  tabIndex={0}
                  aria-activedescendant={col.items.find((i) => i.selected) ? `${baseId}-${col.key}-${col.items.find((i) => i.selected)!.key}` : undefined}
                  onKeyDown={(e) => onColumnKey(e, ci)}
                >
                  {col.items.map((item) => (
                    <div
                      key={item.key}
                      id={`${baseId}-${col.key}-${item.key}`}
                      role="option"
                      aria-selected={item.selected}
                      aria-disabled={item.disabled || undefined}
                      className={cn('tp-item', item.selected && 'selected', item.disabled && 'disabled')}
                      onMouseDown={(e) => e.preventDefault()}
                      onClick={() => choose(item)}
                    >
                      {item.label}
                    </div>
                  ))}
                </div>
              </div>
            ))}
          </div>
          <div className="dp-footer">
            {showNow && <button type="button" className="dp-link" disabled={outOfRange(nowMinutes)} onClick={() => onChange(now)}>Now</button>}
            {clearable && value && <button type="button" className="dp-link" onClick={() => onChange('')}>Clear</button>}
            <span className="spacer" />
            <button type="button" className="btn btn-primary btn-sm" onClick={() => close()}>Done</button>
          </div>
        </div>,
        document.body,
      )}
    </div>
  );
}
