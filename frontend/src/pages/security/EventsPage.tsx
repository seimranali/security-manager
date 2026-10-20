import { useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Send } from 'lucide-react';
import { GRID_LIMIT, securityApi } from '../../lib/api';
import { formatDateTime, severityRank, splitCamel } from '../../lib/format';
import type { EventInput, SecurityEvent } from '../../lib/types';
import { EVENT_GROUP_ORDER, eventLookup, eventTypeOptions, severityOptions } from '../../lib/options';
import { useApiMutation } from '../../hooks/useApiMutation';
import { Card, PageHeader } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge, SeverityBadge } from '../../components/ui/Badge';
import { DatePicker, Dropdown, Field, Input, Select, Textarea, type DateRange } from '../../components/ui/Form';
import { isCompleteRange } from '../../components/ui/DatePicker';
import { dayBoundsUtc, today } from '../../lib/dates';
import { Modal } from '../../components/ui/Modal';
import { DataTable, type Column } from '../../components/ui/DataTable';

const typeOptions = eventTypeOptions();
const EMPTY: EventInput = {
  eventType: 'AuthenticationFailure', severity: 'Low', source: 'Manual', message: 'Failed login for user admin',
  sourceIp: '185.220.101.4', userName: 'admin', targetHost: 'web-01',
};

export default function EventsPage() {
  const [type, setType] = useState('');
  const [minSeverity, setMinSeverity] = useState('');
  const [sourceIp, setSourceIp] = useState('');
  const [userName, setUserName] = useState('');
  const [targetHost, setTargetHost] = useState('');
  const [dates, setDates] = useState<DateRange>({ start: '', end: '' });
  const [form, setForm] = useState<EventInput | null>(null);
  const [selected, setSelected] = useState<SecurityEvent | null>(null);
  const bounds = isCompleteRange(dates) ? dayBoundsUtc(dates.start, dates.end) : null;

  const events = useQuery({
    queryKey: ['detections', 'events', type, minSeverity, sourceIp, userName, targetHost, bounds?.from, bounds?.to],
    queryFn: () => securityApi.events({
      eventType: type, minSeverity, sourceIp, userName, targetHost, from: bounds?.from, to: bounds?.to, page: 1, pageSize: GRID_LIMIT,
    }),
    placeholderData: keepPreviousData,
    refetchInterval: 30_000,
  });

  const ingest = useApiMutation(securityApi.ingest, {
    invalidate: [['detections']],
    success: (r) => (r.newDetections.length ? `Event ingested — ${r.newDetections.length} new detection(s) raised!` : 'Event ingested'),
  });
  const set = <K extends keyof EventInput>(k: K, v: EventInput[K]) => setForm((f) => f && { ...f, [k]: v });

  const columns: Column<SecurityEvent>[] = [
    { key: 'time', header: 'Time', value: (e) => new Date(e.timestamp), filter: false, footer: 'count',
      render: (e) => <span className="nowrap small">{formatDateTime(e.timestamp)}</span> },
    { key: 'sev', header: 'Severity', value: (e) => e.severity, sortValue: (e) => severityRank(e.severity), groupCell: true,
      render: (e) => <SeverityBadge severity={e.severity} /> },
    { key: 'type', header: 'Type', value: (e) => splitCamel(e.eventType), groupCell: true, render: (e) => <Badge>{splitCamel(e.eventType)}</Badge> },
    { key: 'source', header: 'Source', value: (e) => e.source },
    { key: 'msg', header: 'Message', value: (e) => e.message, groupable: false, render: (e) => <div className="truncate" style={{ maxWidth: 360 }}>{e.message}</div> },
    { key: 'ip', header: 'Source IP', group: 'Context', value: (e) => e.sourceIp, render: (e) => e.sourceIp ? <code>{e.sourceIp}</code> : '—' },
    { key: 'user', header: 'User', group: 'Context', value: (e) => e.userName },
    { key: 'host', header: 'Host', group: 'Context', value: (e) => e.targetHost },
  ];

  return (
    <>
      <PageHeader
        title="Security Events"
        description="Normalized events from firewall, identity and endpoint sources. Every event runs through the detection engine."
        actions={<Button variant="primary" icon={<Send size={16} />} onClick={() => setForm(EMPTY)}>Ingest event</Button>}
      />
      <Card>
        <DataTable
          columns={columns} rows={events.data?.items} totalCount={events.data?.totalCount} rowKey={(e) => e.id}
          loading={events.isLoading} onRowClick={setSelected} initialSort={[{ key: 'time', dir: 'desc' }]}
          exportName="security-events" storageKey="security-events"
          toolbar={<>
            <DatePicker mode="range" aria-label="Date range" placeholder="Any date" max={today()} value={dates} onChange={setDates} />
            <Select options={typeOptions} groupOrder={EVENT_GROUP_ORDER} searchable placeholder="All types" aria-label="Event type" value={type} onChange={(e) => setType(e.target.value)} />
            <Select options={severityOptions('+')} placeholder="Any severity" aria-label="Minimum severity" value={minSeverity} onChange={(e) => setMinSeverity(e.target.value)} />
            {/* Dynamically loaded (server-side search + infinite scroll) distinct values, most frequent first. */}
            <Dropdown loadOptions={eventLookup('sourceIp')} sortToggle clearable placeholder="Any source IP" searchPlaceholder="Search IPs…"
              aria-label="Source IP" value={sourceIp} onChange={setSourceIp} />
            <Dropdown loadOptions={eventLookup('userName')} sortToggle clearable placeholder="Any user" searchPlaceholder="Search users…"
              aria-label="User" value={userName} onChange={setUserName} />
            <Dropdown loadOptions={eventLookup('targetHost')} sortToggle clearable placeholder="Any host" searchPlaceholder="Search hosts…"
              aria-label="Host" value={targetHost} onChange={setTargetHost} />
          </>}
          empty={{ title: 'No events match' }}
        />
      </Card>

      <Modal open={!!form} onClose={() => setForm(null)} title="Ingest security event" size="lg"
        onSubmit={() => form && ingest.mutate(form)}
        footer={<><Button onClick={() => setForm(null)}>Close</Button><Button type="submit" variant="primary" loading={ingest.isPending} icon={<Send size={15} />}>Send event</Button></>}>
        {form && (
          <div className="stack">
            <p className="callout">Tip: send the default brute-force event 5 times to trip the <b>Brute Force Attack</b> rule and watch a real-time alert arrive.</p>
            <div className="form-grid">
              <Field label="Event type"><Select options={typeOptions} groupOrder={EVENT_GROUP_ORDER} searchable value={form.eventType} onChange={(e) => set('eventType', e.target.value as EventInput['eventType'])} /></Field>
              <Field label="Severity"><Select options={severityOptions()} value={form.severity} onChange={(e) => set('severity', e.target.value as EventInput['severity'])} /></Field>
              <Field label="Source system"><Input required maxLength={100} value={form.source} onChange={(e) => set('source', e.target.value)} /></Field>
              <Field label="Source IP"><Input className="mono" value={form.sourceIp ?? ''} onChange={(e) => set('sourceIp', e.target.value)} /></Field>
              <Field label="User name"><Input value={form.userName ?? ''} onChange={(e) => set('userName', e.target.value)} /></Field>
              <Field label="Target host"><Input value={form.targetHost ?? ''} onChange={(e) => set('targetHost', e.target.value)} /></Field>
              <Field label="Message" full><Textarea required maxLength={1000} value={form.message} onChange={(e) => set('message', e.target.value)} /></Field>
            </div>
          </div>
        )}
      </Modal>

      <Modal open={!!selected} onClose={() => setSelected(null)} title="Event details" variant="drawer">
        {selected && (
          <dl className="kv">
            <dt>Time</dt><dd>{formatDateTime(selected.timestamp)}</dd>
            <dt>Type</dt><dd>{splitCamel(selected.eventType)}</dd>
            <dt>Severity</dt><dd><SeverityBadge severity={selected.severity} /></dd>
            <dt>Source</dt><dd>{selected.source}</dd>
            <dt>Message</dt><dd>{selected.message}</dd>
            <dt>Source IP</dt><dd>{selected.sourceIp ?? '—'}</dd>
            <dt>User</dt><dd>{selected.userName ?? '—'}</dd>
            <dt>Target host</dt><dd>{selected.targetHost ?? '—'}</dd>
            <dt>Processed</dt><dd>{selected.isProcessed ? 'Yes' : 'Pending'}</dd>
            {selected.rawData && <><dt>Raw data</dt><dd><div className="evidence">{selected.rawData}</div></dd></>}
          </dl>
        )}
      </Modal>
    </>
  );
}
