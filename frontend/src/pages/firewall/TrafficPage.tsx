import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { ShieldCheck, ShieldX, Zap } from 'lucide-react';
import { GRID_LIMIT, firewallApi } from '../../lib/api';
import { formatDateTime } from '../../lib/format';
import type { EvaluateInput, EvaluationResult, TrafficLog } from '../../lib/types';
import { useApiMutation } from '../../hooks/useApiMutation';
import { Card, CardBody, CardHeader, PageHeader } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { StatusBadge } from '../../components/ui/Badge';
import { DatePicker, Field, Input, Select, type DateRange } from '../../components/ui/Form';
import { isCompleteRange } from '../../components/ui/DatePicker';
import { dayBoundsUtc, today } from '../../lib/dates';
import { DataTable, type Column } from '../../components/ui/DataTable';

const PRESETS: { label: string; input: EvaluateInput }[] = [
  { label: 'HTTPS from internet', input: { sourceIp: '8.8.8.8', destinationIp: '10.0.1.15', sourcePort: 51544, destinationPort: 443, protocol: 'Tcp', direction: 'Inbound' } },
  { label: 'RDP attempt', input: { sourceIp: '185.220.101.4', destinationIp: '10.0.1.15', sourcePort: 50122, destinationPort: 3389, protocol: 'Tcp', direction: 'Inbound' } },
  { label: 'Threat-intel source', input: { sourceIp: '203.0.113.45', destinationIp: '10.0.2.40', sourcePort: 40000, destinationPort: 443, protocol: 'Tcp', direction: 'Inbound' } },
  { label: 'Outbound to C2', input: { sourceIp: '10.0.3.8', destinationIp: '198.51.100.23', sourcePort: 49152, destinationPort: 8080, protocol: 'Tcp', direction: 'Outbound' } },
];

export default function TrafficPage() {
  const [form, setForm] = useState<EvaluateInput>(PRESETS[0].input);
  const [result, setResult] = useState<EvaluationResult | null>(null);
  const [action, setAction] = useState('');
  const [dates, setDates] = useState<DateRange>({ start: '', end: '' });
  const bounds = isCompleteRange(dates) ? dayBoundsUtc(dates.start, dates.end) : null;

  const logs = useQuery({
    queryKey: ['firewall', 'traffic', action, bounds?.from, bounds?.to],
    queryFn: () => firewallApi.traffic({ action, from: bounds?.from, to: bounds?.to, page: 1, pageSize: GRID_LIMIT }),
    placeholderData: keepPreviousData,
    refetchInterval: 30_000,
  });

  const evaluate = useApiMutation(firewallApi.evaluate, { invalidate: [['firewall']], onSuccess: setResult });
  const set = <K extends keyof EvaluateInput>(k: K, v: EvaluateInput[K]) => setForm((f) => ({ ...f, [k]: v }));
  const portValue = (v: string) => (v === '' ? null : Number(v));

  const columns: Column<TrafficLog>[] = [
    { key: 'time', header: 'Time', value: (l) => new Date(l.timestamp), filter: false, footer: 'count',
      render: (l) => <span className="nowrap small">{formatDateTime(l.timestamp)}</span> },
    { key: 'action', header: 'Action', value: (l) => l.action, groupCell: true, render: (l) => <StatusBadge status={l.action} /> },
    { key: 'srcIp', header: 'IP', headerText: 'Source IP', group: 'Source', value: (l) => l.sourceIp, render: (l) => <code>{l.sourceIp}</code> },
    { key: 'srcPort', header: 'Port', headerText: 'Source port', group: 'Source', value: (l) => l.sourcePort, hidden: true },
    { key: 'dstIp', header: 'IP', headerText: 'Destination IP', group: 'Destination', value: (l) => l.destinationIp, render: (l) => <code>{l.destinationIp}</code> },
    { key: 'dstPort', header: 'Port', headerText: 'Destination port', group: 'Destination', value: (l) => l.destinationPort, align: 'right' },
    { key: 'protocol', header: 'Protocol', value: (l) => l.protocol.toUpperCase() },
    { key: 'direction', header: 'Direction', value: (l) => l.direction },
    { key: 'rule', header: 'Matched rule', value: (l) => l.matchedRuleName ?? 'Default action' },
    { key: 'policy', header: 'Policy', value: (l) => l.policyName ?? 'Implicit deny' },
  ];

  return (
    <div className="stack">
      <PageHeader title="Traffic" description="Test packets against live policies and inspect the decision log." />

      <Card>
        <CardHeader title="Packet tester" subtitle="Runs the real firewall engine. Denied packets are forwarded to the Security Service as events." />
        <CardBody>
          <div className="stack">
            <div className="chip-group">
              {PRESETS.map((p) => <button key={p.label} type="button" className="chip" onClick={() => { setForm(p.input); setResult(null); }}>{p.label}</button>)}
            </div>
            <form className="form-grid" style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))' }}
              onSubmit={(e) => { e.preventDefault(); evaluate.mutate(form); }}>
              <Field label="Source IP"><Input required className="mono" value={form.sourceIp} onChange={(e) => set('sourceIp', e.target.value)} /></Field>
              <Field label="Source port"><Input type="number" min={0} max={65535} value={form.sourcePort ?? ''} onChange={(e) => set('sourcePort', portValue(e.target.value))} /></Field>
              <Field label="Destination IP"><Input required className="mono" value={form.destinationIp} onChange={(e) => set('destinationIp', e.target.value)} /></Field>
              <Field label="Destination port"><Input type="number" min={0} max={65535} value={form.destinationPort ?? ''} onChange={(e) => set('destinationPort', portValue(e.target.value))} /></Field>
              <Field label="Protocol"><Select options={[{ value: 'Tcp', label: 'TCP' }, { value: 'Udp', label: 'UDP' }, { value: 'Icmp', label: 'ICMP' }]} value={form.protocol} onChange={(e) => set('protocol', e.target.value as EvaluateInput['protocol'])} /></Field>
              <Field label="Direction"><Select options={['Inbound', 'Outbound']} value={form.direction} onChange={(e) => set('direction', e.target.value as EvaluateInput['direction'])} /></Field>
              <div className="field" style={{ justifyContent: 'flex-end' }}>
                <Button type="submit" variant="primary" icon={<Zap size={16} />} loading={evaluate.isPending}>Evaluate</Button>
              </div>
            </form>
            {result && (
              <div className={`result-card ${result.action === 'Allow' ? 'allow' : 'deny'}`} role="status">
                {result.action === 'Allow' ? <ShieldCheck size={28} color="var(--success)" /> : <ShieldX size={28} color="var(--danger)" />}
                <div>
                  <div style={{ fontWeight: 700, fontSize: 16 }}>{result.action === 'Allow' ? 'Allowed' : 'Denied'}</div>
                  <div>{result.reason}</div>
                  <div className="muted small">Evaluated {formatDateTime(result.evaluatedAt)}</div>
                </div>
              </div>
            )}
          </div>
        </CardBody>
      </Card>

      <Card>
        <CardHeader title="Traffic log" subtitle="Refreshes every 30 seconds. Expand a row to see why it was allowed or denied." />
        <div style={{ marginTop: 12 }}>
          <DataTable
            columns={columns} rows={logs.data?.items} totalCount={logs.data?.totalCount} rowKey={(l) => l.id} loading={logs.isLoading}
            initialSort={[{ key: 'time', dir: 'desc' }]} exportName="traffic-log" storageKey="traffic-log"
            toolbar={<>
              <DatePicker mode="range" aria-label="Date range" placeholder="Any date" max={today()} value={dates} onChange={setDates} />
              <Select options={['Allow', 'Deny']} placeholder="Any action" aria-label="Action" value={action} onChange={(e) => setAction(e.target.value)} />
            </>}
            renderDetail={(l) => (
              <dl className="kv">
                <dt>Decision</dt><dd>{l.reason}</dd>
                <dt>Connection</dt><dd><code>{l.sourceIp}:{l.sourcePort ?? '*'}</code> → <code>{l.destinationIp}:{l.destinationPort ?? '*'}</code> ({l.protocol.toUpperCase()}, {l.direction})</dd>
                <dt>Timestamp</dt><dd>{formatDateTime(l.timestamp)}</dd>
              </dl>
            )}
            empty={{ title: 'No traffic recorded' }}
          />
        </div>
      </Card>
    </div>
  );
}
