import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { Pencil, Play, Plus, Trash2 } from 'lucide-react';
import { scannerApi } from '../../lib/api';
import { timeAgo, toDate } from '../../lib/format';
import { SCAN_TYPES, type ScanTarget, type ScanTypeFlag, type TargetInput } from '../../lib/types';
import { useApiMutation } from '../../hooks/useApiMutation';
import { Card, PageHeader } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { StatusBadge } from '../../components/ui/Badge';
import { ChipGroup, Field, Input, Switch, Textarea } from '../../components/ui/Form';
import { ConfirmDialog, Modal } from '../../components/ui/Modal';
import { DataTable, type Column } from '../../components/ui/DataTable';
import { RiskMeter } from '../../components/ui/Charts';

const EMPTY: TargetInput = { name: '', host: '', ports: '21,22,23,25,80,443,445,3306,3389,5432,6379,8080', description: '', isEnabled: true };

export default function TargetsPage() {
  const navigate = useNavigate();
  const targets = useQuery({ queryKey: ['scan-jobs', 'targets'], queryFn: scannerApi.targets });
  const [editing, setEditing] = useState<{ id?: string; form: TargetInput } | null>(null);
  const [deleting, setDeleting] = useState<ScanTarget | null>(null);
  const [scanning, setScanning] = useState<ScanTarget | null>(null);
  const [types, setTypes] = useState<ScanTypeFlag[]>(['PortScan', 'HttpHeaders', 'SslCertificate']);

  const save = useApiMutation(
    (e: { id?: string; form: TargetInput }) => (e.id ? scannerApi.updateTarget(e.id, e.form) : scannerApi.createTarget(e.form)),
    { invalidate: [['scan-jobs']], success: 'Target saved', onSuccess: () => setEditing(null) },
  );
  const remove = useApiMutation((id: string) => scannerApi.deleteTarget(id), { invalidate: [['scan-jobs']], success: 'Target deleted', onSuccess: () => setDeleting(null) });
  const start = useApiMutation((t: ScanTarget) => scannerApi.startScan(t.id, types.join(', ')), {
    invalidate: [['scan-jobs']], success: 'Scan queued',
    onSuccess: () => { setScanning(null); navigate('/scanner/scans'); },
  });
  const set = <K extends keyof TargetInput>(k: K, v: TargetInput[K]) => setEditing((e) => e && { ...e, form: { ...e.form, [k]: v } });

  const columns: Column<ScanTarget>[] = [
    { key: 'name', header: 'Target', value: (t) => t.name, groupable: false, render: (t) => <><div className="cell-title">{t.name}</div><div className="cell-sub">{t.description}</div></> },
    { key: 'host', header: 'Host', value: (t) => t.host, render: (t) => <code>{t.host}</code> },
    { key: 'ports', header: 'Ports', value: (t) => t.ports, groupable: false, render: (t) => <span className="small truncate" style={{ display: 'block', maxWidth: 220 }} title={t.ports}>{t.ports}</span> },
    { key: 'portCount', header: '# Ports', align: 'right', value: (t) => t.ports.split(',').length, hidden: true, footer: 'sum' },
    { key: 'risk', header: 'Risk score', width: 160, value: (t) => t.riskScore, aggregate: 'avg', footer: 'avg', render: (t) => <RiskMeter score={t.riskScore} /> },
    { key: 'last', header: 'Last scan', value: (t) => toDate(t.lastScannedAt), filter: false, render: (t) => <span className="small nowrap">{timeAgo(t.lastScannedAt)}</span> },
    { key: 'status', header: 'Status', value: (t) => (t.isEnabled ? 'Enabled' : 'Disabled'), groupCell: true, render: (t) => <StatusBadge status={t.isEnabled ? 'Enabled' : 'Disabled'} /> },
    { key: 'actions', header: '', headerText: 'Actions', hideable: false, className: 'text-right nowrap', render: (t) => (
      <>
        <Button size="sm" variant="primary" icon={<Play size={14} />} disabled={!t.isEnabled} onClick={() => setScanning(t)}>Scan</Button>
        <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={15} />} aria-label="Edit"
          onClick={() => setEditing({ id: t.id, form: { name: t.name, host: t.host, ports: t.ports, description: t.description ?? '', isEnabled: t.isEnabled } })} />
        <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={15} />} aria-label="Delete" onClick={() => setDeleting(t)} />
      </>
    ) },
  ];

  return (
    <>
      <PageHeader title="Scan Targets" description="Hosts to assess. Only scan systems you own or are authorized to test."
        actions={<Button variant="primary" icon={<Plus size={16} />} onClick={() => setEditing({ form: EMPTY })}>Add target</Button>} />
      <Card>
        <DataTable columns={columns} rows={targets.data} rowKey={(t) => t.id} loading={targets.isLoading}
          initialSort={[{ key: 'risk', dir: 'desc' }]} exportName="scan-targets" storageKey="scan-targets"
          empty={{ title: 'No targets', description: 'Add a host to start scanning.' }} />
      </Card>

      <Modal open={!!editing} onClose={() => setEditing(null)} title={editing?.id ? 'Edit target' : 'Add target'}
        onSubmit={() => editing && save.mutate(editing)}
        footer={<><Button onClick={() => setEditing(null)}>Cancel</Button><Button type="submit" variant="primary" loading={save.isPending}>Save target</Button></>}>
        {editing && (
          <div className="form-grid">
            <Field label="Name"><Input required minLength={2} maxLength={100} value={editing.form.name} onChange={(e) => set('name', e.target.value)} /></Field>
            <Field label="Host" hint="Hostname or IP"><Input required className="mono" maxLength={253} value={editing.form.host} onChange={(e) => set('host', e.target.value)} /></Field>
            <Field label="Ports" full hint="Comma list and ranges, max 2000 ports (e.g. 22,80,443,8000-8100)"><Input required className="mono" value={editing.form.ports} onChange={(e) => set('ports', e.target.value)} /></Field>
            <Field label="Description" full><Textarea maxLength={500} value={editing.form.description ?? ''} onChange={(e) => set('description', e.target.value)} /></Field>
            <Field label="Status"><Switch checked={editing.form.isEnabled} onChange={(v) => set('isEnabled', v)} label={editing.form.isEnabled ? 'Enabled' : 'Disabled'} /></Field>
          </div>
        )}
      </Modal>

      <Modal open={!!scanning} onClose={() => setScanning(null)} title={`Scan ${scanning?.name ?? ''}`} size="sm"
        footer={<><Button onClick={() => setScanning(null)}>Cancel</Button><Button variant="primary" icon={<Play size={14} />} disabled={!types.length} loading={start.isPending} onClick={() => scanning && start.mutate(scanning)}>Start scan</Button></>}>
        <div className="stack-sm">
          <span className="field-label">Checks to run</span>
          <ChipGroup options={SCAN_TYPES} value={types} onChange={setTypes} />
          <p className="muted small">Host: <code>{scanning?.host}</code></p>
        </div>
      </Modal>

      <ConfirmDialog open={!!deleting} title="Delete target" message={<>Delete <b>{deleting?.name}</b> and its schedules? Past scan results are kept.</>}
        loading={remove.isPending} onConfirm={() => deleting && remove.mutate(deleting.id)} onClose={() => setDeleting(null)} />
    </>
  );
}
