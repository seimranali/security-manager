export type Severity = 'Info' | 'Low' | 'Medium' | 'High' | 'Critical';
export const SEVERITIES: Severity[] = ['Critical', 'High', 'Medium', 'Low', 'Info'];

export interface Paged<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

// ---------- Firewall ----------
export type RuleAction = 'Allow' | 'Deny';
export type NetworkProtocol = 'Any' | 'Tcp' | 'Udp' | 'Icmp';
export type TrafficDirection = 'Inbound' | 'Outbound' | 'Any';

export interface Policy {
  id: string;
  name: string;
  description?: string;
  isEnabled: boolean;
  priority: number;
  direction: TrafficDirection;
  defaultAction: RuleAction;
  enforceOnHosts: boolean;
  ruleCount: number;
  activeRuleCount: number;
  createdAt: string;
  updatedAt?: string;
}
export type PolicyInput = Pick<Policy, 'name' | 'description' | 'isEnabled' | 'priority' | 'direction' | 'defaultAction' | 'enforceOnHosts'>;

// ---------- Host enforcement ----------
export interface EnforcedRule {
  key: string;
  displayName: string;
  description: string;
  direction: 'In' | 'Out';
  action: 'Allow' | 'Block';
  protocol: string;
  localAddresses: string;
  remoteAddresses: string;
  localPorts: string;
  remotePorts: string;
  origin: string;
}
export interface EnforcementSettings {
  enabled: boolean;
  enforceBlocklist: boolean;
  enforceAllowlist: boolean;
  updatedAt?: string;
}
export interface EnforcementAgent {
  id: string;
  agentId: string;
  hostname: string;
  operatingSystem: string;
  agentVersion: string;
  mode: 'Enforce' | 'Audit';
  status: 'InSync' | 'Pending' | 'Error' | 'Disabled';
  isOnline: boolean;
  inSync: boolean;
  appliedVersion?: string;
  managedRuleCount: number;
  lastCreated: number;
  lastUpdated: number;
  lastRemoved: number;
  intervalSeconds: number;
  lastSeenAt: string;
  lastChangeAt?: string;
  profiles: { name: string; enabled: boolean }[];
  errors: string[];
  warnings: string[];
}
export interface EnforcementOverview {
  settings: EnforcementSettings;
  desiredState: { version: string; enabled: boolean; rules: EnforcedRule[]; warnings: string[]; generatedAt: string };
  agents: EnforcementAgent[];
  enforcedPolicies: string[];
}

export interface FirewallRule {
  id: string;
  policyId: string;
  policyName: string;
  name: string;
  description?: string;
  priority: number;
  action: RuleAction;
  direction: TrafficDirection;
  protocol: NetworkProtocol;
  sourceAddress: string;
  destinationAddress: string;
  sourcePort: string;
  destinationPort: string;
  isEnabled: boolean;
  hitCount: number;
  lastHitAt?: string;
  createdAt: string;
}
export type RuleInput = Omit<FirewallRule, 'id' | 'policyName' | 'hitCount' | 'lastHitAt' | 'createdAt'>;

export interface EvaluateInput {
  sourceIp: string;
  destinationIp: string;
  sourcePort?: number | null;
  destinationPort?: number | null;
  protocol: NetworkProtocol;
  direction: TrafficDirection;
}
export interface EvaluationResult {
  action: RuleAction;
  matchedRuleId?: string;
  matchedRuleName?: string;
  policyName?: string;
  reason: string;
  evaluatedAt: string;
}
export interface TrafficLog {
  id: string;
  timestamp: string;
  sourceIp: string;
  destinationIp: string;
  sourcePort?: number;
  destinationPort?: number;
  protocol: NetworkProtocol;
  direction: TrafficDirection;
  action: RuleAction;
  matchedRuleName?: string;
  policyName?: string;
  reason: string;
}
export interface FirewallStats {
  totalPolicies: number;
  activePolicies: number;
  totalRules: number;
  activeRules: number;
  allowed24h: number;
  denied24h: number;
  topRules: { id: string; name: string; action: RuleAction; hitCount: number }[];
  timeline: { hour: string; allowed: number; denied: number }[];
}

export type IpListType = 'Allow' | 'Block';
export type IpListSource = 'Manual' | 'Import' | 'ThreatIntel' | 'Detection' | 'Country';

export interface IpListEntry {
  id: string;
  listType: IpListType;
  address: string;
  description?: string;
  source: IpListSource;
  sourceReference?: string;
  expiresAt?: string;
  isEnabled: boolean;
  isActive: boolean;
  isExpired: boolean;
  hitCount: number;
  lastHitAt?: string;
  createdBy?: string;
  createdAt: string;
  updatedAt?: string;
}
export interface IpListEntryInput {
  listType: IpListType;
  address: string;
  description?: string;
  expiresAt?: string | null;
  isEnabled: boolean;
}
export interface IpImportResult {
  added: number;
  skipped: number;
  invalid: string[];
}
export interface IpCheckResult {
  ip: string;
  verdict: 'Allowed' | 'Blocked' | 'NotListed';
  decidingEntry?: IpListEntry;
  matches: IpListEntry[];
}

// ---------- Network ----------
export type InterfaceType = 'Physical' | 'Vlan' | 'Bridge' | 'Bond' | 'Loopback';
export type InterfaceZone = 'Wan' | 'Lan' | 'Dmz' | 'Management' | 'Guest' | 'Other';
export type AddressingMode = 'Static' | 'Dhcp' | 'None';
export type NatType = 'Masquerade' | 'SourceNat' | 'PortForward' | 'OneToOne';
export type NatProtocol = 'Any' | 'Tcp' | 'Udp';
export type VpnType = 'WireGuard' | 'IpsecSiteToSite';
export type IssueSeverity = 'Error' | 'Warning' | 'Info';

export interface NetGateway {
  id: string;
  name: string;
  hostname: string;
  description?: string;
  managementAddress?: string;
  clusterId?: string;
  clusterName?: string;
  haPriority: number;
  haRole?: 'Master' | 'Backup';
  interfaceCount: number;
  routeCount: number;
  natRuleCount: number;
  tunnelCount: number;
  errorCount: number;
  warningCount: number;
  configRevision: number;
  deployedRevision?: number;
  deployedAt?: string;
  deployedBy?: string;
  pendingChanges: boolean;
  createdAt: string;
  updatedAt?: string;
}
export interface GatewayInput {
  name: string;
  hostname: string;
  description?: string;
  managementAddress?: string;
  clusterId?: string | null;
  haPriority: number;
}
export interface NetInterface {
  id: string;
  name: string;
  type: InterfaceType;
  zone: InterfaceZone;
  mode: AddressingMode;
  address?: string;
  mtu?: number;
  vlanId?: number;
  parent?: string;
  members?: string;
  isEnabled: boolean;
  description?: string;
}
export interface NetRoute {
  id: string;
  destination: string;
  nextHop?: string;
  interface?: string;
  metric: number;
  table: number;
  isDefault: boolean;
  isEnabled: boolean;
  description?: string;
}
export interface RoutingPolicyItem {
  id: string;
  priority: number;
  source?: string;
  destination?: string;
  inInterface?: string;
  table: number;
  isEnabled: boolean;
  description?: string;
}
export interface NatRuleItem {
  id: string;
  name: string;
  type: NatType;
  protocol: NatProtocol;
  inInterface?: string;
  outInterface?: string;
  source?: string;
  destination?: string;
  destinationPort?: string;
  translatedAddress?: string;
  translatedPort?: number;
  priority: number;
  isEnabled: boolean;
  description?: string;
}
export interface VpnPeerItem {
  id: string;
  tunnelId: string;
  name: string;
  publicKey: string;
  endpoint?: string;
  allowedIps: string;
  persistentKeepalive: number;
  isEnabled: boolean;
}
export interface VpnTunnelItem {
  id: string;
  name: string;
  type: VpnType;
  interfaceName?: string;
  listenPort?: number;
  tunnelAddress?: string;
  publicKey?: string;
  mtu?: number;
  remoteGateway?: string;
  localSubnets?: string;
  remoteSubnets?: string;
  ikeVersion: number;
  proposals?: string;
  isEnabled: boolean;
  description?: string;
  peers: VpnPeerItem[];
}
export interface ConfigIssue {
  severity: IssueSeverity;
  area: string;
  message: string;
  itemId?: string;
}
export interface GatewayDetail {
  gateway: NetGateway;
  interfaces: NetInterface[];
  routes: NetRoute[];
  policies: RoutingPolicyItem[];
  natRules: NatRuleItem[];
  tunnels: VpnTunnelItem[];
  issues: ConfigIssue[];
}
export interface GeneratedFile {
  path: string;
  language: string;
  description: string;
  content: string;
}
export interface GeneratedConfig {
  gatewayId: string;
  hostname: string;
  revision: number;
  files: GeneratedFile[];
  issues: ConfigIssue[];
}
export interface ClusterMember {
  gatewayId: string;
  name: string;
  hostname: string;
  priority: number;
  role: 'Master' | 'Backup';
  pendingChanges: boolean;
}
export interface VirtualIpItem {
  id: string;
  clusterId: string;
  interface: string;
  address: string;
  description?: string;
}
export interface HaClusterItem {
  id: string;
  name: string;
  description?: string;
  vrrpRouterId: number;
  advertIntervalSeconds: number;
  preempt: boolean;
  members: ClusterMember[];
  virtualIps: VirtualIpItem[];
  createdAt: string;
}

// ---------- Security ----------
export type SecurityEventType =
  | 'AuthenticationFailure' | 'AuthenticationSuccess' | 'FirewallBlock' | 'MalwareDetected' | 'PortScan'
  | 'PrivilegeEscalation' | 'DataExfiltration' | 'ConfigurationChange' | 'SuspiciousProcess' | 'Other';
export const EVENT_TYPES: SecurityEventType[] = [
  'AuthenticationFailure', 'AuthenticationSuccess', 'FirewallBlock', 'MalwareDetected', 'PortScan',
  'PrivilegeEscalation', 'DataExfiltration', 'ConfigurationChange', 'SuspiciousProcess', 'Other',
];
export type DetectionStatus = 'New' | 'Investigating' | 'Resolved' | 'FalsePositive';
export type CorrelationField = 'None' | 'SourceIp' | 'UserName' | 'TargetHost';

export interface SecurityEvent {
  id: string;
  timestamp: string;
  eventType: SecurityEventType;
  severity: Severity;
  source: string;
  message: string;
  sourceIp?: string;
  targetHost?: string;
  userName?: string;
  rawData?: string;
  isProcessed: boolean;
}
export interface EventInput {
  eventType: SecurityEventType;
  severity: Severity;
  source: string;
  message: string;
  sourceIp?: string;
  targetHost?: string;
  userName?: string;
}
export interface Detection {
  id: string;
  ruleId: string;
  ruleName: string;
  title: string;
  description: string;
  severity: Severity;
  status: DetectionStatus;
  eventType: SecurityEventType;
  correlateBy: CorrelationField;
  correlationKey: string;
  eventCount: number;
  firstSeen: string;
  lastSeen: string;
  mitreTechnique?: string;
  notes?: string;
  assignedTo?: string;
  resolvedAt?: string;
  createdAt: string;
}
export interface DetectionRule {
  id: string;
  name: string;
  description?: string;
  eventType: SecurityEventType;
  minEventSeverity: Severity;
  threshold: number;
  timeWindowMinutes: number;
  correlateBy: CorrelationField;
  severity: Severity;
  mitreTechnique?: string;
  isEnabled: boolean;
  triggerCount: number;
  lastTriggeredAt?: string;
  createdAt: string;
}
export type DetectionRuleInput = Omit<DetectionRule, 'id' | 'triggerCount' | 'lastTriggeredAt' | 'createdAt'>;
export interface CountItem { name: string; count: number }
export interface SecurityStats {
  events24h: number;
  criticalEvents24h: number;
  openDetections: number;
  criticalOpenDetections: number;
  activeRules: number;
  eventsBySeverity: CountItem[];
  eventsByType: CountItem[];
  detectionsByStatus: CountItem[];
  topSources: CountItem[];
  timeline: { hour: string; events: number; detections: number }[];
}

// ---------- Scanner ----------
export type ScanStatus = 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';
export type ScanTypeFlag = 'PortScan' | 'HttpHeaders' | 'SslCertificate';
export const SCAN_TYPES: { value: ScanTypeFlag; label: string }[] = [
  { value: 'PortScan', label: 'Port scan' },
  { value: 'HttpHeaders', label: 'HTTP headers' },
  { value: 'SslCertificate', label: 'TLS certificate' },
];
export type FindingStatus = 'Open' | 'Resolved' | 'Accepted';

export interface ScanTarget {
  id: string;
  name: string;
  host: string;
  ports: string;
  description?: string;
  isEnabled: boolean;
  lastScannedAt?: string;
  riskScore?: number;
  createdAt: string;
}
export type TargetInput = Pick<ScanTarget, 'name' | 'host' | 'ports' | 'description' | 'isEnabled'>;
export interface ScanSchedule {
  id: string;
  targetId: string;
  targetName: string;
  name: string;
  scanTypes: string;
  intervalMinutes: number;
  /** UTC "HH:mm" for whole-day intervals. */
  runAtUtc?: string;
  isEnabled: boolean;
  lastRunAt?: string;
  nextRunAt?: string;
  createdAt: string;
}
export interface ScheduleInput {
  targetId: string;
  name: string;
  scanTypes: string;
  intervalMinutes: number;
  runAtUtc?: string;
  isEnabled: boolean;
}
export interface ScanJob {
  id: string;
  targetId?: string;
  targetName: string;
  host: string;
  scanTypes: string;
  status: ScanStatus;
  trigger: 'Manual' | 'Scheduled';
  progress: number;
  startedAt?: string;
  completedAt?: string;
  durationSeconds?: number;
  openPorts?: string;
  findingsCount: number;
  criticalCount: number;
  highCount: number;
  mediumCount: number;
  lowCount: number;
  error?: string;
  createdAt: string;
}
export interface Finding {
  id: string;
  jobId: string;
  targetId?: string;
  targetName: string;
  title: string;
  description: string;
  severity: Severity;
  category: string;
  port?: number;
  evidence?: string;
  recommendation?: string;
  status: FindingStatus;
  createdAt: string;
}
export interface ScannerStats {
  targets: number;
  activeSchedules: number;
  scans24h: number;
  activeScans: number;
  openFindings: number;
  criticalOpenFindings: number;
  openFindingsBySeverity: { severity: Severity; count: number }[];
  topRiskTargets: { id: string; name: string; host: string; riskScore: number; lastScannedAt?: string }[];
  recentJobs: ScanJob[];
}

// ---------- Alerts ----------
export type AlertStatus = 'Open' | 'Acknowledged' | 'Resolved';
export interface Alert {
  id: string;
  title: string;
  message: string;
  severity: Severity;
  source: string;
  sourceReference?: string;
  category?: string;
  status: AlertStatus;
  occurrenceCount: number;
  createdAt: string;
  lastOccurredAt: string;
  acknowledgedBy?: string;
  acknowledgedAt?: string;
  resolvedBy?: string;
  resolvedAt?: string;
  resolutionNotes?: string;
}
export interface AlertStats {
  open: number;
  acknowledged: number;
  resolved24h: number;
  criticalActive: number;
  meanTimeToAcknowledgeMinutes?: number;
  meanTimeToResolveMinutes?: number;
  activeBySeverity: CountItem[];
  activeBySource: CountItem[];
  last7Days: { day: string; critical: number; high: number; medium: number; low: number; info: number }[];
  latest: Alert[];
}

// ---------- Auth ----------
export type UserRole = 'Admin' | 'Analyst' | 'Viewer';
export const USER_ROLES: UserRole[] = ['Admin', 'Analyst', 'Viewer'];

export interface AuthUser {
  id: string;
  userName: string;
  email: string;
  fullName: string;
  role: UserRole;
  isActive: boolean;
  isLockedOut: boolean;
  lastLoginAt?: string;
  createdAt: string;
  mfaEnabled?: boolean;
}
export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  tokenType: 'Bearer';
  user: AuthUser;
  mfaRequired?: false;
}
/** Password accepted; the account needs an authenticator code. */
export interface MfaChallenge {
  mfaRequired: true;
}
export interface Session {
  id: string;
  createdAt: string;
  expiresAt: string;
  createdByIp?: string;
  userAgent?: string;
}
export interface CreateUserInput {
  userName: string;
  email: string;
  fullName: string;
  role: UserRole;
  password: string;
}
export interface UpdateUserInput {
  email: string;
  fullName: string;
  role: UserRole;
  isActive: boolean;
  newPassword?: string;
}

export interface ServiceHealth {
  service: string;
  status: 'Healthy' | 'Unhealthy' | 'Unreachable';
  latencyMs?: number;
}

// ---------- Identity: MFA & sign-in audit
export interface LoginAttempt {
  id: string;
  timestamp: string;
  userName: string;
  succeeded: boolean;
  failureReason?: 'UnknownUser' | 'BadPassword' | 'LockedOut' | 'Disabled' | 'BadMfaCode';
  usedMfa: boolean;
  ipAddress?: string;
  userAgent?: string;
}
export interface MfaSetup {
  secret: string;
  provisioningUri: string;
}

// ---------- Alert notifications
export type ChannelType = 'Email' | 'Webhook';
export type WebhookFormat = 'Generic' | 'Slack' | 'Teams';
export interface NotificationChannel {
  id: string;
  name: string;
  type: ChannelType;
  isEnabled: boolean;
  minSeverity: Severity;
  notifyOnEscalation: boolean;
  smtpHost?: string;
  smtpPort: number;
  smtpUseTls: boolean;
  smtpUserName?: string;
  hasSmtpPassword: boolean;
  fromAddress?: string;
  recipients?: string;
  webhookUrl?: string;
  webhookFormat: WebhookFormat;
  hasWebhookSecret: boolean;
  lastSentAt?: string;
  lastFailureAt?: string;
  lastError?: string;
  sentCount: number;
  createdAt: string;
}
export interface NotificationChannelInput {
  name: string;
  type: ChannelType;
  isEnabled: boolean;
  minSeverity: Severity;
  notifyOnEscalation: boolean;
  smtpHost?: string;
  smtpPort: number;
  smtpUseTls: boolean;
  smtpUserName?: string;
  /** undefined keeps the stored value, '' clears it. */
  smtpPassword?: string;
  fromAddress?: string;
  recipients?: string;
  webhookUrl?: string;
  webhookFormat: WebhookFormat;
  webhookSecret?: string;
}

// ---------- Threat feeds & config backup
export type ThreatFeedKind = 'ThreatIntel' | 'Country';
export interface ThreatFeed {
  id: string;
  name: string;
  url: string;
  kind: ThreatFeedKind;
  countryCode?: string;
  listType: IpListType;
  isEnabled: boolean;
  refreshMinutes: number;
  maxEntries: number;
  lastAttemptAt?: string;
  lastSuccessAt?: string;
  lastError?: string;
  entryCount: number;
  skippedCount: number;
  createdAt: string;
}
export interface ThreatFeedInput {
  name: string;
  url: string;
  kind: ThreatFeedKind;
  countryCode?: string;
  listType: IpListType;
  isEnabled: boolean;
  refreshMinutes: number;
  maxEntries: number;
}
export interface FeedRefreshResult {
  feed: ThreatFeed;
  added: number;
  removed: number;
  skippedExamples: string[];
}
export interface ConfigRestoreResult {
  policies: number;
  rules: number;
  ipEntries: number;
  feeds: number;
  replaced: boolean;
}
