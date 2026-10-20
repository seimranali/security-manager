import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Earth, Pencil, Plus, RefreshCw, Rss, Trash2 } from 'lucide-react';
import { feedApi } from '../../lib/api';
import { formatNumber, timeAgo } from '../../lib/format';
import type { ThreatFeed, ThreatFeedInput } from '../../lib/types';
import { useAuth } from '../../hooks/useAuth';
import { useApiMutation } from '../../hooks/useApiMutation';
import { Card, PageHeader } from '../../components/ui/Card';
import { Button } from '../../components/ui/Button';
import { Badge } from '../../components/ui/Badge';
import { Field, Input, Select, Switch } from '../../components/ui/Form';
import { ConfirmDialog, Modal } from '../../components/ui/Modal';
import { DataTable, type Column } from '../../components/ui/DataTable';

/** Well-known free reputation lists (plain-text IP/CIDR). Check each provider's terms for commercial use. */
const PRESETS: { name: string; url: string; note: string }[] = [
  { name: 'Spamhaus DROP', url: 'https://www.spamhaus.org/drop/drop.txt', note: 'Hijacked / criminal netblocks. Very low false positives.' },
  { name: 'abuse.ch Feodo Tracker', url: 'https://feodotracker.abuse.ch/downloads/ipblocklist.txt', note: 'Botnet command-and-control servers.' },
  { name: 'Emerging Threats compromised', url: 'https://rules.emergingthreats.net/blockrules/compromised-ips.txt', note: 'Known compromised hosts.' },
  { name: 'CINS Army', url: 'https://cinsscore.com/list/ci-badguys.txt', note: 'IPs with poor reputation scores.' },
  { name: 'FireHOL level 1', url: 'https://iplists.firehol.org/files/firehol_level1.netset', note: 'Aggregated attack sources. Private ranges are skipped automatically.' },
];

const COUNTRY_CODES = ('AF AL DZ AD AO AR AM AU AT AZ BH BD BY BE BZ BJ BT BO BA BW BR BN BG BF BI KH CM CA CF TD CL CN CO KM CG CD CR CI HR CU CY CZ ' +
  'DK DJ DO EC EG SV GQ ER EE ET FI FR GA GM GE DE GH GR GT GN GW GY HT HN HK HU IS IN ID IR IQ IE IL IT JM JP JO KZ KE KP KR KW KG LA LV LB LS LR LY ' +
  'LI LT LU MO MG MW MY MV ML MT MR MU MX MD MC MN ME MA MZ MM NA NP NL NZ NI NE NG MK NO OM PK PA PG PY PE PH PL PT QA RO RU RW SA SN RS SL SG SK SI ' +
  'SO ZA SS ES LK SD SR SE CH SY TW TJ TZ TH TG TT TN TR TM UG UA AE GB US UY UZ VE VN YE ZM ZW').split(' ');

const countryName = (() => {
  let names: Intl.DisplayNames | null = null;
  try { names = new Intl.DisplayNames(['en'], { type: 'region' }); } catch { /* unsupported */ }
  return (code: string) => names?.of(code) ?? code;
})();

const flag = (code?: string) => code ? String.fromCodePoint(...[...code.toUpperCase()].map((c) => 0x1f1e6 + c.charCodeAt(0) - 65)) : '';

const EMPTY: ThreatFeedInput = { name: '', url: '', kind: 'ThreatIntel', listType: 'Block', isEnabled: true, refreshMinutes: 360, maxEntries: 50000 };
const KEY = [['threat-feeds'], ['ip-lists']];

export default function ThreatFeedsPage() {
  const { canWrite } = useAuth();
  const feeds = useQuery({ queryKey: ['threat-feeds'], queryFn: feedApi.feeds, refetchInterval: 30_000 });
  const [form, setForm] = useState<(ThreatFeedInput & { id?: string }) | null>(null);
  const [deleting, setDeleting] = useState<ThreatFeed | null>(null);

  const countryOptions = useMemo(() => COUNTRY_CODES.map((c) => ({ value: c, label: `${flag(c)}  ${countryName(c)}`, description: c }))
    .sort((a, b) => a.label.localeCompare(b.label)), []);

  const save = useApiMutation((f: ThreatFeedInput & { id?: string }) => {
    const { id, ...body } = f;
    return id ? feedApi.update(id, body) : feedApi.create(body);
  }, { invalidate: KEY, success: 'Feed saved. It is downloaded within a minute.', onSuccess: () => setForm(null) });
  const refresh = useApiMutation((id: string) => feedApi.refresh(id), {
    invalidate: KEY,
    success: (r) => r.feed.lastError ? `Refresh failed: ${r.feed.lastError}` : `Updated: ${formatNumber(r.added)} added, ${formatNumber(r.removed)} removed`,
  });
  const remove = useApiMutation((id: string) => feedApi.remove(id), { invalidate: KEY, success: 'Feed and its entries removed', onSuccess: () => setDeleting(null) });
  const set = <K extends keyof ThreatFeedInput>(k: K, v: ThreatFeedInput[K]) => setForm((f) => f && { ...f, [k]: v });

  const pickCountry = (code: string) => setForm((f) => f && {
    ...f, countryCode: code, name: f.name && !f.name.startsWith('Country: ') ? f.name : `Country: ${countryName(code)}`,
    url: `https://www.ipdeny.com/ipblocks/data/aggregated/${code.toLowerCase()}-aggregated.zone`,
  });

  const columns: Column<ThreatFeed>[] = [
    { key: 'name', header: 'Feed', value: (f) => f.name, groupable: false,
      render: (f) => <><div className="cell-title row" style={{ gap: 6 }}>{f.kind === 'Country' ? <span>{flag(f.countryCode)}</span> : <Rss size={14} />}{f.name}</div>
        <div className="cell-sub truncate mono" style={{ maxWidth: 360 }} title={f.url}>{f.url}</div></> },
    { key: 'kind', header: 'Type', value: (f) => (f.kind === 'Country' ? 'Country' : 'Threat intel') },
    { key: 'list', header: 'Action', value: (f) => f.listType, groupCell: true,
      render: (f) => f.listType === 'Block' ? <Badge tone="danger">Block</Badge> : <Badge tone="success">Allow</Badge> },
    { key: 'entries', header: 'Entries', align: 'right', value: (f) => f.entryCount, aggregate: 'sum', footer: 'sum',
      render: (f) => <><div>{formatNumber(f.entryCount)}</div>{f.skippedCount > 0 && <div className="cell-sub" title="Invalid, too broad or private/reserved">{formatNumber(f.skippedCount)} skipped</div>}</> },
    { key: 'status', header: 'Status', value: (f) => (!f.isEnabled ? 'Disabled' : f.lastError ? 'Failing' : f.lastSuccessAt ? 'Up to date' : 'Pending'), groupCell: true,
      render: (f) => !f.isEnabled ? <Badge>Disabled</Badge> : f.lastError ? <Badge tone="danger" dot>Failing</Badge>
        : f.lastSuccessAt ? <Badge tone="success" dot>Up to date</Badge> : <Badge tone="warning" dot>Pending</Badge> },
    { key: 'updated', header: 'Last update', value: (f) => f.lastSuccessAt ?? '', filter: false,
      render: (f) => <><div className="small">{f.lastSuccessAt ? timeAgo(f.lastSuccessAt) : 'never'}</div>
        {f.lastError && <div className="cell-sub text-danger truncate" style={{ maxWidth: 260 }} title={f.lastError}>{f.lastError}</div>}</> },
    { key: 'every', header: 'Refresh', value: (f) => f.refreshMinutes, render: (f) => <span className="small">every {f.refreshMinutes >= 60 ? `${f.refreshMinutes / 60} h` : `${f.refreshMinutes} min`}</span> },
    { key: 'actions', header: '', headerText: 'Actions', hideable: false, className: 'text-right nowrap', render: (f) => canWrite && (
      <>
        <Button size="sm" variant="ghost" iconOnly icon={<RefreshCw size={15} />} aria-label="Refresh now" title="Download now"
          loading={refresh.isPending && refresh.variables === f.id} onClick={() => refresh.mutate(f.id)} />
        <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={15} />} aria-label="Edit" onClick={() => setForm({ ...f })} />
        <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={15} />} aria-label="Delete" onClick={() => setDeleting(f)} />
      </>
    ) },
  ];

  return (
    <>
      <PageHeader title="Threat & Geo Feeds"
        description="Keep the IP blocklist current from threat-intelligence lists, and block or allow whole countries. Entries are enforced like any other IP list entry; private and reserved ranges are never blocked."
        actions={canWrite && <div className="row-wrap">
          <Button icon={<Earth size={16} />} onClick={() => setForm({ ...EMPTY, kind: 'Country', refreshMinutes: 1440, name: '', url: '' })}>Block a country</Button>
          <Button variant="primary" icon={<Plus size={16} />} onClick={() => setForm(EMPTY)}>Add feed</Button>
        </div>} />
      <Card>
        <DataTable columns={columns} rows={feeds.data} rowKey={(f) => f.id} loading={feeds.isLoading} pivot={false} exportName="threat-feeds"
          empty={{ title: 'No feeds yet', description: 'Add Spamhaus DROP or another reputation list, or block traffic from specific countries.' }} />
      </Card>

      <Modal open={!!form} onClose={() => setForm(null)} size="lg" onSubmit={() => form && save.mutate(form)}
        title={form?.id ? 'Edit feed' : form?.kind === 'Country' ? 'Block or allow a country' : 'Add threat feed'}
        footer={<><Button onClick={() => setForm(null)}>Cancel</Button><Button type="submit" variant="primary" loading={save.isPending}>Save feed</Button></>}>
        {form && (
          <div className="form-grid">
            {form.kind === 'Country' ? (
              <Field label="Country" full hint="Address blocks from ipdeny.com (IPv4, aggregated). Geo data is approximate: VPNs and proxies are not covered.">
                <Select searchable value={form.countryCode ?? ''} placeholder="Choose a country" options={countryOptions} onChange={(e) => pickCountry(e.target.value)} />
              </Field>
            ) : !form.id && (
              <Field label="Start from a known list" full>
                <Select searchable value={PRESETS.find((p) => p.url === form.url)?.url ?? ''} placeholder="Custom URL"
                  options={PRESETS.map((p) => ({ value: p.url, label: p.name, description: p.note }))}
                  onChange={(e) => { const p = PRESETS.find((x) => x.url === e.target.value); if (p) setForm({ ...form, name: p.name, url: p.url }); }} />
              </Field>
            )}
            <Field label="Name"><Input required maxLength={100} value={form.name} onChange={(e) => set('name', e.target.value)} /></Field>
            <Field label="Action">
              <Select value={form.listType} onChange={(e) => set('listType', e.target.value as ThreatFeedInput['listType'])}
                options={[{ value: 'Block', label: 'Block these addresses' }, { value: 'Allow', label: 'Allow these addresses', description: 'Allowlist wins over every block' }]} />
            </Field>
            <Field label="Feed URL" full hint="Plain text, one IP / CIDR / range per line; # and ; start comments.">
              <Input required type="url" className="mono" value={form.url} onChange={(e) => set('url', e.target.value)} />
            </Field>
            <Field label="Refresh every">
              <Select value={String(form.refreshMinutes)} onChange={(e) => set('refreshMinutes', Number(e.target.value))}
                options={[['60', '1 hour'], ['180', '3 hours'], ['360', '6 hours'], ['720', '12 hours'], ['1440', '1 day'], ['10080', '1 week']].map(([value, label]) => ({ value, label }))} />
            </Field>
            <Field label="Safety limit (entries)" hint="A download larger than this is rejected and the old list kept.">
              <Input type="number" min={1} max={200000} value={form.maxEntries} onChange={(e) => set('maxEntries', Number(e.target.value))} />
            </Field>
            <Field label="Status"><Switch checked={form.isEnabled} onChange={(v) => set('isEnabled', v)} label={form.isEnabled ? 'Enabled' : 'Disabled'} /></Field>
            {form.listType === 'Allow' && <div className="callout callout-warning full">Allowlisted addresses bypass every block rule. Use allow feeds only for sources you trust completely.</div>}
          </div>
        )}
      </Modal>

      <ConfirmDialog open={!!deleting} title="Delete feed"
        message={<>Delete <b>{deleting?.name}</b> and its {formatNumber(deleting?.entryCount)} IP list entries? Hosts stop enforcing them on their next sync.</>}
        loading={remove.isPending} onConfirm={() => deleting && remove.mutate(deleting.id)} onClose={() => setDeleting(null)} />
    </>
  );
}
