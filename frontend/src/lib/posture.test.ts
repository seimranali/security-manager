import { describe, expect, it } from 'vitest';
import { computePosture } from './posture';
import type { AuthUser, ThreatFeed } from './types';

const user = (role: AuthUser['role'], mfaEnabled: boolean): AuthUser => ({
  id: role + mfaEnabled, userName: role, email: '', fullName: role, role, isActive: true, isLockedOut: false, createdAt: '', mfaEnabled,
});

describe('computePosture', () => {
  it('scores 100 when every available factor is healthy', () => {
    const feed = { isEnabled: true, listType: 'Block', kind: 'ThreatIntel', entryCount: 900 } as ThreatFeed;
    const p = computePosture({ users: [user('Admin', true)], feeds: [feed] });
    expect(p.score).toBe(100);
    expect(p.grade).toBe('A');
  });

  it('caps MFA when an administrator has no second factor and lists the worst factor first', () => {
    const p = computePosture({ users: [user('Admin', false), user('Analyst', true)], feeds: [] });
    expect(p.factors[0].key).toBe('feeds');
    expect(p.factors.find((f) => f.key === 'mfa')!.score).toBe(0.5);
    expect(p.score).toBe(30); // (15*0.5 + 10*0) / 25
  });

  it('treats any active critical alert as a failing factor', () => {
    const alerts = { open: 0, acknowledged: 0, resolved24h: 0, criticalActive: 1, activeBySeverity: [], activeBySource: [], last7Days: [], latest: [] };
    expect(computePosture({ alerts }).score).toBe(0);
  });

  it('returns 0 with no data rather than dividing by zero', () => {
    expect(computePosture({}).score).toBe(0);
  });
});
