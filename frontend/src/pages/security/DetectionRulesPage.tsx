import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { securityApi } from '../../lib/api';
import { formatNumber, severityRank, splitCamel, timeAgo } from '../../lib/format';
import type { DetectionRule, DetectionRuleInput } from '../../lib/types';
import { EVENT_GROUP_ORDER, eventTypeOptions, severityOptions } from '../../lib/options';
import { useApiMutation } from '../../hooks/useApiMutation';
import { Card, PageHeader } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge, SeverityBadge } from '../../components/ui/Badge';
import { Field, Input, Select, Switch, Textarea } from '../../components/ui/Form';
import { ConfirmDialog, Modal } from '../../components/ui/Modal';
import { DataTable, type Column } from '../../components/ui/DataTable';

const KEY = [['detections']];
const EMPTY: DetectionRuleInput = {
  name: '', description: '', eventType: 'AuthenticationFailure', minEventSeverity: 'Info', threshold: 5,
  timeWindowMinutes: 5, correlateBy: 'SourceIp', severity: 'High', mitreTechnique: '', isEnabled: true,
};

export default function DetectionRulesPage() {
  const rules = useQuery({ queryKey: ['detections', 'rules'], queryFn: securityApi.rules });
  const [editing, setEditing] = useState<{ id?: string; form: DetectionRuleInput } | null>(null);
  const [deleting, setDeleting] = useState<DetectionRule | null>(null);

  const save = useApiMutation(
    (e: { id?: string; form: DetectionRuleInput }) => (e.id ? securityApi.updateRule(e.id, e.form) : securityApi.createRule(e.form)),
    { invalidate: KEY, success: 'Detection rule saved', onSuccess: () => setEditing(null) },
  );
  const toggle = useApiMutation((r: DetectionRule) => securityApi.setRuleEnabled(r.id, !r.isEnabled), { invalidate: KEY });
  const remove = useApiMutation((id: string) => securityApi.deleteRule(id), { invalidate: KEY, success: 'Rule deleted', onSuccess: () => setDeleting(null) });
  const set = <K extends keyof DetectionRuleInput>(k: K, v: DetectionRuleInput[K]) => setEditing((e) => e && { ...e, form: { ...e.form, [k]: v } });

  const columns: Column<DetectionRule>[] = [
    { key: 'on', header: 'On', headerText: 'Enabled', width: 60, value: (r) => (r.isEnabled ? 'Enabled' : 'Disabled'),
      render: (r) => <Switch checked={r.isEnabled} ariaLabel="Enable rule" onChange={() => toggle.mutate(r)} /> },
    { key: 'name', header: 'Rule', value: (r) => r.name, groupable: false, render: (r) => <><div className="cell-title">{r.name}</div><div className="cell-sub">{r.description}</div></> },
    { key: 'type', header: 'Event type', group: 'Logic', value: (r) => splitCamel(r.eventType) },
    { key: 'threshold', header: 'Threshold', group: 'Logic', align: 'right', value: (r) => r.threshold, aggregate: 'avg' },
    { key: 'window', header: 'Window', group: 'Logic', align: 'right', value: (r) => r.timeWindowMinutes, format: (v) => `${v} min` },
    { key: 'by', header: 'Per', group: 'Logic', value: (r) => splitCamel(r.correlateBy) },
    { key: 'sev', header: 'Raises', value: (r) => r.severity, sortValue: (r) => severityRank(r.severity), groupCell: true, render: (r) => <SeverityBadge severity={r.severity} /> },
    { key: 'mitre', header: 'MITRE', value: (r) => r.mitreTechnique, render: (r) => r.mitreTechnique ? <Badge tone="primary">{r.mitreTechnique}</Badge> : '—' },
    { key: 'trig', header: 'Triggered', align: 'right', value: (r) => r.triggerCount, aggregate: 'sum', footer: 'sum',
      render: (r) => <><div className="cell-title">{formatNumber(r.triggerCount)}</div><div className="cell-sub">{timeAgo(r.lastTriggeredAt)}</div></> },
    { key: 'actions', header: '', headerText: 'Actions', hideable: false, className: 'text-right nowrap', render: (r) => (
      <>
        <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={15} />} aria-label="Edit"
          onClick={() => setEditing({ id: r.id, form: { ...r, description: r.description ?? '', mitreTechnique: r.mitreTechnique ?? '' } })} />
        <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={15} />} aria-label="Delete" onClick={() => setDeleting(r)} />
      </>
    ) },
  ];

  return (
    <>
      <PageHeader title="Detection Rules" description="Threshold correlation rules mapped to MITRE ATT&CK techniques."
        actions={<Button variant="primary" icon={<Plus size={16} />} onClick={() => setEditing({ form: EMPTY })}>New rule</Button>} />
      <Card>
        <DataTable columns={columns} rows={rules.data} rowKey={(r) => r.id} loading={rules.isLoading}
          initialSort={[{ key: 'sev', dir: 'desc' }]} exportName="detection-rules" storageKey="detection-rules" empty={{ title: 'No detection rules' }} />
      </Card>

      <Modal open={!!editing} onClose={() => setEditing(null)} size="lg" title={editing?.id ? 'Edit detection rule' : 'New detection rule'}
        onSubmit={() => editing && save.mutate(editing)}
        footer={<><Button onClick={() => setEditing(null)}>Cancel</Button><Button type="submit" variant="primary" loading={save.isPending}>Save rule</Button></>}>
        {editing && (
          <div className="form-grid">
            <Field label="Name" full><Input required minLength={3} maxLength={120} value={editing.form.name} onChange={(e) => set('name', e.target.value)} /></Field>
            <Field label="Event type"><Select options={eventTypeOptions()} groupOrder={EVENT_GROUP_ORDER} searchable value={editing.form.eventType} onChange={(e) => set('eventType', e.target.value as DetectionRuleInput['eventType'])} /></Field>
            <Field label="Minimum event severity"><Select options={severityOptions()} value={editing.form.minEventSeverity} onChange={(e) => set('minEventSeverity', e.target.value as DetectionRuleInput['severity'])} /></Field>
            <Field label="Threshold (events)"><Input type="number" min={1} max={10000} required value={editing.form.threshold} onChange={(e) => set('threshold', Number(e.target.value))} /></Field>
            <Field label="Time window (minutes)"><Input type="number" min={1} max={10080} required value={editing.form.timeWindowMinutes} onChange={(e) => set('timeWindowMinutes', Number(e.target.value))} /></Field>
            <Field label="Correlate by"><Select options={[
              { value: 'SourceIp', label: 'Source IP', description: 'One detection per attacking address' },
              { value: 'UserName', label: 'User name', description: 'One detection per targeted account' },
              { value: 'TargetHost', label: 'Target host', description: 'One detection per affected machine' },
              { value: 'None', label: 'None (global)', description: 'Count all matching events together' },
            ]} value={editing.form.correlateBy} onChange={(e) => set('correlateBy', e.target.value as DetectionRuleInput['correlateBy'])} /></Field>
            <Field label="Detection severity"><Select options={severityOptions()} value={editing.form.severity} onChange={(e) => set('severity', e.target.value as DetectionRuleInput['severity'])} /></Field>
            <Field label="MITRE technique" hint="e.g. T1110"><Input maxLength={20} className="mono" value={editing.form.mitreTechnique ?? ''} onChange={(e) => set('mitreTechnique', e.target.value)} /></Field>
            <Field label="Status"><Switch checked={editing.form.isEnabled} onChange={(v) => set('isEnabled', v)} label={editing.form.isEnabled ? 'Enabled' : 'Disabled'} /></Field>
            <Field label="Description" full><Textarea maxLength={500} value={editing.form.description ?? ''} onChange={(e) => set('description', e.target.value)} /></Field>
          </div>
        )}
      </Modal>

      <ConfirmDialog open={!!deleting} title="Delete detection rule" message={<>Delete <b>{deleting?.name}</b>? Existing detections are kept.</>}
        loading={remove.isPending} onConfirm={() => deleting && remove.mutate(deleting.id)} onClose={() => setDeleting(null)} />
    </>
  );
}
