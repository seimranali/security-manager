import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { authApi, setSessionExpiredHandler } from '../lib/api';
import { authRequests, broadcastLogout, onLogoutBroadcast, refreshSession, tokenStore } from '../lib/auth';
import type { AuthResponse, AuthUser, UserRole } from '../lib/types';

type Status = 'loading' | 'authenticated' | 'anonymous';

interface AuthContextValue {
  status: Status;
  user: AuthUser | null;
  /** Resolves 'mfa' when the password was accepted but an authenticator code is still needed. */
  login: (userName: string, password: string, code?: string) => Promise<'ok' | 'mfa'>;
  /** Re-reads the signed-in user (e.g. after enabling MFA). */
  reloadUser: () => Promise<void>;
  logout: () => Promise<void>;
  changePassword: (current: string, next: string) => Promise<void>;
  hasRole: (...roles: UserRole[]) => boolean;
  canWrite: boolean;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [status, setStatus] = useState<Status>('loading');
  const [user, setUser] = useState<AuthUser | null>(null);
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);

  const endSession = useCallback(() => {
    clearTimeout(timer.current);
    tokenStore.clear();
    setUser(null);
    setStatus('anonymous');
    queryClient.clear();
  }, [queryClient]);

  const applyRef = useRef<(response: AuthResponse) => void>(() => {});

  /** Keeps the session alive by refreshing ~1 minute before the access token expires. */
  const schedule = useCallback((response: AuthResponse) => {
    clearTimeout(timer.current);
    const delay = Math.max(5_000, new Date(response.expiresAt).getTime() - Date.now() - 60_000);
    timer.current = setTimeout(() => {
      refreshSession().then((r) => applyRef.current(r)).catch(() => {
        endSession();
        toast.info('Your session has expired. Please sign in again.');
      });
    }, delay);
  }, [endSession]);

  const apply = useCallback((response: AuthResponse) => {
    tokenStore.set(response);
    setUser(response.user);
    setStatus('authenticated');
    schedule(response);
  }, [schedule]);
  applyRef.current = apply;

  // Restore the session from the refresh cookie on first load.
  useEffect(() => {
    refreshSession().then(apply).catch(() => setStatus('anonymous'));
    setSessionExpiredHandler(() => {
      endSession();
      toast.info('Your session has expired. Please sign in again.');
    });
    const unsubscribe = onLogoutBroadcast(endSession);
    return () => {
      unsubscribe();
      clearTimeout(timer.current);
    };
  }, [apply, endSession]);

  const login = useCallback(async (userName: string, password: string, code?: string) => {
    const response = await authRequests.login(userName, password, code);
    if (response.mfaRequired) return 'mfa' as const;
    apply(response);
    return 'ok' as const;
  }, [apply]);

  const reloadUser = useCallback(async () => setUser(await authApi.me()), []);

  const logout = useCallback(async () => {
    await authRequests.logout();
    broadcastLogout();
    endSession();
  }, [endSession]);

  const changePassword = useCallback(async (current: string, next: string) => {
    apply(await authApi.changePassword(current, next));
  }, [apply]);

  const value = useMemo<AuthContextValue>(() => ({
    status,
    user,
    login,
    reloadUser,
    logout,
    changePassword,
    hasRole: (...roles) => !!user && roles.includes(user.role),
    canWrite: !!user && user.role !== 'Viewer',
  }), [status, user, login, reloadUser, logout, changePassword]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}
