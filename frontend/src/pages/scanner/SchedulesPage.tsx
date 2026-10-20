import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { scannerApi } from '../../lib/api';
import { formatInterval, parseScanTypes, timeAgo, toDate } from '../../lib/format';
import { formatDisplayTime, localTimeToUtc, utcTimeToLocal } from '../../lib/dates';
import { SCAN_TYPES, type ScanSchedule, type ScanTypeFlag, type ScheduleInput } from '../../lib/types';
import { useApiMutation } from '../../hooks/useApiMutation';
import { Card, PageHeader } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { ChipGroup, Field, Input, Select, Switch, TimePicker } from '../../components/ui/Form';
import { ConfirmDialog, Modal } from '../../components/ui/Modal';
import { DataTable, type Column } from '../../components/ui/DataTable';

const INTERVALS = [
  { value: '15', label: 'Every 15 minutes' }, { value: '60', label: 'Hourly' }, { value: '360', label: 'Every 6 hours' },
  { value: '720', label: 'Every 12 hours' }, { value: '1440', label: 'Daily' }, { value: '10080', label: 'Weekly' },
];
const KEY = [['scan-jobs']];
const MINUTES_PER_DAY = 1440;
const isWholeDays = (minutes: number) => minutes % MINUTES_PER_DAY === 0;

/** Form state keeps the run time in the user's local time; it is converted to UTC on save. */
interface Editing {
  id?: string;
  form: ScheduleInput;
  runAtLocal: string;
}

export default function SchedulesPage() {
  const schedules = useQuery({ queryKey: ['scan-jobs', 'schedules'], queryFn: scannerApi.schedules, refetchInterval: 30_000 });
  const targets = useQuery({ queryKey: ['scan-jobs', 'targets'], queryFn: scannerApi.targets });
  const [editing, setEditing] = useState<Editing | null>(null);
  const [deleting, setDeleting] = useState<ScanSchedule | null>(null);

  const save = useApiMutation(
    (e: Editing) => {
      const form: ScheduleInput = {
        ...e.form,
        runAtUtc: isWholeDays(e.form.intervalMinutes) && e.runAtLocal ? localTimeToUtc(e.runAtLocal) : undefined,
      };
      return e.id ? scannerApi.updateSchedule(e.id, form) : scannerApi.createSchedule(form);
    },
    { invalidate: KEY, success: 'Schedule saved', onSuccess: () => setEditing(null) },
  );
  const toggle = useApiMutation((s: ScanSchedule) => scannerApi.setScheduleEnabled(s.id, !s.isEnabled), { invalidate: KEY });
  const remove = useApiMutation((id: string) => scannerApi.deleteSchedule(id), { invalidate: KEY, success: 'Schedule deleted', onSuccess: () => setDeleting(null) });

  const set = <K extends keyof ScheduleInput>(k: K, v: ScheduleInput[K]) => setEditing((e) => e && { ...e, form: { ...e.form, [k]: v } });
  const targetOptions = (targets.data ?? []).map((t) => ({
    value: t.id, label: t.name, description: t.host, disabled: !t.isEnabled,
    meta: t.riskScore != null ? `risk ${t.riskScore}` : undefined,
  }));
  const intervalOptions = editing && !INTERVALS.some((i) => i.value === String(editing.form.intervalMinutes))
    ? [...INTERVALS, { value: String(editing.form.intervalMinutes), label: formatInterval(editing.form.intervalMinutes) }]
    : INTERVALS;

  const checks = (s: ScanSchedule) => parseScanTypes(s.scanTypes).map((t) => SCAN_TYPES.find((x) => x.value === t)?.label).join(', ');
  const runAtLocal = (s: ScanSchedule) => (s.runAtUtc ? utcTimeToLocal(s.runAtUtc) : '');

  const columns: Column<ScanSchedule>[] = [
    { key: 'on', header: 'On', headerText: 'Enabled', width: 60, value: (s) => (s.isEnabled ? 'Enabled' : 'Paused'),
      render: (s) => <Switch checked={s.isEnabled} ariaLabel="Enable schedule" onChange={() => toggle.mutate(s)} /> },
    { key: 'target', header: 'Target', value: (s) => s.targetName, mergeRepeated: true },
    { key: 'name', header: 'Schedule', value: (s) => s.name, groupable: false, render: (s) => <span className="cell-title">{s.name}</span> },
    { key: 'types', header: 'Checks', value: checks },
    { key: 'interval', header: 'Frequency', value: (s) => s.intervalMinutes, format: (v) => formatInterval(Number(v)), filter: 'select' },
    { key: 'runAt', header: 'Run at', value: (s) => runAtLocal(s) || null, groupable: false,
      render: (s) => (s.runAtUtc ? formatDisplayTime(runAtLocal(s)) : <span className="muted">—</span>) },
    { key: 'last', header: 'Last run', value: (s) => toDate(s.lastRunAt), filter: false, render: (s) => <span className="small">{timeAgo(s.lastRunAt)}</span> },
    { key: 'next', header: 'Next run', value: (s) => (s.isEnabled ? toDate(s.nextRunAt) : null), filter: false,
      render: (s) => <span className="small">{s.isEnabled ? timeAgo(s.nextRunAt) : 'Paused'}</span> },
    { key: 'actions', header: '', headerText: 'Actions', hideable: false, className: 'text-right nowrap', render: (s) => (
      <>
        <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={15} />} aria-label="Edit"
          onClick={() => setEditing({
            id: s.id,
            runAtLocal: runAtLocal(s),
            form: { targetId: s.targetId, name: s.name, scanTypes: parseScanTypes(s.scanTypes).join(', '), intervalMinutes: s.intervalMinutes, isEnabled: s.isEnabled },
          })} />
        <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={15} />} aria-label="Delete" onClick={() => setDeleting(s)} />
      </>
    ) },
  ];

  return (
    <>
      <PageHeader title="Scan Schedules" description="Automated recurring scans. Daily and weekly schedules can run at a fixed time of day."
        actions={<Button variant="primary" icon={<Plus size={16} />} disabled={!targetOptions.length}
          onClick={() => setEditing({ runAtLocal: '', form: { targetId: targetOptions[0]?.value ?? '', name: '', scanTypes: 'PortScan', intervalMinutes: 60, isEnabled: true } })}>New schedule</Button>} />
      <Card>
        <DataTable columns={columns} rows={schedules.data} rowKey={(s) => s.id} loading={schedules.isLoading}
          initialSort={[{ key: 'target', dir: 'asc' }]} exportName="scan-schedules" storageKey="scan-schedules"
          empty={{ title: 'No schedules', description: 'Automate recurring scans of your targets.' }} />
      </Card>

      <Modal open={!!editing} onClose={() => setEditing(null)} title={editing?.id ? 'Edit schedule' : 'New schedule'}
        onSubmit={() => editing && save.mutate(editing)}
        footer={<><Button onClick={() => setEditing(null)}>Cancel</Button><Button type="submit" variant="primary" loading={save.isPending} disabled={!editing?.form.scanTypes}>Save schedule</Button></>}>
        {editing && (
          <div className="form-grid">
            <Field label="Name" full><Input required minLength={2} maxLength={100} value={editing.form.name} onChange={(e) => set('name', e.target.value)} /></Field>
            <Field label="Target"><Select required options={targetOptions} value={editing.form.targetId} onChange={(e) => set('targetId', e.target.value)} /></Field>
            <Field label="Frequency"><Select options={intervalOptions} value={String(editing.form.intervalMinutes)} onChange={(e) => set('intervalMinutes', Number(e.target.value))} /></Field>
            {isWholeDays(editing.form.intervalMinutes) && (
              <Field label="Run at" hint={`Your local time${editing.runAtLocal ? ` · ${localTimeToUtc(editing.runAtLocal)} UTC` : ' · leave empty to count from creation'}`}>
                <TimePicker aria-label="Run at" minuteStep={15} value={editing.runAtLocal}
                  onChange={(v) => setEditing((e) => e && { ...e, runAtLocal: v })} />
              </Field>
            )}
            <div className="field full">
              <span className="field-label">Checks</span>
              <ChipGroup options={SCAN_TYPES} value={parseScanTypes(editing.form.scanTypes) as ScanTypeFlag[]} onChange={(v) => set('scanTypes', v.join(', '))} />
            </div>
            <Field label="Status"><Switch checked={editing.form.isEnabled} onChange={(v) => set('isEnabled', v)} label={editing.form.isEnabled ? 'Enabled' : 'Paused'} /></Field>
          </div>
        )}
      </Modal>

      <ConfirmDialog open={!!deleting} title="Delete schedule" message={<>Delete schedule <b>{deleting?.name}</b>?</>}
        loading={remove.isPending} onConfirm={() => deleting && remove.mutate(deleting.id)} onClose={() => setDeleting(null)} />
    </>
  );
}
