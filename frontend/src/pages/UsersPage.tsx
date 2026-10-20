import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { LogOut, Pencil, Plus, ShieldOff, Trash2 } from 'lucide-react';
import { authApi } from '../lib/api';
import { timeAgo, toDate } from '../lib/format';
import type { AuthUser, UserRole } from '../lib/types';
import { roleOptions } from '../lib/options';
import { useAuth } from '../hooks/useAuth';
import { useApiMutation } from '../hooks/useApiMutation';
import { Card, PageHeader } from '../components/ui/Card';
import { Button } from '../components/ui/Button';
import { Badge, StatusBadge } from '../components/ui/Badge';
import { Field, Input, Select, Switch } from '../components/ui/Form';
import { ConfirmDialog, Modal } from '../components/ui/Modal';
import { DataTable, type Column } from '../components/ui/DataTable';

interface FormState {
  id?: string;
  userName: string;
  email: string;
  fullName: string;
  role: UserRole;
  isActive: boolean;
  password: string;
}

const EMPTY: FormState = { userName: '', email: '', fullName: '', role: 'Viewer', isActive: true, password: '' };
const KEY = [['users']];
const ROLE_TONE = { Admin: 'primary', Analyst: 'success', Viewer: 'neutral' } as const;

export default function UsersPage() {
  const { user: me } = useAuth();
  const users = useQuery({ queryKey: ['users'], queryFn: authApi.users });
  const [form, setForm] = useState<FormState | null>(null);
  const [deleting, setDeleting] = useState<AuthUser | null>(null);
  const [resetting, setResetting] = useState<AuthUser | null>(null);

  const save = useApiMutation(
    (f: FormState) => f.id
      ? authApi.updateUser(f.id, { email: f.email, fullName: f.fullName, role: f.role, isActive: f.isActive, newPassword: f.password || undefined })
      : authApi.createUser({ userName: f.userName, email: f.email, fullName: f.fullName, role: f.role, password: f.password }),
    { invalidate: KEY, success: 'User saved', onSuccess: () => setForm(null) },
  );
  const revoke = useApiMutation((u: AuthUser) => authApi.revokeSessions(u.id), {
    invalidate: KEY, success: (r) => `Signed out of ${r.revoked} session(s)`,
  });
  const resetMfa = useApiMutation((u: AuthUser) => authApi.resetMfa(u.id), {
    invalidate: KEY, success: 'Two-step verification removed; the user was signed out', onSuccess: () => setResetting(null),
  });
  const remove = useApiMutation((id: string) => authApi.deleteUser(id), { invalidate: KEY, success: 'User deleted', onSuccess: () => setDeleting(null) });
  const set = <K extends keyof FormState>(k: K, v: FormState[K]) => setForm((f) => f && { ...f, [k]: v });

  const statusOf = (u: AuthUser) => (u.isLockedOut ? 'Locked' : u.isActive ? 'Enabled' : 'Disabled');
  const columns: Column<AuthUser>[] = [
    { key: 'user', header: 'User', value: (u) => u.fullName, groupable: false,
      render: (u) => <><div className="cell-title">{u.fullName}{u.id === me?.id && <span className="muted"> (you)</span>}</div><div className="cell-sub">@{u.userName}</div></> },
    { key: 'email', header: 'Email', value: (u) => u.email, groupable: false },
    { key: 'role', header: 'Role', value: (u) => u.role, groupCell: true, footer: 'distinct', render: (u) => <Badge tone={ROLE_TONE[u.role]}>{u.role}</Badge> },
    { key: 'status', header: 'Status', value: statusOf, groupCell: true,
      render: (u) => u.isLockedOut ? <Badge tone="warning" dot>Locked</Badge> : <StatusBadge status={u.isActive ? 'Enabled' : 'Disabled'} /> },
    { key: 'mfa', header: 'MFA', value: (u) => (u.mfaEnabled ? 'On' : 'Off'), groupCell: true,
      render: (u) => u.mfaEnabled ? <Badge tone="success" dot>On</Badge> : <Badge tone="warning">Off</Badge> },
    { key: 'last', header: 'Last sign-in', value: (u) => toDate(u.lastLoginAt), filter: false, render: (u) => <span className="small">{timeAgo(u.lastLoginAt)}</span> },
    { key: 'created', header: 'Created', value: (u) => new Date(u.createdAt), filter: false, hidden: true },
    { key: 'actions', header: '', headerText: 'Actions', hideable: false, className: 'text-right nowrap', render: (u) => (
      <>
        <Button size="sm" variant="ghost" iconOnly icon={<LogOut size={15} />} aria-label="Sign out everywhere" title="Sign out everywhere" onClick={() => revoke.mutate(u)} />
        {u.mfaEnabled && <Button size="sm" variant="ghost" iconOnly icon={<ShieldOff size={15} />} aria-label="Reset two-step verification" title="Reset two-step verification (lost device)" onClick={() => setResetting(u)} />}
        <Button size="sm" variant="ghost" iconOnly icon={<Pencil size={15} />} aria-label="Edit"
          onClick={() => setForm({ id: u.id, userName: u.userName, email: u.email, fullName: u.fullName, role: u.role, isActive: u.isActive, password: '' })} />
        <Button size="sm" variant="ghost" iconOnly icon={<Trash2 size={15} />} aria-label="Delete" disabled={u.id === me?.id} onClick={() => setDeleting(u)} />
      </>
    ) },
  ];

  return (
    <>
      <PageHeader title="Users" description="Manage accounts and roles. Admins manage users; Analysts can change configuration; Viewers are read-only."
        actions={<Button variant="primary" icon={<Plus size={16} />} onClick={() => setForm(EMPTY)}>New user</Button>} />
      <Card>
        <DataTable columns={columns} rows={users.data} rowKey={(u) => u.id} loading={users.isLoading}
          initialSort={[{ key: 'user', dir: 'asc' }]} exportName="users" storageKey="users" empty={{ title: 'No users' }} />
      </Card>

      <Modal open={!!form} onClose={() => setForm(null)} title={form?.id ? 'Edit user' : 'New user'} onSubmit={() => form && save.mutate(form)}
        footer={<><Button onClick={() => setForm(null)}>Cancel</Button><Button type="submit" variant="primary" loading={save.isPending}>Save user</Button></>}>
        {form && (
          <div className="form-grid">
            <Field label="User name"><Input required disabled={!!form.id} minLength={3} maxLength={50} pattern="[a-zA-Z0-9._\-]+" value={form.userName} onChange={(e) => set('userName', e.target.value)} /></Field>
            <Field label="Role"><Select options={roleOptions()} value={form.role} disabled={form.id === me?.id} onChange={(e) => set('role', e.target.value as UserRole)} /></Field>
            <Field label="Full name" full><Input required maxLength={100} value={form.fullName} onChange={(e) => set('fullName', e.target.value)} /></Field>
            <Field label="Email" full><Input required type="email" maxLength={200} value={form.email} onChange={(e) => set('email', e.target.value)} /></Field>
            <Field label={form.id ? 'Reset password (optional)' : 'Password'} full hint="8+ characters with upper, lower, digit and symbol">
              <Input type="password" required={!form.id} autoComplete="new-password" value={form.password} onChange={(e) => set('password', e.target.value)} />
            </Field>
            {form.id && (
              <Field label="Status"><Switch checked={form.isActive} disabled={form.id === me?.id} onChange={(v) => set('isActive', v)} label={form.isActive ? 'Active' : 'Disabled'} /></Field>
            )}
          </div>
        )}
      </Modal>

      <ConfirmDialog open={!!resetting} title="Reset two-step verification"
        message={<>Remove the authenticator from <b>{resetting?.fullName}</b>? They are signed out everywhere and can sign in with their password only until they set MFA up again. Do this only after confirming their identity.</>}
        loading={resetMfa.isPending} onConfirm={() => resetting && resetMfa.mutate(resetting)} onClose={() => setResetting(null)} />

      <ConfirmDialog open={!!deleting} title="Delete user" message={<>Delete <b>{deleting?.fullName}</b>? Their sessions end immediately.</>}
        loading={remove.isPending} onConfirm={() => deleting && remove.mutate(deleting.id)} onClose={() => setDeleting(null)} />
    </>
  );
}
