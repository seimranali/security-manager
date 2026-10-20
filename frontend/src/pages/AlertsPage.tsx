import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Bell, CheckCheck, CircleCheck, Clock, Plus, RotateCcw, Timer } from 'lucide-react';
import { GRID_LIMIT, alertApi } from '../lib/api';
import { formatDateTime, formatMinutes, formatNumber, severityRank, timeAgo } from '../lib/format';
import type { Alert, Severity } from '../lib/types';
import { severityOptions } from '../lib/options';
import { useApiMutation } from '../hooks/useApiMutation';
import { Card, PageHeader, StatCard } from '../components/ui/Card';
import { Button } from '../components/ui/Button';
import { Badge, SeverityBadge, StatusBadge } from '../components/ui/Badge';
import { Field, Input, Select, Textarea } from '../components/ui/Form';
import { Modal } from '../components/ui/Modal';
import { DataTable, type Column } from '../components/ui/DataTable';

const KEY = [['alerts']];

export default function AlertsPage() {
  const [status, setStatus] = useState('active');
  const [selection, setSelection] = useState<Set<string>>(new Set());
  const [detail, setDetail] = useState<Alert | null>(null);
  const [notes, setNotes] = useState('');
  const [creating, setCreating] = useState(false);
  const [newAlert, setNewAlert] = useState({ title: '', message: '', severity: 'Medium' as Severity });

  const stats = useQuery({ queryKey: ['alerts', 'stats'], queryFn: alertApi.stats });
  const list = useQuery({
    queryKey: ['alerts', 'list', status],
    queryFn: () => alertApi.list({
      page: 1, pageSize: GRID_LIMIT,
      activeOnly: status === 'active' || undefined, status: status === 'active' ? undefined : status,
    }),
    placeholderData: keepPreviousData,
  });

  const clear = () => setSelection(new Set());
  const ack = useApiMutation(alertApi.acknowledge, { invalidate: KEY, success: 'Alert acknowledged', onSuccess: setDetail });
  const resolve = useApiMutation((a: Alert) => alertApi.resolve(a.id, notes), { invalidate: KEY, success: 'Alert resolved', onSuccess: () => setDetail(null) });
  const reopen = useApiMutation(alertApi.reopen, { invalidate: KEY, success: 'Alert reopened', onSuccess: setDetail });
  const bulkAck = useApiMutation(() => alertApi.bulkAcknowledge([...selection]), { invalidate: KEY, success: (r) => `${r.updated} alert(s) acknowledged`, onSuccess: clear });
  const bulkResolve = useApiMutation(() => alertApi.bulkResolve([...selection]), { invalidate: KEY, success: (r) => `${r.updated} alert(s) resolved`, onSuccess: clear });
  const create = useApiMutation(() => alertApi.create({ ...newAlert, source: 'Manual' }), {
    invalidate: KEY, success: 'Alert raised', onSuccess: () => { setCreating(false); setNewAlert({ title: '', message: '', severity: 'Medium' }); },
  });

  const openDetail = (a: Alert) => { setDetail(a); setNotes(''); };

  const columns: Column<Alert>[] = [
    { key: 'sev', header: 'Severity', value: (a) => a.severity, sortValue: (a) => severityRank(a.severity), groupCell: true,
      render: (a) => <SeverityBadge severity={a.severity} /> },
    { key: 'title', header: 'Alert', value: (a) => a.title, groupable: false, render: (a) => (
      <><div className="cell-title">{a.title}</div><div className="cell-sub truncate" style={{ maxWidth: 420 }}>{a.message}</div></>
    ) },
    { key: 'source', header: 'Source', value: (a) => a.source, groupCell: true, render: (a) => <Badge>{a.source}</Badge> },
    { key: 'category', header: 'Category', value: (a) => a.category },
    { key: 'count', header: 'Occurrences', align: 'right', value: (a) => a.occurrenceCount, aggregate: 'sum', footer: 'sum' },
    { key: 'status', header: 'Status', value: (a) => a.status, groupCell: true, render: (a) => <StatusBadge status={a.status} /> },
    { key: 'first', header: 'First seen', value: (a) => new Date(a.createdAt), filter: false, hidden: true, render: (a) => <span className="small nowrap">{timeAgo(a.createdAt)}</span> },
    { key: 'last', header: 'Last seen', value: (a) => new Date(a.lastOccurredAt), filter: false, render: (a) => <span className="small nowrap">{timeAgo(a.lastOccurredAt)}</span> },
    { key: 'owner', header: 'Handled by', value: (a) => a.resolvedBy ?? a.acknowledgedBy, hidden: true },
  ];

  return (
    <div className="stack">
      <PageHeader title="Alerts" description="Unified alert queue from all services. Updates arrive in real time."
        actions={<Button variant="primary" icon={<Plus size={16} />} onClick={() => setCreating(true)}>Raise alert</Button>} />

      <div className="grid grid-kpi">
        <StatCard label="Open" value={formatNumber(stats.data?.open)} icon={<Bell size={20} />} tone="danger" hint={`${stats.data?.criticalActive ?? 0} critical active`} />
        <StatCard label="Acknowledged" value={formatNumber(stats.data?.acknowledged)} icon={<Clock size={20} />} tone="warning" />
        <StatCard label="Resolved (24h)" value={formatNumber(stats.data?.resolved24h)} icon={<CircleCheck size={20} />} tone="success" />
        <StatCard label="Mean time to acknowledge" value={formatMinutes(stats.data?.meanTimeToAcknowledgeMinutes)} icon={<Timer size={20} />} />
        <StatCard label="Mean time to resolve" value={formatMinutes(stats.data?.meanTimeToResolveMinutes)} icon={<Timer size={20} />} />
      </div>

      <Card>
        <DataTable
          columns={columns} rows={list.data?.items} totalCount={list.data?.totalCount} rowKey={(a) => a.id} loading={list.isLoading}
          onRowClick={openDetail} selection={{ selected: selection, onChange: setSelection }}
          initialSort={[{ key: 'sev', dir: 'desc' }, { key: 'last', dir: 'desc' }]} exportName="alerts" storageKey="alerts"
          toolbar={<>
            <Select options={[{ value: 'active', label: 'Active' }, { value: 'Open', label: 'Open' }, { value: 'Acknowledged', label: 'Acknowledged' }, { value: 'Resolved', label: 'Resolved' }]}
              placeholder="All statuses" aria-label="Status" value={status} onChange={(e) => { setStatus(e.target.value); clear(); }} />
            {selection.size > 0 && (
              <>
                <span className="muted small">{selection.size} selected</span>
                <Button size="sm" icon={<CheckCheck size={14} />} loading={bulkAck.isPending} onClick={() => bulkAck.mutate(undefined)}>Acknowledge</Button>
                <Button size="sm" variant="primary" icon={<CircleCheck size={14} />} loading={bulkResolve.isPending} onClick={() => bulkResolve.mutate(undefined)}>Resolve</Button>
              </>
            )}
          </>}
          empty={{ title: 'No alerts', description: 'You are all caught up.' }}
        />
      </Card>

      <Modal open={!!detail} onClose={() => setDetail(null)} variant="drawer" title="Alert"
        footer={detail && (detail.status === 'Resolved'
          ? <Button icon={<RotateCcw size={15} />} loading={reopen.isPending} onClick={() => reopen.mutate(detail.id)}>Reopen</Button>
          : <>
              {detail.status === 'Open' && <Button icon={<CheckCheck size={15} />} loading={ack.isPending} onClick={() => ack.mutate(detail.id)}>Acknowledge</Button>}
              <Button variant="primary" icon={<CircleCheck size={15} />} loading={resolve.isPending} onClick={() => resolve.mutate(detail)}>Resolve</Button>
            </>)}>
        {detail && (
          <div className="stack">
            <div className="row-wrap"><SeverityBadge severity={detail.severity} /><StatusBadge status={detail.status} /><Badge>{detail.source}</Badge></div>
            <h3 style={{ fontSize: 17 }}>{detail.title}</h3>
            <p>{detail.message}</p>
            <dl className="kv">
              <dt>Category</dt><dd>{detail.category ?? '—'}</dd>
              <dt>Occurrences</dt><dd>{detail.occurrenceCount}</dd>
              <dt>First seen</dt><dd>{formatDateTime(detail.createdAt)}</dd>
              <dt>Last seen</dt><dd>{formatDateTime(detail.lastOccurredAt)}</dd>
              <dt>Acknowledged</dt><dd>{detail.acknowledgedAt ? `${formatDateTime(detail.acknowledgedAt)} by ${detail.acknowledgedBy}` : '—'}</dd>
              <dt>Resolved</dt><dd>{detail.resolvedAt ? `${formatDateTime(detail.resolvedAt)} by ${detail.resolvedBy}` : '—'}</dd>
              {detail.resolutionNotes && <><dt>Resolution</dt><dd>{detail.resolutionNotes}</dd></>}
              {detail.sourceReference && <><dt>Reference</dt><dd><code>{detail.sourceReference}</code></dd></>}
            </dl>
            {detail.status !== 'Resolved' && (
              <Field label="Resolution notes"><Textarea maxLength={2000} value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="What was done to remediate?" /></Field>
            )}
          </div>
        )}
      </Modal>

      <Modal open={creating} onClose={() => setCreating(false)} title="Raise manual alert" onSubmit={() => create.mutate(undefined)}
        footer={<><Button onClick={() => setCreating(false)}>Cancel</Button><Button type="submit" variant="primary" loading={create.isPending}>Raise alert</Button></>}>
        <div className="form-grid">
          <Field label="Title" full><Input required maxLength={300} value={newAlert.title} onChange={(e) => setNewAlert({ ...newAlert, title: e.target.value })} /></Field>
          <Field label="Severity"><Select options={severityOptions()} value={newAlert.severity} onChange={(e) => setNewAlert({ ...newAlert, severity: e.target.value as Severity })} /></Field>
          <Field label="Message" full><Textarea required maxLength={2000} value={newAlert.message} onChange={(e) => setNewAlert({ ...newAlert, message: e.target.value })} /></Field>
        </div>
      </Modal>
    </div>
  );
}
