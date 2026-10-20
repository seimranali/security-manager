import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { GRID_LIMIT, scannerApi } from '../../lib/api';
import { severityRank, timeAgo } from '../../lib/format';
import type { Finding, FindingStatus } from '../../lib/types';
import { useApiMutation } from '../../hooks/useApiMutation';
import { Card, PageHeader } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge, SeverityBadge, StatusBadge } from '../../components/ui/Badge';
import { Select } from '../../components/ui/Form';
import { Modal } from '../../components/ui/Modal';
import { DataTable, type Column } from '../../components/ui/DataTable';
import { FindingCard } from './FindingCard';

export default function FindingsPage() {
  const [status, setStatus] = useState('Open');
  const [selected, setSelected] = useState<Finding | null>(null);

  const findings = useQuery({
    queryKey: ['scan-jobs', 'findings', status],
    queryFn: () => scannerApi.findings({ status, page: 1, pageSize: GRID_LIMIT }),
    placeholderData: keepPreviousData,
  });

  const update = useApiMutation((v: { id: string; status: FindingStatus }) => scannerApi.updateFinding(v.id, v.status), {
    invalidate: [['scan-jobs']], success: 'Finding updated', onSuccess: () => setSelected(null),
  });

  const columns: Column<Finding>[] = [
    { key: 'target', header: 'Target', value: (f) => f.targetName, mergeRepeated: true, render: (f) => <span className="cell-title">{f.targetName}</span> },
    { key: 'sev', header: 'Severity', value: (f) => f.severity, sortValue: (f) => severityRank(f.severity), groupCell: true,
      render: (f) => <SeverityBadge severity={f.severity} /> },
    { key: 'title', header: 'Finding', value: (f) => f.title, render: (f) => <span className="cell-title">{f.title}</span> },
    { key: 'cat', header: 'Category', value: (f) => f.category, groupCell: true, render: (f) => <Badge>{f.category}</Badge> },
    { key: 'port', header: 'Port', align: 'right', value: (f) => f.port, filter: 'text' },
    { key: 'status', header: 'Status', value: (f) => f.status, groupCell: true, render: (f) => <StatusBadge status={f.status} /> },
    { key: 'risk', header: 'Risk pts', align: 'right', hidden: true, aggregate: 'sum', footer: 'sum',
      value: (f) => ({ Critical: 40, High: 20, Medium: 8, Low: 2, Info: 0 })[f.severity] },
    { key: 'when', header: 'Found', value: (f) => new Date(f.createdAt), filter: false, render: (f) => <span className="small nowrap">{timeAgo(f.createdAt)}</span> },
  ];

  return (
    <>
      <PageHeader title="Findings" description="Vulnerabilities and misconfigurations discovered by scans. Try Pivot: Target × Severity." />
      <Card>
        <DataTable
          columns={columns} rows={findings.data?.items} totalCount={findings.data?.totalCount} rowKey={(f) => f.id}
          loading={findings.isLoading} onRowClick={setSelected}
          initialSort={[{ key: 'target', dir: 'asc' }, { key: 'sev', dir: 'desc' }]}
          exportName="findings" storageKey="findings"
          toolbar={<Select options={['Open', 'Resolved', 'Accepted']} placeholder="All statuses" aria-label="Status" value={status} onChange={(e) => setStatus(e.target.value)} />}
          renderDetail={(f) => <FindingCard finding={f} />}
          empty={{ title: 'No findings', description: 'Run a scan or change the status filter.' }}
        />
      </Card>

      <Modal open={!!selected} onClose={() => setSelected(null)} title="Finding"
        footer={selected && (
          <>
            {selected.status !== 'Accepted' && <Button loading={update.isPending} onClick={() => update.mutate({ id: selected.id, status: 'Accepted' })}>Accept risk</Button>}
            {selected.status !== 'Open' && <Button loading={update.isPending} onClick={() => update.mutate({ id: selected.id, status: 'Open' })}>Reopen</Button>}
            {selected.status !== 'Resolved' && <Button variant="primary" loading={update.isPending} onClick={() => update.mutate({ id: selected.id, status: 'Resolved' })}>Mark resolved</Button>}
          </>
        )}>
        {selected && (
          <div className="stack-sm">
            <div className="row"><span className="muted">Target:</span> <b>{selected.targetName}</b> <span className="spacer" /><StatusBadge status={selected.status} /></div>
            <FindingCard finding={selected} />
          </div>
        )}
      </Modal>
    </>
  );
}
