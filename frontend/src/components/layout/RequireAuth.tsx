import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { ShieldAlert } from 'lucide-react';
import { useAuth } from '../../hooks/useAuth';
import type { UserRole } from '../../lib/types';
import { EmptyState } from '../ui/DataTable';

/** Gate for authenticated routes; optionally restricted to roles. */
export function RequireAuth({ children, roles }: { children: ReactNode; roles?: UserRole[] }) {
  const { status, hasRole } = useAuth();
  const location = useLocation();

  if (status === 'loading') {
    return <div className="splash"><div className="spinner" role="status" aria-label="Restoring session" /></div>;
  }
  if (status === 'anonymous') {
    return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
  }
  if (roles && !hasRole(...roles)) {
    return <EmptyState icon={<ShieldAlert size={22} />} title="Access denied" description="Your role does not have access to this page." />;
  }
  return <>{children}</>;
}
