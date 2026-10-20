import { useState, type FormEvent } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { ArrowLeft, Eye, EyeOff, KeyRound, LogIn, ShieldCheck } from 'lucide-react';
import { useAuth } from '../hooks/useAuth';
import { errorMessage } from '../lib/api';
import { Button } from '../components/ui/Button';
import { Field, Input } from '../components/ui/Form';

export default function LoginPage() {
  const { status, login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const from = (location.state as { from?: string } | null)?.from ?? '/';

  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const [step, setStep] = useState<'password' | 'code'>('password');
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (status === 'authenticated') return <Navigate to={from} replace />;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = await login(userName, password, step === 'code' ? code : undefined);
      if (result === 'mfa') {
        setStep('code');
        return;
      }
      navigate(from, { replace: true });
    } catch (err) {
      setError(errorMessage(err));
      if (step === 'code') setCode('');
    } finally {
      setBusy(false);
    }
  };

  const back = () => {
    setStep('password');
    setCode('');
    setError(null);
  };

  return (
    <div className="auth-page">
      <div className="auth-card card">
        <div className="auth-brand">
          <div className="brand-logo" style={{ width: 44, height: 44, borderRadius: 12 }}><ShieldCheck size={24} /></div>
          <div>
            <h1 className="page-title" style={{ fontSize: 20 }}>Security Manager</h1>
            <p className="muted">{step === 'password' ? 'Sign in to your security console' : 'Two-step verification'}</p>
          </div>
        </div>

        <form className="stack" onSubmit={submit} noValidate>
          {error && <div className="callout callout-danger" role="alert">{error}</div>}
          {step === 'password' ? (
            <>
              <Field label="User name">
                <Input autoFocus required autoComplete="username" value={userName} onChange={(e) => setUserName(e.target.value)} />
              </Field>
              <Field label="Password">
                <div className="input-affix">
                  <Input required type={showPassword ? 'text' : 'password'} autoComplete="current-password" value={password} onChange={(e) => setPassword(e.target.value)} />
                  <button type="button" className="affix-btn" aria-label={showPassword ? 'Hide password' : 'Show password'} onClick={() => setShowPassword((s) => !s)}>
                    {showPassword ? <EyeOff size={16} /> : <Eye size={16} />}
                  </button>
                </div>
              </Field>
              <Button type="submit" variant="primary" icon={<LogIn size={16} />} loading={busy} disabled={!userName || !password} style={{ width: '100%', height: 40 }}>
                Sign in
              </Button>
            </>
          ) : (
            <>
              <p className="small muted">Open your authenticator app and enter the 6-digit code for <b>{userName}</b>. Lost your phone? Enter one of your recovery codes instead.</p>
              <Field label="Verification code">
                <Input autoFocus required className="mono otp-input" inputMode="text" autoComplete="one-time-code" maxLength={11}
                  placeholder="123 456" value={code} onChange={(e) => setCode(e.target.value)} />
              </Field>
              <Button type="submit" variant="primary" icon={<KeyRound size={16} />} loading={busy} disabled={code.replace(/\s/g, '').length < 6} style={{ width: '100%', height: 40 }}>
                Verify
              </Button>
              <Button type="button" variant="ghost" icon={<ArrowLeft size={16} />} onClick={back}>Use a different account</Button>
            </>
          )}
        </form>
      </div>
      <p className="small muted" style={{ textAlign: 'center' }}>Sessions use short-lived JWTs with rotating refresh tokens.</p>
    </div>
  );
}
