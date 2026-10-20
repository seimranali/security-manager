import { Fragment, useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react';
import {
  AlignJustify, ArrowDown, ArrowUp, ArrowUpDown, ChevronLeft, ChevronRight, ChevronsLeft, ChevronsRight,
  Columns3, Download, Filter, FilterX, Grid3x3, Inbox, Layers, Pin, PinOff, Search, X,
} from 'lucide-react';
import { cn } from '../../lib/format';
import { Button } from './Button';
import { Dropdown } from './Dropdown';
import { EmptyState } from './Feedback';
import { Popover } from './Popover';
import { PivotView } from './datatable/PivotView';
import {
  aggregate, aggregateLabel, allGroupIds, columnLabel, computeRowSpans, displayValue, distinctValues, downloadCsv,
  filterRows, flattenGroups, formatAggregate, groupRows, inferFilterType, sortRows, toCsv, type GridItem, type GroupNode, type PivotConfig,
} from './datatable/engine';
import type { Column, Selection, SortRule } from './datatable/types';

export type { Column, Selection, SortRule } from './datatable/types';
export { EmptyState, Spinner } from './Feedback';

export const DEFAULT_PAGE_SIZE = 20;
const PAGE_SIZES = [10, 20, 50, 100];
const MAX_GROUP_LEVELS = 3;

interface DataTableProps<T> {
  columns: Column<T>[];
  rows: T[] | undefined;
  rowKey: (row: T) => string;
  loading?: boolean;
  onRowClick?: (row: T) => void;
  isSelected?: (row: T) => boolean;
  empty?: { title: string; description?: string };
  /** Rows per page (default 20). */
  pageSize?: number;
  /** Extra controls (e.g. server-side filters) rendered at the start of the grid toolbar. */
  toolbar?: ReactNode;
  /** Adds a checkbox column with select-all for the current page. */
  selection?: Selection;
  /** Expandable detail panel per row. */
  renderDetail?: (row: T) => ReactNode;
  rowClassName?: (row: T) => string | undefined;
  initialSort?: SortRule[];
  initialGroupBy?: string[];
  /** Enables the pivot view (default true). */
  pivot?: boolean;
  /** File name stem for CSV export. */
  exportName?: string;
  /** Server-side total when only a subset was loaded (shows a notice). */
  totalCount?: number;
  /** Persists column visibility per grid in localStorage. */
  storageKey?: string;
}

const alignClass = <T,>(c: Column<T>) => (c.align === 'right' ? 'text-right' : c.align === 'center' ? 'text-center' : undefined);

function loadHidden(storageKey?: string): Set<string> | null {
  if (!storageKey) return null;
  try {
    const raw = localStorage.getItem(`sm-grid:${storageKey}:hidden`);
    return raw ? new Set(JSON.parse(raw) as string[]) : null;
  } catch {
    return null;
  }
}

function saveHidden(storageKey: string | undefined, hidden: Set<string>) {
  if (!storageKey) return;
  try {
    localStorage.setItem(`sm-grid:${storageKey}:hidden`, JSON.stringify([...hidden]));
  } catch {
    /* storage unavailable */
  }
}

/**
 * Client-side data grid: sorting (Shift+click for multi-column), quick + per-column filters, multi-level grouping
 * with aggregates, pivot table, merged cells, grouped headers, selectable/expandable rows, aggregate footer,
 * column visibility, density, CSV export and pagination (20 rows per page by default).
 */
export function DataTable<T>({
  columns, rows, rowKey, loading, onRowClick, isSelected, empty, pageSize: initialPageSize = DEFAULT_PAGE_SIZE,
  toolbar, selection, renderDetail, rowClassName, initialSort = [], initialGroupBy = [], pivot = true,
  exportName = 'export', totalCount, storageKey,
}: DataTableProps<T>) {
  const data = useMemo(() => rows ?? [], [rows]);
  const colMap = useMemo(() => new Map(columns.map((c) => [c.key, c])), [columns]);

  const [sorts, setSorts] = useState<SortRule[]>(initialSort);
  const [quick, setQuick] = useState('');
  const [filters, setFilters] = useState<Record<string, string>>({});
  const [showFilters, setShowFilters] = useState(false);
  const [groupBy, setGroupBy] = useState<string[]>(initialGroupBy);
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());
  const [hidden, setHidden] = useState<Set<string>>(() => loadHidden(storageKey) ?? new Set(columns.filter((c) => c.hidden).map((c) => c.key)));
  const [compact, setCompact] = useState(false);
  const [view, setView] = useState<'table' | 'pivot'>('table');
  const [pivotConfig, setPivotConfig] = useState<PivotConfig | null>(null);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(initialPageSize);
  const [expanded, setExpanded] = useState<Set<string>>(new Set());
  // Last frozen column: it and every visible column before it stick to the left edge.
  const [frozenUntil, setFrozenUntil] = useState<string | null>(() => [...columns].reverse().find((c) => c.frozen)?.key ?? null);
  const [offsets, setOffsets] = useState<Record<string, number>>({});
  const tableRef = useRef<HTMLTableElement>(null);

  useEffect(() => saveHidden(storageKey, hidden), [storageKey, hidden]);
  useEffect(() => setPage(1), [quick, filters, sorts, groupBy, pageSize]);

  const visible = useMemo(() => columns.filter((c) => !hidden.has(c.key)), [columns, hidden]);
  const frozenKeys = useMemo(() => {
    const keys: string[] = [];
    const end = visible.findIndex((c) => c.key === frozenUntil);
    for (let i = 0; i <= end && !visible[i].group; i++) keys.push(visible[i].key);
    return keys;
  }, [visible, frozenUntil]);
  const frozenSet = new Set(frozenKeys);
  const lastFrozen = frozenKeys[frozenKeys.length - 1];

  // Measure the header cells so each frozen column knows its left offset (column widths depend on content).
  useLayoutEffect(() => {
    if (!frozenKeys.length || !tableRef.current) {
      setOffsets((o) => (Object.keys(o).length ? {} : o));
      return;
    }
    // Offsets are the running sum of the preceding frozen header widths (offsetLeft would be relative to whatever
    // positioned ancestor the sticky cell resolves to, e.g. the page, and push every cell off to the right).
    const next: Record<string, number> = {};
    let left = 0;
    tableRef.current.querySelectorAll<HTMLElement>('thead tr:first-child th[data-freeze]').forEach((th) => {
      next[th.dataset.freeze!] = left;
      left += th.getBoundingClientRect().width;
    });
    setOffsets((o) => (JSON.stringify(o) === JSON.stringify(next) ? o : next));
  });

  /** Sticky style + classes for a frozen cell (leading checkbox columns use keys "__select" / "__detail"). */
  const freeze = (key: string, frozen: boolean) =>
    frozen
      ? { style: { position: 'sticky', left: offsets[key] ?? 0 } as CSSProperties, className: cn('dt-frozen', key === lastFrozen && 'dt-frozen-last') }
      : { style: undefined, className: undefined };
  const leadFrozen = frozenKeys.length > 0;
  const filterTypes = useMemo(() => new Map(columns.map((c) => [c.key, inferFilterType(c, data)])), [columns, data]);
  const groupable = columns.filter((c) => c.value && c.groupable !== false);
  const measures = columns.filter((c) => c.value && (filterTypes.get(c.key) === 'number' || (c.aggregate && c.aggregate !== 'count')));

  // Pipeline: filter → sort → group → flatten → paginate.
  const filtered = useMemo(() => filterRows(data, columns, visible, filterTypes, quick, filters), [data, columns, visible, filterTypes, quick, filters]);
  const sorted = useMemo(() => sortRows(filtered, sorts, colMap), [filtered, sorts, colMap]);
  const groups = useMemo<GroupNode<T>[] | null>(() => {
    const groupColumns = groupBy.map((k) => colMap.get(k)).filter((c): c is Column<T> => !!c?.value);
    return groupColumns.length ? groupRows(sorted, groupColumns, new Set(sorts.map((s) => s.key))) : null;
  }, [sorted, groupBy, colMap, sorts]);
  const items = useMemo<GridItem<T>[]>(
    () => (groups ? flattenGroups(groups, collapsed) : sorted.map((row) => ({ kind: 'row', row, depth: 0 }))),
    [groups, collapsed, sorted],
  );

  const pageCount = Math.max(1, Math.ceil(items.length / pageSize));
  const currentPage = Math.min(page, pageCount);
  const pageItems = items.slice((currentPage - 1) * pageSize, currentPage * pageSize);
  const pageRows = pageItems.flatMap((i) => (i.kind === 'row' ? [i.row] : []));
  // Merged cells are computed per page; an expanded detail row breaks a merge run.
  const spans = computeRowSpans(pageItems, visible, (i) => {
    const it = pageItems[i];
    return it?.kind === 'row' && expanded.has(rowKey(it.row));
  });

  const leading = (selection ? 1 : 0) + (renderDetail ? 1 : 0);
  const totalColumns = leading + visible.length;
  const activeFilters = Object.entries(filters).filter(([, v]) => v.trim() !== '');
  const hasHeaderGroups = visible.some((c) => c.group);
  const hasFooter = visible.some((c) => c.footer);
  const isSortable = (c: Column<T>) => c.sortable !== false && !!(c.value || c.sortValue);

  // ---------- Actions ----------
  const toggleSort = (key: string, multi: boolean) =>
    setSorts((prev) => {
      const existing = prev.find((s) => s.key === key);
      const nextDir = !existing ? 'asc' : existing.dir === 'asc' ? 'desc' : null;
      if (!multi) return nextDir ? [{ key, dir: nextDir }] : [];
      if (!nextDir) return prev.filter((s) => s.key !== key);
      return existing ? prev.map((s) => (s.key === key ? { key, dir: nextDir } : s)) : [...prev, { key, dir: nextDir }];
    });

  const toggleSet = (setter: (fn: (s: Set<string>) => Set<string>) => void, key: string) =>
    setter((s) => {
      const next = new Set(s);
      if (next.has(key)) next.delete(key); else next.add(key);
      return next;
    });

  const setFilter = (key: string, value: string) => setFilters((f) => ({ ...f, [key]: value }));
  const clearAll = () => {
    setQuick('');
    setFilters({});
    setSorts([]);
    setGroupBy([]);
  };

  const toggleColumn = (key: string) => {
    toggleSet(setHidden, key);
    if (!hidden.has(key)) setFilters((f) => ({ ...f, [key]: '' })); // hiding a column drops its filter
  };

  const openPivot = () => {
    if (view === 'pivot') return setView('table');
    if (!pivotConfig || !colMap.has(pivotConfig.rowKey)) {
      const rowKey = groupBy[0] ?? groupable.find((c) => filterTypes.get(c.key) === 'select')?.key ?? groupable[0]?.key;
      const colKey = groupable.find((c) => c.key !== rowKey && filterTypes.get(c.key) === 'select')?.key ?? '';
      if (!rowKey) return;
      setPivotConfig({ rowKey, colKey, valueKey: '', agg: 'count' });
    }
    setView('pivot');
  };

  const exportCsv = () => {
    const cols = visible.filter((c) => c.value);
    downloadCsv(exportName, toCsv(cols.map(columnLabel), sorted.map((r) => cols.map((c) => displayValue(c, c.value!(r))))));
  };

  const pageSelected = selection && pageRows.length > 0 && pageRows.every((r) => selection.selected.has(rowKey(r)));
  const togglePageSelection = () => {
    if (!selection) return;
    const next = new Set(selection.selected);
    pageRows.forEach((r) => (pageSelected ? next.delete(rowKey(r)) : next.add(rowKey(r))));
    selection.onChange(next);
  };

  // ---------- Rendering helpers ----------
  const cellContent = (c: Column<T>, row: T) =>
    c.render ? c.render(row) : c.value ? displayValue(c, c.value(row)) : null;

  const aggregateCell = (c: Column<T>, source: T[], fn = c.aggregate) =>
    fn ? (
      <span className="dt-agg">
        <span className="dt-agg-label">{aggregateLabel(fn)}</span>
        {formatAggregate(aggregate(source.map((r) => (c.value ? c.value(r) : 1)), fn), fn, c)}
      </span>
    ) : null;

  const headerCell = (c: Column<T>, rowSpan?: number) => {
    const sort = sorts.find((s) => s.key === c.key);
    const sortIndex = sorts.length > 1 && sort ? sorts.indexOf(sort) + 1 : null;
    const fz = freeze(c.key, frozenSet.has(c.key));
    return (
      <th key={c.key} rowSpan={rowSpan} style={{ width: c.width, ...fz.style }} className={cn(c.className, alignClass(c), fz.className)}
        data-freeze={frozenSet.has(c.key) ? c.key : undefined}
        aria-sort={sort ? (sort.dir === 'asc' ? 'ascending' : 'descending') : undefined}>
        {isSortable(c) ? (
          <button type="button" className={cn('dt-sort', sort && 'active')} title="Sort (Shift+click to add a sort level)"
            onClick={(e) => toggleSort(c.key, e.shiftKey)}>
            {c.header}
            {sort ? (sort.dir === 'asc' ? <ArrowUp size={12} /> : <ArrowDown size={12} />) : <ArrowUpDown size={12} className="dt-sort-idle" />}
            {sortIndex && <sup>{sortIndex}</sup>}
          </button>
        ) : c.header}
      </th>
    );
  };

  const leadingHeaders = (rowSpan?: number) => (
    <>
      {selection && (
        <th className={cn('dt-check', freeze('__select', leadFrozen).className)} style={freeze('__select', leadFrozen).style}
          data-freeze={leadFrozen ? '__select' : undefined} rowSpan={rowSpan}>
          <input type="checkbox" className="checkbox" aria-label="Select page" checked={!!pageSelected} onChange={togglePageSelection} />
        </th>
      )}
      {renderDetail && <th className={cn('dt-check', freeze('__detail', leadFrozen).className)} style={freeze('__detail', leadFrozen).style}
        data-freeze={leadFrozen ? '__detail' : undefined} rowSpan={rowSpan} aria-label="Details" />}
    </>
  );

  const renderHeader = () => {
    if (!hasHeaderGroups) return <tr>{leadingHeaders()}{visible.map((c) => headerCell(c))}</tr>;

    // Two header rows: spanning group headers on top, leaf headers below.
    const top: ReactNode[] = [];
    let i = 0;
    while (i < visible.length) {
      const c = visible[i];
      if (!c.group) {
        top.push(headerCell(c, 2));
        i++;
        continue;
      }
      let span = 1;
      while (visible[i + span]?.group === c.group) span++;
      top.push(<th key={`g:${c.group}:${i}`} colSpan={span} className="dt-header-group">{c.group}</th>);
      i += span;
    }
    return (
      <>
        <tr>{leadingHeaders(2)}{top}</tr>
        <tr>{visible.filter((c) => c.group).map((c) => headerCell(c))}</tr>
      </>
    );
  };

  const renderFilterRow = () => (
    <tr className="dt-filter-row">
      {leading > 0 && <th colSpan={leading} {...freeze(selection ? '__select' : '__detail', leadFrozen)} />}
      {visible.map((c) => {
        const type = filterTypes.get(c.key);
        const value = filters[c.key] ?? '';
        return (
          <th key={c.key} {...freeze(c.key, frozenSet.has(c.key))}>
            {type === 'select' ? (
              <Dropdown
                size="sm" searchable sortToggle menuWidth={200} aria-label={`Filter ${columnLabel(c)}`} value={value}
                onChange={(v) => setFilter(c.key, v)}
                options={[{ value: '', label: 'All', pinned: true }, ...distinctValues(c, data).map((o) => ({ value: o.key, label: o.label }))]}
              />
            ) : type ? (
              <input className="input dt-filter-input" value={value} aria-label={`Filter ${columnLabel(c)}`}
                placeholder={type === 'number' ? '>5, 1..10' : 'Contains…'} onChange={(e) => setFilter(c.key, e.target.value)} />
            ) : null}
          </th>
        );
      })}
    </tr>
  );

  const renderGroupRow = (node: GroupNode<T>) => {
    const firstAgg = visible.findIndex((c) => c.aggregate);
    const labelColumns = firstAgg === -1 ? visible.length : Math.max(firstAgg, 1);
    const isCollapsed = collapsed.has(node.id);
    const label = node.column.groupCell && node.column.render ? node.column.render(node.rows[0]) : displayValue(node.column, node.value) || '(empty)';
    return (
      <tr key={node.id} className="dt-group-row">
        <td colSpan={leading + labelColumns}>
          <button type="button" className="dt-group-toggle" style={{ paddingLeft: node.depth * 20 }} aria-expanded={!isCollapsed}
            onClick={() => toggleSet(setCollapsed, node.id)}>
            <ChevronRight size={15} className={cn('dt-chevron', !isCollapsed && 'open')} />
            <span className="muted">{columnLabel(node.column)}:</span>
            <span className="dt-group-label">{label}</span>
            <span className="dt-count">{node.rows.length}</span>
          </button>
        </td>
        {firstAgg !== -1 && visible.slice(labelColumns).map((c) => (
          <td key={c.key} className={alignClass(c)}>{aggregateCell(c, node.rows)}</td>
        ))}
      </tr>
    );
  };

  const renderRow = (row: T, depth: number, index: number) => {
    const key = rowKey(row);
    const isExpanded = expanded.has(key);
    const selected = isSelected?.(row) || selection?.selected.has(key);
    return (
      <Fragment key={key}>
        <tr className={cn(onRowClick && 'clickable', selected && 'selected', rowClassName?.(row))} onClick={onRowClick ? () => onRowClick(row) : undefined}>
          {selection && (
            <td className={cn('dt-check', freeze('__select', leadFrozen).className)} style={freeze('__select', leadFrozen).style} onClick={(e) => e.stopPropagation()}>
              <input type="checkbox" className="checkbox" aria-label="Select row" checked={!!selection.selected.has(key)}
                onChange={() => {
                  const next = new Set(selection.selected);
                  if (next.has(key)) next.delete(key); else next.add(key);
                  selection.onChange(next);
                }} />
            </td>
          )}
          {renderDetail && (
            <td className={cn('dt-check', freeze('__detail', leadFrozen).className)} style={freeze('__detail', leadFrozen).style} onClick={(e) => e.stopPropagation()}>
              <button type="button" className="dt-expander" aria-expanded={isExpanded} aria-label={isExpanded ? 'Collapse details' : 'Expand details'}
                onClick={() => toggleSet(setExpanded, key)}>
                <ChevronRight size={15} className={cn('dt-chevron', isExpanded && 'open')} />
              </button>
            </td>
          )}
          {visible.map((c, ci) => {
            const span = spans.get(`${index}|${c.key}`);
            if (span === 0) return null;
            const fz = freeze(c.key, frozenSet.has(c.key));
            return (
              <td key={c.key} rowSpan={span && span > 1 ? span : undefined}
                className={cn(c.className, alignClass(c), span && span > 1 && 'dt-merged', fz.className)}
                style={{ ...(ci === 0 && depth ? { paddingLeft: 14 + depth * 20 } : {}), ...fz.style }}>
                {cellContent(c, row)}
              </td>
            );
          })}
        </tr>
        {renderDetail && isExpanded && (
          <tr className="dt-detail-row"><td colSpan={totalColumns}>{renderDetail(row)}</td></tr>
        )}
      </Fragment>
    );
  };

  // ---------- Layout ----------
  const from = items.length ? (currentPage - 1) * pageSize + 1 : 0;
  const to = Math.min(currentPage * pageSize, items.length);
  const summary = groups
    ? `${filtered.length.toLocaleString()} rows in ${groups.length} group${groups.length === 1 ? '' : 's'}`
    : `${from.toLocaleString()}–${to.toLocaleString()} of ${filtered.length.toLocaleString()}`;

  return (
    <div className={cn('dt', compact && 'dt-compact')}>
      <div className="dt-toolbar">
        {toolbar}
        <div className="input-icon dt-quick">
          <Search size={15} />
          <input className="input" type="search" placeholder="Quick filter…" aria-label="Quick filter" value={quick} onChange={(e) => setQuick(e.target.value)} />
        </div>
        <span className="spacer" />
        <div className="row-wrap dt-tools">
          <Button size="sm" icon={<Filter size={14} />} className={cn(showFilters && 'btn-active')} aria-pressed={showFilters} onClick={() => setShowFilters((s) => !s)}>
            <span className="hide-sm">Filters</span>{activeFilters.length > 0 && <span className="btn-badge">{activeFilters.length}</span>}
          </Button>
          {groupable.length > 0 && (
            <Popover label="Group" icon={<Layers size={14} />} badge={groupBy.length} active={groupBy.length > 0}>
              <div className="popover-title">Group rows by <span className="muted small">(up to {MAX_GROUP_LEVELS} levels)</span></div>
              {groupable.map((c) => {
                const level = groupBy.indexOf(c.key);
                return (
                  <label key={c.key} className="menu-check">
                    <input type="checkbox" className="checkbox" checked={level >= 0} disabled={level < 0 && groupBy.length >= MAX_GROUP_LEVELS}
                      onChange={() => setGroupBy((g) => (level >= 0 ? g.filter((k) => k !== c.key) : [...g, c.key]))} />
                    {columnLabel(c)}
                    {level >= 0 && <span className="dt-level">Level {level + 1}</span>}
                  </label>
                );
              })}
              {groupBy.length > 0 && (
                <div className="popover-actions">
                  <Button size="sm" variant="ghost" onClick={() => setCollapsed(new Set())}>Expand all</Button>
                  <Button size="sm" variant="ghost" onClick={() => setCollapsed(new Set(groups ? allGroupIds(groups) : []))}>Collapse all</Button>
                  <Button size="sm" variant="ghost" onClick={() => setGroupBy([])}>Clear</Button>
                </div>
              )}
            </Popover>
          )}
          <Popover label="Columns" icon={<Columns3 size={14} />} badge={hidden.size} align="right">
            <div className="popover-title">Visible columns <span className="muted small">· <Pin size={11} /> freezes up to that column</span></div>
            {columns.map((c) => {
              const canFreeze = !hidden.has(c.key) && !c.group && !visible.slice(0, visible.indexOf(c)).some((v) => v.group);
              const isEdge = frozenUntil === c.key && frozenSet.has(c.key);
              return (
                <div key={c.key} className="menu-check-row">
                  <label className="menu-check">
                    <input type="checkbox" className="checkbox" checked={!hidden.has(c.key)}
                      disabled={c.hideable === false || (!hidden.has(c.key) && visible.length === 1)} onChange={() => toggleColumn(c.key)} />
                    {columnLabel(c) || <span className="muted">(unnamed)</span>}
                    {frozenSet.has(c.key) && <span className="dt-level">Frozen</span>}
                  </label>
                  {canFreeze && (
                    <button type="button" className={cn('dt-pin', isEdge && 'active')} aria-pressed={isEdge}
                      title={isEdge ? 'Unfreeze columns' : 'Freeze columns up to here'} aria-label={isEdge ? 'Unfreeze columns' : `Freeze up to ${columnLabel(c)}`}
                      onClick={() => setFrozenUntil(isEdge ? null : c.key)}>
                      {isEdge ? <PinOff size={13} /> : <Pin size={13} />}
                    </button>
                  )}
                </div>
              );
            })}
          </Popover>
          {pivot && groupable.length > 0 && (
            <Button size="sm" icon={<Grid3x3 size={14} />} className={cn(view === 'pivot' && 'btn-active')} aria-pressed={view === 'pivot'} onClick={openPivot}>
              <span className="hide-sm">Pivot</span>
            </Button>
          )}
          <Button size="sm" iconOnly icon={<AlignJustify size={14} />} className={cn(compact && 'btn-active')} aria-pressed={compact}
            aria-label="Compact rows" title="Compact rows" onClick={() => setCompact((c) => !c)} />
          <Button size="sm" iconOnly icon={<Download size={14} />} aria-label="Export CSV" title="Export filtered rows as CSV" disabled={!filtered.length} onClick={exportCsv} />
        </div>
      </div>

      {(sorts.length > 0 || activeFilters.length > 0 || groupBy.length > 0 || quick) && view === 'table' && (
        <div className="dt-chips">
          {sorts.map((s, i) => (
            <span key={`s:${s.key}`} className="dt-chip">
              {i === 0 ? 'Sorted by' : 'then'} <b>{columnLabel(colMap.get(s.key)!)}</b> {s.dir === 'asc' ? '↑' : '↓'}
              <button type="button" aria-label="Remove sort" onClick={() => setSorts((p) => p.filter((x) => x.key !== s.key))}><X size={12} /></button>
            </span>
          ))}
          {groupBy.map((k, i) => (
            <span key={`g:${k}`} className="dt-chip">
              {i === 0 ? 'Grouped by' : 'then'} <b>{columnLabel(colMap.get(k)!)}</b>
              <button type="button" aria-label="Remove grouping" onClick={() => setGroupBy((g) => g.filter((x) => x !== k))}><X size={12} /></button>
            </span>
          ))}
          {activeFilters.map(([k, v]) => {
            const c = colMap.get(k)!;
            const label = filterTypes.get(k) === 'select' ? distinctValues(c, data).find((o) => o.key === v)?.label ?? v : v;
            return (
              <span key={`f:${k}`} className="dt-chip">
                <b>{columnLabel(c)}</b> {filterTypes.get(k) === 'text' ? '∋' : filterTypes.get(k) === 'number' ? '' : '='} {label}
                <button type="button" aria-label="Remove filter" onClick={() => setFilter(k, '')}><X size={12} /></button>
              </span>
            );
          })}
          <button type="button" className="dt-clear" onClick={clearAll}>Clear all</button>
        </div>
      )}

      {loading && !rows ? (
        <div className="stack-sm" style={{ padding: 18 }}>
          {Array.from({ length: 8 }).map((_, i) => <div key={i} className="skeleton" style={{ height: 34 }} />)}
        </div>
      ) : view === 'pivot' && pivotConfig ? (
        <PivotView rows={filtered} columns={columns} dimensions={groupable} measures={measures}
          config={pivotConfig} onConfigChange={setPivotConfig} exportName={exportName} />
      ) : data.length === 0 ? (
        <EmptyState icon={<Inbox size={22} />} title={empty?.title ?? 'Nothing here yet'} description={empty?.description} />
      ) : (
        <>
          <div className="dt-scroll">
            <table ref={tableRef} className="table dt-table">
              <thead>
                {renderHeader()}
                {showFilters && renderFilterRow()}
              </thead>
              <tbody>
                {filtered.length === 0 ? (
                  <tr><td colSpan={totalColumns}>
                    <EmptyState icon={<FilterX size={22} />} title="No rows match" description="Try a different filter or quick search."
                      action={<Button size="sm" onClick={clearAll}>Clear filters</Button>} />
                  </td></tr>
                ) : pageItems.map((item, i) => (item.kind === 'group' ? renderGroupRow(item.node) : renderRow(item.row, item.depth, i)))}
              </tbody>
              {hasFooter && filtered.length > 0 && (
                <tfoot>
                  <tr>
                    {(() => {
                      // The label spans every column before the first footer value, so it never wraps in a narrow column.
                      const firstFooter = visible.findIndex((c) => c.footer);
                      const labelSpan = leading + firstFooter;
                      const label = (
                        <span className="dt-foot-label">
                          Totals
                          <span className="dt-foot-count">{filtered.length.toLocaleString()} {filtered.length === 1 ? 'row' : 'rows'}</span>
                        </span>
                      );
                      return (
                        <>
                          {labelSpan > 0 ? <td colSpan={labelSpan} className={leadFrozen ? 'dt-frozen dt-frozen-foot' : undefined}>{label}</td> : null}
                          {visible.slice(firstFooter).map((c, i) => (
                            <td key={c.key} className={alignClass(c)}>
                              {labelSpan === 0 && i === 0 && <>{label} </>}
                              {c.footer
                                ? typeof c.footer === 'function' ? c.footer(filtered) : aggregateCell(c, filtered, c.footer)
                                : null}
                            </td>
                          ))}
                        </>
                      );
                    })()}
                  </tr>
                </tfoot>
              )}
            </table>
          </div>

          <div className="pagination">
            <span>
              {summary}
              {filtered.length !== data.length && <> · filtered from {data.length.toLocaleString()}</>}
              {totalCount != null && totalCount > data.length && <> · <span className="dt-truncated">latest {data.length.toLocaleString()} of {totalCount.toLocaleString()} loaded</span></>}
            </span>
            <div className="row-wrap">
              <span className="row small">
                Rows
                <Dropdown size="sm" className="dt-page-size" menuWidth={110} aria-label="Rows per page" value={String(pageSize)}
                  onChange={(v) => setPageSize(Number(v))} options={PAGE_SIZES.map((s) => ({ value: String(s), label: `${s} / page` }))} />
              </span>
              <Button size="sm" iconOnly icon={<ChevronsLeft size={15} />} aria-label="First page" disabled={currentPage <= 1} onClick={() => setPage(1)} />
              <Button size="sm" iconOnly icon={<ChevronLeft size={15} />} aria-label="Previous page" disabled={currentPage <= 1} onClick={() => setPage(currentPage - 1)} />
              <span className="nowrap">Page {currentPage} of {pageCount}</span>
              <Button size="sm" iconOnly icon={<ChevronRight size={15} />} aria-label="Next page" disabled={currentPage >= pageCount} onClick={() => setPage(currentPage + 1)} />
              <Button size="sm" iconOnly icon={<ChevronsRight size={15} />} aria-label="Last page" disabled={currentPage >= pageCount} onClick={() => setPage(pageCount)} />
            </div>
          </div>
        </>
      )}
    </div>
  );
}
