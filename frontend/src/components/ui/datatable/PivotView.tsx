import { useMemo } from 'react';
import { Download, Grid3x3 } from 'lucide-react';
import { Button } from '../Button';
import { Dropdown } from '../Dropdown';
import { EmptyState } from '../Feedback';
import { AGGREGATES, buildPivot, columnLabel, downloadCsv, formatAggregate, toCsv, type PivotConfig } from './engine';
import type { AggregateFn, Column } from './types';

interface PivotViewProps<T> {
  rows: T[];
  columns: Column<T>[];
  dimensions: Column<T>[];
  measures: Column<T>[];
  config: PivotConfig;
  onConfigChange: (c: PivotConfig) => void;
  exportName?: string;
}

/** Cross-tab of the filtered rows: rows × columns with an aggregate, plus row/column/grand totals. */
export function PivotView<T>({ rows, columns, dimensions, measures, config, onConfigChange, exportName }: PivotViewProps<T>) {
  const map = useMemo(() => new Map(columns.map((c) => [c.key, c])), [columns]);
  const pivot = useMemo(() => buildPivot(rows, config, map), [rows, config, map]);
  const valueColumn = config.valueKey ? map.get(config.valueKey) : undefined;
  const fmt = (v: Parameters<typeof formatAggregate>[0]) => formatAggregate(v, pivot?.agg ?? 'count', valueColumn);
  const set = (patch: Partial<PivotConfig>) => onConfigChange({ ...config, ...patch });

  const exportPivot = () => {
    if (!pivot) return;
    const rowLabel = columnLabel(map.get(config.rowKey)!);
    const headers = [rowLabel, ...pivot.colKeys.map((c) => c.label), ...(config.colKey ? ['Total'] : [])];
    const body = pivot.rowKeys.map((r) => [
      r.label,
      ...pivot.colKeys.map((c) => fmt(pivot.cell(r.key, c.key))),
      ...(config.colKey ? [fmt(pivot.rowTotal(r.key))] : []),
    ]);
    body.push(['Total', ...pivot.colKeys.map((c) => fmt(pivot.colTotal(c.key))), ...(config.colKey ? [fmt(pivot.grandTotal)] : [])]);
    downloadCsv(`${exportName ?? 'pivot'}-pivot`, toCsv(headers, body));
  };

  return (
    <div className="pivot">
      <div className="pivot-config">
        <label className="pivot-field">
          <span>Rows</span>
          <Dropdown size="sm" sortToggle value={config.rowKey} onChange={(v) => set({ rowKey: v, colKey: v === config.colKey ? '' : config.colKey })}
            options={dimensions.map((d) => ({ value: d.key, label: columnLabel(d) }))} />
        </label>
        <label className="pivot-field">
          <span>Columns</span>
          <Dropdown size="sm" sortToggle value={config.colKey} onChange={(v) => set({ colKey: v })}
            options={[{ value: '', label: '(none)', pinned: true }, ...dimensions.filter((d) => d.key !== config.rowKey).map((d) => ({ value: d.key, label: columnLabel(d) }))]} />
        </label>
        <label className="pivot-field">
          <span>Values</span>
          <Dropdown size="sm" value={config.valueKey}
            onChange={(v) => {
              const m = map.get(v);
              set({ valueKey: v, agg: m?.aggregate && m.aggregate !== 'count' ? m.aggregate : config.agg === 'count' ? 'sum' : config.agg });
            }}
            options={[{ value: '', label: 'Row count', description: 'Number of rows', pinned: true },
              ...measures.map((m) => ({ value: m.key, label: columnLabel(m), group: 'Numeric columns' }))]} />
        </label>
        <label className="pivot-field">
          <span>Aggregate</span>
          <Dropdown size="sm" value={config.valueKey ? config.agg : 'count'} disabled={!config.valueKey}
            onChange={(v) => set({ agg: v as AggregateFn })}
            options={AGGREGATES.map((a) => ({ value: a.value, label: a.label }))} />
        </label>
        <span className="spacer" />
        <Button size="sm" icon={<Download size={14} />} onClick={exportPivot} disabled={!pivot}>Export</Button>
      </div>

      {!pivot || rows.length === 0 ? (
        <EmptyState icon={<Grid3x3 size={22} />} title="Nothing to pivot" description="No rows match the current filters." />
      ) : (
        <div className="dt-scroll">
          <table className="table pivot-table">
            <thead>
              <tr>
                <th>{columnLabel(map.get(config.rowKey)!)}{config.colKey && <span className="muted"> \ {columnLabel(map.get(config.colKey)!)}</span>}</th>
                {pivot.colKeys.map((c) => <th key={c.key} className="text-right">{c.label}</th>)}
                {config.colKey && <th className="text-right pivot-total">Total</th>}
              </tr>
            </thead>
            <tbody>
              {pivot.rowKeys.map((r) => (
                <tr key={r.key}>
                  <th scope="row" className="pivot-rowhead">{r.label}</th>
                  {pivot.colKeys.map((c) => {
                    const v = pivot.cell(r.key, c.key);
                    const intensity = typeof v === 'number' && pivot.max > 0 ? v / pivot.max : 0;
                    return (
                      <td key={c.key} className="text-right num" title={`${r.label} · ${c.label}: ${fmt(v)}`}
                        style={intensity ? { background: `color-mix(in srgb, var(--primary) ${Math.round(6 + intensity * 30)}%, transparent)` } : undefined}>
                        {v == null ? <span className="muted">·</span> : fmt(v)}
                      </td>
                    );
                  })}
                  {config.colKey && <td className="text-right num pivot-total">{fmt(pivot.rowTotal(r.key))}</td>}
                </tr>
              ))}
            </tbody>
            <tfoot>
              <tr>
                <th scope="row">Total</th>
                {pivot.colKeys.map((c) => <td key={c.key} className="text-right num">{fmt(pivot.colTotal(c.key))}</td>)}
                {config.colKey && <td className="text-right num pivot-total">{fmt(pivot.grandTotal)}</td>}
              </tr>
            </tfoot>
          </table>
        </div>
      )}
      {pivot && pivot.truncatedColumns > 0 && (
        <p className="small muted" style={{ padding: '8px 18px' }}>{pivot.truncatedColumns} more column value(s) hidden — pick a lower-cardinality column field.</p>
      )}
    </div>
  );
}
