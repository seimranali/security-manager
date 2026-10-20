import type { AggregateFn, CellValue, Column, FilterType, SortRule } from './types';

/* Pure data operations for the grid: no React, fully deterministic. */

export const columnLabel = <T,>(c: Column<T>) =>
  c.headerText ?? (typeof c.header === 'string' ? c.header : c.key);

export const isEmpty = (v: CellValue) => v === null || v === undefined || v === '';

const dateFmt = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' });
const numFmt = new Intl.NumberFormat();
const avgFmt = new Intl.NumberFormat(undefined, { maximumFractionDigits: 1 });

export function toText(v: CellValue): string {
  if (isEmpty(v)) return '';
  if (v instanceof Date) return dateFmt.format(v);
  if (typeof v === 'boolean') return v ? 'Yes' : 'No';
  if (typeof v === 'number') return numFmt.format(v);
  return String(v);
}

/** Key used for equality (grouping, select filters, merging) — unformatted. */
export const keyOf = (v: CellValue): string =>
  isEmpty(v) ? '' : v instanceof Date ? String(v.getTime()) : String(v);

export const displayValue = <T,>(c: Column<T>, v: CellValue) => (c.format ? c.format(v) : toText(v));

export function compareValues(a: CellValue, b: CellValue): number {
  if (a instanceof Date || b instanceof Date) return toTime(a) - toTime(b);
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  if (typeof a === 'boolean' && typeof b === 'boolean') return Number(a) - Number(b);
  return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: 'base' });
}

const toTime = (v: CellValue) => (v instanceof Date ? v.getTime() : typeof v === 'number' ? v : new Date(String(v)).getTime());

// ---------- Sorting ----------
export function sortRows<T>(rows: T[], sorts: SortRule[], columns: Map<string, Column<T>>): T[] {
  const rules = sorts
    .map((s) => ({ s, get: columns.get(s.key)?.sortValue ?? columns.get(s.key)?.value }))
    .filter((r): r is { s: SortRule; get: (row: T) => CellValue } => !!r.get);
  if (!rules.length) return rows;

  return rows
    .map((row, index) => ({ row, index }))
    .sort((x, y) => {
      for (const { s, get } of rules) {
        const a = get(x.row);
        const b = get(y.row);
        const ae = isEmpty(a);
        const be = isEmpty(b);
        if (ae !== be) return ae ? 1 : -1; // empty values always last
        if (ae) continue;
        const d = compareValues(a, b);
        if (d !== 0) return s.dir === 'asc' ? d : -d;
      }
      return x.index - y.index; // stable
    })
    .map((x) => x.row);
}

// ---------- Filtering ----------
export function inferFilterType<T>(c: Column<T>, rows: T[]): FilterType | null {
  if (c.filter === false || !c.value) return null;
  if (c.filter) return c.filter;
  // Numbers get expression filters; low-cardinality values get a dropdown; everything else free text.
  const distinct = new Set<string>();
  for (const row of rows) {
    const v = c.value(row);
    if (isEmpty(v)) continue;
    if (typeof v === 'number') return 'number';
    if (v instanceof Date) return 'text';
    distinct.add(keyOf(v));
    if (distinct.size > 12) return 'text';
  }
  return 'select';
}

/** Number expressions: "5", "=5", ">5", ">=5", "<5", "<=5", "!=5", "5..10" or "5-10". */
export function matchesNumber(expression: string, v: CellValue): boolean {
  const e = expression.trim();
  const range = e.match(/^(-?\d+(?:\.\d+)?)\s*(?:\.\.|-)\s*(-?\d+(?:\.\d+)?)$/);
  const cmp = range ? null : e.match(/^(>=|<=|!=|>|<|=)?\s*(-?\d+(?:\.\d+)?)$/);
  if (!range && !cmp) return true; // incomplete expression: don't hide rows while typing

  const n = typeof v === 'number' ? v : Number(v);
  if (isEmpty(v) || Number.isNaN(n)) return false;
  if (range) return n >= Number(range[1]) && n <= Number(range[2]);
  const t = Number(cmp![2]);
  switch (cmp![1]) {
    case '>': return n > t;
    case '>=': return n >= t;
    case '<': return n < t;
    case '<=': return n <= t;
    case '!=': return n !== t;
    default: return n === t;
  }
}

export function filterRows<T>(
  rows: T[],
  columns: Column<T>[],
  searchColumns: Column<T>[],
  filterTypes: Map<string, FilterType | null>,
  quick: string,
  filters: Record<string, string>,
): T[] {
  const q = quick.trim().toLowerCase();
  const active = Object.entries(filters)
    .filter(([, value]) => value.trim() !== '')
    .map(([key, value]) => ({ column: columns.find((c) => c.key === key), value, type: filterTypes.get(key) }))
    .filter((f) => f.column?.value && f.type);
  const searchable = searchColumns.filter((c) => c.value);
  if (!q && !active.length) return rows;

  return rows.filter((row) => {
    for (const f of active) {
      const v = f.column!.value!(row);
      if (f.type === 'number') {
        if (!matchesNumber(f.value, v)) return false;
      } else if (f.type === 'select') {
        if (keyOf(v) !== f.value) return false;
      } else if (!displayValue(f.column!, v).toLowerCase().includes(f.value.toLowerCase())) {
        return false;
      }
    }
    return !q || searchable.some((c) => displayValue(c, c.value!(row)).toLowerCase().includes(q));
  });
}

export function distinctValues<T>(c: Column<T>, rows: T[], limit = 200): { key: string; label: string }[] {
  if (!c.value) return [];
  const seen = new Map<string, CellValue>();
  for (const row of rows) {
    const v = c.value(row);
    if (!isEmpty(v) && !seen.has(keyOf(v))) seen.set(keyOf(v), v);
    if (seen.size >= limit) break;
  }
  const order = c.sortValue;
  const firstRow = new Map<string, T>();
  if (order) for (const row of rows) { const k = keyOf(c.value(row)); if (!firstRow.has(k)) firstRow.set(k, row); }
  return [...seen.entries()]
    .sort(([ka, a], [kb, b]) => (order ? compareValues(order(firstRow.get(ka)!), order(firstRow.get(kb)!)) : compareValues(a, b)))
    .map(([key, v]) => ({ key, label: displayValue(c, v) }));
}

// ---------- Aggregation ----------
export const AGGREGATES: { value: AggregateFn; label: string }[] = [
  { value: 'count', label: 'Count' },
  { value: 'sum', label: 'Sum' },
  { value: 'avg', label: 'Average' },
  { value: 'min', label: 'Min' },
  { value: 'max', label: 'Max' },
  { value: 'distinct', label: 'Distinct' },
];
export const aggregateLabel = (fn: AggregateFn) => AGGREGATES.find((a) => a.value === fn)!.label;

export function aggregate(values: CellValue[], fn: AggregateFn): CellValue {
  if (fn === 'count') return values.length;
  if (fn === 'distinct') return new Set(values.filter((v) => !isEmpty(v)).map(keyOf)).size;

  const dates = values.filter((v): v is Date => v instanceof Date);
  if (dates.length && (fn === 'min' || fn === 'max')) {
    const times = dates.map((d) => d.getTime());
    return new Date(fn === 'min' ? Math.min(...times) : Math.max(...times));
  }

  // Empty cells are ignored (not treated as zero) so averages and minimums stay honest.
  const nums = values
    .filter((v) => !isEmpty(v) && typeof v !== 'boolean')
    .map((v) => (typeof v === 'number' ? v : Number(v)))
    .filter((n) => Number.isFinite(n));
  if (!nums.length) return null;
  switch (fn) {
    case 'sum': return nums.reduce((a, b) => a + b, 0);
    case 'avg': return nums.reduce((a, b) => a + b, 0) / nums.length;
    case 'min': return Math.min(...nums);
    case 'max': return Math.max(...nums);
  }
}

export function formatAggregate<T>(value: CellValue, fn: AggregateFn, c?: Column<T>): string {
  if (isEmpty(value)) return '—';
  if (fn === 'count' || fn === 'distinct') return numFmt.format(value as number);
  if (c?.format) return c.format(fn === 'avg' && typeof value === 'number' ? Math.round(value * 10) / 10 : value);
  if (fn === 'avg' && typeof value === 'number') return avgFmt.format(value);
  return toText(value);
}

// ---------- Grouping ----------
export interface GroupNode<T> {
  id: string;
  column: Column<T>;
  value: CellValue;
  depth: number;
  rows: T[];
  children?: GroupNode<T>[];
}

export function groupRows<T>(rows: T[], groupColumns: Column<T>[], sortedKeys: Set<string>, depth = 0, parentId = ''): GroupNode<T>[] {
  const [column, ...rest] = groupColumns;
  if (!column?.value) return [];
  const buckets = new Map<string, { value: CellValue; rows: T[] }>();
  for (const row of rows) {
    const v = column.value(row);
    const k = keyOf(v);
    let bucket = buckets.get(k);
    if (!bucket) buckets.set(k, (bucket = { value: v, rows: [] }));
    bucket.rows.push(row);
  }

  let entries = [...buckets.entries()];
  // If the user sorted by this column, keep the sorted order; otherwise order groups ascending.
  if (!sortedKeys.has(column.key)) {
    const order = column.sortValue ?? column.value;
    entries = entries.sort(([, a], [, b]) => {
      const av = order(a.rows[0]);
      const bv = order(b.rows[0]);
      if (isEmpty(av) !== isEmpty(bv)) return isEmpty(av) ? 1 : -1;
      return compareValues(av, bv);
    });
  }

  return entries.map(([k, bucket]) => {
    const id = `${parentId}/${column.key}:${k}`;
    return {
      id,
      column,
      value: bucket.value,
      depth,
      rows: bucket.rows,
      children: rest.length ? groupRows(bucket.rows, rest, sortedKeys, depth + 1, id) : undefined,
    };
  });
}

export type GridItem<T> =
  | { kind: 'group'; node: GroupNode<T> }
  | { kind: 'row'; row: T; depth: number };

export function flattenGroups<T>(nodes: GroupNode<T>[], collapsed: Set<string>, out: GridItem<T>[] = []): GridItem<T>[] {
  for (const node of nodes) {
    out.push({ kind: 'group', node });
    if (collapsed.has(node.id)) continue;
    if (node.children) flattenGroups(node.children, collapsed, out);
    else for (const row of node.rows) out.push({ kind: 'row', row, depth: node.depth + 1 });
  }
  return out;
}

export function allGroupIds<T>(nodes: GroupNode<T>[], out: string[] = []): string[] {
  for (const n of nodes) {
    out.push(n.id);
    if (n.children) allGroupIds(n.children, out);
  }
  return out;
}

// ---------- Cell merging ----------
/** Returns rowSpan per `${rowIndex}|${columnKey}`: >1 starts a merged run, 0 means the cell is covered. */
export function computeRowSpans<T>(items: GridItem<T>[], columns: Column<T>[], breaksAfter: (index: number) => boolean): Map<string, number> {
  const spans = new Map<string, number>();
  for (const c of columns.filter((col) => col.mergeRepeated && col.value)) {
    let start = -1;
    let startKey = '';
    items.forEach((item, i) => {
      if (item.kind !== 'row') { start = -1; return; }
      const k = keyOf(c.value!(item.row));
      const canExtend = start >= 0 && k === startKey && k !== '' && !breaksAfter(i - 1) && items[i - 1]?.kind === 'row';
      if (canExtend) {
        spans.set(`${start}|${c.key}`, (spans.get(`${start}|${c.key}`) ?? 1) + 1);
        spans.set(`${i}|${c.key}`, 0);
      } else {
        start = i;
        startKey = k;
      }
    });
  }
  return spans;
}

// ---------- Pivot ----------
export interface PivotConfig {
  rowKey: string;
  colKey: string; // '' = no column dimension
  valueKey: string; // '' = count of rows
  agg: AggregateFn;
}

export interface PivotResult {
  rowKeys: { key: string; label: string }[];
  colKeys: { key: string; label: string }[];
  truncatedColumns: number;
  cell: (r: string, c: string) => CellValue;
  rowTotal: (r: string) => CellValue;
  colTotal: (c: string) => CellValue;
  grandTotal: CellValue;
  max: number;
  agg: AggregateFn;
}

const MAX_PIVOT_COLUMNS = 30;

export function buildPivot<T>(rows: T[], cfg: PivotConfig, columns: Map<string, Column<T>>): PivotResult | null {
  const rc = columns.get(cfg.rowKey);
  const cc = cfg.colKey ? columns.get(cfg.colKey) : undefined;
  const vc = cfg.valueKey ? columns.get(cfg.valueKey) : undefined;
  if (!rc?.value) return null;
  const agg: AggregateFn = vc ? cfg.agg : 'count';

  const cells = new Map<string, CellValue[]>();
  const byRow = new Map<string, CellValue[]>();
  const byCol = new Map<string, CellValue[]>();
  const all: CellValue[] = [];
  const rowMeta = new Map<string, { label: string; order: CellValue }>();
  const colMeta = new Map<string, { label: string; order: CellValue }>();
  const push = (map: Map<string, CellValue[]>, k: string, v: CellValue) => {
    const list = map.get(k);
    if (list) list.push(v); else map.set(k, [v]);
  };

  for (const row of rows) {
    const rv = rc.value(row);
    const rk = keyOf(rv) || '∅';
    if (!rowMeta.has(rk)) rowMeta.set(rk, { label: displayValue(rc, rv) || '(empty)', order: (rc.sortValue ?? rc.value)(row) });
    let ck = 'Σ';
    if (cc?.value) {
      const cv = cc.value(row);
      ck = keyOf(cv) || '∅';
      if (!colMeta.has(ck)) colMeta.set(ck, { label: displayValue(cc, cv) || '(empty)', order: (cc.sortValue ?? cc.value)(row) });
    }
    const v = vc?.value ? vc.value(row) : 1;
    push(cells, `${rk}\u0000${ck}`, v);
    push(byRow, rk, v);
    push(byCol, ck, v);
    all.push(v);
  }
  if (!cc) colMeta.set('Σ', { label: aggregateLabel(agg), order: 0 });

  const ordered = (meta: Map<string, { label: string; order: CellValue }>) =>
    [...meta.entries()]
      .sort(([, a], [, b]) => (isEmpty(a.order) !== isEmpty(b.order) ? (isEmpty(a.order) ? 1 : -1) : compareValues(a.order, b.order)))
      .map(([key, m]) => ({ key, label: m.label }));

  const allCols = ordered(colMeta);
  const colKeys = allCols.slice(0, MAX_PIVOT_COLUMNS);
  const rowKeys = ordered(rowMeta);

  let max = 0;
  const cellValue = (r: string, c: string) => {
    const values = cells.get(`${r}\u0000${c}`);
    return values ? aggregate(values, agg) : null;
  };
  for (const r of rowKeys) for (const c of colKeys) {
    const v = cellValue(r.key, c.key);
    if (typeof v === 'number') max = Math.max(max, v);
  }

  return {
    rowKeys,
    colKeys,
    truncatedColumns: allCols.length - colKeys.length,
    cell: cellValue,
    rowTotal: (r) => aggregate(byRow.get(r) ?? [], agg),
    colTotal: (c) => aggregate(byCol.get(c) ?? [], agg),
    grandTotal: aggregate(all, agg),
    max,
    agg,
  };
}

// ---------- Export ----------
function csvCell(text: string): string {
  // Neutralise spreadsheet formula injection.
  const safe = /^[=+\-@\t\r]/.test(text) ? `'${text}` : text;
  return /[",\r\n]/.test(safe) ? `"${safe.replace(/"/g, '""')}"` : safe;
}

export function toCsv(headers: string[], rows: string[][]): string {
  return [headers, ...rows].map((r) => r.map(csvCell).join(',')).join('\r\n');
}

export function downloadCsv(fileName: string, csv: string) {
  const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName.endsWith('.csv') ? fileName : `${fileName}.csv`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}
