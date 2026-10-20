import { describe, expect, it } from 'vitest';
import * as E from './engine';
import type { Column } from './types';

interface Row {
  id: number;
  host: string;
  sev: 'Low' | 'Medium' | 'High' | 'Critical';
  hits: number | null;
  at: Date | null;
}

const rank: Record<Row['sev'], number> = { Low: 1, Medium: 2, High: 3, Critical: 4 };
const rows: Row[] = [
  { id: 1, host: 'web', sev: 'High', hits: 10, at: new Date('2026-01-03') },
  { id: 2, host: 'db', sev: 'Critical', hits: 5, at: new Date('2026-01-01') },
  { id: 3, host: 'web', sev: 'Low', hits: null, at: new Date('2026-01-02') },
  { id: 4, host: 'db', sev: 'High', hits: 20, at: null },
  { id: 5, host: 'app', sev: 'Medium', hits: 2, at: new Date('2026-01-05') },
];
const cols: Column<Row>[] = [
  { key: 'host', header: 'Host', value: (r) => r.host, mergeRepeated: true },
  { key: 'sev', header: 'Severity', value: (r) => r.sev, sortValue: (r) => rank[r.sev] },
  { key: 'hits', header: 'Hits', value: (r) => r.hits, aggregate: 'sum' },
  { key: 'at', header: 'At', value: (r) => r.at },
];
const map = new Map(cols.map((c) => [c.key, c]));
const col = (key: string) => map.get(key)!;
const ids = (list: Row[]) => list.map((r) => r.id).join(',');
const types = new Map(cols.map((c) => [c.key, E.inferFilterType(c, rows)]));
const filter = (quick: string, filters: Record<string, string>) => ids(E.filterRows(rows, cols, cols, types, quick, filters));

describe('sorting', () => {
  it('sorts by sortValue, keeps empty values last in both directions, and is stable', () => {
    expect(ids(E.sortRows(rows, [{ key: 'sev', dir: 'desc' }], map))).toBe('2,1,4,5,3');
    expect(ids(E.sortRows(rows, [{ key: 'hits', dir: 'asc' }], map))).toBe('5,2,1,4,3');
    expect(ids(E.sortRows(rows, [{ key: 'hits', dir: 'desc' }], map))).toBe('4,1,2,5,3');
    expect(ids(E.sortRows(rows, [{ key: 'at', dir: 'asc' }], map))).toBe('2,3,1,5,4');
  });

  it('supports multi-column sorting', () => {
    expect(ids(E.sortRows(rows, [{ key: 'host', dir: 'asc' }, { key: 'hits', dir: 'desc' }], map))).toBe('5,4,2,1,3');
  });
});

describe('filtering', () => {
  it('infers filter types from data', () => {
    expect(types.get('hits')).toBe('number');
    expect(types.get('sev')).toBe('select');
    expect(types.get('at')).toBe('text');
  });

  it('applies number expressions, ignoring incomplete ones', () => {
    expect(filter('', { hits: '>5' })).toBe('1,4');
    expect(filter('', { hits: '2..10' })).toBe('1,2,5');
    expect(filter('', { hits: '>' })).toBe('1,2,3,4,5');
  });

  it('combines select filters with quick search', () => {
    expect(filter('', { sev: 'High' })).toBe('1,4');
    expect(filter('DB', {})).toBe('2,4');
    expect(filter('web', { sev: 'Low' })).toBe('3');
  });
});

describe('aggregation', () => {
  it('ignores empty values instead of treating them as zero', () => {
    expect(E.aggregate([1, 2, null, 3], 'sum')).toBe(6);
    expect(E.aggregate([1, null, 3], 'avg')).toBe(2);
    expect(E.aggregate([5, null], 'min')).toBe(5);
    expect(E.aggregate(['a', 'b', 'a', null], 'distinct')).toBe(2);
    expect(E.aggregate([null, null], 'sum')).toBeNull();
    expect(E.formatAggregate(2.345, 'avg')).toBe('2.3');
  });
});

describe('grouping', () => {
  it('orders groups by sortValue and nests levels', () => {
    const bySeverity = E.groupRows(rows, [col('sev')], new Set());
    expect(bySeverity.map((n) => n.value)).toEqual(['Low', 'Medium', 'High', 'Critical']);

    const nested = E.groupRows(rows, [col('host'), col('sev')], new Set());
    expect(nested.map((n) => `${n.value}:${n.rows.length}`)).toEqual(['app:1', 'db:2', 'web:2']);
    expect(nested[1].children).toHaveLength(2);
    expect(E.allGroupIds(nested)).toHaveLength(8);
  });

  it('flattens groups and hides rows of collapsed groups', () => {
    const nested = E.groupRows(rows, [col('host'), col('sev')], new Set());
    const flat = E.flattenGroups(nested, new Set());
    expect(flat).toHaveLength(3 + 5 + 5);
    expect(E.flattenGroups(nested, new Set([nested[1].id]))).toHaveLength(flat.length - 4);
  });
});

describe('cell merging', () => {
  const items: E.GridItem<Row>[] = E.sortRows(rows, [{ key: 'host', dir: 'asc' }], map)
    .map((row) => ({ kind: 'row' as const, row, depth: 0 }));

  it('merges consecutive equal values', () => {
    const spans = E.computeRowSpans(items, cols, () => false);
    expect(spans.get('1|host')).toBe(2);
    expect(spans.get('2|host')).toBe(0);
    expect(spans.get('3|host')).toBe(2);
  });

  it('breaks a merge after an expanded row', () => {
    expect(E.computeRowSpans(items, cols, (i) => i === 1).get('1|host')).toBeUndefined();
  });
});

describe('pivot', () => {
  it('cross-tabulates counts with row, column and grand totals', () => {
    const p = E.buildPivot(rows, { rowKey: 'host', colKey: 'sev', valueKey: '', agg: 'count' }, map)!;
    expect(p.rowKeys.map((r) => r.label)).toEqual(['app', 'db', 'web']);
    expect(p.colKeys.map((c) => c.label)).toEqual(['Low', 'Medium', 'High', 'Critical']);
    expect(p.cell('db', 'High')).toBe(1);
    expect(p.cell('app', 'High')).toBeNull();
    expect(p.rowTotal('db')).toBe(2);
    expect(p.colTotal('High')).toBe(2);
    expect(p.grandTotal).toBe(5);
  });

  it('aggregates a measure without a column dimension', () => {
    const p = E.buildPivot(rows, { rowKey: 'host', colKey: '', valueKey: 'hits', agg: 'sum' }, map)!;
    expect(p.cell('db', p.colKeys[0].key)).toBe(25);
    expect(p.grandTotal).toBe(37);
  });
});

describe('CSV export', () => {
  it('quotes special characters and neutralises formula injection', () => {
    expect(E.toCsv(['a', 'b'], [['x,y', '=cmd()'], ['say "hi"', '@x']]))
      .toBe('a,b\r\n"x,y",\'=cmd()\r\n"say ""hi""",\'@x');
  });
});
