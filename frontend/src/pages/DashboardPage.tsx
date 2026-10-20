import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { Area, AreaChart, Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { Bell, ChevronRight, Crosshair, FileWarning, Server, ShieldBan, ShieldCheck } from 'lucide-react';
import { alertApi, authApi, feedApi, firewallApi, notificationApi, scannerApi, securityApi, systemApi } from '../lib/api';
import { computePosture } from '../lib/posture';
import { useAuth } from '../hooks/useAuth';
import { formatDay, formatNumber, formatTime, timeAgo } from '../lib/format';
import { useChartColors } from '../hooks/useTheme';
import { Card, CardBody, CardHeader, PageHeader, StatCard } from '../components/ui/Card';
import { SeverityBadge, StatusBadge } from '../components/ui/Badge';
import { ChartLegend, ChartTooltip, RiskMeter } from '../components/ui/Charts';
import { EmptyState, Spinner } from '../components/ui/DataTable';

const REFRESH = 30_000;

export default function DashboardPage() {
  const c = useChartColors();
  const firewall = useQuery({ queryKey: ['firewall', 'stats'], queryFn: firewallApi.stats, refetchInterval: REFRESH });
  const security = useQuery({ queryKey: ['detections', 'stats'], queryFn: securityApi.stats, refetchInterval: REFRESH });
  const scanner = useQuery({ queryKey: ['scan-jobs', 'stats'], queryFn: scannerApi.stats, refetchInterval: REFRESH });
  const alerts = useQuery({ queryKey: ['alerts', 'stats'], queryFn: alertApi.stats, refetchInterval: REFRESH });
  const health = useQuery({ queryKey: ['health'], queryFn: systemApi.health, refetchInterval: REFRESH });
  const { hasRole } = useAuth();
  const isAdmin = hasRole('Admin');
  const enforcement = useQuery({ queryKey: ['enforcement'], queryFn: firewallApi.enforcement, refetchInterval: REFRESH });
  const feeds = useQuery({ queryKey: ['threat-feeds'], queryFn: feedApi.feeds, refetchInterval: REFRESH });
  const users = useQuery({ queryKey: ['users'], queryFn: authApi.users, enabled: isAdmin });
  const channels = useQuery({ queryKey: ['notification-channels'], queryFn: notificationApi.channels, enabled: isAdmin });
  const posture = computePosture({
    enforcement: enforcement.data, scanner: scanner.data, alerts: alerts.data, security: security.data,
    feeds: feeds.data, users: isAdmin ? users.data : undefined, channels: isAdmin ? channels.data : undefined,
  });
  const postureReady = !!(enforcement.data && scanner.data && alerts.data && security.data);

  const healthy = health.data?.filter((s) => s.status === 'Healthy').length ?? 0;
  const axis = { stroke: c.axis, fontSize: 11, tickLine: false, axisLine: false };
  const severityKeys = ['critical', 'high', 'medium', 'low', 'info'] as const;

  return (
    <div className="stack">
      <PageHeader title="Security Overview" description="Real-time posture across firewall, detections, scanning and alerts." />

      <div className="grid grid-kpi">
        <StatCard label="Active alerts" icon={<Bell size={20} />} tone="danger"
          value={formatNumber(alerts.data ? alerts.data.open + alerts.data.acknowledged : undefined)}
          hint={`${alerts.data?.criticalActive ?? 0} critical`} />
        <StatCard label="Open detections" icon={<Crosshair size={20} />} tone="warning"
          value={formatNumber(security.data?.openDetections)}
          hint={`${formatNumber(security.data?.events24h)} events in 24h`} />
        <StatCard label="Blocked connections (24h)" icon={<ShieldBan size={20} />}
          value={formatNumber(firewall.data?.denied24h)}
          hint={`${formatNumber(firewall.data?.allowed24h)} allowed`} />
        <StatCard label="Open vulnerabilities" icon={<FileWarning size={20} />} tone="danger"
          value={formatNumber(scanner.data?.openFindings)}
          hint={`${scanner.data?.criticalOpenFindings ?? 0} critical`} />
        <StatCard label="Services healthy" icon={<Server size={20} />} tone={healthy === 4 ? 'success' : 'warning'}
          value={health.data ? `${healthy}/${health.data.length}` : '—'}
          hint={`${firewall.data?.activeRules ?? 0} firewall rules active`} />
      </div>

      <Card>
        <CardHeader title="Security posture" subtitle="Weighted score across enforcement, vulnerabilities, alerts, MFA, threat intel and notifications. Worst areas first." />
        <CardBody>
          {!postureReady ? <Spinner /> : (
            <div className="posture">
              <div className={`posture-score grade-${posture.grade.toLowerCase()}`} aria-label={`Posture score ${posture.score} out of 100, grade ${posture.grade}`}>
                <span className="posture-value">{posture.score}</span>
                <span className="posture-grade">Grade {posture.grade}</span>
              </div>
              <div className="posture-factors">
                {posture.factors.map((f) => (
                  <Link key={f.key} to={f.link} className="posture-factor">
                    <span className={`posture-dot ${f.score >= 0.9 ? 'ok' : f.score >= 0.5 ? 'warn' : 'bad'}`} aria-hidden />
                    <span className="posture-label">{f.label}</span>
                    <span className="posture-detail">{f.detail}</span>
                    <span className="posture-pct">{Math.round(f.score * 100)}%</span>
                    <ChevronRight size={14} className="muted" />
                  </Link>
                ))}
              </div>
            </div>
          )}
        </CardBody>
      </Card>

      <div className="grid grid-3">
        <Card className="span-2">
          <CardHeader title="Firewall traffic" subtitle="Connections per hour, last 24 hours"
            actions={<ChartLegend items={[{ label: 'Allowed', color: c.series1 }, { label: 'Denied', color: c.series2 }]} />} />
          <CardBody>
            {firewall.data ? (
              <ResponsiveContainer width="100%" height={260}>
                <AreaChart data={firewall.data.timeline} margin={{ top: 8, right: 8, left: -18, bottom: 0 }}>
                  <CartesianGrid vertical={false} stroke={c.grid} />
                  <XAxis dataKey="hour" tickFormatter={formatTime} {...axis} minTickGap={24} />
                  <YAxis allowDecimals={false} {...axis} />
                  <Tooltip content={<ChartTooltip labelFormatter={formatTime} />} cursor={{ stroke: c.axis, strokeDasharray: '3 3' }} />
                  <Area type="monotone" dataKey="allowed" name="Allowed" stroke={c.series1} fill={c.series1} fillOpacity={0.12} strokeWidth={2} />
                  <Area type="monotone" dataKey="denied" name="Denied" stroke={c.series2} fill={c.series2} fillOpacity={0.12} strokeWidth={2} />
                </AreaChart>
              </ResponsiveContainer>
            ) : <Spinner />}
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Service health" subtitle="Via API gateway" />
          <div className="list" style={{ marginTop: 8 }}>
            {health.isError && <div className="list-item"><span className="callout callout-danger" style={{ flex: 1 }}>Gateway unreachable</span></div>}
            {health.data?.map((s) => (
              <div key={s.service} className="list-item">
                <ShieldCheck size={16} className="muted" />
                <span style={{ textTransform: 'capitalize', fontWeight: 500 }}>{s.service} service</span>
                <span className="spacer" />
                {s.latencyMs != null && <span className="muted small">{s.latencyMs} ms</span>}
                <StatusBadge status={s.status} />
              </div>
            ))}
          </div>
        </Card>
      </div>

      <div className="grid grid-3">
        <Card className="span-2">
          <CardHeader title="Alerts by severity" subtitle="New alerts per day, last 7 days"
            actions={<ChartLegend items={severityKeys.map((k) => ({ label: k[0].toUpperCase() + k.slice(1), color: c.severity[k[0].toUpperCase() + k.slice(1)] }))} />} />
          <CardBody>
            {alerts.data ? (
              <ResponsiveContainer width="100%" height={240}>
                <BarChart data={alerts.data.last7Days} margin={{ top: 8, right: 8, left: -18, bottom: 0 }} barCategoryGap="28%">
                  <CartesianGrid vertical={false} stroke={c.grid} />
                  <XAxis dataKey="day" tickFormatter={formatDay} {...axis} />
                  <YAxis allowDecimals={false} {...axis} />
                  <Tooltip content={<ChartTooltip labelFormatter={formatDay} />} cursor={{ fill: c.grid, opacity: 0.4 }} />
                  {severityKeys.map((k, i) => {
                    const name = k[0].toUpperCase() + k.slice(1);
                    return (
                      <Bar key={k} dataKey={k} name={name} stackId="s" fill={c.severity[name]} stroke={c.surface} strokeWidth={1}
                        radius={i === severityKeys.length - 1 ? [4, 4, 0, 0] : 0} />
                    );
                  })}
                </BarChart>
              </ResponsiveContainer>
            ) : <Spinner />}
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Latest active alerts" actions={<Link to="/alerts" className="small">View all</Link>} />
          <div className="list" style={{ marginTop: 8 }}>
            {alerts.data?.latest.length === 0 && <EmptyState icon={<Bell size={20} />} title="All clear" description="No active alerts." />}
            {alerts.data?.latest.map((a) => (
              <div key={a.id} className="list-item">
                <div style={{ minWidth: 0, flex: 1 }}>
                  <div className="truncate" style={{ fontWeight: 500 }}>{a.title}</div>
                  <div className="muted small">{a.source} · {timeAgo(a.lastOccurredAt)}</div>
                </div>
                <SeverityBadge severity={a.severity} />
              </div>
            ))}
          </div>
        </Card>
      </div>

      <div className="grid grid-3">
        <Card>
          <CardHeader title="Security events" subtitle="Per hour, last 24 hours" />
          <CardBody>
            {security.data ? (
              <ResponsiveContainer width="100%" height={200}>
                <BarChart data={security.data.timeline} margin={{ top: 8, right: 4, left: -24, bottom: 0 }}>
                  <CartesianGrid vertical={false} stroke={c.grid} />
                  <XAxis dataKey="hour" tickFormatter={formatTime} {...axis} minTickGap={30} />
                  <YAxis allowDecimals={false} {...axis} />
                  <Tooltip content={<ChartTooltip labelFormatter={formatTime} />} cursor={{ fill: c.grid, opacity: 0.4 }} />
                  <Bar dataKey="events" name="Events" fill={c.series1} radius={[4, 4, 0, 0]} />
                </BarChart>
              </ResponsiveContainer>
            ) : <Spinner />}
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Top attacking sources" subtitle="By event volume, 24h" />
          <div className="list" style={{ marginTop: 8 }}>
            {security.data?.topSources.map((s) => {
              const max = security.data!.topSources[0]?.count || 1;
              return (
                <div key={s.name} className="list-item">
                  <code style={{ minWidth: 120 }}>{s.name}</code>
                  <div className="progress" style={{ flex: 1 }}><span style={{ width: `${(s.count / max) * 100}%`, background: c.series2 }} /></div>
                  <b style={{ minWidth: 32, textAlign: 'right' }}>{s.count}</b>
                </div>
              );
            })}
          </div>
        </Card>

        <Card>
          <CardHeader title="Highest-risk targets" actions={<Link to="/scanner/targets" className="small">Targets</Link>} />
          <div className="list" style={{ marginTop: 8 }}>
            {scanner.data?.topRiskTargets.length === 0 && <EmptyState icon={<FileWarning size={20} />} title="No scans yet" />}
            {scanner.data?.topRiskTargets.map((t) => (
              <div key={t.id} className="list-item">
                <div style={{ minWidth: 0, flex: 1 }}>
                  <div className="truncate" style={{ fontWeight: 500 }}>{t.name}</div>
                  <div className="muted small">{t.host} · {timeAgo(t.lastScannedAt)}</div>
                </div>
                <div style={{ width: 110 }}><RiskMeter score={t.riskScore} /></div>
              </div>
            ))}
          </div>
        </Card>
      </div>
    </div>
  );
}
