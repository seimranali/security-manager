import { useMemo, useState } from 'react';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { Ban, CheckCircle2, CircleHelp, Clock, Pencil, Search, ShieldCheck, ShieldX, Trash2, Upload } from 'lucide-react';
import { GRID_LIMIT, errorMessage, firewallApi } from '../../lib/api';
import { cn, formatDateTime, timeAgo, toDate } from '../../lib/format';
import { addDays, parseTime, today, toISODate } from '../../lib/dates';
import { describeAddress, isValidIp } from '../../lib/ip';
import type { IpCheckResult, IpImportResult, IpListEntry, IpListEntryInput, IpListType } from '../../lib/types';
import { useApiMutation } from '../../hooks/useApiMutation';
import { useAuth } from '../../hooks/useAuth';
import { Card, CardBody, CardHeader, PageHeader, StatCard } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { DatePicker, Field, Input, Select, Switch, Textarea, TimePicker } from '../../components/ui/Form';
import { ConfirmDialog, Modal } from '../../components/ui/Modal';
import { DataTable, type Column } from '../../components/ui/DataTable';

const KEY = [['firewall']];
type Tab = 'all' | IpListType;

// ---------- Expiry ----------
const EXPIRY_PRESETS = [
  { value: 'never', label: 'Never (permanent)' },
  { value: '60', label: '1 hour' },
  { value: '1440', label: '24 hours' },
  { value: '10080', label: '7 days' },
  { value: '43200', label: '30 days' },
  { value: 'custom', label: 'Custom date & time…' },
];

interface ExpiryState {
  preset: string;
  date: string;
  time: string;
}

const expiryToIso = (e: ExpiryState): string | null => {
  if (e.preset === 'never') return null;
  if (e.preset === 'custom') {
    if (!e.date) return null;
    const minutes = parseTime(e.time) ?? 0;
    const [y, m, d] = e.date.split('-').map(Number);
    return new Date(y, m - 1, d, Math.floor(minutes / 60), minutes % 60).toISOString();
  }
  return new Date(Date.now() + Number(e.preset) * 60_000).toISOString();
};

function ExpiryField({ value, onChange }: { value: ExpiryState; onChange: (v: ExpiryState) => void }) {
  return (
    <div className="stack-sm">
      <Select options={EXPIRY_PRESETS} value={value.preset} aria-label="Expiry" onChange={(e) => onChange({ ...value, preset: e.target.value })} />
      {value.preset === 'custom' && (
        <div className="row" style={{ gap: 8 }}>
          <DatePicker aria-label="Expiry date" min={today()} value={value.date} clearable={false} onChange={(date) => onChange({ ...value, date })} />
          <TimePicker aria-label="Expiry time" minuteStep={15} value={value.time} clearable={false} onChange={(time) => onChange({ ...value, time })} />
        </div>
      )}
    </div>
  );
}

// ---------- Status helpers ----------
function StatusCell({ e }: { e: IpListEntry }) {
  if (!e.isEnabled) return <Badge dot>Disabled</Badge>;
  if (e.isExpired) return <Badge tone="neutral" dot>Expired</Badge>;
  if (e.expiresAt) return <Badge tone="warning" dot><Clock size={11} /> Expires {timeAgo(e.expiresAt)}</Badge>;
  return <Badge tone="success" dot>Active</Badge>;
}
const statusText = (e: IpListEntry) => (!e.isEnabled ? 'Disabled' : e.isExpired ? 'Expired' : e.expiresAt ? 'Temporary' : 'Active');
const SOURCE_LABEL: Record<string, string> = { Manual: 'Manual', Import: 'Import', ThreatIntel: 'Threat intel', Detection: 'Auto (detection)' };

export default function IpListsPage() {
  const { canWrite } = useAuth();
  const [tab, setTab] = useState<Tab>('all');
  const [selection, setSelection] = useState<Set<string>>(new Set());
  const [editing, setEditing] = useState<{ id?: string; form: IpListEntryInput; expiry: ExpiryState } | null>(null);
  const [deleting, setDeleting] = useState<IpListEntry[] | null>(null);
  const [importing, setImporting] = useState(false);
  const [importForm, setImportForm] = useState({ listType: 'Block' as IpListType, addresses: '', description: '', expiry: { preset: 'never', date: '', time: '' } as ExpiryState });
  const [importResult, setImportResult] = useState<IpImportResult | null>(null);
  const [checkIp, setCheckIp] = useState('');
  const [checkResult, setCheckResult] = useState<IpCheckResult | null>(null);
  const [checkError, setCheckError] = useState<string | null>(null);

  const list = useQuery({
    queryKey: ['firewall', 'ip-lists'],
    queryFn: () => firewallApi.ipList({ page: 1, pageSize: GRID_LIMIT }),
    placeholderData: keepPreviousData,
    refetchInterval: 30_000,
  });
  const all = useMemo(() => list.data?.items ?? [], [list.data]);
  const rows = useMemo(() => (tab === 'all' ? all : all.filter((e) => e.listType === tab)), [all, tab]);
  const counts = useMemo(() => ({
    allowActive: all.filter((e) => e.listType === 'Allow' && e.isActive).length,
    blockActive: all.filter((e) => e.listType === 'Block' && e.isActive).length,
    temporary: all.filter((e) => e.isActive && e.expiresAt).length,
    auto: all.filter((e) => e.source === 'Detection' && e.isActive).length,
    hits: all.reduce((s, e) => s + e.hitCount, 0),
  }), [all]);

  const save = useApiMutation(
    (e: NonNullable<typeof editing>) => {
      const body = { ...e.form, expiresAt: expiryToIso(e.expiry) };
      return e.id ? firewallApi.updateIpEntry(e.id, body) : firewallApi.createIpEntry(body);
    },
    { invalidate: KEY, success: (r) => `${r.address} saved to the ${r.listType === 'Allow' ? 'allowlist' : 'blocklist'}`, onSuccess: () => setEditing(null) },
  );
  const toggle = useApiMutation((e: IpListEntry) => firewallApi.setIpEntryEnabled(e.id, !e.isEnabled), { invalidate: KEY });
  const remove = useApiMutation(
    (entries: IpListEntry[]) => (entries.length === 1 ? firewallApi.deleteIpEntry(entries[0].id).then(() => ({ deleted: 1 })) : firewallApi.deleteIpEntries(entries.map((e) => e.id))),
    { invalidate: KEY, success: (r) => `Removed ${r.deleted} entr${r.deleted === 1 ? 'y' : 'ies'}`, onSuccess: () => { setDeleting(null); setSelection(new Set()); } },
  );
  const runImport = useApiMutation(
    () => firewallApi.importIpList({ listType: importForm.listType, addresses: importForm.addresses, description: importForm.description || undefined, expiresAt: expiryToIso(importForm.expiry) }),
    { invalidate: KEY, success: (r) => `Imported ${r.added} address${r.added === 1 ? '' : 'es'}`, onSuccess: setImportResult },
  );

  const runCheck = async () => {
    setCheckError(null);
    setCheckResult(null);
    try {
      setCheckResult(await firewallApi.checkIp(checkIp.trim()));
    } catch (err) {
      setCheckError(errorMessage(err));
    }
  };

  const openNew = (listType: IpListType) =>
    setEditing({ form: { listType, address: '', description: '', isEnabled: true }, expiry: { preset: listType === 'Block' ? '1440' : 'never', date: toISODate(new Date(Date.now() + 86_400_000)), time: '00:00' } });
  const openEdit = (e: IpListEntry) => {
    const exp = e.expiresAt ? new Date(e.expiresAt) : null;
    setEditing({
      id: e.id,
      form: { listType: e.listType, address: e.address, description: e.description ?? '', isEnabled: e.isEnabled },
      expiry: exp && exp > new Date()
        ? { preset: 'custom', date: toISODate(exp), time: `${String(exp.getHours()).padStart(2, '0')}:${String(exp.getMinutes()).padStart(2, '0')}` }
        : { preset: 'never', date: addDays(today(), 1), time: '00:00' },
    });
  };

  const columns: Column<IpListEntry>[] = [
    { key: 'on', header: 'On', headerText: 'Enabled', width: 60, value: (e) => (e.isEnabled ? 'Enabled' : 'Disabled'),
      render: (e) => <Switch checked={e.isEnabled} disabled={!canWrite} ariaLabel="Enable entry" onChange={() => toggle.mutate(e)} /> },
    { key: 'list', header: 'List', value: (e) => (e.listType === 'Allow' ? 'Allowlist' : 'Blocklist'), groupCell: true,
      render: (e) => e.listType === 'Allow'
        ? <Badge tone="success"><ShieldCheck size={12} /> Allow</Badge>
        : <Badge tone="danger"><Ban size={12} /> Block</Badge> },
    { key: 'address', header: 'Address', value: (e) => e.address, groupable: false,
      render: (e) => <><code className="cell-title">{e.address}</code><div className="cell-sub">{describeAddress(e.address).detail}</div></> },
    { key: 'description', header: 'Reason', value: (e) => e.description, groupable: false,
      render: (e) => <span className="truncate" style={{ display: 'block', maxWidth: 280 }} title={e.description}>{e.description ?? <span className="muted">—</span>}</span> },
    { key: 'source', header: 'Source', value: (e) => SOURCE_LABEL[e.source], groupCell: true,
      render: (e) => <Badge tone={e.source === 'Detection' ? 'primary' : e.source === 'ThreatIntel' ? 'warning' : 'neutral'}>{SOURCE_LABEL[e.source]}</Badge> },
    { key: 'status', header: 'Status', value: statusText, groupCell: true, render: (e) => <StatusCell e={e} /> },
    { key: 'hits', header: 'Hits', align: 'right', value: (e) => e.hitCount, aggregate: 'sum', footer: 'sum' },
    { key: 'lastHit', header: 'Last hit', value: (e) => toDate(e.lastHitAt), filter: false, render: (e) => <span className="small nowrap">{timeAgo(e.lastHitAt)}</span> },
    { key: 'expires', header: 'Expires', value: (e) => toDate(e.expiresAt), filter: false, hidden: true,
      render: (e) => <span className="small nowrap">{e.expiresAt ? formatDateTime(e.expiresAt) : 'Never'}</span> },
    { key: 'createdBy', header: 'Added by', value: (e) => e.createdBy, hidden: true },
    { key: 'created', header: 'Added', value: (e) => new Date(e.createdAt), filter: false, render: (e) => <span className="small nowrap">{timeAgo(e.createdAt)}</span> },
    { key: 'actions', header: '', headerText: 'Actions', hideable: false, className: 'text-right nowrap', render: (e) => canWrite && (
      <>
        <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={15} />} aria-label="Edit" onClick={() => openEdit(e)} />
        <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={15} />} aria-label="Delete" onClick={() => setDeleting([e])} />
      </>
    ) },
  ];

  const addressInfo = editing ? describeAddress(editing.form.address) : null;
  const selectedEntries = all.filter((e) => selection.has(e.id));
  const setForm = <K extends keyof IpListEntryInput>(k: K, v: IpListEntryInput[K]) => setEditing((s) => s && { ...s, form: { ...s.form, [k]: v } });

  return (
    <div className="stack">
      <PageHeader
        title="IP Allowlist & Blocklist"
        description="Checked before any firewall rule: allowlisted addresses are always permitted, blocklisted addresses are always denied. Matches the remote party (source for inbound, destination for outbound)."
        actions={canWrite && (
          <>
            <Button icon={<Upload size={16} />} onClick={() => { setImportResult(null); setImporting(true); }}>Import</Button>
            <Button icon={<ShieldCheck size={16} />} onClick={() => openNew('Allow')}>Allow IP</Button>
            <Button variant="primary" icon={<Ban size={16} />} onClick={() => openNew('Block')}>Block IP</Button>
          </>
        )}
      />

      <div className="grid grid-kpi">
        <StatCard label="Active blocks" value={counts.blockActive.toLocaleString()} icon={<ShieldX size={20} />} tone="danger" hint={`${counts.auto} added automatically`} />
        <StatCard label="Trusted (allowlist)" value={counts.allowActive.toLocaleString()} icon={<ShieldCheck size={20} />} tone="success" />
        <StatCard label="Temporary entries" value={counts.temporary.toLocaleString()} icon={<Clock size={20} />} tone="warning" hint="Expire automatically" />
        <StatCard label="Total matches" value={counts.hits.toLocaleString()} icon={<CheckCircle2 size={20} />} hint="Connections decided by a list" />
      </div>

      <div className="grid grid-3">
        <Card className="span-2">
          <div className="tabs" role="tablist" aria-label="List">
            {([['all', 'All', all.length], ['Block', 'Blocklist', all.filter((e) => e.listType === 'Block').length], ['Allow', 'Allowlist', all.filter((e) => e.listType === 'Allow').length]] as const).map(([value, label, count]) => (
              <button key={value} type="button" role="tab" aria-selected={tab === value} className={cn('tab', tab === value && 'active')}
                onClick={() => { setTab(value); setSelection(new Set()); }}>
                {label}<span className="tab-count">{count}</span>
              </button>
            ))}
          </div>
          <DataTable
            columns={columns} rows={rows} totalCount={list.data?.totalCount} rowKey={(e) => e.id} loading={list.isLoading}
            selection={canWrite ? { selected: selection, onChange: setSelection } : undefined}
            initialSort={[{ key: 'created', dir: 'desc' }]} exportName={tab === 'all' ? 'ip-lists' : tab === 'Allow' ? 'ip-allowlist' : 'ip-blocklist'}
            storageKey="ip-lists"
            toolbar={selection.size > 0 && (
              <Button size="sm" variant="danger" icon={<Trash2 size={14} />} onClick={() => setDeleting(selectedEntries)}>Delete {selection.size}</Button>
            )}
            empty={{ title: 'No entries', description: 'Block or allow an address to get started.' }}
          />
        </Card>

        <Card>
          <CardHeader title="Test an IP" subtitle="See whether an address would be allowed or blocked by the lists." />
          <CardBody>
            <form className="stack-sm" onSubmit={(e) => { e.preventDefault(); runCheck(); }}>
              <div className="row">
                <Input className="mono" placeholder="e.g. 203.0.113.10" aria-label="IP address" value={checkIp} onChange={(e) => setCheckIp(e.target.value)} />
                <Button type="submit" icon={<Search size={15} />} disabled={!isValidIp(checkIp)}>Check</Button>
              </div>
              {checkError && <div className="callout callout-danger">{checkError}</div>}
              {checkResult && (
                <div className={cn('result-card', checkResult.verdict === 'Allowed' ? 'allow' : checkResult.verdict === 'Blocked' ? 'deny' : '')}>
                  {checkResult.verdict === 'Allowed' ? <ShieldCheck size={24} color="var(--success)" />
                    : checkResult.verdict === 'Blocked' ? <ShieldX size={24} color="var(--danger)" /> : <CircleHelp size={24} className="muted" />}
                  <div className="stack-sm" style={{ gap: 4, minWidth: 0 }}>
                    <b>{checkResult.verdict === 'NotListed' ? 'Not on any list' : checkResult.verdict} · <code>{checkResult.ip}</code></b>
                    <span className="small">
                      {checkResult.decidingEntry
                        ? <>Decided by <code>{checkResult.decidingEntry.address}</code>{checkResult.decidingEntry.description && ` — ${checkResult.decidingEntry.description}`}</>
                        : 'Firewall rules will decide.'}
                    </span>
                    {checkResult.matches.length > (checkResult.decidingEntry ? 1 : 0) && (
                      <span className="small muted">
                        Also matches: {checkResult.matches.filter((m) => m.id !== checkResult.decidingEntry?.id)
                          .map((m) => `${m.address} (${m.listType}${m.isActive ? '' : ', inactive'})`).join(', ')}
                      </span>
                    )}
                  </div>
                </div>
              )}
            </form>
          </CardBody>
        </Card>
      </div>

      {/* ---------- Add / edit ---------- */}
      <Modal open={!!editing} onClose={() => setEditing(null)} title={editing?.id ? 'Edit entry' : editing?.form.listType === 'Allow' ? 'Allow an address' : 'Block an address'}
        onSubmit={() => editing && save.mutate(editing)}
        footer={<><Button onClick={() => setEditing(null)}>Cancel</Button>
          <Button type="submit" variant={editing?.form.listType === 'Block' ? 'danger' : 'primary'} loading={save.isPending} disabled={!addressInfo?.valid}>
            {editing?.form.listType === 'Block' ? 'Save block' : 'Save allow'}
          </Button></>}>
        {editing && (
          <div className="form-grid">
            <div className="field full">
              <span className="field-label">List</span>
              <div className="segmented" role="radiogroup" aria-label="List">
                {(['Block', 'Allow'] as const).map((t) => (
                  <button key={t} type="button" role="radio" aria-checked={editing.form.listType === t}
                    className={cn('segment', editing.form.listType === t && 'active', t === 'Block' ? 'danger' : 'success')}
                    onClick={() => setForm('listType', t)}>
                    {t === 'Block' ? <><Ban size={14} /> Blocklist</> : <><ShieldCheck size={14} /> Allowlist</>}
                  </button>
                ))}
              </div>
            </div>
            <Field label="Address" full hint={addressInfo?.error ? <span style={{ color: 'var(--danger)' }}>{addressInfo.error}</span>
              : addressInfo?.valid ? `${addressInfo.kind}${addressInfo.detail ? ` · ${addressInfo.detail}` : ''}` : 'IP (1.2.3.4), CIDR (10.0.0.0/24) or range (10.0.0.1-10.0.0.50); IPv6 supported'}>
              <Input required autoFocus className="mono" placeholder="203.0.113.10" value={editing.form.address} onChange={(e) => setForm('address', e.target.value)} />
            </Field>
            <Field label="Reason" full><Textarea maxLength={500} placeholder="Why is this address listed?" value={editing.form.description} onChange={(e) => setForm('description', e.target.value)} /></Field>
            <Field label="Expires"><ExpiryField value={editing.expiry} onChange={(expiry) => setEditing((s) => s && { ...s, expiry })} /></Field>
            <Field label="Status"><Switch checked={editing.form.isEnabled} onChange={(v) => setForm('isEnabled', v)} label={editing.form.isEnabled ? 'Enabled' : 'Disabled'} /></Field>
          </div>
        )}
      </Modal>

      {/* ---------- Import ---------- */}
      <Modal open={importing} onClose={() => setImporting(false)} title="Import addresses" size="lg"
        onSubmit={importResult ? undefined : () => runImport.mutate(undefined)}
        footer={importResult
          ? <Button variant="primary" onClick={() => setImporting(false)}>Done</Button>
          : <><Button onClick={() => setImporting(false)}>Cancel</Button><Button type="submit" variant="primary" icon={<Upload size={15} />} loading={runImport.isPending} disabled={!importForm.addresses.trim()}>Import</Button></>}>
        {importResult ? (
          <div className="stack">
            <div className="grid grid-3">
              <StatCard label="Added" value={importResult.added} icon={<CheckCircle2 size={20} />} tone="success" />
              <StatCard label="Already listed" value={importResult.skipped} icon={<CircleHelp size={20} />} />
              <StatCard label="Invalid" value={importResult.invalid.length} icon={<ShieldX size={20} />} tone="danger" />
            </div>
            {importResult.invalid.length > 0 && (
              <div><div className="section-title">Rejected entries</div><div className="evidence">{importResult.invalid.join('\n')}</div></div>
            )}
          </div>
        ) : (
          <div className="form-grid">
            <Field label="Add to"><Select options={[{ value: 'Block', label: 'Blocklist', color: 'var(--danger)' }, { value: 'Allow', label: 'Allowlist', color: 'var(--success)' }]}
              value={importForm.listType} onChange={(e) => setImportForm({ ...importForm, listType: e.target.value as IpListType })} /></Field>
            <Field label="Expires"><ExpiryField value={importForm.expiry} onChange={(expiry) => setImportForm({ ...importForm, expiry })} /></Field>
            <Field label="Addresses" full hint="One per line, or separated by commas/spaces. Lines starting with # are comments. Up to 5,000 per import.">
              <Textarea className="mono" style={{ minHeight: 180 }} placeholder={'# Threat feed 2026-10-19\n192.0.2.15\n198.51.100.0/24\n203.0.113.1-203.0.113.20'}
                value={importForm.addresses} onChange={(e) => setImportForm({ ...importForm, addresses: e.target.value })} />
            </Field>
            <Field label="Reason (applied to all)" full><Input maxLength={500} value={importForm.description} onChange={(e) => setImportForm({ ...importForm, description: e.target.value })} /></Field>
          </div>
        )}
      </Modal>

      <ConfirmDialog open={!!deleting} title={deleting?.length === 1 ? 'Remove entry' : `Remove ${deleting?.length} entries`}
        message={deleting?.length === 1
          ? <>Remove <code>{deleting[0].address}</code> from the {deleting[0].listType === 'Allow' ? 'allowlist' : 'blocklist'}?</>
          : <>Remove {deleting?.length} selected entries? Traffic from these addresses will be evaluated by firewall rules again.</>}
        confirmLabel="Remove" loading={remove.isPending} onConfirm={() => deleting && remove.mutate(deleting)} onClose={() => setDeleting(null)} />
    </div>
  );
}
