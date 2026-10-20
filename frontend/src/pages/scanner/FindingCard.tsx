import { Lightbulb } from 'lucide-react';
import type { Finding } from '../../lib/types';
import { Badge, SeverityBadge } from '../../components/ui/Badge';

export function FindingCard({ finding: f }: { finding: Finding }) {
  return (
    <div className="card" style={{ padding: 14 }}>
      <div className="row-wrap" style={{ marginBottom: 6 }}>
        <SeverityBadge severity={f.severity} />
        <Badge>{f.category}</Badge>
        {f.port != null && <Badge>Port {f.port}</Badge>}
      </div>
      <div style={{ fontWeight: 600 }}>{f.title}</div>
      <p className="muted" style={{ margin: '4px 0 8px' }}>{f.description}</p>
      {f.evidence && <div className="evidence">{f.evidence}</div>}
      {f.recommendation && (
        <div className="row small" style={{ marginTop: 8, alignItems: 'flex-start' }}>
          <Lightbulb size={14} color="var(--warning)" style={{ marginTop: 2, flexShrink: 0 }} />
          <span>{f.recommendation}</span>
        </div>
      )}
    </div>
  );
}
