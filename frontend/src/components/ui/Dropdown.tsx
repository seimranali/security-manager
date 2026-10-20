import {
  useCallback, useEffect, useId, useMemo, useRef, useState,
  type KeyboardEvent, type ReactNode, type UIEvent,
} from 'react';
import { createPortal } from 'react-dom';
import { ArrowDownAZ, ArrowUpZA, ArrowUpDown, Check, ChevronDown, Loader2, RotateCw, Search, X } from 'lucide-react';
import { cn } from '../../lib/format';
import { useFloating } from '../../hooks/useFloating';

export interface DropdownOption {
  value: string;
  label: string;
  description?: string;
  /** Options sharing a group are rendered under a sticky group header. */
  group?: string;
  icon?: ReactNode;
  /** Colour dot shown before the label (e.g. severity). */
  color?: string;
  /** Right-aligned secondary text (e.g. a count). */
  meta?: ReactNode;
  disabled?: boolean;
  /** Always listed first, ignoring sort and search (e.g. "All"). */
  pinned?: boolean;
}

export interface LoadOptionsArgs {
  search: string;
  page: number;
  signal: AbortSignal;
}

export interface LoadOptionsResult {
  options: DropdownOption[];
  hasMore: boolean;
}

export type SortMode = 'none' | 'asc' | 'desc';

interface CommonProps {
  /** Static options. */
  options?: DropdownOption[];
  /** Dynamic options: called with the search text and page (1-based); supports infinite scroll. */
  loadOptions?: (args: LoadOptionsArgs) => Promise<LoadOptionsResult>;
  /** Change to discard cached remote results (e.g. when a dependent filter changes). */
  cacheKey?: string;
  placeholder?: string;
  /** Show the search box (default: dynamic sources or more than 8 options). */
  searchable?: boolean;
  searchPlaceholder?: string;
  /** Initial option order, or a custom comparator. */
  sort?: SortMode | ((a: DropdownOption, b: DropdownOption) => number);
  /** Lets the user cycle the order (none → A–Z → Z–A) from the panel. */
  sortToggle?: boolean;
  /** Explicit group order; unlisted groups follow in first-seen (or sorted) order. */
  groupOrder?: string[];
  clearable?: boolean;
  disabled?: boolean;
  required?: boolean;
  invalid?: boolean;
  size?: 'sm' | 'md';
  'aria-label'?: string;
  id?: string;
  className?: string;
  emptyText?: string;
  debounceMs?: number;
  /** Labels for selected values that may not be loaded yet (dynamic sources). */
  selectedOptions?: DropdownOption[];
  /** Minimum panel width in px (defaults to the trigger width, at least 220). */
  menuWidth?: number;
}

interface SingleProps extends CommonProps {
  multiple?: false;
  value: string;
  onChange: (value: string, option?: DropdownOption) => void;
}

interface MultiProps extends CommonProps {
  multiple: true;
  value: string[];
  onChange: (value: string[]) => void;
  /** Chips shown in the trigger before collapsing into "+N". */
  maxChips?: number;
}

export type DropdownProps = SingleProps | MultiProps;

type RenderItem = { kind: 'header'; label: string } | { kind: 'option'; option: DropdownOption; index: number };

interface RemoteState {
  options: DropdownOption[];
  page: number;
  hasMore: boolean;
  loading: boolean;
  loadingMore: boolean;
  error: string | null;
}

const EMPTY_REMOTE: RemoteState = { options: [], page: 0, hasMore: false, loading: false, loadingMore: false, error: null };
const collator = new Intl.Collator(undefined, { numeric: true, sensitivity: 'base' });

function Highlight({ text, query }: { text: string; query: string }) {
  const q = query.trim();
  const at = q ? text.toLowerCase().indexOf(q.toLowerCase()) : -1;
  if (at < 0) return <>{text}</>;
  return <>{text.slice(0, at)}<mark>{text.slice(at, at + q.length)}</mark>{text.slice(at + q.length)}</>;
}

/**
 * Accessible combobox: single/multi select, search with highlighting, client-side sorting (with user toggle),
 * option groups, and dynamic paged loading with debounce, caching, cancellation and infinite scroll.
 * The panel renders in a portal so it is never clipped by tables, cards or modals.
 */
export function Dropdown(props: DropdownProps) {
  const {
    options: staticOptions, loadOptions, cacheKey = '', placeholder = 'Select…', searchPlaceholder = 'Search…',
    sort = 'none', sortToggle = false, groupOrder, clearable, disabled, required, invalid, size = 'md',
    id, className, emptyText = 'No options', debounceMs = 250, selectedOptions, menuWidth,
  } = props;
  const multiple = props.multiple === true;
  const values = useMemo(() => (props.multiple ? props.value : props.value ? [props.value] : []), [props.multiple, props.value]);

  const autoId = useId();
  const baseId = id ?? `dd${autoId.replace(/:/g, '')}`;
  const searchRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);

  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');
  const [active, setActive] = useState(-1);
  const [sortMode, setSortMode] = useState<SortMode>(typeof sort === 'string' ? sort : 'none');
  const [remote, setRemote] = useState<RemoteState>(EMPTY_REMOTE);
  const [reloadToken, setReloadToken] = useState(0);
  const { triggerRef, panelRef, style: panelStyle, ready: panelReady } = useFloating({
    open,
    minWidth: menuWidth ?? 220,
    onOutside: () => {
      setOpen(false);
      setSearch('');
    },
  });

  const searchable = props.searchable ?? (!!loadOptions || (staticOptions?.length ?? 0) > 8);

  // Remember every option we have seen so selected values keep their labels across remote pages/searches.
  const known = useRef(new Map<string, DropdownOption>());
  const remember = (list?: DropdownOption[]) => list?.forEach((o) => known.current.set(o.value, o));
  remember(staticOptions);
  remember(selectedOptions);

  // ---------- Dynamic loading ----------
  const loader = useRef(loadOptions);
  loader.current = loadOptions;
  const cache = useRef(new Map<string, RemoteState>());
  useEffect(() => cache.current.clear(), [cacheKey]);

  useEffect(() => {
    if (!open || !loader.current) return;
    const key = `${cacheKey}\u0000${search.trim()}`;
    const cached = cache.current.get(key);
    if (cached && reloadToken === 0) {
      setRemote(cached);
      return;
    }
    const controller = new AbortController();
    setRemote((r) => ({ ...r, loading: true, error: null }));
    const timer = setTimeout(async () => {
      try {
        const result = await loader.current!({ search: search.trim(), page: 1, signal: controller.signal });
        if (controller.signal.aborted) return;
        remember(result.options);
        const next = { ...EMPTY_REMOTE, options: result.options, page: 1, hasMore: result.hasMore };
        cache.current.set(key, next);
        setRemote(next);
      } catch (err) {
        if (!controller.signal.aborted) setRemote((r) => ({ ...r, loading: false, error: err instanceof Error ? err.message : 'Failed to load options' }));
      }
    }, search ? debounceMs : 0);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [open, search, cacheKey, debounceMs, reloadToken]);

  const loadMore = useCallback(async () => {
    if (!loader.current || remote.loading || remote.loadingMore || !remote.hasMore) return;
    setRemote((r) => ({ ...r, loadingMore: true }));
    try {
      const controller = new AbortController();
      const result = await loader.current({ search: search.trim(), page: remote.page + 1, signal: controller.signal });
      remember(result.options);
      setRemote((r) => {
        const seen = new Set(r.options.map((o) => o.value));
        const next = { ...r, options: [...r.options, ...result.options.filter((o) => !seen.has(o.value))], page: r.page + 1, hasMore: result.hasMore, loadingMore: false };
        cache.current.set(`${cacheKey}\u0000${search.trim()}`, next);
        return next;
      });
    } catch (err) {
      setRemote((r) => ({ ...r, loadingMore: false, error: err instanceof Error ? err.message : 'Failed to load more' }));
    }
  }, [remote, search, cacheKey]);

  // ---------- Filter → sort → group ----------
  const { items, selectable } = useMemo(() => {
    const q = search.trim().toLowerCase();
    let list = loadOptions ? remote.options : staticOptions ?? [];
    if (!loadOptions && q) {
      list = list.filter((o) => !o.pinned && [o.label, o.description, o.value, o.group].some((t) => t?.toLowerCase().includes(q)));
    }
    if (q && loadOptions) list = list.filter((o) => !o.pinned);

    const comparator = typeof sort === 'function'
      ? sort
      : sortMode === 'none' ? null
      : (a: DropdownOption, b: DropdownOption) => collator.compare(a.label, b.label) * (sortMode === 'asc' ? 1 : -1);
    const pinned = list.filter((o) => o.pinned);
    const rest = list.filter((o) => !o.pinned);
    if (comparator) rest.sort(comparator);

    const result: RenderItem[] = [];
    const flat: DropdownOption[] = [];
    const push = (o: DropdownOption) => {
      result.push({ kind: 'option', option: o, index: flat.length });
      flat.push(o);
    };
    pinned.forEach(push);

    if (rest.some((o) => o.group)) {
      const buckets = new Map<string, DropdownOption[]>();
      for (const o of rest) {
        const g = o.group ?? '';
        if (!buckets.has(g)) buckets.set(g, []);
        buckets.get(g)!.push(o);
      }
      let names = [...buckets.keys()];
      if (sortMode !== 'none' && typeof sort !== 'function') names.sort((a, b) => collator.compare(a, b) * (sortMode === 'asc' ? 1 : -1));
      if (groupOrder) names = [...groupOrder.filter((g) => buckets.has(g)), ...names.filter((g) => !groupOrder.includes(g))];
      names.sort((a, b) => (a === '' ? -1 : b === '' ? 1 : 0)); // ungrouped first
      for (const g of names) {
        if (g) result.push({ kind: 'header', label: g });
        buckets.get(g)!.forEach(push);
      }
    } else {
      rest.forEach(push);
    }
    return { items: result, selectable: flat };
  }, [search, loadOptions, remote.options, staticOptions, sort, sortMode, groupOrder]);

  // ---------- Open / close ----------
  const openMenu = (initialSearch = '') => {
    if (disabled) return;
    setSearch(initialSearch);
    setReloadToken(0);
    setOpen(true);
  };

  const close = useCallback((focusTrigger = true) => {
    setOpen(false);
    setSearch('');
    if (focusTrigger) triggerRef.current?.focus();
  }, [triggerRef]);

  // The panel mounts only after it has been positioned, so move focus once it exists.
  useEffect(() => {
    if (panelReady) (searchable ? searchRef.current : listRef.current)?.focus();
  }, [panelReady, searchable]);

  // Keep the active option valid and visible.
  useEffect(() => {
    if (!open) return;
    const selectedIndex = selectable.findIndex((o) => values.includes(o.value));
    setActive(selectedIndex >= 0 && !search ? selectedIndex : selectable.findIndex((o) => !o.disabled));
  }, [open, items]); // intentionally not on `values`: keep the cursor where the user is while toggling (multi)

  useEffect(() => {
    if (open && active >= 0) document.getElementById(`${baseId}-opt-${active}`)?.scrollIntoView({ block: 'nearest' });
  }, [active, open, baseId]);

  // ---------- Selection ----------
  const choose = (option: DropdownOption) => {
    if (option.disabled) return;
    remember([option]);
    if (props.multiple) {
      const next = values.includes(option.value) ? values.filter((v) => v !== option.value) : [...values, option.value];
      props.onChange(next);
    } else {
      props.onChange(option.value, option);
      close();
    }
  };

  const clear = () => {
    if (props.multiple) props.onChange([]);
    else props.onChange('', undefined);
  };

  const selectAllVisible = () => {
    if (!props.multiple) return;
    const add = selectable.filter((o) => !o.disabled && !o.pinned).map((o) => o.value);
    props.onChange([...new Set([...values, ...add])]);
  };

  const move = (delta: number) => {
    if (!selectable.length) return;
    let i = active;
    for (let step = 0; step < selectable.length; step++) {
      i = (i + delta + selectable.length) % selectable.length;
      if (!selectable[i].disabled) break;
    }
    setActive(i);
  };

  const onPanelKey = (e: KeyboardEvent) => {
    switch (e.key) {
      case 'ArrowDown': e.preventDefault(); move(1); break;
      case 'ArrowUp': e.preventDefault(); move(-1); break;
      case 'Home': if (!searchable) { e.preventDefault(); setActive(selectable.findIndex((o) => !o.disabled)); } break;
      case 'End': if (!searchable) { e.preventDefault(); move(-1 - active); } break;
      case 'Enter':
        e.preventDefault();
        if (selectable[active]) choose(selectable[active]);
        break;
      case 'Escape':
        e.preventDefault();
        e.stopPropagation(); // don't close an enclosing modal
        close();
        break;
      case 'Tab':
        close(false);
        break;
    }
  };

  const onTriggerKey = (e: KeyboardEvent<HTMLButtonElement>) => {
    if (['ArrowDown', 'ArrowUp', 'Enter', ' '].includes(e.key)) {
      e.preventDefault();
      openMenu();
    } else if (searchable && e.key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
      openMenu(e.key);
    } else if ((e.key === 'Backspace' || e.key === 'Delete') && clearable && values.length) {
      clear();
    }
  };

  const onListScroll = (e: UIEvent<HTMLDivElement>) => {
    const el = e.currentTarget;
    if (el.scrollTop + el.clientHeight >= el.scrollHeight - 48) loadMore();
  };

  // ---------- Trigger content ----------
  const labelOf = (v: string) => known.current.get(v);
  const hasValue = multiple ? values.length > 0 : props.value !== '' || !!labelOf('');
  const single = !multiple ? labelOf(props.value as string) : undefined;
  const showClear = clearable && !disabled && (multiple ? values.length > 0 : props.value !== '');
  const maxChips = props.multiple ? props.maxChips ?? 2 : 0;

  const triggerContent = multiple ? (
    values.length === 0 ? <span className="dd-placeholder">{placeholder}</span> : (
      <span className="dd-chips">
        {values.slice(0, maxChips).map((v) => (
          <span key={v} className="dd-chip">
            {labelOf(v)?.label ?? v}
            <span role="button" tabIndex={-1} aria-label={`Remove ${labelOf(v)?.label ?? v}`} className="dd-chip-x"
              onMouseDown={(e) => { e.preventDefault(); e.stopPropagation(); if (props.multiple) props.onChange(values.filter((x) => x !== v)); }}>
              <X size={11} />
            </span>
          </span>
        ))}
        {values.length > maxChips && <span className="dd-more">+{values.length - maxChips}</span>}
      </span>
    )
  ) : single && (props.value !== '' || single.pinned) ? (
    <span className="dd-value">
      {single.color && <span className="dd-dot" style={{ background: single.color }} />}
      {single.icon}
      <span className="truncate">{single.label}</span>
    </span>
  ) : props.value ? <span className="dd-value truncate">{props.value}</span> : <span className="dd-placeholder">{placeholder}</span>;

  const activeId = active >= 0 ? `${baseId}-opt-${active}` : undefined;

  return (
    <div className={cn('dropdown', `dropdown-${size}`, disabled && 'is-disabled', className)}>
      <button
        ref={triggerRef}
        id={baseId}
        type="button"
        className={cn('dd-trigger', open && 'is-open', invalid && 'is-invalid')}
        role="combobox"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={open ? `${baseId}-list` : undefined}
        aria-label={props['aria-label']}
        disabled={disabled}
        onClick={() => (open ? close() : openMenu())}
        onKeyDown={onTriggerKey}
      >
        {triggerContent}
        <span className="dd-icons">
          {showClear && (
            <span role="button" tabIndex={-1} className="dd-clear" aria-label="Clear selection"
              onMouseDown={(e) => { e.preventDefault(); e.stopPropagation(); clear(); }}>
              <X size={13} />
            </span>
          )}
          <ChevronDown size={15} className={cn('dd-caret', open && 'open')} />
        </span>
      </button>
      {/* Native validation hook for required fields inside forms. */}
      {required && <input className="dd-native" tabIndex={-1} aria-hidden required value={hasValue ? 'set' : ''} onChange={() => {}}
        onFocus={() => triggerRef.current?.focus()} />}

      {panelReady && createPortal(
        <div
          ref={panelRef}
          className="dd-panel"
          style={panelStyle}
          onKeyDown={onPanelKey}
        >
          {(searchable || sortToggle) && (
            <div className="dd-search">
              {searchable && (
                <>
                  <Search size={14} />
                  <input
                    ref={searchRef}
                    value={search}
                    placeholder={searchPlaceholder}
                    aria-label="Search options"
                    aria-controls={`${baseId}-list`}
                    aria-activedescendant={activeId}
                    autoComplete="off"
                    onChange={(e) => { setSearch(e.target.value); setReloadToken(0); }}
                  />
                </>
              )}
              {!searchable && <span className="spacer" />}
              {sortToggle && typeof sort !== 'function' && (
                <button type="button" className="dd-sort" aria-label={`Sort: ${sortMode}`} title={sortMode === 'none' ? 'Default order' : sortMode === 'asc' ? 'A → Z' : 'Z → A'}
                  onClick={() => setSortMode((m) => (m === 'none' ? 'asc' : m === 'asc' ? 'desc' : 'none'))}>
                  {sortMode === 'asc' ? <ArrowDownAZ size={15} /> : sortMode === 'desc' ? <ArrowUpZA size={15} /> : <ArrowUpDown size={15} />}
                </button>
              )}
            </div>
          )}

          {multiple && (
            <div className="dd-actions">
              <span className="muted small">{values.length} selected</span>
              <span className="spacer" />
              <button type="button" onClick={selectAllVisible}>Select {search ? 'matches' : 'all'}</button>
              <button type="button" onClick={clear} disabled={!values.length}>Clear</button>
            </div>
          )}

          <div
            ref={listRef}
            id={`${baseId}-list`}
            className="dd-list"
            role="listbox"
            tabIndex={-1}
            aria-multiselectable={multiple || undefined}
            aria-activedescendant={!searchable ? activeId : undefined}
            onScroll={onListScroll}
          >
            {remote.loading && !remote.options.length ? (
              <div className="dd-status"><Loader2 size={15} className="spin" /> Loading…</div>
            ) : remote.error && !remote.options.length ? (
              <div className="dd-status dd-error">
                {remote.error}
                <button type="button" onClick={() => setReloadToken((t) => t + 1)}><RotateCw size={13} /> Retry</button>
              </div>
            ) : selectable.length === 0 ? (
              <div className="dd-status">{search ? `No matches for “${search}”` : emptyText}</div>
            ) : (
              items.map((item) =>
                item.kind === 'header' ? (
                  <div key={`h:${item.label}`} className="dd-group" role="presentation">{item.label}</div>
                ) : (
                  <div
                    key={item.option.value || '__empty'}
                    id={`${baseId}-opt-${item.index}`}
                    role="option"
                    aria-selected={values.includes(item.option.value) || (!multiple && props.value === item.option.value)}
                    aria-disabled={item.option.disabled || undefined}
                    className={cn('dd-option', item.index === active && 'is-active', item.option.disabled && 'is-disabled',
                      (values.includes(item.option.value) || (!multiple && props.value === item.option.value)) && 'is-selected')}
                    onMouseMove={() => item.index !== active && setActive(item.index)}
                    onMouseDown={(e) => e.preventDefault()}
                    onClick={() => choose(item.option)}
                  >
                    {multiple ? (
                      <span className={cn('dd-check', values.includes(item.option.value) && 'on')}><Check size={11} /></span>
                    ) : null}
                    {item.option.color && <span className="dd-dot" style={{ background: item.option.color }} />}
                    {item.option.icon && <span className="dd-icon">{item.option.icon}</span>}
                    <span className="dd-text">
                      <span className="dd-label"><Highlight text={item.option.label} query={search} /></span>
                      {item.option.description && <span className="dd-desc">{item.option.description}</span>}
                    </span>
                    {item.option.meta != null && <span className="dd-meta">{item.option.meta}</span>}
                    {!multiple && props.value === item.option.value && <Check size={14} className="dd-tick" />}
                  </div>
                ),
              )
            )}
            {remote.loadingMore && <div className="dd-status"><Loader2 size={14} className="spin" /> Loading more…</div>}
            {!remote.loadingMore && remote.hasMore && !remote.loading && (
              <button type="button" className="dd-load-more" onClick={() => { loadMore(); (searchable ? searchRef.current : listRef.current)?.focus(); }}>Load more</button>
            )}
          </div>
        </div>,
        document.body,
      )}
    </div>
  );
}
