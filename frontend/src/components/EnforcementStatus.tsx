import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { CheckCircle2, ChevronDown, CircleAlert, Loader2, ShieldAlert } from 'lucide-react';
import { firewallApi } from '../lib/api';
import { timeAgo } from '../lib/format';
import type { EnforcementAgent } from '../lib/types';

const isAccessDenied = (a: EnforcementAgent) => a.errors.some((e) => /access denied|permission denied|administrator|CAP_NET_ADMIN/i.test(e));

/**
 * One-line answer to "are my rules actually blocking traffic on this server?". Enforcement is done by the Firewall
 * service itself (built-in) or, on other machines, by an optional agent; both report here.
 */
export function EnforcementStatus() {
  const [open, setOpen] = useState(false);
  const { data, isLoading } = useQuery({ queryKey: ['enforcement'], queryFn: firewallApi.enforcement, refetchInterval: 10_000 });

  if (isLoading || !data) {
    return <div className="enf-status"><Loader2 size={16} className="spin" /> Checking firewall enforcement…</div>;
  }

  const hosts = data.agents.filter((a) => a.isOnline);
  const problems = hosts.filter((a) => a.status !== 'InSync');
  const rules = data.desiredState.rules.length;
  const host = hosts.find((a) => a.agentId.startsWith('local:')) ?? hosts[0];

  let tone: 'ok' | 'warn' | 'bad';
  let title: string;
  let detail: string;
  if (!data.settings.enabled) {
    tone = 'warn'; title = 'Enforcement is switched off'; detail = 'Rules are saved but not applied to any firewall.';
  } else if (!host) {
    tone = 'bad'; title = 'Rules are not being enforced';
    detail = 'The Firewall service is not applying rules to Windows Firewall. Install the services once as Administrator: .\\Install-Services.ps1';
  } else if (problems.length && problems.every(isAccessDenied)) {
    tone = 'bad'; title = 'Rules saved but not enforced: administrator rights needed';
    detail = 'Changing Windows Firewall needs admin rights. In C:\\Security Manager run, as Administrator: .\\Install-Services.ps1';
  } else if (problems.length) {
    tone = 'warn'; title = `Enforcement problem on ${problems.map((p) => p.hostname).join(', ')}`;
    detail = problems[0].errors[0] ?? problems[0].warnings[0] ?? `Status: ${problems[0].status}`;
  } else {
    tone = 'ok';
    title = hosts.length === 1 ? `Enforced on ${host.hostname}` : `Enforced on ${hosts.length} hosts`;
    detail = `${rules} firewall rule${rules === 1 ? '' : 's'} active in ${/linux/i.test(host.operatingSystem) ? 'nftables' : 'Windows Firewall'} · checked ${timeAgo(host.lastSeenAt)}`;
  }
  const warnings = [...new Set([...data.desiredState.warnings, ...hosts.flatMap((a) => a.warnings)])];

  return (
    <div className={`enf-status enf-${tone}`} role="status">
      {tone === 'ok' ? <CheckCircle2 size={18} /> : tone === 'warn' ? <CircleAlert size={18} /> : <ShieldAlert size={18} />}
      <div className="enf-text">
        <b>{title}</b>
        <span>{detail}</span>
        {open && warnings.length > 0 && <ul className="enf-warnings">{warnings.map((w) => <li key={w}>{w}</li>)}</ul>}
      </div>
      {warnings.length > 0 && (
        <button type="button" className="enf-more" aria-expanded={open} onClick={() => setOpen((o) => !o)}>
          {warnings.length} note{warnings.length === 1 ? '' : 's'} <ChevronDown size={14} className={open ? 'open' : ''} />
        </button>
      )}
    </div>
  );
}
