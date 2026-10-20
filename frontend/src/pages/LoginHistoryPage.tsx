import { useQuery } from '@tanstack/react-query';
import { Globe, Lock, LogIn, UserX } from 'lucide-react';
import { authApi } from '../lib/api';
import { Card, PageHeader, StatCard } from '../components/ui/Card';
import { DataTable } from '../components/ui/DataTable';
import { loginColumns } from './AccountSecurityPage';

/** Sign-in audit log for every account (administrators). */
export default function LoginHistoryPage() {
  const history = useQuery({ queryKey: ['login-history'], queryFn: () => authApi.loginHistory(), refetchInterval: 30_000 });
  const rows = history.data ?? [];
  const since = Date.now() - 86_400_000;
  const recent = rows.filter((a) => new Date(a.timestamp).getTime() > since);
  const failed = recent.filter((a) => !a.succeeded);
  const sources = new Set(failed.map((a) => a.ipAddress).filter(Boolean));

  return (
    <>
      <PageHeader title="Sign-in audit" description="Every sign-in attempt to Security Manager. Failed attempts also feed the brute-force detection rules and raise alerts." />
      <div className="grid grid-kpi" style={{ marginBottom: 16 }}>
        <StatCard label="Sign-ins (24h)" icon={<LogIn size={20} />} tone="success" value={recent.length - failed.length} />
        <StatCard label="Failed attempts (24h)" icon={<UserX size={20} />} tone={failed.length ? 'danger' : 'primary'} value={failed.length} />
        <StatCard label="Failing source IPs (24h)" icon={<Globe size={20} />} tone="warning" value={sources.size} />
        <StatCard label="Attempts on locked accounts (24h)" icon={<Lock size={20} />} value={failed.filter((a) => a.failureReason === 'LockedOut').length} />
      </div>
      <Card>
        <DataTable columns={loginColumns(true)} rows={history.data} rowKey={(a) => a.id} loading={history.isLoading}
          initialSort={[{ key: 'time', dir: 'desc' }]} exportName="sign-in-audit" storageKey="sign-in-audit" empty={{ title: 'No sign-ins recorded yet' }} />
      </Card>
    </>
  );
}
