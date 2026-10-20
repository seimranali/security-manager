import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { Crown, Pencil, Plus, ServerCog, Trash2 } from 'lucide-react';
import { networkApi } from '../../lib/api';
import type { HaClusterItem, VirtualIpItem } from '../../lib/types';
import { useApiMutation } from '../../hooks/useApiMutation';
import { useAuth } from '../../hooks/useAuth';
import { Card, CardBody, CardHeader, PageHeader } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { EmptyState, Spinner } from '../../components/ui/DataTable';
import { Field, Input, Switch, Textarea } from '../../components/ui/Form';
import { ConfirmDialog, Modal } from '../../components/ui/Modal';
import { NETWORK_KEY, opt } from './shared';

interface ClusterForm { name: string; description: string; vrrpRouterId: string; advertIntervalSeconds: string; preempt: boolean }
interface VipForm { interface: string; address: string; description: string }

export default function ClustersPage() {
  const { canWrite } = useAuth();
  const clusters = useQuery({ queryKey: ['network', 'clusters'], queryFn: networkApi.clusters });
  const [cluster, setCluster] = useState<{ id?: string; form: ClusterForm } | null>(null);
  const [vip, setVip] = useState<{ cluster: HaClusterItem; id?: string; form: VipForm } | null>(null);
  const [deleting, setDeleting] = useState<{ cluster: HaClusterItem; vip?: VirtualIpItem } | null>(null);

  const saveCluster = useApiMutation(
    (e: NonNullable<typeof cluster>) => {
      const body = { name: e.form.name.trim(), description: opt(e.form.description), vrrpRouterId: Number(e.form.vrrpRouterId), advertIntervalSeconds: Number(e.form.advertIntervalSeconds), preempt: e.form.preempt };
      return e.id ? networkApi.updateCluster(e.id, body) : networkApi.createCluster(body);
    },
    { invalidate: NETWORK_KEY, success: (c) => `Cluster '${c.name}' saved`, onSuccess: () => setCluster(null) },
  );
  const saveVip = useApiMutation(
    (e: NonNullable<typeof vip>) => {
      const body = { interface: e.form.interface.trim(), address: e.form.address.trim(), description: opt(e.form.description) };
      return e.id ? networkApi.updateVip(e.cluster.id, e.id, body) : networkApi.addVip(e.cluster.id, body);
    },
    { invalidate: NETWORK_KEY, success: (v) => `Virtual IP ${v.address} saved`, onSuccess: () => setVip(null) },
  );
  const remove = useApiMutation(
    (d: NonNullable<typeof deleting>) => (d.vip ? networkApi.deleteVip(d.cluster.id, d.vip.id) : networkApi.deleteCluster(d.cluster.id)),
    { invalidate: NETWORK_KEY, success: 'Removed', onSuccess: () => setDeleting(null) },
  );
  const setC = <K extends keyof ClusterForm>(k: K, v: ClusterForm[K]) => setCluster((e) => e && { ...e, form: { ...e.form, [k]: v } });
  const setV = <K extends keyof VipForm>(k: K, v: VipForm[K]) => setVip((e) => e && { ...e, form: { ...e.form, [k]: v } });

  return (
    <div className="stack">
      <PageHeader
        title="High availability"
        description="Active/passive gateway clusters using VRRP (keepalived). The member with the highest priority holds the virtual IPs; a backup takes over within seconds if it fails. Assign gateways to a cluster from the Gateways page."
        actions={canWrite && <Button variant="primary" icon={<Plus size={16} />}
          onClick={() => setCluster({ form: { name: '', description: '', vrrpRouterId: '51', advertIntervalSeconds: '1', preempt: true } })}>New cluster</Button>}
      />

      {clusters.isLoading ? <Spinner /> : !clusters.data?.length ? (
        <Card><EmptyState icon={<ServerCog size={22} />} title="No HA clusters" description="Create a cluster, then add two or more gateways to it." /></Card>
      ) : clusters.data.map((c) => (
        <Card key={c.id}>
          <CardHeader
            title={<span className="row" style={{ gap: 8 }}><ServerCog size={17} /> {c.name}</span>}
            subtitle={`${c.description ? `${c.description} · ` : ''}VRRP id ${c.vrrpRouterId}${c.virtualIps.length > 1 ? `–${c.vrrpRouterId + new Set(c.virtualIps.map((v) => v.interface)).size - 1}` : ''} · advert ${c.advertIntervalSeconds}s · ${c.preempt ? 'preemptive' : 'non-preemptive'}`}
            actions={canWrite && <>
              <Button size="sm" icon={<Pencil size={14} />} onClick={() => setCluster({ id: c.id, form: { name: c.name, description: c.description ?? '', vrrpRouterId: String(c.vrrpRouterId), advertIntervalSeconds: String(c.advertIntervalSeconds), preempt: c.preempt } })}>Edit</Button>
              <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={14} />} aria-label="Delete cluster" onClick={() => setDeleting({ cluster: c })} />
            </>}
          />
          <CardBody>
            <div className="grid grid-2">
              <div>
                <div className="section-title">Members</div>
                {c.members.length === 0 && <div className="callout">No members yet — edit a gateway and choose this cluster.</div>}
                {c.members.length === 1 && <div className="callout" style={{ marginBottom: 8 }}>Only one member: add a second gateway for failover.</div>}
                <div className="stack-sm">
                  {c.members.map((m) => (
                    <Link key={m.gatewayId} to={`/network/gateways/${m.gatewayId}`} className={`ha-member ${m.role === 'Master' ? 'master' : ''}`}>
                      {m.role === 'Master' ? <Crown size={16} color="var(--warning)" /> : <ServerCog size={16} className="muted" />}
                      <div style={{ minWidth: 0, flex: 1 }}>
                        <div className="cell-title">{m.name}</div>
                        <div className="cell-sub mono">{m.hostname}</div>
                      </div>
                      {m.pendingChanges && <Badge tone="warning">Pending deploy</Badge>}
                      <Badge tone={m.role === 'Master' ? 'success' : 'neutral'} dot>{m.role}</Badge>
                      <span className="small muted">p{m.priority}</span>
                    </Link>
                  ))}
                </div>
              </div>
              <div>
                <div className="row" style={{ marginBottom: 8 }}>
                  <div className="section-title" style={{ margin: 0 }}>Virtual IPs</div>
                  <span className="spacer" />
                  {canWrite && <Button size="sm" icon={<Plus size={14} />} onClick={() => setVip({ cluster: c, form: { interface: '', address: '', description: '' } })}>Add VIP</Button>}
                </div>
                {c.virtualIps.length === 0 && <div className="callout">No virtual IPs. Add the address clients use as their gateway (e.g. 10.0.0.1/24 on the LAN interface).</div>}
                <div className="stack-sm">
                  {c.virtualIps.map((v) => (
                    <div key={v.id} className="ha-member">
                      <code className="cell-title">{v.address}</code>
                      <span className="muted small">on <code>{v.interface}</code></span>
                      <span className="small truncate" style={{ flex: 1 }}>{v.description}</span>
                      {canWrite && <>
                        <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={14} />} aria-label="Edit VIP"
                          onClick={() => setVip({ cluster: c, id: v.id, form: { interface: v.interface, address: v.address, description: v.description ?? '' } })} />
                        <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={14} />} aria-label="Delete VIP" onClick={() => setDeleting({ cluster: c, vip: v })} />
                      </>}
                    </div>
                  ))}
                </div>
              </div>
            </div>
          </CardBody>
        </Card>
      ))}

      <Modal open={!!cluster} onClose={() => setCluster(null)} title={cluster?.id ? 'Edit cluster' : 'New HA cluster'} onSubmit={() => cluster && saveCluster.mutate(cluster)}
        footer={<><Button onClick={() => setCluster(null)}>Cancel</Button><Button type="submit" variant="primary" loading={saveCluster.isPending}>Save cluster</Button></>}>
        {cluster && (
          <div className="form-grid">
            <Field label="Name" full><Input required minLength={2} maxLength={100} value={cluster.form.name} onChange={(e) => setC('name', e.target.value)} /></Field>
            <Field label="VRRP router id" hint="1–255, unique per network segment"><Input required type="number" min={1} max={255} value={cluster.form.vrrpRouterId} onChange={(e) => setC('vrrpRouterId', e.target.value)} /></Field>
            <Field label="Advertisement interval (s)" hint="Failover takes ~3× this"><Input required type="number" min={1} max={255} value={cluster.form.advertIntervalSeconds} onChange={(e) => setC('advertIntervalSeconds', e.target.value)} /></Field>
            <Field label="Preemption" full hint="When on, a recovered higher-priority node takes the VIPs back automatically.">
              <Switch checked={cluster.form.preempt} onChange={(v) => setC('preempt', v)} label={cluster.form.preempt ? 'Preemptive' : 'Stay on current master'} />
            </Field>
            <Field label="Description" full><Textarea maxLength={500} value={cluster.form.description} onChange={(e) => setC('description', e.target.value)} /></Field>
          </div>
        )}
      </Modal>

      <Modal open={!!vip} onClose={() => setVip(null)} title={vip?.id ? 'Edit virtual IP' : `Add virtual IP to ${vip?.cluster.name}`} onSubmit={() => vip && saveVip.mutate(vip)}
        footer={<><Button onClick={() => setVip(null)}>Cancel</Button><Button type="submit" variant="primary" loading={saveVip.isPending}>Save</Button></>}>
        {vip && (
          <div className="form-grid">
            <Field label="Interface" hint="Must exist on every member"><Input required className="mono" placeholder="eth1" value={vip.form.interface} onChange={(e) => setV('interface', e.target.value)} /></Field>
            <Field label="Virtual IP (CIDR)" hint="Same subnet as the interface"><Input required className="mono" placeholder="10.0.0.1/24" value={vip.form.address} onChange={(e) => setV('address', e.target.value)} /></Field>
            <Field label="Description" full><Input maxLength={500} placeholder="LAN default gateway" value={vip.form.description} onChange={(e) => setV('description', e.target.value)} /></Field>
          </div>
        )}
      </Modal>

      <ConfirmDialog open={!!deleting} title={deleting?.vip ? 'Remove virtual IP' : 'Delete cluster'}
        message={deleting?.vip ? <>Remove <code>{deleting.vip.address}</code>? Clients using it as their gateway will lose connectivity after deployment.</>
          : <>Delete <b>{deleting?.cluster.name}</b>? Its gateways stay in place but become standalone.</>}
        confirmLabel={deleting?.vip ? 'Remove' : 'Delete'} loading={remove.isPending} onConfirm={() => deleting && remove.mutate(deleting)} onClose={() => setDeleting(null)} />
    </div>
  );
}
