import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Mail, Pencil, Plus, Send, Trash2, Webhook } from 'lucide-react';
import { notificationApi } from '../lib/api';
import { timeAgo } from '../lib/format';
import { severityOptions } from '../lib/options';
import type { NotificationChannel, NotificationChannelInput, Severity } from '../lib/types';
import { useApiMutation } from '../hooks/useApiMutation';
import { Card, PageHeader } from '../components/ui/Card';
import { Button } from '../components/ui/Button';
import { Badge, SeverityBadge } from '../components/ui/Badge';
import { Field, Input, Select, Switch, Textarea } from '../components/ui/Form';
import { ConfirmDialog, Modal } from '../components/ui/Modal';
import { DataTable, type Column } from '../components/ui/DataTable';

type FormState = NotificationChannelInput & { id?: string; hasSmtpPassword?: boolean; hasWebhookSecret?: boolean };

const EMPTY: FormState = {
  name: '', type: 'Email', isEnabled: true, minSeverity: 'High', notifyOnEscalation: true,
  smtpHost: '', smtpPort: 587, smtpUseTls: true, smtpUserName: '', fromAddress: '', recipients: '',
  webhookUrl: '', webhookFormat: 'Generic',
};
const KEY = [['notification-channels']];

const destination = (c: NotificationChannel) =>
  c.type === 'Email' ? c.recipients ?? '' : c.webhookUrl?.replace(/^(https?:\/\/[^/]+).*/, '$1/…') ?? '';

export default function NotificationsPage() {
  const channels = useQuery({ queryKey: ['notification-channels'], queryFn: notificationApi.channels });
  const [form, setForm] = useState<FormState | null>(null);
  const [deleting, setDeleting] = useState<NotificationChannel | null>(null);

  const save = useApiMutation((f: FormState) => {
    const { id, hasSmtpPassword: _p, hasWebhookSecret: _s, ...body } = f;
    return id ? notificationApi.update(id, body) : notificationApi.create(body);
  }, { invalidate: KEY, success: 'Channel saved', onSuccess: () => setForm(null) });
  const test = useApiMutation((id: string) => notificationApi.test(id), {
    invalidate: KEY, success: (c) => (c.lastError ? `Test failed: ${c.lastError}` : 'Test notification sent'),
  });
  const remove = useApiMutation((id: string) => notificationApi.remove(id), { invalidate: KEY, success: 'Channel deleted', onSuccess: () => setDeleting(null) });
  const set = <K extends keyof FormState>(k: K, v: FormState[K]) => setForm((f) => f && { ...f, [k]: v });

  const columns: Column<NotificationChannel>[] = [
    { key: 'name', header: 'Channel', value: (c) => c.name, groupable: false,
      render: (c) => <><div className="cell-title row" style={{ gap: 6 }}>{c.type === 'Email' ? <Mail size={14} /> : <Webhook size={14} />}{c.name}</div><div className="cell-sub truncate" style={{ maxWidth: 320 }}>{destination(c)}</div></> },
    { key: 'type', header: 'Type', value: (c) => (c.type === 'Webhook' ? `Webhook (${c.webhookFormat})` : 'Email') },
    { key: 'min', header: 'Sends', value: (c) => c.minSeverity, render: (c) => <span className="row" style={{ gap: 6 }}><SeverityBadge severity={c.minSeverity} /><span className="small muted">and above</span></span> },
    { key: 'enabled', header: 'Status', value: (c) => (c.isEnabled ? 'Enabled' : 'Disabled'), groupCell: true,
      render: (c) => !c.isEnabled ? <Badge>Disabled</Badge> : c.lastError ? <Badge tone="danger" dot>Failing</Badge> : <Badge tone="success" dot>Active</Badge> },
    { key: 'last', header: 'Last delivery', value: (c) => c.lastSentAt ?? '', filter: false,
      render: (c) => <><div className="small">{c.lastSentAt ? timeAgo(c.lastSentAt) : 'never'}</div>{c.lastError && <div className="cell-sub text-danger truncate" style={{ maxWidth: 280 }} title={c.lastError}>{c.lastError}</div>}</> },
    { key: 'sent', header: 'Sent', align: 'right', value: (c) => c.sentCount, aggregate: 'sum', footer: 'sum' },
    { key: 'actions', header: '', headerText: 'Actions', hideable: false, className: 'text-right nowrap', render: (c) => (
      <>
        <Button size="sm" variant="ghost" iconOnly icon={<Send size={15} />} aria-label="Send test" title="Send a test notification"
          loading={test.isPending && test.variables === c.id} onClick={() => test.mutate(c.id)} />
        <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={15} />} aria-label="Edit"
          onClick={() => setForm({ ...EMPTY, ...c, smtpHost: c.smtpHost ?? '', smtpUserName: c.smtpUserName ?? '', fromAddress: c.fromAddress ?? '',
            recipients: c.recipients ?? '', webhookUrl: c.webhookUrl ?? '', smtpPassword: undefined, webhookSecret: undefined })} />
        <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={15} />} aria-label="Delete" onClick={() => setDeleting(c)} />
      </>
    ) },
  ];

  return (
    <>
      <PageHeader title="Notifications" description="Send alerts by email or to webhooks (Slack, Microsoft Teams, SIEM/SOAR, ticketing) the moment they are raised or escalate."
        actions={<Button variant="primary" icon={<Plus size={16} />} onClick={() => setForm(EMPTY)}>New channel</Button>} />
      <Card>
        <DataTable columns={columns} rows={channels.data} rowKey={(c) => c.id} loading={channels.isLoading} pivot={false}
          exportName="notification-channels" empty={{ title: 'No notification channels', description: 'Add an email or webhook channel to be told about critical alerts right away.' }} />
      </Card>

      <Modal open={!!form} onClose={() => setForm(null)} title={form?.id ? 'Edit channel' : 'New notification channel'} size="lg"
        onSubmit={() => form && save.mutate(form)}
        footer={<><Button onClick={() => setForm(null)}>Cancel</Button><Button type="submit" variant="primary" loading={save.isPending}>Save channel</Button></>}>
        {form && (
          <div className="form-grid">
            <Field label="Name"><Input required maxLength={100} placeholder="SOC on-call" value={form.name} onChange={(e) => set('name', e.target.value)} /></Field>
            <Field label="Type">
              <Select value={form.type} onChange={(e) => set('type', e.target.value as FormState['type'])}
                options={[{ value: 'Email', label: 'Email (SMTP)' }, { value: 'Webhook', label: 'Webhook' }]} />
            </Field>
            <Field label="Minimum severity" hint="Alerts below this level are not sent.">
              <Select value={form.minSeverity} onChange={(e) => set('minSeverity', e.target.value as Severity)} options={severityOptions()} />
            </Field>
            <Field label="Options">
              <div className="stack-sm">
                <Switch checked={form.isEnabled} onChange={(v) => set('isEnabled', v)} label="Enabled" />
                <Switch checked={form.notifyOnEscalation} onChange={(v) => set('notifyOnEscalation', v)} label="Also when an alert escalates" />
              </div>
            </Field>

            {form.type === 'Email' ? (
              <>
                <Field label="Recipients" full hint="Separate addresses with commas.">
                  <Textarea required rows={2} placeholder="soc@example.com, oncall@example.com" value={form.recipients} onChange={(e) => set('recipients', e.target.value)} />
                </Field>
                <Field label="SMTP server"><Input required placeholder="smtp.office365.com" value={form.smtpHost} onChange={(e) => set('smtpHost', e.target.value)} /></Field>
                <Field label="Port"><Input required type="number" min={1} max={65535} value={form.smtpPort} onChange={(e) => set('smtpPort', Number(e.target.value))} /></Field>
                <Field label="From address"><Input required type="email" placeholder="security-manager@example.com" value={form.fromAddress} onChange={(e) => set('fromAddress', e.target.value)} /></Field>
                <Field label="Encryption"><Switch checked={form.smtpUseTls} onChange={(v) => set('smtpUseTls', v)} label={form.smtpUseTls ? 'STARTTLS / TLS' : 'None (not recommended)'} /></Field>
                <Field label="SMTP user name"><Input autoComplete="off" value={form.smtpUserName} onChange={(e) => set('smtpUserName', e.target.value)} /></Field>
                <Field label="SMTP password" hint={form.hasSmtpPassword ? 'Leave empty to keep the saved password.' : 'Stored encrypted.'}>
                  <Input type="password" autoComplete="new-password" placeholder={form.hasSmtpPassword ? '••••••••' : ''} value={form.smtpPassword ?? ''}
                    onChange={(e) => set('smtpPassword', e.target.value || undefined)} />
                </Field>
              </>
            ) : (
              <>
                <Field label="Webhook URL" full><Input required type="url" placeholder="https://hooks.slack.com/services/…" value={form.webhookUrl} onChange={(e) => set('webhookUrl', e.target.value)} /></Field>
                <Field label="Payload format">
                  <Select value={form.webhookFormat} onChange={(e) => set('webhookFormat', e.target.value as FormState['webhookFormat'])}
                    options={[{ value: 'Generic', label: 'Generic JSON', description: '{ event, alert }' }, { value: 'Slack', label: 'Slack incoming webhook' }, { value: 'Teams', label: 'Microsoft Teams webhook' }]} />
                </Field>
                <Field label="Signing secret (optional)" hint={form.hasWebhookSecret ? 'Leave empty to keep the saved secret.' : 'Adds X-SM-Signature: HMAC-SHA256(secret, timestamp + "." + body).'}>
                  <Input type="password" autoComplete="new-password" placeholder={form.hasWebhookSecret ? '••••••••' : ''} value={form.webhookSecret ?? ''}
                    onChange={(e) => set('webhookSecret', e.target.value || undefined)} />
                </Field>
              </>
            )}
          </div>
        )}
      </Modal>

      <ConfirmDialog open={!!deleting} title="Delete channel" message={<>Delete <b>{deleting?.name}</b>? Alerts will no longer be sent there.</>}
        loading={remove.isPending} onConfirm={() => deleting && remove.mutate(deleting.id)} onClose={() => setDeleting(null)} />
    </>
  );
}
