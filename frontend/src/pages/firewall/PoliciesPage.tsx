import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { ArrowRightLeft, ListChecks, Pencil, Plus, ShieldHalf, Trash2 } from 'lucide-react';
import { firewallApi } from '../../lib/api';
import { timeAgo } from '../../lib/format';
import type { Policy, PolicyInput } from '../../lib/types';
import { useApiMutation } from '../../hooks/useApiMutation';
import { Card, PageHeader } from '../../components/ui/Card';
import { EnforcementStatus } from '../../components/EnforcementStatus';
import { Button } from '../../components/ui/Button';
import { Badge, StatusBadge } from '../../components/ui/Badge';
import { Field, Input, Select, Switch, Textarea } from '../../components/ui/Form';
import { ConfirmDialog, Modal } from '../../components/ui/Modal';
import { EmptyState, Spinner } from '../../components/ui/DataTable';

const EMPTY: PolicyInput = { name: '', description: '', isEnabled: true, priority: 100, direction: 'Any', defaultAction: 'Deny', enforceOnHosts: false };
const KEY = [['firewall']];

export default function PoliciesPage() {
  const navigate = useNavigate();
  const { data, isLoading } = useQuery({ queryKey: ['firewall', 'policies'], queryFn: firewallApi.policies });
  const [editing, setEditing] = useState<{ id?: string; form: PolicyInput } | null>(null);
  const [deleting, setDeleting] = useState<Policy | null>(null);

  const save = useApiMutation(
    (e: { id?: string; form: PolicyInput }) => (e.id ? firewallApi.updatePolicy(e.id, e.form) : firewallApi.createPolicy(e.form)),
    { invalidate: KEY, success: 'Policy saved', onSuccess: () => setEditing(null) },
  );
  const toggle = useApiMutation((p: Policy) => firewallApi.setPolicyEnabled(p.id, !p.isEnabled), { invalidate: KEY });
  const remove = useApiMutation((id: string) => firewallApi.deletePolicy(id), {
    invalidate: KEY, success: 'Policy deleted', onSuccess: () => setDeleting(null),
  });

  const set = <K extends keyof PolicyInput>(k: K, v: PolicyInput[K]) =>
    setEditing((e) => e && { ...e, form: { ...e.form, [k]: v } });

  return (
    <>
      <PageHeader
        title="Firewall Policies"
        description="Policies group rules and are evaluated in priority order (lowest first). The first matching rule wins."
        actions={<Button variant="primary" icon={<Plus size={16} />} onClick={() => setEditing({ form: EMPTY })}>New policy</Button>}
      />
      <EnforcementStatus />

      {isLoading ? <Spinner /> : !data?.length ? (
        <Card><EmptyState icon={<ShieldHalf size={22} />} title="No policies" description="Create a policy to start adding rules." /></Card>
      ) : (
        <div className="grid grid-cards">
          {data.map((p) => (
            <Card key={p.id} className={p.isEnabled ? 'policy-card' : 'policy-card disabled'}>
              <div className="row">
                <Badge tone="primary">P{p.priority}</Badge>
                <h3 className="card-title truncate" style={{ flex: 1 }}>{p.name}</h3>
                <Switch checked={p.isEnabled} ariaLabel="Enable policy" onChange={() => toggle.mutate(p)} />
              </div>
              <p className="muted" style={{ minHeight: 42 }}>{p.description || 'No description'}</p>
              <div className="meta">
                <span><ArrowRightLeft size={13} />{p.direction}</span>
                <span><ListChecks size={13} />{p.activeRuleCount}/{p.ruleCount} rules active</span>
                <span>Updated {timeAgo(p.updatedAt ?? p.createdAt)}</span>
              </div>
              <div className="row">
                <span className="muted small">Default</span>
                <StatusBadge status={p.defaultAction} />
                <span className="spacer" />
                <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={15} />} aria-label="Edit"
                  onClick={() => setEditing({ id: p.id, form: { name: p.name, description: p.description ?? '', isEnabled: p.isEnabled, priority: p.priority, direction: p.direction, defaultAction: p.defaultAction, enforceOnHosts: p.enforceOnHosts } })} />
                <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={15} />} aria-label="Delete" onClick={() => setDeleting(p)} />
                <Button size="sm" onClick={() => navigate(`/firewall/rules?policyId=${p.id}`)}>View rules</Button>
              </div>
            </Card>
          ))}
        </div>
      )}

      <Modal
        open={!!editing}
        onClose={() => setEditing(null)}
        title={editing?.id ? 'Edit policy' : 'New policy'}
        onSubmit={() => editing && save.mutate(editing)}
        footer={<><Button onClick={() => setEditing(null)}>Cancel</Button><Button type="submit" variant="primary" loading={save.isPending}>Save policy</Button></>}
      >
        {editing && (
          <div className="form-grid">
            <Field label="Name" full><Input required minLength={2} maxLength={100} value={editing.form.name} onChange={(e) => set('name', e.target.value)} /></Field>
            <Field label="Description" full><Textarea maxLength={500} value={editing.form.description} onChange={(e) => set('description', e.target.value)} /></Field>
            <Field label="Priority" hint="1 = evaluated first"><Input type="number" min={1} max={10000} required value={editing.form.priority} onChange={(e) => set('priority', Number(e.target.value))} /></Field>
            <Field label="Applies to"><Select options={['Inbound', 'Outbound', 'Any']} value={editing.form.direction} onChange={(e) => set('direction', e.target.value as PolicyInput['direction'])} /></Field>
            <Field label="Default action" hint="Used when no rule matches"><Select options={['Deny', 'Allow']} value={editing.form.defaultAction} onChange={(e) => set('defaultAction', e.target.value as PolicyInput['defaultAction'])} /></Field>
            <Field label="Status"><Switch checked={editing.form.isEnabled} onChange={(v) => set('isEnabled', v)} label={editing.form.isEnabled ? 'Enabled (rules enforced)' : 'Disabled (not enforced)'} /></Field>
          </div>
        )}
      </Modal>

      <ConfirmDialog
        open={!!deleting}
        title="Delete policy"
        message={<>Delete <b>{deleting?.name}</b> and its {deleting?.ruleCount} rule(s)? This cannot be undone.</>}
        loading={remove.isPending}
        onConfirm={() => deleting && remove.mutate(deleting.id)}
        onClose={() => setDeleting(null)}
      />
    </>
  );
}
