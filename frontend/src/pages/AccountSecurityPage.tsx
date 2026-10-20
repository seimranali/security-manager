import { useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import QRCode from 'qrcode';
import { Copy, Download, ShieldCheck, ShieldOff, Smartphone } from 'lucide-react';
import { toast } from 'sonner';
import { authApi } from '../lib/api';
import { formatDateTime, timeAgo } from '../lib/format';
import type { LoginAttempt, MfaSetup } from '../lib/types';
import { useAuth } from '../hooks/useAuth';
import { useApiMutation } from '../hooks/useApiMutation';
import { Card, CardBody, CardHeader, PageHeader } from '../components/ui/Card';
import { Button } from '../components/ui/Button';
import { Badge } from '../components/ui/Badge';
import { Field, Input } from '../components/ui/Form';
import { Modal } from '../components/ui/Modal';
import { DataTable, type Column } from '../components/ui/DataTable';

export const FAILURE_LABEL: Record<string, string> = {
  UnknownUser: 'Unknown user',
  BadPassword: 'Wrong password',
  LockedOut: 'Account locked',
  Disabled: 'Account disabled',
  BadMfaCode: 'Wrong verification code',
};

export const loginColumns = (showUser: boolean): Column<LoginAttempt>[] => [
  { key: 'time', header: 'Time', value: (a) => new Date(a.timestamp), filter: false,
    render: (a) => <><div>{formatDateTime(a.timestamp)}</div><div className="cell-sub">{timeAgo(a.timestamp)}</div></> },
  ...(showUser ? [{ key: 'user', header: 'User', value: (a: LoginAttempt) => a.userName, render: (a: LoginAttempt) => <span className="cell-title">{a.userName}</span> }] : []),
  { key: 'result', header: 'Result', value: (a) => (a.succeeded ? 'Success' : 'Failed'), groupCell: true,
    render: (a) => a.succeeded
      ? <Badge tone="success" dot>Success{a.usedMfa ? ' · MFA' : ''}</Badge>
      : <Badge tone="danger" dot>{FAILURE_LABEL[a.failureReason ?? ''] ?? 'Failed'}</Badge> },
  { key: 'ip', header: 'IP address', value: (a) => a.ipAddress ?? '', render: (a) => <code>{a.ipAddress ?? '—'}</code> },
  { key: 'agent', header: 'Device', value: (a) => a.userAgent ?? '', render: (a) => <span className="small muted truncate" style={{ maxWidth: 360, display: 'inline-block' }} title={a.userAgent}>{a.userAgent ?? '—'}</span> },
];

export default function AccountSecurityPage() {
  const { user, reloadUser } = useAuth();
  const history = useQuery({ queryKey: ['my-login-history'], queryFn: authApi.myLoginHistory });
  const sessions = useQuery({ queryKey: ['my-sessions'], queryFn: authApi.sessions });

  const [setup, setSetup] = useState<MfaSetup | null>(null);
  const [qr, setQr] = useState<string>('');
  const [code, setCode] = useState('');
  const [recovery, setRecovery] = useState<string[] | null>(null);
  const [disabling, setDisabling] = useState(false);
  const [disableForm, setDisableForm] = useState({ password: '', code: '' });

  useEffect(() => {
    if (!setup) return;
    QRCode.toDataURL(setup.provisioningUri, { margin: 1, width: 200, errorCorrectionLevel: 'M' }).then(setQr).catch(() => setQr(''));
  }, [setup]);

  const begin = useApiMutation(() => authApi.mfaSetup(), { invalidate: [], onSuccess: (s) => { setSetup(s); setCode(''); } });
  const enable = useApiMutation((c: string) => authApi.mfaEnable(c), {
    invalidate: [['my-login-history']], success: 'Two-step verification is on',
    onSuccess: (r) => { setSetup(null); setRecovery(r.recoveryCodes); reloadUser(); },
  });
  const disable = useApiMutation(() => authApi.mfaDisable(disableForm.password, disableForm.code), {
    invalidate: [], success: 'Two-step verification is off',
    onSuccess: () => { setDisabling(false); setDisableForm({ password: '', code: '' }); reloadUser(); },
  });

  const copy = (text: string) => navigator.clipboard.writeText(text).then(() => toast.success('Copied'));
  const downloadCodes = () => {
    const blob = new Blob([`Security Manager recovery codes for ${user?.userName}\nEach code works once.\n\n${recovery?.join('\n')}\n`], { type: 'text/plain' });
    const a = Object.assign(document.createElement('a'), { href: URL.createObjectURL(blob), download: 'security-manager-recovery-codes.txt' });
    a.click();
    URL.revokeObjectURL(a.href);
  };

  const failures = (history.data ?? []).filter((a) => !a.succeeded && Date.now() - new Date(a.timestamp).getTime() < 7 * 86_400_000).length;

  return (
    <>
      <PageHeader title="Account security" description="Two-step verification, active sessions and your sign-in history." />

      <div className="grid grid-2">
        <Card>
          <CardHeader title="Two-step verification (MFA)" subtitle="Require a code from an authenticator app in addition to your password."
            actions={user?.mfaEnabled ? <Badge tone="success" dot>On</Badge> : <Badge tone="warning" dot>Off</Badge>} />
          <CardBody>
            {user?.mfaEnabled ? (
              <div className="stack">
                <p className="small">Signing in needs your password and a 6-digit code from your authenticator app. Keep your recovery codes somewhere safe: each works once if you lose your phone.</p>
                <div><Button icon={<ShieldOff size={15} />} onClick={() => setDisabling(true)}>Turn off</Button></div>
              </div>
            ) : (
              <div className="stack">
                <p className="small">Works with Microsoft Authenticator, Google Authenticator, Authy, 1Password and other TOTP apps.</p>
                <div><Button variant="primary" icon={<Smartphone size={15} />} loading={begin.isPending} onClick={() => begin.mutate(undefined)}>Set up authenticator</Button></div>
              </div>
            )}
          </CardBody>
        </Card>

        <Card>
          <CardHeader title="Sign-in activity" subtitle="Last 50 attempts on your account." />
          <CardBody>
            <div className="stack-sm">
              <div className="row"><span className="muted small" style={{ width: 160 }}>Last successful sign-in</span><b>{timeAgo(user?.lastLoginAt)}</b></div>
              <div className="row"><span className="muted small" style={{ width: 160 }}>Failed attempts (7 days)</span>
                {failures > 0 ? <Badge tone="danger">{failures}</Badge> : <Badge tone="success">0</Badge>}</div>
              <div className="row"><span className="muted small" style={{ width: 160 }}>Active sessions</span><b>{sessions.data?.length ?? '—'}</b></div>
              {failures > 0 && <p className="small muted">Unfamiliar failures? Change your password and turn on two-step verification.</p>}
            </div>
          </CardBody>
        </Card>
      </div>

      <Card className="mt">
        <CardHeader title="Sign-in history" />
        <DataTable columns={loginColumns(false)} rows={history.data} rowKey={(a) => a.id} loading={history.isLoading}
          initialSort={[{ key: 'time', dir: 'desc' }]} exportName="my-sign-ins" pivot={false} empty={{ title: 'No sign-ins recorded yet' }} />
      </Card>

      <Modal open={!!setup} onClose={() => setSetup(null)} title="Set up authenticator" onSubmit={() => enable.mutate(code)}
        footer={<><Button onClick={() => setSetup(null)}>Cancel</Button><Button type="submit" variant="primary" icon={<ShieldCheck size={15} />} loading={enable.isPending} disabled={code.replace(/\s/g, '').length !== 6}>Turn on</Button></>}>
        {setup && (
          <div className="stack">
            <ol className="small steps">
              <li>Scan this QR code with your authenticator app.</li>
              <li>Enter the 6-digit code the app shows.</li>
            </ol>
            <div className="mfa-qr">{qr ? <img src={qr} alt="QR code for your authenticator app" width={200} height={200} /> : <div className="skeleton" style={{ width: 200, height: 200 }} />}</div>
            <Field label="Can't scan? Enter this key manually">
              <div className="input-affix">
                <Input readOnly className="mono" value={setup.secret.replace(/(.{4})/g, '$1 ').trim()} />
                <button type="button" className="affix-btn" aria-label="Copy key" onClick={() => copy(setup.secret)}><Copy size={15} /></button>
              </div>
            </Field>
            <Field label="Verification code">
              <Input autoFocus required className="mono otp-input" inputMode="numeric" autoComplete="one-time-code" maxLength={7} placeholder="123456"
                value={code} onChange={(e) => setCode(e.target.value)} />
            </Field>
          </div>
        )}
      </Modal>

      <Modal open={!!recovery} onClose={() => setRecovery(null)} title="Save your recovery codes"
        footer={<><Button icon={<Download size={15} />} onClick={downloadCodes}>Download</Button><Button variant="primary" onClick={() => setRecovery(null)}>I saved them</Button></>}>
        <div className="stack">
          <div className="callout callout-warning">These codes are shown only once. Each one signs you in a single time if you lose access to your authenticator.</div>
          <div className="recovery-codes">{recovery?.map((c) => <code key={c}>{c}</code>)}</div>
          <div><Button size="sm" icon={<Copy size={14} />} onClick={() => copy(recovery?.join('\n') ?? '')}>Copy all</Button></div>
        </div>
      </Modal>

      <Modal open={disabling} onClose={() => setDisabling(false)} title="Turn off two-step verification" size="sm" onSubmit={() => disable.mutate(undefined)}
        footer={<><Button onClick={() => setDisabling(false)}>Cancel</Button><Button type="submit" variant="danger" loading={disable.isPending}>Turn off</Button></>}>
        <div className="stack">
          <p className="small muted">Your account will be protected by your password only.</p>
          <Field label="Password"><Input type="password" required autoComplete="current-password" value={disableForm.password} onChange={(e) => setDisableForm({ ...disableForm, password: e.target.value })} /></Field>
          <Field label="Authenticator or recovery code"><Input required className="mono" value={disableForm.code} onChange={(e) => setDisableForm({ ...disableForm, code: e.target.value })} /></Field>
        </div>
      </Modal>
    </>
  );
}

