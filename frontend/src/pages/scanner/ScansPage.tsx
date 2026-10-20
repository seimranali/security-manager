import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { GRID_LIMIT, scannerApi } from '../../lib/api';
import { formatDateTime, parseScanTypes, timeAgo } from '../../lib/format';
import { SCAN_TYPES, type ScanJob } from '../../lib/types';
import { useApiMutation } from '../../hooks/useApiMutation';
import { Card, PageHeader } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge, StatusBadge } from '../../components/ui/Badge';
import { Select } from '../../components/ui/Form';
import { Modal } from '../../components/ui/Modal';
import { DataTable, Spinner, type Column } from '../../components/ui/DataTable';
import { FindingCard } from './FindingCard';

const typeLabel = (v: string) => parseScanTypes(v).map((t) => SCAN_TYPES.find((s) => s.value === t)?.label ?? t).join(', ');

export default function ScansPage() {
  const [status, setStatus] = useState('');
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const jobs = useQuery({
    queryKey: ['scan-jobs', 'list', status],
    queryFn: () => scannerApi.jobs({ status, page: 1, pageSize: GRID_LIMIT }),
    placeholderData: keepPreviousData,
    // Poll quickly while any scan is active.
    refetchInterval: (q) => (q.state.data?.items.some((j) => j.status === 'Queued' || j.status === 'Running') ? 3000 : 30000),
  });

  const done = (j: ScanJob) => j.status === 'Completed';
  const columns: Column<ScanJob>[] = [
    { key: 'target', header: 'Target', value: (j) => j.targetName, mergeRepeated: true,
      render: (j) => <><div className="cell-title">{j.targetName}</div><div className="cell-sub mono">{j.host}</div></> },
    { key: 'types', header: 'Checks', value: (j) => typeLabel(j.scanTypes), render: (j) => <span className="small">{typeLabel(j.scanTypes)}</span> },
    { key: 'trigger', header: 'Trigger', value: (j) => j.trigger, groupCell: true, render: (j) => <Badge>{j.trigger}</Badge> },
    { key: 'status', header: 'Status', width: 160, value: (j) => j.status, groupCell: true, render: (j) => j.status === 'Running'
      ? <div className="stack-sm" style={{ gap: 4 }}><StatusBadge status="Running" /><div className="progress"><span style={{ width: `${j.progress}%` }} /></div></div>
      : <StatusBadge status={j.status} /> },
    // Grouped "Findings" header over per-severity counts, with totals in group rows and footer.
    { key: 'critical', header: 'Critical', group: 'Findings', align: 'right', value: (j) => (done(j) ? j.criticalCount : null), aggregate: 'sum', footer: 'sum',
      render: (j) => (done(j) ? (j.criticalCount ? <Badge tone="critical">{j.criticalCount}</Badge> : <span className="muted">0</span>) : '—') },
    { key: 'high', header: 'High', group: 'Findings', align: 'right', value: (j) => (done(j) ? j.highCount : null), aggregate: 'sum', footer: 'sum',
      render: (j) => (done(j) ? (j.highCount ? <Badge tone="high">{j.highCount}</Badge> : <span className="muted">0</span>) : '—') },
    { key: 'medium', header: 'Medium', group: 'Findings', align: 'right', value: (j) => (done(j) ? j.mediumCount : null), aggregate: 'sum', footer: 'sum' },
    { key: 'total', header: 'Total', group: 'Findings', align: 'right', value: (j) => (done(j) ? j.findingsCount : null), aggregate: 'sum', footer: 'sum' },
    { key: 'when', header: 'Started', value: (j) => new Date(j.startedAt ?? j.createdAt), filter: false,
      render: (j) => <span className="small nowrap">{timeAgo(j.startedAt ?? j.createdAt)}</span> },
    { key: 'duration', header: 'Duration', align: 'right', value: (j) => j.durationSeconds, format: (v) => (v == null ? '—' : `${v}s`), aggregate: 'avg', footer: 'avg' },
  ];

  return (
    <>
      <PageHeader title="Scans" description="Manual and scheduled scan executions. Click a scan to review its findings." />
      <Card>
        <DataTable
          columns={columns} rows={jobs.data?.items} totalCount={jobs.data?.totalCount} rowKey={(j) => j.id} loading={jobs.isLoading}
          onRowClick={(j) => setSelectedId(j.id)} initialSort={[{ key: 'when', dir: 'desc' }]} exportName="scans" storageKey="scans"
          toolbar={<Select options={['Queued', 'Running', 'Completed', 'Failed', 'Cancelled']} placeholder="All statuses" aria-label="Status"
            value={status} onChange={(e) => setStatus(e.target.value)} />}
          empty={{ title: 'No scans yet', description: 'Start a scan from the Targets page.' }}
        />
      </Card>
      <ScanDrawer id={selectedId} onClose={() => setSelectedId(null)} />
    </>
  );
}

function ScanDrawer({ id, onClose }: { id: string | null; onClose: () => void }) {
  const detail = useQuery({
    queryKey: ['scan-jobs', 'detail', id],
    queryFn: () => scannerApi.job(id!),
    enabled: !!id,
    refetchInterval: (q) => (q.state.data && ['Queued', 'Running'].includes(q.state.data.job.status) ? 2000 : false),
  });
  const cancel = useApiMutation(() => scannerApi.cancelScan(id!), { invalidate: [['scan-jobs']], success: 'Scan cancelled' });
  const job = detail.data?.job;

  return (
    <Modal open={!!id} onClose={onClose} variant="drawer" title="Scan details"
      footer={job?.status === 'Queued' && <Button variant="danger" loading={cancel.isPending} onClick={() => cancel.mutate(undefined)}>Cancel scan</Button>}>
      {!job ? <Spinner /> : (
        <div className="stack">
          <div className="row-wrap"><StatusBadge status={job.status} /><Badge>{job.trigger}</Badge></div>
          <h3 style={{ fontSize: 17 }}>{job.targetName} <span className="muted mono" style={{ fontWeight: 400 }}>{job.host}</span></h3>
          {job.status === 'Running' && <div className="progress"><span style={{ width: `${job.progress}%` }} /></div>}
          {job.error && <div className="callout callout-danger">{job.error}</div>}
          <dl className="kv">
            <dt>Checks</dt><dd>{typeLabel(job.scanTypes)}</dd>
            <dt>Started</dt><dd>{formatDateTime(job.startedAt)}</dd>
            <dt>Completed</dt><dd>{formatDateTime(job.completedAt)}</dd>
            {job.durationSeconds != null && <><dt>Duration</dt><dd>{job.durationSeconds}s</dd></>}
            <dt>Open ports</dt><dd>{job.openPorts ? <code>{job.openPorts}</code> : '—'}</dd>
          </dl>
          {job.status === 'Completed' && (
            <div className="stack-sm">
              <div className="section-title">Findings ({detail.data!.findings.length})</div>
              {detail.data!.findings.length === 0 && <div className="callout callout-success">No issues found.</div>}
              {detail.data!.findings.map((f) => <FindingCard key={f.id} finding={f} />)}
            </div>
          )}
        </div>
      )}
    </Modal>
  );
}
