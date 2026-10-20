import type { AlertStats, AuthUser, EnforcementOverview, NotificationChannel, ScannerStats, SecurityStats, ThreatFeed } from './types';

export interface PostureFactor {
  key: string;
  label: string;
  /** Relative importance; factors without data are left out and the rest re-weighted. */
  weight: number;
  /** 0 (bad) … 1 (good). */
  score: number;
  detail: string;
  /** Where to fix it. */
  link: string;
}

export interface Posture {
  score: number;
  grade: 'A' | 'B' | 'C' | 'D' | 'F';
  factors: PostureFactor[];
}

export interface PostureInputs {
  enforcement?: EnforcementOverview;
  scanner?: ScannerStats;
  alerts?: AlertStats;
  security?: SecurityStats;
  /** Admin-only data: undefined when the viewer cannot read it. */
  users?: AuthUser[];
  feeds?: ThreatFeed[];
  channels?: NotificationChannel[];
}

const clamp = (n: number) => Math.max(0, Math.min(1, n));
const plural = (n: number, word: string) => `${n} ${word}${n === 1 ? '' : 's'}`;

/** Summarises security weaknesses into one 0–100 score with the factors behind it (worst first). */
export function computePosture(i: PostureInputs): Posture {
  const factors: PostureFactor[] = [];

  if (i.enforcement) {
    const agents = i.enforcement.agents;
    const online = agents.filter((a) => a.isOnline);
    const healthy = online.filter((a) => a.status === 'InSync' && a.profiles.every((p) => p.enabled));
    const firewallOff = online.filter((a) => a.profiles.some((p) => !p.enabled)).length;
    factors.push({
      key: 'enforcement', label: 'Firewall enforcement', weight: 20, link: '/firewall/rules',
      score: agents.length === 0 || !i.enforcement.settings.enabled ? 0 : healthy.length / agents.length,
      detail: !i.enforcement.settings.enabled ? 'Enforcement is switched off: rules exist only on paper.'
        : agents.length === 0 ? 'Rules are saved but not applied to the firewall (needs the services installed as Administrator).'
        : healthy.length === agents.length ? `${plural(agents.length, 'host')} enforcing, all in sync.`
        : `${healthy.length} of ${plural(agents.length, 'host')} healthy${firewallOff ? `; firewall off on ${firewallOff}` : ''}.`,
    });
  }

  if (i.scanner) {
    const bySeverity = Object.fromEntries(i.scanner.openFindingsBySeverity.map((s) => [s.severity, s.count]));
    const critical = bySeverity.Critical ?? 0;
    const high = bySeverity.High ?? 0;
    factors.push({
      key: 'vulnerabilities', label: 'Vulnerabilities', weight: 20, link: '/scanner/findings',
      score: i.scanner.targets === 0 ? 0.5 : critical > 0 ? 0 : clamp(1 - high * 0.2),
      detail: i.scanner.targets === 0 ? 'No scan targets: exposure is unknown.'
        : critical + high === 0 ? 'No open critical or high findings.'
        : `${plural(critical, 'critical')} and ${high} high open finding${high === 1 ? '' : 's'}.`,
    });
  }

  if (i.alerts) {
    const critical = i.alerts.criticalActive;
    factors.push({
      key: 'alerts', label: 'Alert backlog', weight: 15, link: '/alerts',
      score: critical > 0 ? 0 : clamp(1 - i.alerts.open / 20),
      detail: critical > 0 ? `${plural(critical, 'critical alert')} still active.`
        : i.alerts.open === 0 ? 'No unacknowledged alerts.' : `${plural(i.alerts.open, 'unacknowledged alert')}.`,
    });
  }

  if (i.security) {
    const critical = i.security.criticalOpenDetections;
    factors.push({
      key: 'detections', label: 'Open detections', weight: 10, link: '/security/detections',
      score: critical > 0 ? 0 : clamp(1 - i.security.openDetections / 10),
      detail: critical > 0 ? `${plural(critical, 'critical detection')} under investigation.`
        : i.security.openDetections === 0 ? 'No open detections.' : `${plural(i.security.openDetections, 'open detection')}.`,
    });
  }

  if (i.users) {
    const active = i.users.filter((u) => u.isActive);
    const withMfa = active.filter((u) => u.mfaEnabled).length;
    const adminsWithout = active.filter((u) => u.role === 'Admin' && !u.mfaEnabled).length;
    factors.push({
      key: 'mfa', label: 'MFA adoption', weight: 15, link: '/admin/users',
      score: active.length === 0 ? 1 : adminsWithout > 0 ? Math.min(0.5, withMfa / active.length) : withMfa / active.length,
      detail: `${withMfa} of ${plural(active.length, 'account')} use two-step verification${adminsWithout ? `; ${plural(adminsWithout, 'administrator')} without it` : ''}.`,
    });
  }

  if (i.feeds) {
    const working = i.feeds.filter((f) => f.isEnabled && f.listType === 'Block' && f.kind === 'ThreatIntel' && !f.lastError && f.entryCount > 0);
    factors.push({
      key: 'feeds', label: 'Threat intelligence', weight: 10, link: '/firewall/feeds',
      score: working.length > 0 ? 1 : 0,
      detail: working.length > 0 ? `${plural(working.length, 'threat feed')} blocking known-bad addresses.` : 'No working threat-intelligence feed.',
    });
  }

  if (i.channels) {
    const working = i.channels.filter((c) => c.isEnabled && !c.lastError);
    factors.push({
      key: 'notifications', label: 'Alert notifications', weight: 10, link: '/admin/notifications',
      score: working.length > 0 ? 1 : 0,
      detail: working.length > 0 ? `${plural(working.length, 'notification channel')} delivering alerts.` : 'Nobody is notified when a critical alert fires.',
    });
  }

  const totalWeight = factors.reduce((n, f) => n + f.weight, 0);
  const score = totalWeight === 0 ? 0 : Math.round((factors.reduce((n, f) => n + f.weight * f.score, 0) / totalWeight) * 100);
  const grade = score >= 90 ? 'A' : score >= 75 ? 'B' : score >= 60 ? 'C' : score >= 40 ? 'D' : 'F';
  factors.sort((a, b) => a.score - b.score || b.weight - a.weight);
  return { score, grade, factors };
}
