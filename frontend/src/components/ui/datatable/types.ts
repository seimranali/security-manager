import type { ReactNode } from 'react';

export type CellValue = string | number | boolean | Date | null | undefined;
export type AggregateFn = 'count' | 'sum' | 'avg' | 'min' | 'max' | 'distinct';
export type FilterType = 'text' | 'select' | 'number';
export type SortDir = 'asc' | 'desc';

export interface SortRule {
  key: string;
  dir: SortDir;
}

export interface Column<T> {
  key: string;
  header: ReactNode;
  /** Plain-text label for menus, pivot and CSV (defaults to `header` when it is a string). */
  headerText?: string;
  /** Rich cell content. Falls back to the formatted `value`. */
  render?: (row: T) => ReactNode;
  /** Raw value used for sorting, filtering, grouping, pivoting, aggregates and export. */
  value?: (row: T) => CellValue;
  /** Overrides `value` for ordering (e.g. severity rank). */
  sortValue?: (row: T) => CellValue;
  sortable?: boolean;
  /** Column filter UI; inferred from the data when omitted. `false` disables it. */
  filter?: FilterType | false;
  groupable?: boolean;
  /** Aggregate shown in group header rows (and used as the default pivot measure). */
  aggregate?: AggregateFn;
  /** Footer cell: an aggregate over all filtered rows, or custom content. */
  footer?: AggregateFn | ((rows: T[]) => ReactNode);
  /** Formats raw/aggregated values (group rows, footer, pivot, default cell). */
  format?: (value: CellValue) => string;
  /** Use `render(firstRow)` for the group header label (e.g. badges). */
  groupCell?: boolean;
  /** Merge consecutive cells with identical values (rowspan). */
  mergeRepeated?: boolean;
  /** Header group label; adjacent columns sharing it get a spanning parent header. */
  group?: string;
  align?: 'left' | 'center' | 'right';
  width?: number | string;
  className?: string;
  /** Freeze this column and every column before it on the left while scrolling horizontally (users can change it from the Columns menu). */
  frozen?: boolean;
  /** Hidden by default (can be shown from the Columns menu). */
  hidden?: boolean;
  /** Set false to keep the column always visible. */
  hideable?: boolean;
}

export interface Selection {
  selected: Set<string>;
  onChange: (next: Set<string>) => void;
}
