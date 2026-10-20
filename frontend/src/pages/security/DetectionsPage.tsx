import { useEffect, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Ban } from 'lucide-react';
import { GRID_LIMIT, firewallApi, securityApi } from '../../lib/api';
import { isValidIp } from '../../lib/ip';
import { formatDateTime, severityRank, splitCamel, timeAgo } from '../../lib/format';
import type { Detection, DetectionStatus } from '../../lib/types';
import { useApiMutation } from '../../hooks/useApiMutation';
import { Card, PageHeader } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge, SeverityBadge, StatusBadge } from '../../components/ui/Badge';
import { Field, Input, Select, Textarea } from '../../components/ui/Form';
import { Modal } from '../../components/ui/Modal';
import { DataTable, Spinner, type Column } from '../../components/ui/DataTable';

const STATUSES: { value: DetectionStatus; label: string }[] = [
  { value: 'New', label: 'New' }, { value: 'Investigating', label: 'Investigating' },
  { value: 'Resolved', label: 'Resolved' }, { value: 'FalsePositive', label: 'False positive' },
];

export default function DetectionsPage() {
  const [status, setStatus] = useState('open');
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const list = useQuery({
    queryKey: ['detections', 'list', status],
    queryFn: () => securityApi.detections({
      page: 1, pageSize: GRID_LIMIT,
      openOnly: status === 'open' || undefined, status: status === 'open' ? undefined : status,
    }),
    placeholderData: keepPreviousData,
    refetchInterval: 30_000,
  });

  const columns: Column<Detection>[] = [
    { key: 'sev', header: 'Severity', value: (d) => d.severity, sortValue: (d) => severityRank(d.severity), groupCell: true,
      render: (d) => <SeverityBadge severity={d.severity} /> },
    { key: 'title', header: 'Detection', value: (d) => d.title, groupable: false,
      render: (d) => <><div className="cell-title">{d.title}</div><div className="cell-sub">{d.description}</div></> },
    { key: 'rule', header: 'Rule', value: (d) => d.ruleName, hidden: true },
    { key: 'key', header: 'Entity', value: (d) => d.correlationKey, render: (d) => <code>{d.correlationKey}</code> },
    { key: 'mitre', header: 'MITRE', value: (d) => d.mitreTechnique, groupCell: true, render: (d) => d.mitreTechnique ? <Badge tone="primary">{d.mitreTechnique}</Badge> : '—' },
    { key: 'count', header: 'Events', align: 'right', value: (d) => d.eventCount, aggregate: 'sum', footer: 'sum' },
    { key: 'status', header: 'Status', value: (d) => splitCamel(d.status), groupCell: true, render: (d) => <StatusBadge status={d.status} /> },
    { key: 'assignee', header: 'Assignee', value: (d) => d.assignedTo, hidden: true },
    { key: 'seen', header: 'Last seen', value: (d) => new Date(d.lastSeen), filter: false, render: (d) => <span className="nowrap small">{timeAgo(d.lastSeen)}</span> },
  ];

  return (
    <>
      <PageHeader title="Detections" description="Correlated threats raised by detection rules. Triage, investigate and close them here." />
      <Card>
        <DataTable
          columns={columns} rows={list.data?.items} totalCount={list.data?.totalCount} rowKey={(d) => d.id} loading={list.isLoading}
          onRowClick={(d) => setSelectedId(d.id)} initialSort={[{ key: 'seen', dir: 'desc' }]}
          exportName="detections" storageKey="detections"
          toolbar={<Select options={[{ value: 'open', label: 'Open (New + Investigating)' }, ...STATUSES]} placeholder="All statuses" aria-label="Status"
            value={status} onChange={(e) => setStatus(e.target.value)} />}
          empty={{ title: 'No detections', description: 'Nothing matches the current filters.' }}
        />
      </Card>
      <DetectionDrawer id={selectedId} onClose={() => setSelectedId(null)} />
    </>
  );
}

function DetectionDrawer({ id, onClose }: { id: string | null; onClose: () => void }) {
  const detail = useQuery({ queryKey: ['detections', 'detail', id], queryFn: () => securityApi.detection(id!), enabled: !!id });
  const d = detail.data?.detection;
  const [status, setStatus] = useState<DetectionStatus>('Investigating');
  const [notes, setNotes] = useState('');
  const [assignedTo, setAssignedTo] = useState('');

  useEffect(() => {
    if (d) { setStatus(d.status); setNotes(d.notes ?? ''); setAssignedTo(d.assignedTo ?? ''); }
  }, [d]);

  const update = useApiMutation(() => securityApi.updateDetection(id!, { status, notes, assignedTo }), {
    invalidate: [['detections']], success: 'Detection updated',
  });
  const block = useApiMutation(() => firewallApi.blockIp(d!.correlationKey, `Blocked from detection: ${d!.ruleName}`, 1440, d!.id), {
    invalidate: [['firewall']], success: (e) => `${e.address} blocked until ${formatDateTime(e.expiresAt)}`,
  });
  const canBlock = d?.correlateBy === 'SourceIp' && isValidIp(d.correlationKey);

  return (
    <Modal open={!!id} onClose={onClose} variant="drawer" title="Detection"
      footer={d && <>
        {canBlock && <Button variant="danger" icon={<Ban size={15} />} loading={block.isPending} onClick={() => block.mutate(undefined)}>Block IP (24h)</Button>}
        <span className="spacer" />
        <Button onClick={onClose}>Close</Button>
        <Button variant="primary" loading={update.isPending} onClick={() => update.mutate(undefined)}>Save triage</Button>
      </>}>
      {!d ? <Spinner /> : (
        <div className="stack">
          <div className="row-wrap"><SeverityBadge severity={d.severity} /><StatusBadge status={d.status} />{d.mitreTechnique && <Badge tone="primary">{d.mitreTechnique}</Badge>}</div>
          <h3 style={{ fontSize: 17 }}>{d.title}</h3>
          <p className="muted">{d.description}</p>
          <dl className="kv">
            <dt>Rule</dt><dd>{d.ruleName}</dd>
            <dt>Correlated by</dt><dd>{splitCamel(d.correlateBy)}: <code>{d.correlationKey}</code></dd>
            <dt>Event count</dt><dd>{d.eventCount}</dd>
            <dt>First seen</dt><dd>{formatDateTime(d.firstSeen)}</dd>
            <dt>Last seen</dt><dd>{formatDateTime(d.lastSeen)}</dd>
            {d.resolvedAt && <><dt>Closed</dt><dd>{formatDateTime(d.resolvedAt)}</dd></>}
          </dl>

          <div className="card" style={{ padding: 16 }}>
            <div className="section-title">Triage</div>
            <div className="form-grid">
              <Field label="Status"><Select options={STATUSES} value={status} onChange={(e) => setStatus(e.target.value as DetectionStatus)} /></Field>
              <Field label="Assigned to"><Input maxLength={100} value={assignedTo} onChange={(e) => setAssignedTo(e.target.value)} placeholder="Analyst name" /></Field>
              <Field label="Notes" full><Textarea maxLength={2000} value={notes} onChange={(e) => setNotes(e.target.value)} /></Field>
            </div>
          </div>

          <div>
            <div className="section-title">Evidence ({detail.data!.evidence.length} events)</div>
            <div className="card">
              <div className="list">
                {detail.data!.evidence.length === 0 && <div className="list-item muted">No matching events in the window.</div>}
                {detail.data!.evidence.map((e) => (
                  <div key={e.id} className="list-item" style={{ alignItems: 'flex-start' }}>
                    <SeverityBadge severity={e.severity} />
                    <div style={{ minWidth: 0 }}>
                      <div>{e.message}</div>
                      <div className="muted small">{formatDateTime(e.timestamp)} · {[e.sourceIp, e.userName, e.targetHost].filter(Boolean).join(' · ')}</div>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      )}
    </Modal>
  );
}
