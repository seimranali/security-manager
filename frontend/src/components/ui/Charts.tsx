import type { ReactNode } from 'react';
import type { TooltipProps } from 'recharts';

/** Legend rendered in HTML so text uses text tokens; the swatch carries identity. */
export function ChartLegend({ items }: { items: { label: string; color: string }[] }) {
  return (
    <div className="legend">
      {items.map((i) => (
        <span key={i.label} className="legend-item">
          <span className="legend-swatch" style={{ background: i.color }} />
          {i.label}
        </span>
      ))}
    </div>
  );
}

/** Shared tooltip for all Recharts charts. */
export function ChartTooltip({ active, payload, label, labelFormatter }: TooltipProps<number, string> & { labelFormatter?: (l: string) => ReactNode }) {
  if (!active || !payload?.length) return null;
  return (
    <div className="chart-tooltip">
      <div className="t-title">{labelFormatter ? labelFormatter(String(label)) : label}</div>
      {payload.map((p) => (
        <div key={String(p.dataKey)} className="t-row">
          <span className="legend-swatch" style={{ background: p.color }} />
          {p.name}
          <b>{p.value}</b>
        </div>
      ))}
    </div>
  );
}

export function RiskMeter({ score }: { score?: number | null }) {
  if (score == null) return <span className="muted">Not scanned</span>;
  const color = score >= 60 ? 'var(--sev-critical)' : score >= 30 ? 'var(--sev-high)' : score >= 10 ? 'var(--sev-medium)' : 'var(--success)';
  return (
    <div className="risk-meter" title={`Risk score ${score}/100`}>
      <div className="progress"><span style={{ width: `${score}%`, background: color }} /></div>
      <b style={{ fontVariantNumeric: 'tabular-nums', minWidth: 26 }}>{score}</b>
    </div>
  );
}
