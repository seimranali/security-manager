import { useState } from 'react';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { networkApi } from '../../../lib/api';
import type { GatewayDetail, NetRoute, RoutingPolicyItem } from '../../../lib/types';
import { useApiMutation } from '../../../hooks/useApiMutation';
import { Button } from '../../../components/ui/Button';
import { Badge, StatusBadge } from '../../../components/ui/Badge';
import { CardHeader } from '../../../components/ui/Card';
import { Field, Input, Select, Switch, Textarea } from '../../../components/ui/Form';
import { ConfirmDialog, Modal } from '../../../components/ui/Modal';
import { DataTable, type Column } from '../../../components/ui/DataTable';
import { NETWORK_KEY, opt } from '../shared';

const MAIN = 254;
const tableLabel = (t: number) => (t === MAIN ? 'main' : `table ${t}`);

interface RouteForm { destination: string; nextHop: string; interface: string; metric: string; table: string; isEnabled: boolean; description: string }
interface PolicyForm { priority: string; source: string; destination: string; inInterface: string; table: string; isEnabled: boolean; description: string }

export function RoutingTab({ gatewayId, detail, canWrite }: { gatewayId: string; detail: GatewayDetail; canWrite: boolean }) {
  const [route, setRoute] = useState<{ id?: string; form: RouteForm } | null>(null);
  const [policy, setPolicy] = useState<{ id?: string; form: PolicyForm } | null>(null);
  const [deleting, setDeleting] = useState<{ kind: 'route' | 'policy'; id: string; label: string } | null>(null);

  const interfaceOptions = [
    ...detail.interfaces.map((i) => ({ value: i.name, label: i.name, description: i.address ?? i.mode, group: 'Interfaces' })),
    ...detail.tunnels.filter((t) => t.interfaceName).map((t) => ({ value: t.interfaceName!, label: t.interfaceName!, description: `WireGuard ${t.name}`, group: 'VPN' })),
  ];
  const tables = [...new Set([MAIN, 100, ...detail.routes.map((r) => r.table), ...detail.policies.map((p) => p.table)])].sort((a, b) => a - b)
    .map((t) => ({ value: String(t), label: t === MAIN ? 'main (254)' : `table ${t}`, description: t === MAIN ? 'Default routing table' : 'Custom table for policy routing' }));

  const saveRoute = useApiMutation(
    (e: NonNullable<typeof route>) => {
      const f = e.form;
      const body = { destination: f.destination.trim(), nextHop: opt(f.nextHop), interface: opt(f.interface), metric: Number(f.metric), table: Number(f.table), isEnabled: f.isEnabled, description: opt(f.description) };
      return e.id ? networkApi.routes.update(gatewayId, e.id, body) : networkApi.routes.create(gatewayId, body);
    },
    { invalidate: NETWORK_KEY, success: 'Route saved', onSuccess: () => setRoute(null) },
  );
  const savePolicy = useApiMutation(
    (e: NonNullable<typeof policy>) => {
      const f = e.form;
      const body = { priority: Number(f.priority), source: opt(f.source), destination: opt(f.destination), inInterface: opt(f.inInterface), table: Number(f.table), isEnabled: f.isEnabled, description: opt(f.description) };
      return e.id ? networkApi.policies.update(gatewayId, e.id, body) : networkApi.policies.create(gatewayId, body);
    },
    { invalidate: NETWORK_KEY, success: 'Routing policy saved', onSuccess: () => setPolicy(null) },
  );
  const remove = useApiMutation(
    (d: NonNullable<typeof deleting>) => (d.kind === 'route' ? networkApi.routes.remove(gatewayId, d.id) : networkApi.policies.remove(gatewayId, d.id)),
    { invalidate: NETWORK_KEY, success: 'Removed', onSuccess: () => setDeleting(null) },
  );
  const setR = <K extends keyof RouteForm>(k: K, v: RouteForm[K]) => setRoute((e) => e && { ...e, form: { ...e.form, [k]: v } });
  const setP = <K extends keyof PolicyForm>(k: K, v: PolicyForm[K]) => setPolicy((e) => e && { ...e, form: { ...e.form, [k]: v } });

  const routeColumns: Column<NetRoute>[] = [
    { key: 'table', header: 'Table', value: (r) => tableLabel(r.table), sortValue: (r) => r.table, mergeRepeated: true, groupCell: true,
      render: (r) => <Badge tone={r.table === MAIN ? 'neutral' : 'primary'}>{tableLabel(r.table)}</Badge> },
    { key: 'dest', header: 'Destination', value: (r) => r.destination, render: (r) => r.isDefault ? <><code>default</code> <span className="muted small">(0.0.0.0/0)</span></> : <code>{r.destination}</code> },
    { key: 'via', header: 'Next hop', value: (r) => r.nextHop, render: (r) => r.nextHop ? <code>{r.nextHop}</code> : <span className="muted">direct</span> },
    { key: 'dev', header: 'Interface', value: (r) => r.interface, render: (r) => r.interface ? <code>{r.interface}</code> : <span className="muted">auto</span> },
    { key: 'metric', header: 'Metric', align: 'right', value: (r) => r.metric },
    { key: 'desc', header: 'Description', value: (r) => r.description, groupable: false },
    { key: 'status', header: 'Status', value: (r) => (r.isEnabled ? 'Enabled' : 'Disabled'), render: (r) => <StatusBadge status={r.isEnabled ? 'Enabled' : 'Disabled'} /> },
    { key: 'actions', header: '', headerText: 'Actions', hideable: false, className: 'text-right nowrap', render: (r) => canWrite && (
      <>
        <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={15} />} aria-label="Edit" onClick={() => setRoute({ id: r.id, form: {
          destination: r.destination, nextHop: r.nextHop ?? '', interface: r.interface ?? '', metric: String(r.metric), table: String(r.table), isEnabled: r.isEnabled, description: r.description ?? '' } })} />
        <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={15} />} aria-label="Delete" onClick={() => setDeleting({ kind: 'route', id: r.id, label: r.destination })} />
      </>
    ) },
  ];

  const policyColumns: Column<RoutingPolicyItem>[] = [
    { key: 'priority', header: 'Priority', align: 'right', value: (p) => p.priority },
    { key: 'match', header: 'Match', value: (p) => [p.source && `from ${p.source}`, p.destination && `to ${p.destination}`, p.inInterface && `iif ${p.inInterface}`].filter(Boolean).join(' '), groupable: false,
      render: (p) => <code>{[p.source && `from ${p.source}`, p.destination && `to ${p.destination}`, p.inInterface && `iif ${p.inInterface}`].filter(Boolean).join('  ')}</code> },
    { key: 'table', header: 'Uses', value: (p) => tableLabel(p.table), render: (p) => <Badge tone="primary">{tableLabel(p.table)}</Badge> },
    { key: 'desc', header: 'Description', value: (p) => p.description, groupable: false },
    { key: 'status', header: 'Status', value: (p) => (p.isEnabled ? 'Enabled' : 'Disabled'), render: (p) => <StatusBadge status={p.isEnabled ? 'Enabled' : 'Disabled'} /> },
    { key: 'actions', header: '', headerText: 'Actions', hideable: false, className: 'text-right nowrap', render: (p) => canWrite && (
      <>
        <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={15} />} aria-label="Edit" onClick={() => setPolicy({ id: p.id, form: {
          priority: String(p.priority), source: p.source ?? '', destination: p.destination ?? '', inInterface: p.inInterface ?? '', table: String(p.table), isEnabled: p.isEnabled, description: p.description ?? '' } })} />
        <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={15} />} aria-label="Delete" onClick={() => setDeleting({ kind: 'policy', id: p.id, label: `priority ${p.priority}` })} />
      </>
    ) },
  ];

  return (
    <>
      <CardHeader title="Static routes" subtitle="Routes in custom tables are only used when a routing policy below selects that table." />
      <div style={{ marginTop: 12 }}>
        <DataTable columns={routeColumns} rows={detail.routes} rowKey={(r) => r.id} initialSort={[{ key: 'table', dir: 'desc' }, { key: 'metric', dir: 'asc' }]} pivot={false}
          exportName="routes" storageKey="net-routes"
          toolbar={canWrite && <Button size="sm" variant="primary" icon={<Plus size={14} />}
            onClick={() => setRoute({ form: { destination: '0.0.0.0/0', nextHop: '', interface: '', metric: '100', table: String(MAIN), isEnabled: true, description: '' } })}>Add route</Button>}
          empty={{ title: 'No static routes' }} />
      </div>

      <div style={{ borderTop: '1px solid var(--border)', marginTop: 8 }}>
        <CardHeader title="Policy routing — dedicated gateways" subtitle="Send selected traffic (by source, destination or input interface) through a specific routing table, e.g. guest Wi-Fi via a second ISP." />
        <div style={{ marginTop: 12 }}>
          <DataTable columns={policyColumns} rows={detail.policies} rowKey={(p) => p.id} initialSort={[{ key: 'priority', dir: 'asc' }]} pivot={false}
            exportName="routing-policies" storageKey="net-policies"
            toolbar={canWrite && <Button size="sm" variant="primary" icon={<Plus size={14} />}
              onClick={() => setPolicy({ form: { priority: String(1000 + detail.policies.length * 10), source: '', destination: '', inInterface: '', table: '100', isEnabled: true, description: '' } })}>Add policy</Button>}
            empty={{ title: 'No routing policies', description: 'All traffic uses the main table.' }} />
        </div>
      </div>

      <Modal open={!!route} onClose={() => setRoute(null)} title={route?.id ? 'Edit route' : 'Add route'} onSubmit={() => route && saveRoute.mutate(route)}
        footer={<><Button onClick={() => setRoute(null)}>Cancel</Button><Button type="submit" variant="primary" loading={saveRoute.isPending}>Save route</Button></>}>
        {route && (
          <div className="form-grid">
            <Field label="Destination" hint="0.0.0.0/0 = default route"><Input required className="mono" value={route.form.destination} onChange={(e) => setR('destination', e.target.value)} /></Field>
            <Field label="Next hop" hint="Gateway IP (optional for directly connected)"><Input className="mono" placeholder="198.51.100.1" value={route.form.nextHop} onChange={(e) => setR('nextHop', e.target.value)} /></Field>
            <Field label="Interface"><Select placeholder="Automatic" options={interfaceOptions} value={route.form.interface} onChange={(e) => setR('interface', e.target.value)} /></Field>
            <Field label="Metric" hint="Lower is preferred"><Input type="number" min={0} max={65535} value={route.form.metric} onChange={(e) => setR('metric', e.target.value)} /></Field>
            <Field label="Routing table" hint="Type a number 1–252 for a new custom table">
              <Input type="number" min={1} max={254} value={route.form.table} onChange={(e) => setR('table', e.target.value)} />
            </Field>
            <Field label="Status"><Switch checked={route.form.isEnabled} onChange={(v) => setR('isEnabled', v)} label={route.form.isEnabled ? 'Enabled' : 'Disabled'} /></Field>
            <Field label="Description" full><Textarea maxLength={500} value={route.form.description} onChange={(e) => setR('description', e.target.value)} /></Field>
          </div>
        )}
      </Modal>

      <Modal open={!!policy} onClose={() => setPolicy(null)} title={policy?.id ? 'Edit routing policy' : 'Add routing policy'} onSubmit={() => policy && savePolicy.mutate(policy)}
        footer={<><Button onClick={() => setPolicy(null)}>Cancel</Button><Button type="submit" variant="primary" loading={savePolicy.isPending}>Save policy</Button></>}>
        {policy && (
          <div className="form-grid">
            <Field label="Priority" hint="1–32765, lower runs first"><Input required type="number" min={1} max={32765} value={policy.form.priority} onChange={(e) => setP('priority', e.target.value)} /></Field>
            <Field label="Use routing table"><Select options={tables} value={policy.form.table} onChange={(e) => setP('table', e.target.value)} /></Field>
            <Field label="From (source)"><Input className="mono" placeholder="10.20.0.0/24" value={policy.form.source} onChange={(e) => setP('source', e.target.value)} /></Field>
            <Field label="To (destination)"><Input className="mono" placeholder="any" value={policy.form.destination} onChange={(e) => setP('destination', e.target.value)} /></Field>
            <Field label="Arriving on interface"><Select placeholder="Any" options={interfaceOptions} value={policy.form.inInterface} onChange={(e) => setP('inInterface', e.target.value)} /></Field>
            <Field label="Status"><Switch checked={policy.form.isEnabled} onChange={(v) => setP('isEnabled', v)} label={policy.form.isEnabled ? 'Enabled' : 'Disabled'} /></Field>
            <Field label="Description" full><Textarea maxLength={500} value={policy.form.description} onChange={(e) => setP('description', e.target.value)} /></Field>
          </div>
        )}
      </Modal>

      <ConfirmDialog open={!!deleting} title="Remove" message={<>Remove {deleting?.kind === 'route' ? 'route to' : 'routing policy'} <code>{deleting?.label}</code>?</>}
        confirmLabel="Remove" loading={remove.isPending} onConfirm={() => deleting && remove.mutate(deleting)} onClose={() => setDeleting(null)} />
    </>
  );
}
