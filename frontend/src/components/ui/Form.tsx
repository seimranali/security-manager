import type { InputHTMLAttributes, ReactNode, TextareaHTMLAttributes } from 'react';
import { Search } from 'lucide-react';
import { cn } from '../../lib/format';
import { Dropdown, type DropdownOption, type SortMode } from './Dropdown';

export function Field({ label, hint, children, full }: { label: string; hint?: ReactNode; children: ReactNode; full?: boolean }) {
  return (
    <label className={cn('field', full && 'full')}>
      <span className="field-label">{label}</span>
      {children}
      {hint && <span className="field-hint">{hint}</span>}
    </label>
  );
}

export function Input({ className, ...rest }: InputHTMLAttributes<HTMLInputElement>) {
  return <input className={cn('input', className)} {...rest} />;
}

export function Textarea({ className, ...rest }: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea className={cn('textarea', className)} {...rest} />;
}

interface SelectProps {
  /** Strings or rich options (description, group, colour dot, icon, meta). */
  options: DropdownOption[] | readonly string[];
  /** Adds an "all/none" option with value '' pinned at the top. */
  placeholder?: string;
  value: string;
  onChange?: (e: { target: { value: string } }) => void;
  required?: boolean;
  disabled?: boolean;
  className?: string;
  id?: string;
  'aria-label'?: string;
  searchable?: boolean;
  sort?: SortMode;
  sortToggle?: boolean;
  clearable?: boolean;
  size?: 'sm' | 'md';
  groupOrder?: string[];
}

/** Single-value select built on <see cref="Dropdown"/>; keeps the familiar `e.target.value` onChange shape. */
export function Select({ options, placeholder, onChange, ...rest }: SelectProps) {
  const normalized: DropdownOption[] = options.map((o) => (typeof o === 'string' ? { value: o, label: o } : o));
  const all = placeholder !== undefined ? [{ value: '', label: placeholder, pinned: true }, ...normalized] : normalized;
  return (
    <Dropdown
      {...rest}
      options={all}
      placeholder={placeholder ?? 'Select…'}
      onChange={(value) => onChange?.({ target: { value } })}
    />
  );
}

export { Dropdown, type DropdownOption } from './Dropdown';
export { DatePicker, Calendar, DEFAULT_RANGE_PRESETS, type DateRange, type RangePreset } from './DatePicker';
export { TimePicker } from './TimePicker';

export function SearchInput({ value, onChange, placeholder = 'Search…' }: { value: string; onChange: (v: string) => void; placeholder?: string }) {
  return (
    <div className="input-icon">
      <Search size={15} />
      <input className="input" type="search" value={value} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} />
    </div>
  );
}

interface SwitchProps {
  checked: boolean;
  onChange: (checked: boolean) => void;
  label?: ReactNode;
  disabled?: boolean;
  ariaLabel?: string;
}

export function Switch({ checked, onChange, label, disabled, ariaLabel }: SwitchProps) {
  return (
    <label className="switch" onClick={(e) => e.stopPropagation()}>
      <input
        type="checkbox"
        role="switch"
        checked={checked}
        disabled={disabled}
        aria-label={ariaLabel}
        onChange={(e) => onChange(e.target.checked)}
      />
      <span className="switch-track" />
      {label && <span>{label}</span>}
    </label>
  );
}

/** Toggleable chips for multi-select or single-select filters. */
export function ChipGroup<T extends string>({
  options, value, onChange,
}: { options: { value: T; label: string }[]; value: T[]; onChange: (v: T[]) => void }) {
  return (
    <div className="chip-group">
      {options.map((o) => {
        const active = value.includes(o.value);
        return (
          <button
            key={o.value}
            type="button"
            className={cn('chip', active && 'active')}
            aria-pressed={active}
            onClick={() => onChange(active ? value.filter((v) => v !== o.value) : [...value, o.value])}
          >
            {o.label}
          </button>
        );
      })}
    </div>
  );
}
