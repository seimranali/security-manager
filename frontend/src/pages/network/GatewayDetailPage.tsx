import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Link, useParams } from 'react-router-dom';
import { ArrowLeft, CheckCheck, Download, FileCode2, Network, Route, Shuffle, ShieldEllipsis } from 'lucide-react';
import { networkApi } from '../../lib/api';
import { cn } from '../../lib/format';
import { useApiMutation } from '../../hooks/useApiMutation';
import { useAuth } from '../../hooks/useAuth';
import { Card, CardBody, CardHeader, PageHeader } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { Spinner } from '../../components/ui/DataTable';
import { DeployStatus, IssueList, NETWORK_KEY } from './shared';
import { InterfacesTab } from './tabs/InterfacesTab';
import { RoutingTab } from './tabs/RoutingTab';
import { NatTab } from './tabs/NatTab';
import { VpnTab } from './tabs/VpnTab';
import { ConfigTab } from './tabs/ConfigTab';

type TabKey = 'interfaces' | 'routing' | 'nat' | 'vpn' | 'config';

export default function GatewayDetailPage() {
  const { id = '' } = useParams();
  const { canWrite } = useAuth();
  const [tab, setTab] = useState<TabKey>('interfaces');
  const detail = useQuery({ queryKey: ['network', 'gateway', id], queryFn: () => networkApi.gateway(id) });
  const deploy = useApiMutation(() => networkApi.markDeployed(id), { invalidate: NETWORK_KEY, success: (g) => `Revision ${g.deployedRevision} marked as deployed` });
  const [downloading, setDownloading] = useState(false);

  if (!detail.data) return detail.isLoading ? <Spinner /> : <div className="callout callout-danger">Gateway not found.</div>;
  const d = detail.data;
  const g = d.gateway;
  const errors = d.issues.filter((i) => i.severity === 'Error').length;

  const tabs: { key: TabKey; label: string; icon: React.ReactNode; count?: number }[] = [
    { key: 'interfaces', label: 'Interfaces', icon: <Network size={15} />, count: d.interfaces.length },
    { key: 'routing', label: 'Routing', icon: <Route size={15} />, count: d.routes.length + d.policies.length },
    { key: 'nat', label: 'NAT', icon: <Shuffle size={15} />, count: d.natRules.length },
    { key: 'vpn', label: 'VPN', icon: <ShieldEllipsis size={15} />, count: d.tunnels.length },
    { key: 'config', label: 'Generated config', icon: <FileCode2 size={15} /> },
  ];

  const download = async () => {
    setDownloading(true);
    try { await networkApi.downloadBundle(id); } finally { setDownloading(false); }
  };

  return (
    <div className="stack">
      <Link to="/network/gateways" className="row small muted" style={{ gap: 4 }}><ArrowLeft size={14} /> All gateways</Link>
      <PageHeader
        title={g.name}
        description={`${g.hostname}${g.managementAddress ? ` · management ${g.managementAddress}` : ''}${g.description ? ` · ${g.description}` : ''}`}
        actions={<>
          <DeployStatus g={g} />
          {g.haRole && <Badge tone={g.haRole === 'Master' ? 'success' : 'neutral'} dot>{g.clusterName}: {g.haRole} (priority {g.haPriority})</Badge>}
          <Button icon={<Download size={16} />} loading={downloading} onClick={download}>Download config</Button>
          {canWrite && (
            <Button variant="primary" icon={<CheckCheck size={16} />} disabled={!g.pendingChanges || errors > 0} loading={deploy.isPending}
              title={errors > 0 ? 'Fix configuration errors first' : 'Record that this revision has been applied to the device'}
              onClick={() => deploy.mutate(undefined)}>
              Mark r{g.configRevision} deployed
            </Button>
          )}
        </>}
      />

      <Card>
        <CardHeader title="Configuration checks" subtitle={`${errors} error(s), ${d.issues.filter((i) => i.severity === 'Warning').length} warning(s) across interfaces, routing, NAT, VPN and HA`} />
        <CardBody><IssueList issues={d.issues} /></CardBody>
      </Card>

      <Card>
        <div className="tabs" role="tablist">
          {tabs.map((t) => (
            <button key={t.key} type="button" role="tab" aria-selected={tab === t.key} className={cn('tab', tab === t.key && 'active')} onClick={() => setTab(t.key)}>
              {t.icon}{t.label}{t.count != null && <span className="tab-count">{t.count}</span>}
            </button>
          ))}
        </div>
        {tab === 'interfaces' && <InterfacesTab gatewayId={id} detail={d} canWrite={canWrite} />}
        {tab === 'routing' && <RoutingTab gatewayId={id} detail={d} canWrite={canWrite} />}
        {tab === 'nat' && <NatTab gatewayId={id} detail={d} canWrite={canWrite} />}
        {tab === 'vpn' && <VpnTab gatewayId={id} detail={d} canWrite={canWrite} />}
        {tab === 'config' && <ConfigTab gatewayId={id} revision={g.configRevision} />}
      </Card>
    </div>
  );
}
