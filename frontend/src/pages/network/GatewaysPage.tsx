import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { AlertCircle, AlertTriangle, CloudUpload, Pencil, Plus, Router, ServerCog, Trash2 } from 'lucide-react';
import { networkApi } from '../../lib/api';
import { timeAgo } from '../../lib/format';
import type { GatewayInput, NetGateway } from '../../lib/types';
import { useApiMutation } from '../../hooks/useApiMutation';
import { useAuth } from '../../hooks/useAuth';
import { Card, PageHeader, StatCard } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { Field, Input, Select, Textarea } from '../../components/ui/Form';
import { ConfirmDialog, Modal } from '../../components/ui/Modal';
import { DataTable, type Column } from '../../components/ui/DataTable';
import { DeployStatus, NETWORK_KEY, opt } from './shared';

const EMPTY: GatewayInput = { name: '', hostname: '', description: '', managementAddress: '', clusterId: null, haPriority: 100 };

export default function GatewaysPage() {
  const navigate = useNavigate();
  const { canWrite } = useAuth();
  const gateways = useQuery({ queryKey: ['network', 'gateways'], queryFn: networkApi.gateways });
  const clusters = useQuery({ queryKey: ['network', 'clusters'], queryFn: networkApi.clusters });
  const [editing, setEditing] = useState<{ id?: string; form: GatewayInput } | null>(null);
  const [deleting, setDeleting] = useState<NetGateway | null>(null);

  const save = useApiMutation(
    (e: NonNullable<typeof editing>) => {
      const body = { ...e.form, description: opt(e.form.description), managementAddress: opt(e.form.managementAddress), clusterId: e.form.clusterId || null };
      return e.id ? networkApi.updateGateway(e.id, body) : networkApi.createGateway(body);
    },
    { invalidate: NETWORK_KEY, success: 'Gateway saved', onSuccess: (g) => { setEditing(null); if (!editing?.id) navigate(`/network/gateways/${g.id}`); } },
  );
  const remove = useApiMutation((id: string) => networkApi.deleteGateway(id), { invalidate: NETWORK_KEY, success: 'Gateway deleted', onSuccess: () => setDeleting(null) });
  const set = <K extends keyof GatewayInput>(k: K, v: GatewayInput[K]) => setEditing((e) => e && { ...e, form: { ...e.form, [k]: v } });

  const rows = gateways.data ?? [];
  const columns: Column<NetGateway>[] = [
    { key: 'name', header: 'Gateway', value: (g) => g.name, groupable: false,
      render: (g) => <><div className="cell-title">{g.name}</div><div className="cell-sub mono">{g.hostname}{g.managementAddress && ` · ${g.managementAddress}`}</div></> },
    { key: 'cluster', header: 'HA cluster', value: (g) => g.clusterName ?? 'Standalone', mergeRepeated: true },
    { key: 'role', header: 'Role', value: (g) => g.haRole ?? 'Standalone', groupCell: true,
      render: (g) => g.haRole ? <Badge tone={g.haRole === 'Master' ? 'success' : 'neutral'} dot>{g.haRole} · p{g.haPriority}</Badge> : <span className="muted">—</span> },
    { key: 'interfaces', header: 'Interfaces', group: 'Configuration', align: 'right', value: (g) => g.interfaceCount, footer: 'sum' },
    { key: 'routes', header: 'Routes', group: 'Configuration', align: 'right', value: (g) => g.routeCount, footer: 'sum' },
    { key: 'nat', header: 'NAT', group: 'Configuration', align: 'right', value: (g) => g.natRuleCount, footer: 'sum' },
    { key: 'vpn', header: 'VPN', group: 'Configuration', align: 'right', value: (g) => g.tunnelCount, footer: 'sum' },
    { key: 'health', header: 'Checks', value: (g) => (g.errorCount ? 'Errors' : g.warningCount ? 'Warnings' : 'OK'), groupCell: true,
      render: (g) => g.errorCount ? <Badge tone="danger"><AlertCircle size={12} /> {g.errorCount} error{g.errorCount > 1 ? 's' : ''}</Badge>
        : g.warningCount ? <Badge tone="warning"><AlertTriangle size={12} /> {g.warningCount} warning{g.warningCount > 1 ? 's' : ''}</Badge>
        : <Badge tone="success">OK</Badge> },
    { key: 'deploy', header: 'Deployment', value: (g) => (g.pendingChanges ? 'Pending' : 'Deployed'), groupCell: true, render: (g) => <DeployStatus g={g} /> },
    { key: 'updated', header: 'Changed', value: (g) => new Date(g.updatedAt ?? g.createdAt), filter: false, render: (g) => <span className="small nowrap">{timeAgo(g.updatedAt ?? g.createdAt)}</span> },
    { key: 'actions', header: '', headerText: 'Actions', hideable: false, className: 'text-right nowrap', render: (g) => canWrite && (
      <span onClick={(e) => e.stopPropagation()}>
        <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={15} />} aria-label="Edit"
          onClick={() => setEditing({ id: g.id, form: { name: g.name, hostname: g.hostname, description: g.description ?? '', managementAddress: g.managementAddress ?? '', clusterId: g.clusterId ?? null, haPriority: g.haPriority } })} />
        <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={15} />} aria-label="Delete" onClick={() => setDeleting(g)} />
      </span>
    ) },
  ];

  return (
    <div className="stack">
      <PageHeader
        title="Gateways"
        description="Linux routers/firewalls whose interfaces, routing, NAT, VPN and high availability are managed here. Configuration is generated for iproute2, nftables, WireGuard, strongSwan and keepalived."
        actions={canWrite && <Button variant="primary" icon={<Plus size={16} />} onClick={() => setEditing({ form: EMPTY })}>Add gateway</Button>}
      />

      <div className="grid grid-kpi">
        <StatCard label="Gateways" value={rows.length} icon={<Router size={20} />} hint={`${rows.filter((g) => g.clusterId).length} in HA clusters`} />
        <StatCard label="Pending deployment" value={rows.filter((g) => g.pendingChanges).length} icon={<CloudUpload size={20} />} tone="warning" hint="Config changed since last deploy" />
        <StatCard label="With errors" value={rows.filter((g) => g.errorCount > 0).length} icon={<AlertCircle size={20} />} tone="danger" />
        <StatCard label="HA clusters" value={clusters.data?.length ?? '—'} icon={<ServerCog size={20} />} tone="success" />
      </div>

      <Card>
        <DataTable columns={columns} rows={gateways.data} rowKey={(g) => g.id} loading={gateways.isLoading}
          onRowClick={(g) => navigate(`/network/gateways/${g.id}`)} initialSort={[{ key: 'cluster', dir: 'asc' }, { key: 'name', dir: 'asc' }]}
          exportName="gateways" storageKey="gateways" empty={{ title: 'No gateways', description: 'Add a gateway to start managing its network configuration.' }} />
      </Card>

      <Modal open={!!editing} onClose={() => setEditing(null)} title={editing?.id ? 'Edit gateway' : 'Add gateway'} onSubmit={() => editing && save.mutate(editing)}
        footer={<><Button onClick={() => setEditing(null)}>Cancel</Button><Button type="submit" variant="primary" loading={save.isPending}>Save gateway</Button></>}>
        {editing && (
          <div className="form-grid">
            <Field label="Name"><Input required minLength={2} maxLength={100} value={editing.form.name} onChange={(e) => set('name', e.target.value)} /></Field>
            <Field label="Hostname"><Input required className="mono" placeholder="edge-gw-03" value={editing.form.hostname} onChange={(e) => set('hostname', e.target.value)} /></Field>
            <Field label="Management address" hint="Optional, for reference"><Input className="mono" placeholder="10.99.0.4" value={editing.form.managementAddress} onChange={(e) => set('managementAddress', e.target.value)} /></Field>
            <Field label="HA cluster"><Select placeholder="Standalone" value={editing.form.clusterId ?? ''}
              options={(clusters.data ?? []).map((c) => ({ value: c.id, label: c.name, description: `${c.members.length} member(s) · VRRP ${c.vrrpRouterId}` }))}
              onChange={(e) => set('clusterId', e.target.value || null)} /></Field>
            {editing.form.clusterId && (
              <Field label="HA priority" hint="1–254; highest priority becomes master"><Input type="number" min={1} max={254} value={editing.form.haPriority} onChange={(e) => set('haPriority', Number(e.target.value))} /></Field>
            )}
            <Field label="Description" full><Textarea maxLength={500} value={editing.form.description} onChange={(e) => set('description', e.target.value)} /></Field>
          </div>
        )}
      </Modal>

      <ConfirmDialog open={!!deleting} title="Delete gateway" message={<>Delete <b>{deleting?.name}</b> and all of its interfaces, routes, NAT rules and VPN tunnels?</>}
        loading={remove.isPending} onConfirm={() => deleting && remove.mutate(deleting.id)} onClose={() => setDeleting(null)} />
    </div>
  );
}
