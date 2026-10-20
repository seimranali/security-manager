import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import { ArrowDownLeft, ArrowUpRight, Pencil, Plus, Trash2 } from 'lucide-react';
import { GRID_LIMIT, firewallApi } from '../../lib/api';
import { timeAgo, toDate } from '../../lib/format';
import type { FirewallRule, RuleInput } from '../../lib/types';
import { useApiMutation } from '../../hooks/useApiMutation';
import { Card, PageHeader } from '../../components/ui/Card';
import { EnforcementStatus } from '../../components/EnforcementStatus';
import { Button } from '../../components/ui/Button';
import { StatusBadge } from '../../components/ui/Badge';
import { Field, Input, Select, Switch, Textarea } from '../../components/ui/Form';
import { ConfirmDialog, Modal } from '../../components/ui/Modal';
import { DataTable, type Column } from '../../components/ui/DataTable';

const KEY = [['firewall']];
const empty = (policyId: string): RuleInput => ({
  policyId, name: '', description: '', priority: 100, action: 'Deny', direction: 'Inbound', protocol: 'Tcp',
  sourceAddress: 'any', destinationAddress: 'any', sourcePort: 'any', destinationPort: 'any', isEnabled: true,
});

export default function RulesPage() {
  const [params, setParams] = useSearchParams();
  const policyId = params.get('policyId') ?? '';

  const policies = useQuery({ queryKey: ['firewall', 'policies'], queryFn: firewallApi.policies });
  const rules = useQuery({
    queryKey: ['firewall', 'rules', policyId],
    queryFn: () => firewallApi.rules({ policyId, page: 1, pageSize: GRID_LIMIT }),
    placeholderData: keepPreviousData,
  });

  const [editing, setEditing] = useState<{ id?: string; form: RuleInput } | null>(null);
  const [deleting, setDeleting] = useState<FirewallRule | null>(null);

  const save = useApiMutation(
    (e: { id?: string; form: RuleInput }) => (e.id ? firewallApi.updateRule(e.id, e.form) : firewallApi.createRule(e.form)),
    { invalidate: KEY, success: 'Rule saved', onSuccess: () => setEditing(null) },
  );
  const toggle = useApiMutation((r: FirewallRule) => firewallApi.setRuleEnabled(r.id, !r.isEnabled), { invalidate: KEY });
  const remove = useApiMutation((id: string) => firewallApi.deleteRule(id), { invalidate: KEY, success: 'Rule deleted', onSuccess: () => setDeleting(null) });

  const set = <K extends keyof RuleInput>(k: K, v: RuleInput[K]) => setEditing((e) => e && { ...e, form: { ...e.form, [k]: v } });
  // Policies grouped by the traffic direction they govern, with priority and status as context.
  const policyOptions = (policies.data ?? []).map((p) => ({
    value: p.id,
    label: p.name,
    group: `${p.direction} policies`,
    description: `Priority ${p.priority} · default ${p.defaultAction}${p.isEnabled ? '' : ' · disabled'}`,
    meta: `${p.ruleCount} rules`,
  }));
  const policyPriority = new Map((policies.data ?? []).map((p) => [p.id, p.priority]));

  const columns: Column<FirewallRule>[] = [
    { key: 'on', header: 'On', headerText: 'Enabled', width: 60, value: (r) => (r.isEnabled ? 'Enabled' : 'Disabled'),
      render: (r) => <Switch checked={r.isEnabled} ariaLabel="Enable rule" onChange={() => toggle.mutate(r)} /> },
    // Policy cells merge across consecutive rules of the same policy.
    { key: 'policy', header: 'Policy', value: (r) => r.policyName, sortValue: (r) => policyPriority.get(r.policyId) ?? 0, mergeRepeated: true,
      render: (r) => <span className="cell-title rule-policy">{r.policyName}</span> },
    { key: 'priority', header: 'Priority', align: 'right', width: 80, value: (r) => r.priority, render: (r) => <span className="rule-prio">{r.priority}</span> },
    { key: 'name', header: 'Rule', className: 'rule-name', value: (r) => r.name,
      render: (r) => <><div className="cell-title">{r.name}</div>{r.description && <div className="cell-sub clamp-2" title={r.description}>{r.description}</div>}</> },
    { key: 'action', header: 'Action', className: 'nowrap', value: (r) => r.action, groupCell: true, render: (r) => <StatusBadge status={r.action} /> },
    { key: 'direction', header: 'Direction', className: 'nowrap', value: (r) => r.direction,
      render: (r) => <span className="rule-dir">{r.direction === 'Outbound' ? <ArrowUpRight size={14} /> : <ArrowDownLeft size={14} />}{r.direction}</span> },
    { key: 'protocol', header: 'Protocol', className: 'nowrap', frozen: true, value: (r) => r.protocol.toUpperCase(), render: (r) => <span className="proto-tag">{r.protocol.toUpperCase()}</span> },
    { key: 'srcAddr', header: 'Address', headerText: 'Source address', group: 'Source', className: 'nowrap', value: (r) => r.sourceAddress, render: (r) => <Endpoint value={r.sourceAddress} /> },
    { key: 'srcPort', header: 'Port', headerText: 'Source port', group: 'Source', className: 'nowrap', value: (r) => r.sourcePort, hidden: true, render: (r) => <Endpoint value={r.sourcePort} /> },
    { key: 'dstAddr', header: 'Address', headerText: 'Destination address', group: 'Destination', className: 'nowrap', value: (r) => r.destinationAddress, render: (r) => <Endpoint value={r.destinationAddress} /> },
    { key: 'dstPort', header: 'Port', headerText: 'Destination port', group: 'Destination', className: 'nowrap', value: (r) => r.destinationPort, render: (r) => <Endpoint value={r.destinationPort} /> },
    { key: 'hits', header: 'Hits', align: 'right', width: 70, value: (r) => r.hitCount, aggregate: 'sum', footer: 'sum' },
    { key: 'lastHit', header: 'Last hit', value: (r) => toDate(r.lastHitAt), render: (r) => <span className="small nowrap">{timeAgo(r.lastHitAt)}</span> },
    { key: 'actions', header: '', headerText: 'Actions', hideable: false, className: 'text-right nowrap', render: (r) => (
      <>
        <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={15} />} aria-label="Edit"
          onClick={() => setEditing({ id: r.id, form: { ...r, description: r.description ?? '' } })} />
        <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={15} />} aria-label="Delete" onClick={() => setDeleting(r)} />
      </>
    ) },
  ];

  return (
    <>
      <PageHeader
        title="Firewall Rules"
        description="Addresses accept IPs, CIDR (10.0.0.0/8), ranges (a-b) or lists. Ports accept 443, 80,443 or 1000-2000."
        actions={<Button variant="primary" icon={<Plus size={16} />} disabled={!policyOptions.length}
          onClick={() => setEditing({ form: empty(policyId || policyOptions[0]?.value || '') })}>New rule</Button>}
      />
      <EnforcementStatus />
      <Card>
        <DataTable
          columns={columns} rows={rules.data?.items} totalCount={rules.data?.totalCount} rowKey={(r) => r.id} loading={rules.isLoading}
          initialSort={[{ key: 'policy', dir: 'asc' }, { key: 'priority', dir: 'asc' }]}
          exportName="firewall-rules" storageKey="firewall-rules"
          toolbar={<Select options={policyOptions} placeholder="All policies" value={policyId} aria-label="Policy"
            onChange={(e) => setParams(e.target.value ? { policyId: e.target.value } : {})} />}
          empty={{ title: 'No rules found', description: 'Create a rule or choose another policy.' }}
        />
      </Card>

      <Modal
        open={!!editing} size="lg" onClose={() => setEditing(null)}
        title={editing?.id ? 'Edit rule' : 'New rule'}
        onSubmit={() => editing && save.mutate(editing)}
        footer={<><Button onClick={() => setEditing(null)}>Cancel</Button><Button type="submit" variant="primary" loading={save.isPending}>Save rule</Button></>}
      >
        {editing && (
          <div className="form-grid">
            <Field label="Name"><Input required minLength={2} maxLength={100} value={editing.form.name} onChange={(e) => set('name', e.target.value)} /></Field>
            <Field label="Policy"><Select required options={policyOptions} value={editing.form.policyId} onChange={(e) => set('policyId', e.target.value)} /></Field>
            <Field label="Action"><Select options={['Deny', 'Allow']} value={editing.form.action} onChange={(e) => set('action', e.target.value as RuleInput['action'])} /></Field>
            <Field label="Priority" hint="Lower runs first within the policy"><Input type="number" min={1} max={10000} value={editing.form.priority} onChange={(e) => set('priority', Number(e.target.value))} /></Field>
            <Field label="Direction"><Select options={['Inbound', 'Outbound', 'Any']} value={editing.form.direction} onChange={(e) => set('direction', e.target.value as RuleInput['direction'])} /></Field>
            <Field label="Protocol"><Select options={[{ value: 'Any', label: 'Any' }, { value: 'Tcp', label: 'TCP' }, { value: 'Udp', label: 'UDP' }, { value: 'Icmp', label: 'ICMP' }]} value={editing.form.protocol} onChange={(e) => set('protocol', e.target.value as RuleInput['protocol'])} /></Field>
            <Field label="Source address"><Input required className="mono" value={editing.form.sourceAddress} onChange={(e) => set('sourceAddress', e.target.value)} /></Field>
            <Field label="Source port"><Input required className="mono" value={editing.form.sourcePort} onChange={(e) => set('sourcePort', e.target.value)} /></Field>
            <Field label="Destination address"><Input required className="mono" value={editing.form.destinationAddress} onChange={(e) => set('destinationAddress', e.target.value)} /></Field>
            <Field label="Destination port"><Input required className="mono" value={editing.form.destinationPort} onChange={(e) => set('destinationPort', e.target.value)} /></Field>
            <Field label="Description" full><Textarea maxLength={500} value={editing.form.description} onChange={(e) => set('description', e.target.value)} /></Field>
            <Field label="Status"><Switch checked={editing.form.isEnabled} onChange={(v) => set('isEnabled', v)} label={editing.form.isEnabled ? 'Enabled' : 'Disabled'} /></Field>
          </div>
        )}
      </Modal>

      <ConfirmDialog open={!!deleting} title="Delete rule" message={<>Delete rule <b>{deleting?.name}</b>?</>}
        loading={remove.isPending} onConfirm={() => deleting && remove.mutate(deleting.id)} onClose={() => setDeleting(null)} />
    </>
  );
}

/** Address/port cell: "any" is shown muted so the specific values stand out. */
function Endpoint({ value }: { value: string }) {
  return !value || value.toLowerCase() === 'any' ? <span className="any-tag">any</span> : <code className="endpoint">{value}</code>;
}
