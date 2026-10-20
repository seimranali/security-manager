import axios, { AxiosError, type InternalAxiosRequestConfig } from 'axios';
import type * as T from './types';
import { isExpiringSoon, refreshSession, tokenStore } from './auth';

/** Rows loaded per grid; the grid then sorts/filters/groups/pivots client-side and pages 20 at a time. */
export const GRID_LIMIT = 1000;

export const http = axios.create({ baseURL: '/api', timeout: 20000, withCredentials: true });

let onSessionExpired: () => void = () => {};
/** Registered by the AuthProvider; called when the session can no longer be refreshed. */
export const setSessionExpiredHandler = (handler: () => void) => {
  onSessionExpired = handler;
};

const isAuthCall = (url?: string) => !!url && /^\/?auth\/(login|refresh|logout)/.test(url);

// Attach the bearer token, refreshing proactively if it is about to expire.
http.interceptors.request.use(async (config) => {
  if (!isAuthCall(config.url) && tokenStore.get() && isExpiringSoon()) {
    try {
      await refreshSession();
    } catch {
      /* the response interceptor handles the resulting 401 */
    }
  }
  const token = tokenStore.get();
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

// On 401, refresh once and replay the request; if refresh fails the session is over.
http.interceptors.response.use(undefined, async (error: AxiosError) => {
  const config = error.config as (InternalAxiosRequestConfig & { _retried?: boolean }) | undefined;
  if (error.response?.status !== 401 || !config || config._retried || isAuthCall(config.url)) throw error;

  config._retried = true;
  try {
    await refreshSession();
  } catch (refreshError) {
    tokenStore.clear();
    onSessionExpired();
    throw refreshError;
  }
  config.headers.Authorization = `Bearer ${tokenStore.get()}`;
  return http(config);
});

/** Extracts a human-readable message from RFC 7807 / validation problem responses. */
export function errorMessage(error: unknown): string {
  if (error instanceof AxiosError) {
    if (error.response?.status === 403) return (error.response.data as { detail?: string })?.detail ?? 'You do not have permission to do that.';
    if (error.response?.status === 429) return 'Too many attempts. Please wait a minute and try again.';
    const data = error.response?.data as
      | { detail?: string; title?: string; errors?: Record<string, string[]> }
      | undefined;
    if (data?.errors) return Object.values(data.errors).flat().join(' ');
    if (data?.detail) return data.detail;
    if (data?.title) return data.title;
    if (!error.response) return 'Cannot reach the API gateway. Is the backend running?';
    return `Request failed (${error.response.status})`;
  }
  return error instanceof Error ? error.message : 'Unexpected error';
}

type Query = Record<string, string | number | boolean | undefined | null>;
const clean = (q: Query) => Object.fromEntries(Object.entries(q).filter(([, v]) => v !== '' && v != null));
const get = <R>(url: string, params?: Query) => http.get<R>(url, { params: params && clean(params) }).then((r) => r.data);
const post = <R>(url: string, body?: unknown) => http.post<R>(url, body).then((r) => r.data);
const put = <R>(url: string, body?: unknown) => http.put<R>(url, body).then((r) => r.data);
const patch = <R>(url: string, params?: Query) => http.patch<R>(url, null, { params }).then((r) => r.data);
const del = (url: string) => http.delete(url).then(() => undefined);

export const firewallApi = {
  policies: () => get<T.Policy[]>('/firewall/policies'),
  createPolicy: (p: T.PolicyInput) => post<T.Policy>('/firewall/policies', p),
  updatePolicy: (id: string, p: T.PolicyInput) => put<T.Policy>(`/firewall/policies/${id}`, p),
  setPolicyEnabled: (id: string, value: boolean) => patch<T.Policy>(`/firewall/policies/${id}/enabled`, { value }),
  deletePolicy: (id: string) => del(`/firewall/policies/${id}`),

  rules: (q: Query) => get<T.Paged<T.FirewallRule>>('/firewall/rules', q),
  createRule: (r: T.RuleInput) => post<T.FirewallRule>('/firewall/rules', r),
  updateRule: (id: string, r: T.RuleInput) => put<T.FirewallRule>(`/firewall/rules/${id}`, r),
  setRuleEnabled: (id: string, value: boolean) => patch<T.FirewallRule>(`/firewall/rules/${id}/enabled`, { value }),
  deleteRule: (id: string) => del(`/firewall/rules/${id}`),

  ipList: (q: Query) => get<T.Paged<T.IpListEntry>>('/firewall/ip-lists', q),
  createIpEntry: (e: T.IpListEntryInput) => post<T.IpListEntry>('/firewall/ip-lists', e),
  updateIpEntry: (id: string, e: T.IpListEntryInput) => put<T.IpListEntry>(`/firewall/ip-lists/${id}`, e),
  setIpEntryEnabled: (id: string, value: boolean) => patch<T.IpListEntry>(`/firewall/ip-lists/${id}/enabled`, { value }),
  deleteIpEntry: (id: string) => del(`/firewall/ip-lists/${id}`),
  deleteIpEntries: (ids: string[]) => post<{ deleted: number }>('/firewall/ip-lists/bulk-delete', { ids }),
  importIpList: (body: { listType: T.IpListType; addresses: string; description?: string; expiresAt?: string | null }) =>
    post<T.IpImportResult>('/firewall/ip-lists/import', body),
  blockIp: (address: string, reason: string, durationMinutes: number, sourceReference?: string) =>
    post<T.IpListEntry>('/firewall/ip-lists/auto-block', { address, reason, durationMinutes, sourceReference }),
  checkIp: (ip: string) => get<T.IpCheckResult>('/firewall/ip-lists/check', { ip }),

  enforcement: () => get<T.EnforcementOverview>('/firewall/enforcement'),
  updateEnforcementSettings: (s: Omit<T.EnforcementSettings, 'updatedAt'>) => put<T.EnforcementSettings>('/firewall/enforcement/settings', s),
  deleteAgent: (id: string) => del(`/firewall/enforcement/agents/${id}`),

  evaluate: (input: T.EvaluateInput) => post<T.EvaluationResult>('/firewall/evaluate', input),
  traffic: (q: Query) => get<T.Paged<T.TrafficLog>>('/firewall/traffic', q),
  stats: () => get<T.FirewallStats>('/firewall/stats'),
};

export const securityApi = {
  events: (q: Query) => get<T.Paged<T.SecurityEvent>>('/security/events', q),
  lookup: (field: string, q: Query, signal?: AbortSignal) =>
    http.get<T.Paged<{ value: string; count: number }>>(`/security/events/lookup/${field}`, { params: clean(q), signal }).then((r) => r.data),
  ingest: (e: T.EventInput) => post<{ event: T.SecurityEvent; newDetections: T.Detection[] }>('/security/events', e),

  detections: (q: Query) => get<T.Paged<T.Detection>>('/security/detections', q),
  detection: (id: string) => get<{ detection: T.Detection; evidence: T.SecurityEvent[] }>(`/security/detections/${id}`),
  updateDetection: (id: string, body: { status: T.DetectionStatus; notes?: string; assignedTo?: string }) =>
    put<T.Detection>(`/security/detections/${id}/status`, body),

  rules: () => get<T.DetectionRule[]>('/security/rules'),
  createRule: (r: T.DetectionRuleInput) => post<T.DetectionRule>('/security/rules', r),
  updateRule: (id: string, r: T.DetectionRuleInput) => put<T.DetectionRule>(`/security/rules/${id}`, r),
  setRuleEnabled: (id: string, value: boolean) => patch<T.DetectionRule>(`/security/rules/${id}/enabled`, { value }),
  deleteRule: (id: string) => del(`/security/rules/${id}`),

  stats: () => get<T.SecurityStats>('/security/stats'),
};

export const scannerApi = {
  targets: () => get<T.ScanTarget[]>('/scanner/targets'),
  createTarget: (t: T.TargetInput) => post<T.ScanTarget>('/scanner/targets', t),
  updateTarget: (id: string, t: T.TargetInput) => put<T.ScanTarget>(`/scanner/targets/${id}`, t),
  deleteTarget: (id: string) => del(`/scanner/targets/${id}`),

  jobs: (q: Query) => get<T.Paged<T.ScanJob>>('/scanner/jobs', q),
  job: (id: string) => get<{ job: T.ScanJob; findings: T.Finding[] }>(`/scanner/jobs/${id}`),
  startScan: (targetId: string, scanTypes: string) => post<T.ScanJob>('/scanner/jobs', { targetId, scanTypes }),
  cancelScan: (id: string) => post<T.ScanJob>(`/scanner/jobs/${id}/cancel`),

  schedules: () => get<T.ScanSchedule[]>('/scanner/schedules'),
  createSchedule: (s: T.ScheduleInput) => post<T.ScanSchedule>('/scanner/schedules', s),
  updateSchedule: (id: string, s: T.ScheduleInput) => put<T.ScanSchedule>(`/scanner/schedules/${id}`, s),
  setScheduleEnabled: (id: string, value: boolean) => patch<T.ScanSchedule>(`/scanner/schedules/${id}/enabled`, { value }),
  deleteSchedule: (id: string) => del(`/scanner/schedules/${id}`),

  findings: (q: Query) => get<T.Paged<T.Finding>>('/scanner/findings', q),
  updateFinding: (id: string, status: T.FindingStatus) => put<T.Finding>(`/scanner/findings/${id}/status`, { status }),
  stats: () => get<T.ScannerStats>('/scanner/stats'),
};

export const alertApi = {
  list: (q: Query) => get<T.Paged<T.Alert>>('/alerts', q),
  stats: () => get<T.AlertStats>('/alerts/stats'),
  create: (a: { title: string; message: string; severity: T.Severity; source: string; category?: string }) =>
    post<T.Alert>('/alerts', a),
  acknowledge: (id: string) => post<T.Alert>(`/alerts/${id}/acknowledge`, {}),
  resolve: (id: string, notes?: string) => post<T.Alert>(`/alerts/${id}/resolve`, { notes }),
  reopen: (id: string) => post<T.Alert>(`/alerts/${id}/reopen`),
  bulkAcknowledge: (ids: string[]) => post<{ updated: number }>('/alerts/bulk/acknowledge', { ids }),
  bulkResolve: (ids: string[]) => post<{ updated: number }>('/alerts/bulk/resolve', { ids }),
  remove: (id: string) => del(`/alerts/${id}`),
};

/** CRUD for configuration items nested under a gateway (interfaces, routes, policies, nat, vpn). */
const gatewayChild = <TItem, TInput>(kind: string) => ({
  create: (gatewayId: string, body: TInput) => post<TItem>(`/network/gateways/${gatewayId}/${kind}`, body),
  update: (gatewayId: string, id: string, body: TInput) => put<TItem>(`/network/gateways/${gatewayId}/${kind}/${id}`, body),
  remove: (gatewayId: string, id: string) => del(`/network/gateways/${gatewayId}/${kind}/${id}`),
});

export const networkApi = {
  gateways: () => get<T.NetGateway[]>('/network/gateways'),
  gateway: (id: string) => get<T.GatewayDetail>(`/network/gateways/${id}`),
  createGateway: (g: T.GatewayInput) => post<T.NetGateway>('/network/gateways', g),
  updateGateway: (id: string, g: T.GatewayInput) => put<T.NetGateway>(`/network/gateways/${id}`, g),
  deleteGateway: (id: string) => del(`/network/gateways/${id}`),
  config: (id: string) => get<T.GeneratedConfig>(`/network/gateways/${id}/config`),
  /** Downloads the zip through the authenticated client and saves it. */
  downloadBundle: async (id: string) => {
    const response = await http.get<Blob>(`/network/gateways/${id}/config/bundle`, { responseType: 'blob' });
    const name = /filename=([^;]+)/.exec(String(response.headers['content-disposition'] ?? ''))?.[1]?.replace(/"/g, '') ?? 'gateway-config.zip';
    const url = URL.createObjectURL(response.data);
    const a = Object.assign(document.createElement('a'), { href: url, download: name });
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  },
  markDeployed: (id: string) => post<T.NetGateway>(`/network/gateways/${id}/mark-deployed`),

  interfaces: gatewayChild<T.NetInterface, Omit<T.NetInterface, 'id'>>('interfaces'),
  routes: gatewayChild<T.NetRoute, Omit<T.NetRoute, 'id' | 'isDefault'>>('routes'),
  policies: gatewayChild<T.RoutingPolicyItem, Omit<T.RoutingPolicyItem, 'id'>>('policies'),
  nat: gatewayChild<T.NatRuleItem, Omit<T.NatRuleItem, 'id'>>('nat'),
  vpn: gatewayChild<T.VpnTunnelItem, Omit<T.VpnTunnelItem, 'id' | 'peers'>>('vpn'),
  createPeer: (gatewayId: string, tunnelId: string, p: Omit<T.VpnPeerItem, 'id' | 'tunnelId'>) =>
    post<T.VpnPeerItem>(`/network/gateways/${gatewayId}/vpn/${tunnelId}/peers`, p),
  updatePeer: (gatewayId: string, tunnelId: string, id: string, p: Omit<T.VpnPeerItem, 'id' | 'tunnelId'>) =>
    put<T.VpnPeerItem>(`/network/gateways/${gatewayId}/vpn/${tunnelId}/peers/${id}`, p),
  deletePeer: (gatewayId: string, tunnelId: string, id: string) => del(`/network/gateways/${gatewayId}/vpn/${tunnelId}/peers/${id}`),

  clusters: () => get<T.HaClusterItem[]>('/network/clusters'),
  createCluster: (c: Omit<T.HaClusterItem, 'id' | 'members' | 'virtualIps' | 'createdAt'>) => post<T.HaClusterItem>('/network/clusters', c),
  updateCluster: (id: string, c: Omit<T.HaClusterItem, 'id' | 'members' | 'virtualIps' | 'createdAt'>) => put<T.HaClusterItem>(`/network/clusters/${id}`, c),
  deleteCluster: (id: string) => del(`/network/clusters/${id}`),
  addVip: (clusterId: string, v: Omit<T.VirtualIpItem, 'id' | 'clusterId'>) => post<T.VirtualIpItem>(`/network/clusters/${clusterId}/virtual-ips`, v),
  updateVip: (clusterId: string, id: string, v: Omit<T.VirtualIpItem, 'id' | 'clusterId'>) => put<T.VirtualIpItem>(`/network/clusters/${clusterId}/virtual-ips/${id}`, v),
  deleteVip: (clusterId: string, id: string) => del(`/network/clusters/${clusterId}/virtual-ips/${id}`),
};

export const authApi = {
  me: () => get<T.AuthUser>('/auth/me'),
  changePassword: (currentPassword: string, newPassword: string) =>
    post<T.AuthResponse>('/auth/change-password', { currentPassword, newPassword }),
  sessions: () => get<T.Session[]>('/auth/sessions'),
  users: () => get<T.AuthUser[]>('/auth/users'),
  createUser: (u: T.CreateUserInput) => post<T.AuthUser>('/auth/users', u),
  updateUser: (id: string, u: T.UpdateUserInput) => put<T.AuthUser>(`/auth/users/${id}`, u),
  deleteUser: (id: string) => del(`/auth/users/${id}`),
  revokeSessions: (id: string) => post<{ revoked: number }>(`/auth/users/${id}/revoke-sessions`),
  resetMfa: (id: string) => post<void>(`/auth/users/${id}/reset-mfa`),
  myLoginHistory: () => get<T.LoginAttempt[]>('/auth/login-history', { take: 50 }),
  loginHistory: (userId?: string) => get<T.LoginAttempt[]>('/auth/users/login-history', { userId, take: 500 }),
  mfaSetup: () => post<T.MfaSetup>('/auth/mfa/setup'),
  mfaEnable: (code: string) => post<{ recoveryCodes: string[] }>('/auth/mfa/enable', { code }),
  mfaDisable: (password: string, code: string) => post<void>('/auth/mfa/disable', { password, code }),
};

export const notificationApi = {
  channels: () => get<T.NotificationChannel[]>('/alerts/channels'),
  create: (c: T.NotificationChannelInput) => post<T.NotificationChannel>('/alerts/channels', c),
  update: (id: string, c: T.NotificationChannelInput) => put<T.NotificationChannel>(`/alerts/channels/${id}`, c),
  remove: (id: string) => del(`/alerts/channels/${id}`),
  test: (id: string) => post<T.NotificationChannel>(`/alerts/channels/${id}/test`),
};

export const feedApi = {
  feeds: () => get<T.ThreatFeed[]>('/firewall/feeds'),
  create: (f: T.ThreatFeedInput) => post<T.ThreatFeed>('/firewall/feeds', f),
  update: (id: string, f: T.ThreatFeedInput) => put<T.ThreatFeed>(`/firewall/feeds/${id}`, f),
  remove: (id: string) => del(`/firewall/feeds/${id}`),
  refresh: (id: string) => post<T.FeedRefreshResult>(`/firewall/feeds/${id}/refresh`),
};

export const backupApi = {
  /** Downloads the firewall configuration as JSON through the authenticated client. */
  exportConfig: async () => {
    const response = await http.get<Blob>('/firewall/config/export', { responseType: 'blob' });
    const name = /filename="?([^";]+)/.exec(String(response.headers['content-disposition'] ?? ''))?.[1] ?? 'firewall-config.json';
    const url = URL.createObjectURL(response.data);
    const a = Object.assign(document.createElement('a'), { href: url, download: name });
    document.body.appendChild(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  },
  importConfig: (backup: unknown, replace: boolean) =>
    http.post<T.ConfigRestoreResult>('/firewall/config/import', backup, { params: { replace } }).then((r) => r.data),
};

export const systemApi = {
  health: () => get<T.ServiceHealth[]>('/system/health'),
};
