import { useState } from 'react';
import { ArrowRight, Pencil, Plus, Trash2 } from 'lucide-react';
import { networkApi } from '../../../lib/api';
import type { GatewayDetail, NatProtocol, NatRuleItem, NatType } from '../../../lib/types';
import { useApiMutation } from '../../../hooks/useApiMutation';
import { Button } from '../../../components/ui/Button';
import { Badge, StatusBadge } from '../../../components/ui/Badge';
import { Field, Input, Select, Switch, Textarea } from '../../../components/ui/Form';
import { ConfirmDialog, Modal } from '../../../components/ui/Modal';
import { DataTable, type Column } from '../../../components/ui/DataTable';
import { NETWORK_KEY, opt, optNum } from '../shared';

const NAT_TYPES: { value: NatType; label: string; description: string }[] = [
  { value: 'Masquerade', label: 'Masquerade', description: 'Hide a network behind the outgoing interface address (LAN → internet)' },
  { value: 'SourceNat', label: 'Source NAT', description: 'Rewrite the source to a fixed public address' },
  { value: 'PortForward', label: 'Port forward', description: 'Publish an internal service on a public port (DNAT)' },
  { value: 'OneToOne', label: '1:1 NAT', description: 'Map a public IP to a private IP in both directions' },
];
const TYPE_TONE: Record<NatType, 'success' | 'primary' | 'warning' | 'neutral'> = { Masquerade: 'success', SourceNat: 'primary', PortForward: 'warning', OneToOne: 'neutral' };

interface Form {
  name: string; type: NatType; protocol: NatProtocol; inInterface: string; outInterface: string; source: string; destination: string;
  destinationPort: string; translatedAddress: string; translatedPort: string; priority: string; isEnabled: boolean; description: string;
}
const EMPTY: Form = { name: '', type: 'PortForward', protocol: 'Tcp', inInterface: '', outInterface: '', source: '', destination: '', destinationPort: '', translatedAddress: '', translatedPort: '', priority: '100', isEnabled: true, description: '' };

function summary(n: NatRuleItem) {
  const port = n.destinationPort ? `:${n.destinationPort}` : '';
  switch (n.type) {
    case 'Masquerade': return <><code>{n.source ?? 'any'}</code> <ArrowRight size={12} /> <code>{n.outInterface}</code> address</>;
    case 'SourceNat': return <><code>{n.source ?? 'any'}</code> <ArrowRight size={12} /> <code>{n.translatedAddress}</code> via {n.outInterface}</>;
    case 'PortForward': return <><code>{n.inInterface}{n.destination ? ` ${n.destination}` : ''}{port}/{n.protocol.toLowerCase()}</code> <ArrowRight size={12} /> <code>{n.translatedAddress}{n.translatedPort ? `:${n.translatedPort}` : ''}</code></>;
    default: return <><code>{n.destination}</code> <ArrowRight size={12} /> <code>{n.translatedAddress}</code></>;
  }
}

export function NatTab({ gatewayId, detail, canWrite }: { gatewayId: string; detail: GatewayDetail; canWrite: boolean }) {
  const [editing, setEditing] = useState<{ id?: string; form: Form } | null>(null);
  const [deleting, setDeleting] = useState<NatRuleItem | null>(null);
  const interfaces = detail.interfaces.map((i) => ({ value: i.name, label: i.name, description: `${i.zone} · ${i.address ?? i.mode}` }));

  const save = useApiMutation(
    (e: NonNullable<typeof editing>) => {
      const f = e.form;
      const forward = f.type === 'PortForward';
      const body = {
        name: f.name.trim(), type: f.type, protocol: forward ? f.protocol : (f.destinationPort ? f.protocol : 'Any') as NatProtocol,
        inInterface: forward || f.type === 'OneToOne' ? opt(f.inInterface) : undefined,
        outInterface: f.type === 'Masquerade' || f.type === 'SourceNat' ? opt(f.outInterface) : undefined,
        source: opt(f.source), destination: opt(f.destination), destinationPort: forward ? opt(f.destinationPort) : undefined,
        translatedAddress: f.type === 'Masquerade' ? undefined : opt(f.translatedAddress), translatedPort: forward ? optNum(f.translatedPort) : undefined,
        priority: Number(f.priority), isEnabled: f.isEnabled, description: opt(f.description),
      };
      return e.id ? networkApi.nat.update(gatewayId, e.id, body) : networkApi.nat.create(gatewayId, body);
    },
    { invalidate: NETWORK_KEY, success: (n) => `NAT rule '${n.name}' saved`, onSuccess: () => setEditing(null) },
  );
  const remove = useApiMutation((n: NatRuleItem) => networkApi.nat.remove(gatewayId, n.id), { invalidate: NETWORK_KEY, success: 'NAT rule removed', onSuccess: () => setDeleting(null) });
  const set = <K extends keyof Form>(k: K, v: Form[K]) => setEditing((e) => e && { ...e, form: { ...e.form, [k]: v } });

  const columns: Column<NatRuleItem>[] = [
    { key: 'priority', header: '#', align: 'right', width: 50, value: (n) => n.priority },
    { key: 'name', header: 'Rule', value: (n) => n.name, groupable: false, render: (n) => <><div className="cell-title">{n.name}</div>{n.description && <div className="cell-sub">{n.description}</div>}</> },
    { key: 'type', header: 'Type', value: (n) => NAT_TYPES.find((t) => t.value === n.type)!.label, groupCell: true,
      render: (n) => <Badge tone={TYPE_TONE[n.type]}>{NAT_TYPES.find((t) => t.value === n.type)!.label}</Badge> },
    { key: 'translation', header: 'Translation', value: (n) => `${n.source ?? ''} ${n.destination ?? ''} ${n.translatedAddress ?? ''}`, groupable: false,
      render: (n) => <span className="row small" style={{ gap: 6, flexWrap: 'wrap' }}>{summary(n)}</span> },
    { key: 'status', header: 'Status', value: (n) => (n.isEnabled ? 'Enabled' : 'Disabled'), render: (n) => <StatusBadge status={n.isEnabled ? 'Enabled' : 'Disabled'} /> },
    { key: 'actions', header: '', headerText: 'Actions', hideable: false, className: 'text-right nowrap', render: (n) => canWrite && (
      <>
        <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={15} />} aria-label="Edit" onClick={() => setEditing({ id: n.id, form: {
          name: n.name, type: n.type, protocol: n.protocol, inInterface: n.inInterface ?? '', outInterface: n.outInterface ?? '', source: n.source ?? '',
          destination: n.destination ?? '', destinationPort: n.destinationPort ?? '', translatedAddress: n.translatedAddress ?? '',
          translatedPort: n.translatedPort?.toString() ?? '', priority: String(n.priority), isEnabled: n.isEnabled, description: n.description ?? '' } })} />
        <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={15} />} aria-label="Delete" onClick={() => setDeleting(n)} />
      </>
    ) },
  ];

  const f = editing?.form;
  return (
    <>
      <DataTable columns={columns} rows={detail.natRules} rowKey={(n) => n.id} initialSort={[{ key: 'priority', dir: 'asc' }]} exportName="nat-rules" storageKey="net-nat"
        toolbar={canWrite && <Button size="sm" variant="primary" icon={<Plus size={14} />} onClick={() => setEditing({ form: EMPTY })}>Add NAT rule</Button>}
        empty={{ title: 'No NAT rules', description: 'Add masquerade for outbound internet access or port forwards to publish services.' }} />

      <Modal open={!!editing} onClose={() => setEditing(null)} size="lg" title={editing?.id ? 'Edit NAT rule' : 'Add NAT rule'} onSubmit={() => editing && save.mutate(editing)}
        footer={<><Button onClick={() => setEditing(null)}>Cancel</Button><Button type="submit" variant="primary" loading={save.isPending}>Save rule</Button></>}>
        {f && (
          <div className="form-grid">
            <Field label="Name"><Input required maxLength={100} value={f.name} onChange={(e) => set('name', e.target.value)} /></Field>
            <Field label="Type"><Select options={NAT_TYPES} value={f.type} onChange={(e) => set('type', e.target.value as NatType)} /></Field>

            {(f.type === 'Masquerade' || f.type === 'SourceNat') && <>
              <Field label="Leaving via interface"><Select required options={interfaces} value={f.outInterface} onChange={(e) => set('outInterface', e.target.value)} /></Field>
              <Field label="Source network" hint="e.g. 10.0.0.0/24 (empty = any)"><Input className="mono" value={f.source} onChange={(e) => set('source', e.target.value)} /></Field>
              {f.type === 'SourceNat' && <Field label="Translate source to"><Input required className="mono" placeholder="198.51.100.10" value={f.translatedAddress} onChange={(e) => set('translatedAddress', e.target.value)} /></Field>}
            </>}

            {f.type === 'PortForward' && <>
              <Field label="Arriving on interface"><Select required options={interfaces} value={f.inInterface} onChange={(e) => set('inInterface', e.target.value)} /></Field>
              <Field label="Protocol"><Select options={[{ value: 'Tcp', label: 'TCP' }, { value: 'Udp', label: 'UDP' }]} value={f.protocol} onChange={(e) => set('protocol', e.target.value as NatProtocol)} /></Field>
              <Field label="Public address" hint="Optional: only this destination IP (e.g. a VIP)"><Input className="mono" value={f.destination} onChange={(e) => set('destination', e.target.value)} /></Field>
              <Field label="Public port(s)" hint="443 or 8000-8100"><Input required className="mono" value={f.destinationPort} onChange={(e) => set('destinationPort', e.target.value)} /></Field>
              <Field label="Internal host"><Input required className="mono" placeholder="172.16.10.10" value={f.translatedAddress} onChange={(e) => set('translatedAddress', e.target.value)} /></Field>
              <Field label="Internal port" hint="Empty = same as public"><Input type="number" min={1} max={65535} value={f.translatedPort} onChange={(e) => set('translatedPort', e.target.value)} /></Field>
              <Field label="Allowed sources" hint="Optional: restrict who can reach it"><Input className="mono" value={f.source} onChange={(e) => set('source', e.target.value)} /></Field>
            </>}

            {f.type === 'OneToOne' && <>
              <Field label="Public IP"><Input required className="mono" value={f.destination} onChange={(e) => set('destination', e.target.value)} /></Field>
              <Field label="Private IP"><Input required className="mono" value={f.translatedAddress} onChange={(e) => set('translatedAddress', e.target.value)} /></Field>
            </>}

            <Field label="Order" hint="Lower is evaluated first"><Input type="number" value={f.priority} onChange={(e) => set('priority', e.target.value)} /></Field>
            <Field label="Status"><Switch checked={f.isEnabled} onChange={(v) => set('isEnabled', v)} label={f.isEnabled ? 'Enabled' : 'Disabled'} /></Field>
            <Field label="Description" full><Textarea maxLength={500} value={f.description} onChange={(e) => set('description', e.target.value)} /></Field>
          </div>
        )}
      </Modal>

      <ConfirmDialog open={!!deleting} title="Remove NAT rule" message={<>Remove <b>{deleting?.name}</b>?</>}
        confirmLabel="Remove" loading={remove.isPending} onConfirm={() => deleting && remove.mutate(deleting)} onClose={() => setDeleting(null)} />
    </>
  );
}
