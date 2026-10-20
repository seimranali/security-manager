import { useState } from 'react';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { networkApi } from '../../../lib/api';
import { describeAddress } from '../../../lib/ip';
import type { AddressingMode, GatewayDetail, InterfaceType, InterfaceZone, NetInterface } from '../../../lib/types';
import { useApiMutation } from '../../../hooks/useApiMutation';
import { Button } from '../../../components/ui/Button';
import { StatusBadge } from '../../../components/ui/Badge';
import { Field, Input, Select, Switch, Textarea } from '../../../components/ui/Form';
import { ConfirmDialog, Modal } from '../../../components/ui/Modal';
import { DataTable, type Column } from '../../../components/ui/DataTable';
import { NETWORK_KEY, opt, optNum, ZONES, ZoneBadge } from '../shared';

interface Form {
  name: string; type: InterfaceType; zone: InterfaceZone; mode: AddressingMode; address: string;
  mtu: string; vlanId: string; parent: string; members: string; isEnabled: boolean; description: string;
}
const EMPTY: Form = { name: '', type: 'Physical', zone: 'Lan', mode: 'Static', address: '', mtu: '', vlanId: '', parent: '', members: '', isEnabled: true, description: '' };

const TYPES: { value: InterfaceType; label: string; description: string }[] = [
  { value: 'Physical', label: 'Physical', description: 'Ethernet port (eth0, enp3s0…)' },
  { value: 'Vlan', label: 'VLAN', description: '802.1Q tagged sub-interface' },
  { value: 'Bond', label: 'Bond', description: 'Link aggregation / failover (active-backup)' },
  { value: 'Bridge', label: 'Bridge', description: 'Layer-2 bridge of member ports' },
  { value: 'Loopback', label: 'Loopback', description: 'Virtual loopback address' },
];

export function InterfacesTab({ gatewayId, detail, canWrite }: { gatewayId: string; detail: GatewayDetail; canWrite: boolean }) {
  const [editing, setEditing] = useState<{ id?: string; form: Form } | null>(null);
  const [deleting, setDeleting] = useState<NetInterface | null>(null);

  const save = useApiMutation(
    (e: NonNullable<typeof editing>) => {
      const f = e.form;
      const body = {
        name: f.name.trim(), type: f.type, zone: f.zone, mode: f.mode, address: f.mode === 'Static' ? opt(f.address) : undefined,
        mtu: optNum(f.mtu), vlanId: f.type === 'Vlan' ? optNum(f.vlanId) : undefined, parent: f.type === 'Vlan' ? opt(f.parent) : undefined,
        members: f.type === 'Bond' || f.type === 'Bridge' ? opt(f.members) : undefined, isEnabled: f.isEnabled, description: opt(f.description),
      };
      return e.id ? networkApi.interfaces.update(gatewayId, e.id, body) : networkApi.interfaces.create(gatewayId, body);
    },
    { invalidate: NETWORK_KEY, success: (i) => `Interface ${i.name} saved`, onSuccess: () => setEditing(null) },
  );
  const remove = useApiMutation((i: NetInterface) => networkApi.interfaces.remove(gatewayId, i.id), { invalidate: NETWORK_KEY, success: 'Interface removed', onSuccess: () => setDeleting(null) });
  const set = <K extends keyof Form>(k: K, v: Form[K]) => setEditing((e) => e && { ...e, form: { ...e.form, [k]: v } });

  const columns: Column<NetInterface>[] = [
    { key: 'name', header: 'Interface', value: (i) => i.name, render: (i) => <><code className="cell-title">{i.name}</code>{i.description && <div className="cell-sub">{i.description}</div>}</> },
    { key: 'zone', header: 'Zone', value: (i) => i.zone, groupCell: true, render: (i) => <ZoneBadge zone={i.zone} /> },
    { key: 'type', header: 'Type', value: (i) => i.type,
      render: (i) => <span className="small">{i.type}{i.type === 'Vlan' && ` ${i.vlanId} on ${i.parent}`}{i.members && ` (${i.members})`}</span> },
    { key: 'address', header: 'Address', value: (i) => i.address ?? i.mode, groupable: false,
      render: (i) => i.mode === 'Static' ? <><code>{i.address}</code><div className="cell-sub">{describeAddress(i.address ?? '').detail}</div></> : <span className="muted">{i.mode}</span> },
    { key: 'mtu', header: 'MTU', align: 'right', value: (i) => i.mtu ?? null, format: (v) => (v == null ? 'default' : String(v)) },
    { key: 'status', header: 'Status', value: (i) => (i.isEnabled ? 'Enabled' : 'Disabled'), render: (i) => <StatusBadge status={i.isEnabled ? 'Enabled' : 'Disabled'} /> },
    { key: 'actions', header: '', headerText: 'Actions', hideable: false, className: 'text-right nowrap', render: (i) => canWrite && (
      <>
        <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={15} />} aria-label="Edit" onClick={() => setEditing({
          id: i.id,
          form: { name: i.name, type: i.type, zone: i.zone, mode: i.mode, address: i.address ?? '', mtu: i.mtu?.toString() ?? '', vlanId: i.vlanId?.toString() ?? '',
            parent: i.parent ?? '', members: i.members ?? '', isEnabled: i.isEnabled, description: i.description ?? '' },
        })} />
        <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={15} />} aria-label="Delete" onClick={() => setDeleting(i)} />
      </>
    ) },
  ];

  const parents = detail.interfaces.filter((i) => i.type !== 'Vlan').map((i) => ({ value: i.name, label: i.name, description: i.description }));
  const addr = editing?.form.mode === 'Static' && editing.form.address ? describeAddress(editing.form.address) : null;

  return (
    <>
      <DataTable columns={columns} rows={detail.interfaces} rowKey={(i) => i.id} initialSort={[{ key: 'name', dir: 'asc' }]} exportName="interfaces" storageKey="net-interfaces"
        toolbar={canWrite && <Button size="sm" variant="primary" icon={<Plus size={14} />} onClick={() => setEditing({ form: EMPTY })}>Add interface</Button>}
        empty={{ title: 'No interfaces', description: 'Add the gateway’s network interfaces first.' }} />

      <Modal open={!!editing} onClose={() => setEditing(null)} title={editing?.id ? `Edit ${editing.form.name}` : 'Add interface'} onSubmit={() => editing && save.mutate(editing)}
        footer={<><Button onClick={() => setEditing(null)}>Cancel</Button><Button type="submit" variant="primary" loading={save.isPending}>Save interface</Button></>}>
        {editing && (
          <div className="form-grid">
            <Field label="Name" hint="Linux name, max 15 chars"><Input required className="mono" maxLength={15} placeholder="eth1 / eth1.20 / bond0" value={editing.form.name} onChange={(e) => set('name', e.target.value)} /></Field>
            <Field label="Type"><Select options={TYPES} value={editing.form.type} onChange={(e) => set('type', e.target.value as InterfaceType)} /></Field>
            <Field label="Zone"><Select options={ZONES.map((z) => ({ value: z.value, label: z.label }))} value={editing.form.zone} onChange={(e) => set('zone', e.target.value as InterfaceZone)} /></Field>
            <Field label="Addressing"><Select options={[{ value: 'Static', label: 'Static' }, { value: 'Dhcp', label: 'DHCP client' }, { value: 'None', label: 'No address (L2 only)' }]}
              value={editing.form.mode} onChange={(e) => set('mode', e.target.value as AddressingMode)} /></Field>
            {editing.form.mode === 'Static' && (
              <Field label="Address (CIDR)" full hint={addr?.error ? <span style={{ color: 'var(--danger)' }}>{addr.error}</span> : addr?.valid ? `${addr.kind} · ${addr.detail}` : 'e.g. 10.0.0.1/24 or 2001:db8::1/64'}>
                <Input required className="mono" placeholder="10.0.0.1/24" value={editing.form.address} onChange={(e) => set('address', e.target.value)} />
              </Field>
            )}
            {editing.form.type === 'Vlan' && <>
              <Field label="Parent interface"><Select required options={parents} value={editing.form.parent} onChange={(e) => set('parent', e.target.value)} /></Field>
              <Field label="VLAN id"><Input required type="number" min={1} max={4094} value={editing.form.vlanId} onChange={(e) => set('vlanId', e.target.value)} /></Field>
            </>}
            {(editing.form.type === 'Bond' || editing.form.type === 'Bridge') && (
              <Field label="Members" full hint="Comma-separated interface names, e.g. eth4, eth5"><Input className="mono" value={editing.form.members} onChange={(e) => set('members', e.target.value)} /></Field>
            )}
            <Field label="MTU" hint="Leave empty for the default (1500)"><Input type="number" min={576} max={9216} value={editing.form.mtu} onChange={(e) => set('mtu', e.target.value)} /></Field>
            <Field label="Status"><Switch checked={editing.form.isEnabled} onChange={(v) => set('isEnabled', v)} label={editing.form.isEnabled ? 'Up' : 'Down'} /></Field>
            <Field label="Description" full><Textarea maxLength={500} value={editing.form.description} onChange={(e) => set('description', e.target.value)} /></Field>
          </div>
        )}
      </Modal>

      <ConfirmDialog open={!!deleting} title="Remove interface" message={<>Remove <code>{deleting?.name}</code>? Routes, NAT rules and VIPs that reference it will be flagged.</>}
        confirmLabel="Remove" loading={remove.isPending} onConfirm={() => deleting && remove.mutate(deleting)} onClose={() => setDeleting(null)} />
    </>
  );
}
