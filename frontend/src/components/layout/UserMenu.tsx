import { useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ChevronDown, KeyRound, LogOut, ShieldCheck } from 'lucide-react';
import { toast } from 'sonner';
import { useAuth } from '../../hooks/useAuth';
import { errorMessage } from '../../lib/api';
import { Badge } from '../ui/Badge';
import { Button } from '../ui/Button';
import { Field, Input } from '../ui/Form';
import { Modal } from '../ui/Modal';

export function UserMenu() {
  const { user, logout, changePassword } = useAuth();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [pwOpen, setPwOpen] = useState(false);
  const [form, setForm] = useState({ current: '', next: '', confirm: '' });
  const [busy, setBusy] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => !ref.current?.contains(e.target as Node) && setOpen(false);
    const esc = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', esc);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', esc);
    };
  }, [open]);

  if (!user) return null;
  const initials = user.fullName.split(' ').map((p) => p[0]).slice(0, 2).join('').toUpperCase();

  const submitPassword = async () => {
    if (form.next !== form.confirm) {
      toast.error('New passwords do not match.');
      return;
    }
    setBusy(true);
    try {
      await changePassword(form.current, form.next);
      toast.success('Password changed. Other sessions were signed out.');
      setPwOpen(false);
      setForm({ current: '', next: '', confirm: '' });
    } catch (err) {
      toast.error(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="menu" ref={ref}>
      <button type="button" className="menu-trigger" aria-haspopup="menu" aria-expanded={open} onClick={() => setOpen((o) => !o)}>
        <span className="avatar">{initials}</span>
        <span className="menu-user">
          <span className="menu-name">{user.fullName}</span>
          <span className="menu-role">{user.role}</span>
        </span>
        <ChevronDown size={14} />
      </button>
      {open && (
        <div className="menu-panel" role="menu">
          <div className="menu-header">
            <div style={{ fontWeight: 600 }}>{user.fullName}</div>
            <div className="muted small">@{user.userName} · {user.email}</div>
            <div style={{ marginTop: 6 }}><Badge tone={user.role === 'Admin' ? 'primary' : user.role === 'Viewer' ? 'neutral' : 'success'}>{user.role}</Badge></div>
          </div>
          <button type="button" role="menuitem" className="menu-item" onClick={() => { setOpen(false); navigate('/account/security'); }}>
            <ShieldCheck size={15} /> Account security{!user.mfaEnabled && <Badge tone="warning">MFA off</Badge>}
          </button>
          <button type="button" role="menuitem" className="menu-item" onClick={() => { setOpen(false); setPwOpen(true); }}>
            <KeyRound size={15} /> Change password
          </button>
          <button type="button" role="menuitem" className="menu-item danger" onClick={() => { setOpen(false); logout(); }}>
            <LogOut size={15} /> Sign out
          </button>
        </div>
      )}

      <Modal open={pwOpen} onClose={() => setPwOpen(false)} title="Change password" size="sm" onSubmit={submitPassword}
        footer={<><Button onClick={() => setPwOpen(false)}>Cancel</Button><Button type="submit" variant="primary" loading={busy}>Update password</Button></>}>
        <div className="stack">
          <Field label="Current password"><Input type="password" required autoComplete="current-password" value={form.current} onChange={(e) => setForm({ ...form, current: e.target.value })} /></Field>
          <Field label="New password" hint="8+ characters with upper, lower, digit and symbol">
            <Input type="password" required minLength={8} autoComplete="new-password" value={form.next} onChange={(e) => setForm({ ...form, next: e.target.value })} />
          </Field>
          <Field label="Confirm new password"><Input type="password" required autoComplete="new-password" value={form.confirm} onChange={(e) => setForm({ ...form, confirm: e.target.value })} /></Field>
        </div>
      </Modal>
    </div>
  );
}
