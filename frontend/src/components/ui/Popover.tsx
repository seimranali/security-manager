import { useEffect, useRef, useState, type ReactNode } from 'react';
import { ChevronDown } from 'lucide-react';
import { cn } from '../../lib/format';

interface PopoverProps {
  label: ReactNode;
  icon?: ReactNode;
  badge?: number;
  active?: boolean;
  align?: 'left' | 'right';
  children: ReactNode | ((close: () => void) => ReactNode);
}

/** Button that toggles a floating panel; closes on outside click or Escape. */
export function Popover({ label, icon, badge, active, align = 'left', children }: PopoverProps) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => !ref.current?.contains(e.target as Node) && setOpen(false);
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', onDown);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onDown);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);

  const close = () => setOpen(false);

  return (
    <div className="popover" ref={ref}>
      <button type="button" className={cn('btn btn-secondary btn-sm', active && 'btn-active')} aria-expanded={open} onClick={() => setOpen((o) => !o)}>
        {icon}
        <span className="hide-sm">{label}</span>
        {!!badge && <span className="btn-badge">{badge}</span>}
        <ChevronDown size={13} />
      </button>
      {open && (
        <div className={cn('popover-panel', align === 'right' && 'right')}>
          {typeof children === 'function' ? children(close) : children}
        </div>
      )}
    </div>
  );
}
