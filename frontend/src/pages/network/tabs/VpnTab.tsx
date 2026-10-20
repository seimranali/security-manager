import { useState } from 'react';
import { KeyRound, Pencil, Plus, Trash2, UserPlus } from 'lucide-react';
import { networkApi } from '../../../lib/api';
import type { GatewayDetail, VpnPeerItem, VpnTunnelItem, VpnType } from '../../../lib/types';
import { useApiMutation } from '../../../hooks/useApiMutation';
import { Button } from '../../../components/ui/Button';
import { Badge, StatusBadge } from '../../../components/ui/Badge';
import { Field, Input, Select, Switch, Textarea } from '../../../components/ui/Form';
import { ConfirmDialog, Modal } from '../../../components/ui/Modal';
import { DataTable, type Column } from '../../../components/ui/DataTable';
import { NETWORK_KEY, opt, optNum } from '../shared';

interface TunnelForm {
  name: string; type: VpnType; interfaceName: string; listenPort: string; tunnelAddress: string; publicKey: string; mtu: string;
  remoteGateway: string; localSubnets: string; remoteSubnets: string; ikeVersion: string; proposals: string; isEnabled: boolean; description: string;
}
interface PeerForm { name: string; publicKey: string; endpoint: string; allowedIps: string; persistentKeepalive: string; isEnabled: boolean }

const newTunnel = (type: VpnType, count: number): TunnelForm => ({
  name: '', type, interfaceName: `wg${count}`, listenPort: String(51820 + count), tunnelAddress: '', publicKey: '', mtu: '1420',
  remoteGateway: '', localSubnets: '', remoteSubnets: '', ikeVersion: '2', proposals: 'aes256gcm16-prfsha384-ecp384', isEnabled: true, description: '',
});
const isKey = (k: string) => /^[A-Za-z0-9+/]{43}=$/.test(k.trim());

export function VpnTab({ gatewayId, detail, canWrite }: { gatewayId: string; detail: GatewayDetail; canWrite: boolean }) {
  const [tunnel, setTunnel] = useState<{ id?: string; form: TunnelForm } | null>(null);
  const [peer, setPeer] = useState<{ tunnel: VpnTunnelItem; id?: string; form: PeerForm } | null>(null);
  const [deleting, setDeleting] = useState<{ kind: 'tunnel' | 'peer'; tunnelId: string; id: string; label: string } | null>(null);

  const saveTunnel = useApiMutation(
    (e: NonNullable<typeof tunnel>) => {
      const f = e.form;
      const wg = f.type === 'WireGuard';
      const body = {
        name: f.name.trim(), type: f.type, isEnabled: f.isEnabled, description: opt(f.description), ikeVersion: Number(f.ikeVersion),
        interfaceName: wg ? opt(f.interfaceName) : undefined, listenPort: wg ? optNum(f.listenPort) : undefined, tunnelAddress: wg ? opt(f.tunnelAddress) : undefined,
        publicKey: wg ? opt(f.publicKey) : undefined, mtu: wg ? optNum(f.mtu) : undefined,
        remoteGateway: wg ? undefined : opt(f.remoteGateway), localSubnets: wg ? undefined : opt(f.localSubnets),
        remoteSubnets: wg ? undefined : opt(f.remoteSubnets), proposals: wg ? undefined : opt(f.proposals),
      };
      return e.id ? networkApi.vpn.update(gatewayId, e.id, body) : networkApi.vpn.create(gatewayId, body);
    },
    { invalidate: NETWORK_KEY, success: (t) => `Tunnel '${t.name}' saved`, onSuccess: () => setTunnel(null) },
  );
  const savePeer = useApiMutation(
    (e: NonNullable<typeof peer>) => {
      const f = e.form;
      const body = { name: f.name.trim(), publicKey: f.publicKey.trim(), endpoint: opt(f.endpoint), allowedIps: f.allowedIps, persistentKeepalive: Number(f.persistentKeepalive || 0), isEnabled: f.isEnabled };
      return e.id ? networkApi.updatePeer(gatewayId, e.tunnel.id, e.id, body) : networkApi.createPeer(gatewayId, e.tunnel.id, body);
    },
    { invalidate: NETWORK_KEY, success: (p) => `Peer '${p.name}' saved`, onSuccess: () => setPeer(null) },
  );
  const remove = useApiMutation(
    (d: NonNullable<typeof deleting>) => (d.kind === 'tunnel' ? networkApi.vpn.remove(gatewayId, d.id) : networkApi.deletePeer(gatewayId, d.tunnelId, d.id)),
    { invalidate: NETWORK_KEY, success: 'Removed', onSuccess: () => setDeleting(null) },
  );
  const setT = <K extends keyof TunnelForm>(k: K, v: TunnelForm[K]) => setTunnel((e) => e && { ...e, form: { ...e.form, [k]: v } });
  const setPf = <K extends keyof PeerForm>(k: K, v: PeerForm[K]) => setPeer((e) => e && { ...e, form: { ...e.form, [k]: v } });

  const editTunnel = (t: VpnTunnelItem) => setTunnel({ id: t.id, form: {
    name: t.name, type: t.type, interfaceName: t.interfaceName ?? '', listenPort: t.listenPort?.toString() ?? '', tunnelAddress: t.tunnelAddress ?? '',
    publicKey: t.publicKey ?? '', mtu: t.mtu?.toString() ?? '', remoteGateway: t.remoteGateway ?? '', localSubnets: t.localSubnets ?? '',
    remoteSubnets: t.remoteSubnets ?? '', ikeVersion: String(t.ikeVersion), proposals: t.proposals ?? '', isEnabled: t.isEnabled, description: t.description ?? '' } });

  const columns: Column<VpnTunnelItem>[] = [
    { key: 'name', header: 'Tunnel', value: (t) => t.name, groupable: false, render: (t) => <><div className="cell-title">{t.name}</div>{t.description && <div className="cell-sub">{t.description}</div>}</> },
    { key: 'type', header: 'Type', value: (t) => (t.type === 'WireGuard' ? 'WireGuard' : 'IPsec IKEv' + t.ikeVersion), groupCell: true,
      render: (t) => <Badge tone={t.type === 'WireGuard' ? 'primary' : 'warning'}>{t.type === 'WireGuard' ? 'WireGuard' : `IPsec IKEv${t.ikeVersion}`}</Badge> },
    { key: 'endpoint', header: 'Endpoint', value: (t) => (t.type === 'WireGuard' ? `${t.interfaceName} :${t.listenPort}` : t.remoteGateway), groupable: false,
      render: (t) => t.type === 'WireGuard'
        ? <><code>{t.interfaceName}</code> <span className="muted small">UDP {t.listenPort} · {t.tunnelAddress}</span></>
        : <><code>{t.remoteGateway}</code> <span className="muted small">{t.localSubnets} ⇄ {t.remoteSubnets}</span></> },
    { key: 'peers', header: 'Peers', align: 'right', value: (t) => (t.type === 'WireGuard' ? t.peers.length : null), footer: 'sum' },
    { key: 'status', header: 'Status', value: (t) => (t.isEnabled ? 'Enabled' : 'Disabled'), render: (t) => <StatusBadge status={t.isEnabled ? 'Enabled' : 'Disabled'} /> },
    { key: 'actions', header: '', headerText: 'Actions', hideable: false, className: 'text-right nowrap', render: (t) => canWrite && (
      <span onClick={(e) => e.stopPropagation()}>
        {t.type === 'WireGuard' && <Button size="sm" variant="ghost" iconOnly icon={<UserPlus size={15} />} aria-label="Add peer"
          onClick={() => setPeer({ tunnel: t, form: { name: '', publicKey: '', endpoint: '', allowedIps: '', persistentKeepalive: '25', isEnabled: true } })} />}
        <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={15} />} aria-label="Edit" onClick={() => editTunnel(t)} />
        <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={15} />} aria-label="Delete" onClick={() => setDeleting({ kind: 'tunnel', tunnelId: t.id, id: t.id, label: t.name })} />
      </span>
    ) },
  ];

  const peerRows = (t: VpnTunnelItem) => (
    <div className="stack-sm">
      <div className="section-title">Peers of {t.name}</div>
      {t.peers.length === 0 && <span className="muted small">No peers yet.</span>}
      {t.peers.map((p: VpnPeerItem) => (
        <div key={p.id} className="peer-row">
          <KeyRound size={14} className="muted" />
          <div style={{ minWidth: 0, flex: 1 }}>
            <div className="row" style={{ gap: 8 }}><b>{p.name}</b>{!p.isEnabled && <Badge>Disabled</Badge>}</div>
            <div className="small muted mono truncate" title={p.publicKey}>{p.publicKey}</div>
            <div className="small">Allowed: <code>{p.allowedIps}</code>{p.endpoint && <> · Endpoint <code>{p.endpoint}</code></>}{p.persistentKeepalive > 0 && ` · keepalive ${p.persistentKeepalive}s`}</div>
          </div>
          {canWrite && <>
            <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={14} />} aria-label="Edit peer" onClick={() => setPeer({ tunnel: t, id: p.id, form: {
              name: p.name, publicKey: p.publicKey, endpoint: p.endpoint ?? '', allowedIps: p.allowedIps, persistentKeepalive: String(p.persistentKeepalive), isEnabled: p.isEnabled } })} />
            <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={14} />} aria-label="Delete peer" onClick={() => setDeleting({ kind: 'peer', tunnelId: t.id, id: p.id, label: p.name })} />
          </>}
        </div>
      ))}
    </div>
  );

  const f = tunnel?.form;
  const wgCount = detail.tunnels.filter((t) => t.type === 'WireGuard').length;
  return (
    <>
      <DataTable columns={columns} rows={detail.tunnels} rowKey={(t) => t.id} pivot={false} exportName="vpn-tunnels" storageKey="net-vpn"
        renderDetail={(t) => t.type === 'WireGuard' ? peerRows(t) : (
          <dl className="kv">
            <dt>Remote gateway</dt><dd><code>{t.remoteGateway}</code></dd>
            <dt>Local subnets</dt><dd><code>{t.localSubnets}</code></dd>
            <dt>Remote subnets</dt><dd><code>{t.remoteSubnets}</code></dd>
            <dt>Proposals</dt><dd><code>{t.proposals}</code></dd>
            <dt>Pre-shared key</dt><dd className="muted">Set on the device (never stored here)</dd>
          </dl>
        )}
        toolbar={canWrite && <>
          <Button size="sm" variant="primary" icon={<Plus size={14} />} onClick={() => setTunnel({ form: newTunnel('WireGuard', wgCount) })}>WireGuard</Button>
          <Button size="sm" icon={<Plus size={14} />} onClick={() => setTunnel({ form: newTunnel('IpsecSiteToSite', wgCount) })}>IPsec site-to-site</Button>
        </>}
        empty={{ title: 'No VPN tunnels', description: 'Add WireGuard for remote users/branches or IPsec to connect to partner networks.' }} />

      <Modal open={!!tunnel} onClose={() => setTunnel(null)} size="lg" title={tunnel?.id ? 'Edit tunnel' : f?.type === 'WireGuard' ? 'Add WireGuard tunnel' : 'Add IPsec tunnel'}
        onSubmit={() => tunnel && saveTunnel.mutate(tunnel)}
        footer={<><Button onClick={() => setTunnel(null)}>Cancel</Button><Button type="submit" variant="primary" loading={saveTunnel.isPending}>Save tunnel</Button></>}>
        {f && (
          <div className="form-grid">
            <Field label="Name" hint="letters, digits, - and _"><Input required className="mono" value={f.name} onChange={(e) => setT('name', e.target.value)} /></Field>
            <Field label="Status"><Switch checked={f.isEnabled} onChange={(v) => setT('isEnabled', v)} label={f.isEnabled ? 'Enabled' : 'Disabled'} /></Field>
            {f.type === 'WireGuard' ? <>
              <Field label="Interface"><Input required className="mono" value={f.interfaceName} onChange={(e) => setT('interfaceName', e.target.value)} /></Field>
              <Field label="Listen port (UDP)"><Input required type="number" min={1} max={65535} value={f.listenPort} onChange={(e) => setT('listenPort', e.target.value)} /></Field>
              <Field label="Tunnel address" hint="Gateway address inside the VPN, e.g. 10.200.0.1/24"><Input required className="mono" value={f.tunnelAddress} onChange={(e) => setT('tunnelAddress', e.target.value)} /></Field>
              <Field label="MTU"><Input type="number" min={1280} max={9000} value={f.mtu} onChange={(e) => setT('mtu', e.target.value)} /></Field>
              <Field label="Gateway public key" full hint={f.publicKey && !isKey(f.publicKey) ? <span style={{ color: 'var(--danger)' }}>Not a valid WireGuard key</span>
                : 'Optional, for reference. The private key is created on the gateway and never stored here.'}>
                <Input className="mono" maxLength={44} value={f.publicKey} onChange={(e) => setT('publicKey', e.target.value)} />
              </Field>
            </> : <>
              <Field label="Remote gateway" hint="Partner’s public IP or hostname"><Input required className="mono" value={f.remoteGateway} onChange={(e) => setT('remoteGateway', e.target.value)} /></Field>
              <Field label="IKE version"><Select options={[{ value: '2', label: 'IKEv2 (recommended)' }, { value: '1', label: 'IKEv1 (legacy)' }]} value={f.ikeVersion} onChange={(e) => setT('ikeVersion', e.target.value)} /></Field>
              <Field label="Local subnets" hint="Comma-separated CIDRs"><Input required className="mono" placeholder="10.0.0.0/24" value={f.localSubnets} onChange={(e) => setT('localSubnets', e.target.value)} /></Field>
              <Field label="Remote subnets"><Input required className="mono" placeholder="10.50.0.0/16" value={f.remoteSubnets} onChange={(e) => setT('remoteSubnets', e.target.value)} /></Field>
              <Field label="Proposals" full hint="strongSwan syntax; must match the partner"><Input required className="mono" value={f.proposals} onChange={(e) => setT('proposals', e.target.value)} /></Field>
              <div className="callout full">The pre-shared key is entered on the gateway (swanctl secrets) and is never stored by Security Manager.</div>
            </>}
            <Field label="Description" full><Textarea maxLength={500} value={f.description} onChange={(e) => setT('description', e.target.value)} /></Field>
          </div>
        )}
      </Modal>

      <Modal open={!!peer} onClose={() => setPeer(null)} title={peer?.id ? 'Edit peer' : `Add peer to ${peer?.tunnel.name}`} onSubmit={() => peer && savePeer.mutate(peer)}
        footer={<><Button onClick={() => setPeer(null)}>Cancel</Button><Button type="submit" variant="primary" loading={savePeer.isPending} disabled={!peer || !isKey(peer.form.publicKey)}>Save peer</Button></>}>
        {peer && (
          <div className="form-grid">
            <Field label="Name" full><Input required maxLength={100} placeholder="Branch office router" value={peer.form.name} onChange={(e) => setPf('name', e.target.value)} /></Field>
            <Field label="Public key" full hint={peer.form.publicKey && !isKey(peer.form.publicKey) ? <span style={{ color: 'var(--danger)' }}>44-character base64 key ending in “=”</span> : 'From “wg pubkey” on the peer'}>
              <Input required className="mono" maxLength={44} value={peer.form.publicKey} onChange={(e) => setPf('publicKey', e.target.value)} />
            </Field>
            <Field label="Allowed IPs" full hint="Peer’s tunnel IP plus any networks behind it, e.g. 10.200.0.10/32, 10.30.0.0/16">
              <Input required className="mono" value={peer.form.allowedIps} onChange={(e) => setPf('allowedIps', e.target.value)} />
            </Field>
            <Field label="Endpoint" hint="host:port, if the peer has a fixed address"><Input className="mono" placeholder="branch.example.net:51820" value={peer.form.endpoint} onChange={(e) => setPf('endpoint', e.target.value)} /></Field>
            <Field label="Keepalive (s)" hint="25 for peers behind NAT, 0 = off"><Input type="number" min={0} max={65535} value={peer.form.persistentKeepalive} onChange={(e) => setPf('persistentKeepalive', e.target.value)} /></Field>
            <Field label="Status"><Switch checked={peer.form.isEnabled} onChange={(v) => setPf('isEnabled', v)} label={peer.form.isEnabled ? 'Enabled' : 'Disabled'} /></Field>
          </div>
        )}
      </Modal>

      <ConfirmDialog open={!!deleting} title={deleting?.kind === 'tunnel' ? 'Remove tunnel' : 'Remove peer'}
        message={<>Remove <b>{deleting?.label}</b>{deleting?.kind === 'tunnel' ? ' and all of its peers' : ''}?</>}
        confirmLabel="Remove" loading={remove.isPending} onConfirm={() => deleting && remove.mutate(deleting)} onClose={() => setDeleting(null)} />
    </>
  );
}
