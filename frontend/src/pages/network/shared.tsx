import { AlertCircle, AlertTriangle, CheckCircle2, Info } from 'lucide-react';
import type { ConfigIssue, InterfaceZone, NetGateway } from '../../lib/types';
import { Badge } from '../../components/ui/Badge';
import { timeAgo } from '../../lib/format';

export const NETWORK_KEY = [['network']];

export const ZONES: { value: InterfaceZone; label: string; tone: 'danger' | 'success' | 'warning' | 'primary' | 'neutral' }[] = [
  { value: 'Wan', label: 'WAN', tone: 'danger' },
  { value: 'Lan', label: 'LAN', tone: 'success' },
  { value: 'Dmz', label: 'DMZ', tone: 'warning' },
  { value: 'Guest', label: 'Guest', tone: 'primary' },
  { value: 'Management', label: 'Management', tone: 'neutral' },
  { value: 'Other', label: 'Other', tone: 'neutral' },
];

export function ZoneBadge({ zone }: { zone: InterfaceZone }) {
  const z = ZONES.find((x) => x.value === zone)!;
  return <Badge tone={z.tone}>{z.label}</Badge>;
}

/** Turns "" into undefined so optional fields are omitted from requests. */
export const opt = (v?: string | null) => (v && v.trim() ? v.trim() : undefined);
export const optNum = (v: string) => (v.trim() === '' ? undefined : Number(v));

export function DeployStatus({ g }: { g: NetGateway }) {
  if (!g.deployedRevision) return <Badge tone="warning" dot>Never deployed · r{g.configRevision}</Badge>;
  if (g.pendingChanges) return <Badge tone="warning" dot>Pending r{g.configRevision} (deployed r{g.deployedRevision})</Badge>;
  return <span title={`Deployed ${timeAgo(g.deployedAt)} by ${g.deployedBy ?? 'unknown'}`}><Badge tone="success" dot>Deployed r{g.deployedRevision}</Badge></span>;
}

const ICONS = {
  Error: <AlertCircle size={15} color="var(--danger)" />,
  Warning: <AlertTriangle size={15} color="var(--warning)" />,
  Info: <Info size={15} color="var(--primary)" />,
};

export function IssueList({ issues, compact }: { issues: ConfigIssue[]; compact?: boolean }) {
  if (issues.length === 0) {
    return <div className="callout callout-success row"><CheckCircle2 size={16} /> Configuration checks passed — no issues found.</div>;
  }
  const order = { Error: 0, Warning: 1, Info: 2 };
  return (
    <div className="issue-list">
      {[...issues].sort((a, b) => order[a.severity] - order[b.severity]).slice(0, compact ? 5 : undefined).map((i, idx) => (
        <div key={idx} className={`issue issue-${i.severity.toLowerCase()}`}>
          {ICONS[i.severity]}
          <span className="issue-area">{i.area}</span>
          <span>{i.message}</span>
        </div>
      ))}
      {compact && issues.length > 5 && <div className="muted small" style={{ padding: '4px 10px' }}>+{issues.length - 5} more</div>}
    </div>
  );
}
