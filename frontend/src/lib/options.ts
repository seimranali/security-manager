import type { DropdownOption, LoadOptionsArgs, LoadOptionsResult } from '../components/ui/Dropdown';
import { securityApi } from './api';
import { splitCamel } from './format';
import type { SecurityEventType, Severity, UserRole } from './types';

/** Reusable option catalogs for dropdowns. */

const SEVERITY_META: Record<Severity, { color: string; description: string }> = {
  Critical: { color: 'var(--sev-critical)', description: 'Immediate action required' },
  High: { color: 'var(--sev-high)', description: 'Serious; address promptly' },
  Medium: { color: 'var(--sev-medium)', description: 'Moderate risk' },
  Low: { color: 'var(--sev-low)', description: 'Minor issue' },
  Info: { color: 'var(--sev-info)', description: 'Informational' },
};

export const severityOptions = (suffix = ''): DropdownOption[] =>
  (['Critical', 'High', 'Medium', 'Low', 'Info'] as Severity[]).map((s) => ({
    value: s, label: `${s}${suffix}`, color: SEVERITY_META[s].color, description: SEVERITY_META[s].description,
  }));

const EVENT_TYPE_META: Record<SecurityEventType, { group: string; description: string }> = {
  AuthenticationFailure: { group: 'Identity', description: 'Failed sign-in attempt' },
  AuthenticationSuccess: { group: 'Identity', description: 'Successful sign-in' },
  PrivilegeEscalation: { group: 'Identity', description: 'Unexpected elevation of rights' },
  FirewallBlock: { group: 'Network', description: 'Connection denied by a firewall rule' },
  PortScan: { group: 'Network', description: 'Reconnaissance / port probing' },
  DataExfiltration: { group: 'Network', description: 'Unusual outbound data transfer' },
  MalwareDetected: { group: 'Endpoint', description: 'Malware found by endpoint protection' },
  SuspiciousProcess: { group: 'Endpoint', description: 'Suspicious process or script' },
  ConfigurationChange: { group: 'Configuration', description: 'Security setting modified' },
  Other: { group: 'Configuration', description: 'Uncategorised event' },
};

export const EVENT_GROUP_ORDER = ['Identity', 'Network', 'Endpoint', 'Configuration'];

export const eventTypeOptions = (): DropdownOption[] =>
  (Object.keys(EVENT_TYPE_META) as SecurityEventType[]).map((t) => ({
    value: t, label: splitCamel(t), group: EVENT_TYPE_META[t].group, description: EVENT_TYPE_META[t].description,
  }));

const ROLE_META: Record<UserRole, string> = {
  Admin: 'Full access, including user management',
  Analyst: 'View and change security configuration',
  Viewer: 'Read-only access',
};

export const roleOptions = (): DropdownOption[] =>
  (Object.keys(ROLE_META) as UserRole[]).map((r) => ({ value: r, label: r, description: ROLE_META[r] }));

export type EventLookupField = 'sourceIp' | 'userName' | 'targetHost' | 'source';

/** Dynamic, paged, searchable distinct values from the Security service (most frequent first). */
export const eventLookup = (field: EventLookupField) =>
  async ({ search, page, signal }: LoadOptionsArgs): Promise<LoadOptionsResult> => {
    const result = await securityApi.lookup(field, { search, page, pageSize: 30 }, signal);
    return {
      options: result.items.map((i) => ({ value: i.value, label: i.value, meta: `${i.count.toLocaleString()} events` })),
      hasMore: page < result.totalPages,
    };
  };
