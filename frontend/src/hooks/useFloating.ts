import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';

export interface FloatingPosition {
  left: number;
  width: number;
  top?: number;
  bottom?: number;
  maxHeight: number;
}

interface Options {
  open: boolean;
  /** Called when the user presses outside both the trigger and the panel. */
  onOutside: () => void;
  /** Minimum panel width (panel is at least as wide as the trigger unless `fixedWidth`). */
  minWidth?: number;
  /** Exact panel width, ignoring the trigger width. */
  fixedWidth?: number;
  /** Space needed below before flipping above the trigger. */
  preferredHeight?: number;
  maxHeight?: number;
}

/**
 * Positions a portal panel next to its trigger (flipping above when needed), keeps it attached on scroll/resize,
 * and reports outside presses. Shared by Dropdown, DatePicker and TimePicker.
 */
export function useFloating<T extends HTMLElement = HTMLButtonElement>({
  open, onOutside, minWidth = 220, fixedWidth, preferredHeight = 300, maxHeight = 380,
}: Options) {
  const triggerRef = useRef<T>(null);
  const panelRef = useRef<HTMLDivElement>(null);
  const [position, setPosition] = useState<FloatingPosition | null>(null);
  const outside = useRef(onOutside);
  outside.current = onOutside;

  const place = useCallback(() => {
    const r = triggerRef.current?.getBoundingClientRect();
    if (!r) return;
    const width = fixedWidth ?? Math.max(r.width, minWidth);
    const below = window.innerHeight - r.bottom;
    const up = below < preferredHeight && r.top > below;
    setPosition({
      left: Math.max(8, Math.min(r.left, window.innerWidth - width - 8)),
      width: Math.min(width, window.innerWidth - 16),
      top: up ? undefined : r.bottom + 4,
      bottom: up ? window.innerHeight - r.top + 4 : undefined,
      maxHeight: Math.max(180, Math.min(maxHeight, (up ? r.top : below) - 16)),
    });
  }, [fixedWidth, minWidth, preferredHeight, maxHeight]);

  useLayoutEffect(() => {
    if (!open) {
      setPosition(null);
      return;
    }
    place();
    window.addEventListener('resize', place);
    window.addEventListener('scroll', place, true);
    return () => {
      window.removeEventListener('resize', place);
      window.removeEventListener('scroll', place, true);
    };
  }, [open, place]);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => {
      const target = e.target as Node;
      if (!triggerRef.current?.contains(target) && !panelRef.current?.contains(target)) outside.current();
    };
    document.addEventListener('mousedown', onDown);
    return () => document.removeEventListener('mousedown', onDown);
  }, [open]);

  const style = position
    ? { left: position.left, width: position.width, top: position.top, bottom: position.bottom, maxHeight: position.maxHeight }
    : undefined;

  return { triggerRef, panelRef, position, style, ready: open && position !== null };
}
