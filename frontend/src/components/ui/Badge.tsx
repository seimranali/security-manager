import type { ReactNode } from 'react';
import { cn, splitCamel } from '../../lib/format';
import type { Severity } from '../../lib/types';

type Tone = 'neutral' | 'success' | 'danger' | 'warning' | 'primary' | 'critical' | 'high' | 'medium' | 'low' | 'info';

export function Badge({ tone = 'neutral', dot, children }: { tone?: Tone; dot?: boolean; children: ReactNode }) {
  return (
    <span className={cn('badge', tone !== 'neutral' && `badge-${tone}`)}>
      {dot && <span className="dot" />}
      {children}
    </span>
  );
}

/** Severity is always shown as colour + text label (never colour alone). */
export function SeverityBadge({ severity }: { severity: Severity }) {
  return <Badge tone={severity.toLowerCase() as Tone} dot>{severity}</Badge>;
}

const STATUS_TONES: Record<string, Tone> = {
  // generic
  Enabled: 'success', Disabled: 'neutral',
  // firewall
  Allow: 'success', Deny: 'danger',
  // detections
  New: 'danger', Investigating: 'warning', Resolved: 'success', FalsePositive: 'neutral',
  // scans
  Queued: 'neutral', Running: 'primary', Completed: 'success', Failed: 'danger', Cancelled: 'neutral',
  // findings / alerts
  Open: 'danger', Accepted: 'neutral', Acknowledged: 'warning',
  Healthy: 'success', Unhealthy: 'warning', Unreachable: 'danger',
};

export function StatusBadge({ status }: { status: string }) {
  return <Badge tone={STATUS_TONES[status] ?? 'neutral'} dot>{splitCamel(status)}</Badge>;
}
