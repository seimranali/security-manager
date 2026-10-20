import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { AlertTriangle, CheckCircle2, CircleOff, Clock, MonitorCheck, MonitorX, ShieldCheck, Trash2 } from 'lucide-react';
import { firewallApi } from '../../lib/api';
import { timeAgo } from '../../lib/format';
import type { EnforcedRule, EnforcementAgent, EnforcementSettings } from '../../lib/types';
import { useApiMutation } from '../../hooks/useApiMutation';
import { useAuth } from '../../hooks/useAuth';
import { Card, CardBody, CardHeader, PageHeader, StatCard } from '../../components/ui/Card';
import { Badge } from '../../components/ui/Badge';
import { Button } from '../../components/ui/Button';
import { Switch } from '../../components/ui/Form';
import { DataTable, EmptyState, Spinner, type Column } from '../../components/ui/DataTable';

const KEY = [['firewall']];

function AgentStatus({ a }: { a: EnforcementAgent }) {
  if (!a.isOnline) return <Badge tone="danger" dot>Offline · {timeAgo(a.lastSeenAt)}</Badge>;
  if (a.status === 'Error') return <Badge tone="danger" dot>Error</Badge>;
  if (a.mode === 'Audit') return <Badge tone="warning" dot>Audit only</Badge>;
  if (a.status === 'Disabled') return <Badge dot>Enforcement off</Badge>;
  if (!a.inSync) return <Badge tone="warning" dot>Syncing…</Badge>;
  return <Badge tone="success" dot>In sync</Badge>;
}

export default function EnforcementPage() {
  const { hasRole } = useAuth();
  const isAdmin = hasRole('Admin');
  const overview = useQuery({ queryKey: ['firewall', 'enforcement'], queryFn: firewallApi.enforcement, refetchInterval: 5000 });
  const save = useApiMutation((s: Omit<EnforcementSettings, 'updatedAt'>) => firewallApi.updateEnforcementSettings(s), {
    invalidate: KEY, success: 'Enforcement settings saved — agents apply them within seconds',
  });
  const removeAgent = useApiMutation((id: string) => firewallApi.deleteAgent(id), { invalidate: KEY, success: 'Agent removed from the list' });

  if (!overview.data) return overview.isLoading ? <Spinner /> : <div className="callout callout-danger">Could not load enforcement status.</div>;
  const { settings, desiredState: desired, agents, enforcedPolicies } = overview.data;
  const online = agents.filter((a) => a.isOnline);
  const update = (patch: Partial<EnforcementSettings>) =>
    save.mutate({ enabled: settings.enabled, enforceBlocklist: settings.enforceBlocklist, enforceAllowlist: settings.enforceAllowlist, ...patch });

  const ruleColumns: Column<EnforcedRule>[] = [
    { key: 'action', header: 'Action', value: (r) => r.action, groupCell: true,
      render: (r) => <Badge tone={r.action === 'Block' ? 'danger' : 'success'}>{r.action}</Badge> },
    { key: 'dir', header: 'Direction', value: (r) => (r.direction === 'In' ? 'Inbound' : 'Outbound') },
    { key: 'proto', header: 'Protocol', value: (r) => r.protocol.toUpperCase() },
    { key: 'ports', header: 'Local ports', value: (r) => (r.localPorts === '*' ? 'any' : r.localPorts), render: (r) => <code>{r.localPorts === '*' ? 'any' : r.localPorts}</code> },
    { key: 'remote', header: 'Remote addresses', value: (r) => r.remoteAddresses, groupable: false,
      render: (r) => <code className="truncate" style={{ display: 'block', maxWidth: 360 }} title={r.remoteAddresses}>{r.remoteAddresses === '*' ? 'any' : r.remoteAddresses}</code> },
    { key: 'origin', header: 'From', value: (r) => r.origin, mergeRepeated: true },
    { key: 'name', header: 'Windows rule name', value: (r) => r.key, hidden: true, render: (r) => <code>{r.key}</code> },
  ];

  const agentColumns: Column<EnforcementAgent>[] = [
    { key: 'host', header: 'Host', value: (a) => a.hostname, render: (a) => <><div className="cell-title">{a.hostname}</div><div className="cell-sub">{a.operatingSystem}</div></> },
    { key: 'status', header: 'Status', value: (a) => (a.isOnline ? a.status : 'Offline'), render: (a) => <AgentStatus a={a} /> },
    { key: 'rules', header: 'Managed rules', align: 'right', value: (a) => a.managedRuleCount },
    { key: 'profiles', header: 'Windows Firewall', value: (a) => (a.profiles.every((p) => p.enabled) ? 'On' : 'Partly off'),
      render: (a) => <span className="row-wrap" style={{ gap: 4 }}>{a.profiles.map((p) => <Badge key={p.name} tone={p.enabled ? 'success' : 'danger'}>{p.name} {p.enabled ? 'on' : 'OFF'}</Badge>)}</span> },
    { key: 'seen', header: 'Last report', value: (a) => new Date(a.lastSeenAt), filter: false, render: (a) => <span className="small nowrap">{timeAgo(a.lastSeenAt)}</span> },
    { key: 'change', header: 'Last change', value: (a) => (a.lastChangeAt ? new Date(a.lastChangeAt) : null), filter: false, render: (a) => <span className="small nowrap">{timeAgo(a.lastChangeAt)}</span> },
    { key: 'version', header: 'Version', value: (a) => a.agentVersion, hidden: true },
    { key: 'actions', header: '', headerText: 'Actions', hideable: false, className: 'text-right', render: (a) => isAdmin && !a.isOnline && (
      <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={15} />} aria-label="Remove agent" title="Remove offline agent from the list" onClick={() => removeAgent.mutate(a.id)} />
    ) },
  ];

  const agentWarnings = agents.flatMap((a) => [...a.errors.map((e) => ({ a, text: e, error: true })), ...a.warnings.filter((w) => !desired.warnings.includes(w)).map((w) => ({ a, text: w, error: false }))]);

  return (
    <div className="stack">
      <PageHeader
        title="Host Enforcement"
        description="The Security Manager agent turns firewall policies and IP lists into real Windows Firewall rules on managed hosts. Changes made here are applied within about 10 seconds."
      />

      {agents.length === 0 && (
        <div className="callout row" style={{ alignItems: 'flex-start' }}>
          <MonitorX size={18} style={{ flexShrink: 0, marginTop: 2 }} />
          <span>No agent has reported yet, so nothing is being enforced on any host. Install the agent from an elevated PowerShell in the deployment folder:
            <code style={{ display: 'block', marginTop: 6 }}>.\Install-Services.ps1 -Components Agent</code></span>
        </div>
      )}

      <div className="grid grid-kpi">
        <StatCard label="Agents online" value={`${online.length}/${agents.length}`} icon={<MonitorCheck size={20} />} tone={online.length === agents.length && agents.length > 0 ? 'success' : 'warning'} />
        <StatCard label="In sync" value={agents.filter((a) => a.isOnline && a.inSync).length} icon={<CheckCircle2 size={20} />} tone="success" hint={`Desired version ${desired.version}`} />
        <StatCard label="Rules to enforce" value={desired.rules.length} icon={<ShieldCheck size={20} />} hint={`${enforcedPolicies.length} polic${enforcedPolicies.length === 1 ? 'y' : 'ies'} + IP lists`} />
        <StatCard label="Warnings" value={desired.warnings.length + agentWarnings.length} icon={<AlertTriangle size={20} />} tone={desired.warnings.length + agentWarnings.length ? 'warning' : 'success'} />
      </div>

      <div className="grid grid-2">
        <Card>
          <CardHeader title="What is enforced" subtitle={isAdmin ? 'Only administrators can change these switches.' : 'Read-only: ask an administrator to change these.'} />
          <CardBody>
            <div className="stack">
              <Switch checked={settings.enabled} disabled={!isAdmin || save.isPending} onChange={(v) => update({ enabled: v })}
                label={<span><b>Host enforcement</b><span className="muted small" style={{ display: 'block' }}>Master switch. When off, agents remove every rule they created.</span></span>} />
              <Switch checked={settings.enforceBlocklist} disabled={!isAdmin || !settings.enabled || save.isPending} onChange={(v) => update({ enforceBlocklist: v })}
                label={<span><b>IP blocklist</b><span className="muted small" style={{ display: 'block' }}>Block listed addresses in both directions (allowlisted addresses are always exempt).</span></span>} />
              <Switch checked={settings.enforceAllowlist} disabled={!isAdmin || !settings.enabled || save.isPending} onChange={(v) => update({ enforceAllowlist: v })}
                label={<span><b>IP allowlist</b><span className="muted small" style={{ display: 'block' }}>Open <u>all</u> inbound ports to allowlisted addresses. Leave off unless those hosts need full access.</span></span>} />
              <div>
                <div className="section-title">Policies enforced on hosts</div>
                {enforcedPolicies.length === 0
                  ? <span className="muted small">None. Turn on “Enforce on Windows hosts” for a policy on the <Link to="/firewall/policies">Policies</Link> page.</span>
                  : <div className="row-wrap">{enforcedPolicies.map((p) => <Badge key={p} tone="primary">{p}</Badge>)}</div>}
              </div>
            </div>
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Warnings" subtitle="Situations where a host cannot behave exactly as Security Manager decides." />
          <CardBody>
            {desired.warnings.length + agentWarnings.length === 0 ? (
              <div className="callout callout-success row"><CheckCircle2 size={16} /> No warnings.</div>
            ) : (
              <div className="issue-list">
                {agentWarnings.map((w, i) => (
                  <div key={`a${i}`} className={`issue ${w.error ? 'issue-error' : 'issue-warning'}`}>
                    {w.error ? <CircleOff size={15} color="var(--danger)" /> : <AlertTriangle size={15} color="var(--warning)" />}
                    <span className="issue-area">{w.a.hostname}</span><span>{w.text}</span>
                  </div>
                ))}
                {desired.warnings.map((w, i) => (
                  <div key={`d${i}`} className="issue issue-warning">
                    <AlertTriangle size={15} color="var(--warning)" /><span className="issue-area">Policy</span><span>{w}</span>
                  </div>
                ))}
              </div>
            )}
          </CardBody>
        </Card>
      </div>

      <Card>
        <CardHeader title="Agents" subtitle="Hosts running the Security Manager agent. Each reports every ~10 seconds." />
        <div style={{ marginTop: 12 }}>
          <DataTable columns={agentColumns} rows={agents} rowKey={(a) => a.id} pivot={false} exportName="agents" storageKey="enforcement-agents"
            empty={{ title: 'No agents', description: 'Install the agent on a Windows host to enforce rules there.' }} />
        </div>
      </Card>

      <Card>
        <CardHeader title="Rules applied to Windows Firewall" subtitle={<span className="row" style={{ gap: 6 }}><Clock size={13} /> Desired state {desired.version}, generated {timeAgo(desired.generatedAt)} · rule group “Security Manager (managed)”</span>} />
        <div style={{ marginTop: 12 }}>
          {!desired.enabled ? <EmptyState icon={<CircleOff size={22} />} title="Enforcement is off" description="Agents keep no Security Manager rules on hosts." /> : (
            <DataTable columns={ruleColumns} rows={desired.rules} rowKey={(r) => r.key} exportName="enforced-rules" storageKey="enforced-rules"
              initialSort={[{ key: 'action', dir: 'asc' }]} empty={{ title: 'Nothing to enforce', description: 'Enable enforcement for a policy or add IP blocklist entries.' }} />
          )}
        </div>
      </Card>
    </div>
  );
}
