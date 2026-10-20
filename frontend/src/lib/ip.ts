/** Lightweight client-side description of IP list address specs (the server remains authoritative). */

const V4 = /^(25[0-5]|2[0-4]\d|1?\d?\d)(\.(25[0-5]|2[0-4]\d|1?\d?\d)){3}$/;
const looksV6 = (s: string) => s.includes(':') && /^[0-9a-f:.]+$/i.test(s);

const v4ToNumber = (ip: string) => ip.split('.').reduce((n, part) => n * 256 + Number(part), 0);

export interface AddressInfo {
  valid: boolean;
  kind?: 'IPv4' | 'IPv6' | 'IPv4 CIDR' | 'IPv6 CIDR' | 'IPv4 range' | 'IPv6 range';
  /** Human summary, e.g. "256 addresses". */
  detail?: string;
  error?: string;
}

const plural = (n: number) => `${n.toLocaleString()} address${n === 1 ? '' : 'es'}`;

export function describeAddress(raw: string): AddressInfo {
  const s = raw.trim().toLowerCase();
  if (!s) return { valid: false };
  if (s.includes(',')) return { valid: false, error: 'One address per entry — use Import for many.' };

  if (s.includes('/')) {
    const [ip, prefixText] = s.split('/');
    const prefix = Number(prefixText);
    if (V4.test(ip) && Number.isInteger(prefix) && prefix >= 0 && prefix <= 32) {
      return { valid: true, kind: 'IPv4 CIDR', detail: plural(2 ** (32 - prefix)) };
    }
    if (looksV6(ip) && Number.isInteger(prefix) && prefix >= 0 && prefix <= 128) {
      return { valid: true, kind: 'IPv6 CIDR', detail: `/${prefix} network` };
    }
    return { valid: false, error: 'Invalid CIDR block.' };
  }

  if (s.includes('-')) {
    const [a, b] = s.split('-').map((p) => p.trim());
    if (V4.test(a) && V4.test(b)) {
      const count = v4ToNumber(b) - v4ToNumber(a) + 1;
      return count > 0 ? { valid: true, kind: 'IPv4 range', detail: plural(count) } : { valid: false, error: 'Range start must not exceed its end.' };
    }
    if (looksV6(a) && looksV6(b)) return { valid: true, kind: 'IPv6 range' };
    return { valid: false, error: 'Invalid address range.' };
  }

  if (V4.test(s)) return { valid: true, kind: 'IPv4', detail: 'Single address' };
  if (looksV6(s)) return { valid: true, kind: 'IPv6', detail: 'Single address' };
  return { valid: false, error: 'Not a valid IP, CIDR block or range.' };
}

export const isValidIp = (s: string) => {
  const info = describeAddress(s);
  return info.valid && (info.kind === 'IPv4' || info.kind === 'IPv6');
};
