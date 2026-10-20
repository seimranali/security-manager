import { describe, expect, it } from 'vitest';
import { describeAddress, isValidIp } from './ip';

describe('describeAddress', () => {
  it.each([
    ['203.0.113.10', 'IPv4', 'Single address'],
    ['10.0.0.0/24', 'IPv4 CIDR', '256 addresses'],
    ['10.0.0.1/32', 'IPv4 CIDR', '1 address'],
    ['10.0.0.1-10.0.0.50', 'IPv4 range', '50 addresses'],
    ['2001:db8::1', 'IPv6', 'Single address'],
    ['2001:db8::/48', 'IPv6 CIDR', '/48 network'],
  ])('%s → %s (%s)', (input, kind, detail) => {
    const info = describeAddress(input);
    expect(info.valid).toBe(true);
    expect(info.kind).toBe(kind);
    expect(info.detail).toBe(detail);
  });

  it.each(['300.1.1.1', '10.0.0.0/33', '10.0.0.9-10.0.0.1', 'example.com', '1.2.3.4,5.6.7.8'])('rejects %s', (input) => {
    expect(describeAddress(input).valid).toBe(false);
  });

  it('only treats single addresses as IPs for the checker', () => {
    expect(isValidIp('8.8.8.8')).toBe(true);
    expect(isValidIp('8.8.8.0/24')).toBe(false);
  });
});
