import { useEffect, useState } from 'react';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import {
  Activity, Ban, Bell, BellRing, DatabaseBackup, History, Rss, BrainCircuit, CalendarClock, Crosshair, FileWarning, Flame, LayoutDashboard, ListChecks,
  Eye, Menu, Moon, Network, Radar, Router, ScanSearch, ServerCog, ShieldCheck, ShieldHalf, Sun, Users,
} from 'lucide-react';
import { useAuth } from '../../hooks/useAuth';
import { UserMenu } from './UserMenu';
import { alertApi } from '../../lib/api';
import { cn } from '../../lib/format';
import { useTheme } from '../../hooks/useTheme';
import { useAlertHub } from '../../hooks/useAlertHub';
import { Button } from '../ui/Button';

const NAV = [
  { label: 'Overview', items: [{ to: '/', label: 'Dashboard', icon: LayoutDashboard }] },
  {
    label: 'Firewall',
    items: [
      { to: '/firewall/policies', label: 'Policies', icon: ShieldHalf },
      { to: '/firewall/rules', label: 'Rules', icon: ListChecks },
      { to: '/firewall/ip-lists', label: 'IP Allow / Block', icon: Ban },
      { to: '/firewall/feeds', label: 'Threat & Geo Feeds', icon: Rss },
      { to: '/firewall/traffic', label: 'Traffic', icon: Network },
    ],
  },
  {
    label: 'Network',
    items: [
      { to: '/network/gateways', label: 'Gateways', icon: Router },
      { to: '/network/clusters', label: 'High Availability', icon: ServerCog },
    ],
  },
  {
    label: 'Security',
    items: [
      { to: '/security/events', label: 'Events', icon: Activity },
      { to: '/security/detections', label: 'Detections', icon: Crosshair },
      { to: '/security/rules', label: 'Detection Rules', icon: BrainCircuit },
    ],
  },
  {
    label: 'Scanner',
    items: [
      { to: '/scanner/targets', label: 'Targets', icon: Radar },
      { to: '/scanner/scans', label: 'Scans', icon: ScanSearch },
      { to: '/scanner/schedules', label: 'Schedules', icon: CalendarClock },
      { to: '/scanner/findings', label: 'Findings', icon: FileWarning },
    ],
  },
  { label: 'Response', items: [{ to: '/alerts', label: 'Alerts', icon: Bell, badge: true }] },
  {
    label: 'Administration',
    adminOnly: true,
    items: [
      { to: '/admin/users', label: 'Users', icon: Users },
      { to: '/admin/sign-ins', label: 'Sign-in Audit', icon: History },
      { to: '/admin/notifications', label: 'Notifications', icon: BellRing },
      { to: '/admin/backup', label: 'Backup & Restore', icon: DatabaseBackup },
    ],
  },
];

const TITLES: Record<string, string> = { ...Object.fromEntries(NAV.flatMap((g) => g.items.map((i) => [i.to, i.label]))), '/account/security': 'Account security' };

export function AppLayout() {
  const [menuOpen, setMenuOpen] = useState(false);
  // Desktop: collapse the sidebar to an icon rail (remembered per browser). Mobile: the same button opens the drawer.
  const [collapsed, setCollapsed] = useState(() => { try { return localStorage.getItem('sm-sidebar-collapsed') === '1'; } catch { return false; } });
  const toggleSidebar = () => {
    if (window.matchMedia('(max-width: 960px)').matches) return setMenuOpen(true);
    setCollapsed((c) => {
      try { localStorage.setItem('sm-sidebar-collapsed', c ? '0' : '1'); } catch { /* storage unavailable */ }
      return !c;
    });
  };
  const { theme, toggle } = useTheme();
  const location = useLocation();
  const hub = useAlertHub();
  const { hasRole, canWrite } = useAuth();

  const { data: stats } = useQuery({ queryKey: ['alerts', 'stats'], queryFn: alertApi.stats, refetchInterval: 60_000 });
  const openAlerts = stats?.open ?? 0;

  useEffect(() => setMenuOpen(false), [location.pathname]);

  return (
    <div className={cn('app', collapsed && 'sidebar-collapsed')}>
      <aside className={cn('sidebar', menuOpen && 'open')} aria-label="Main navigation">
        <div className="sidebar-brand">
          <div className="brand-logo"><ShieldCheck size={18} /></div>
          <div className="brand-text">
            <div className="brand-name">Security Manager</div>
            <div className="brand-sub">Firewall · SOC · Scanner</div>
          </div>
        </div>
        <nav className="nav">
          {NAV.filter((g) => !('adminOnly' in g) || hasRole('Admin')).map((group) => (
            <div key={group.label} className="nav-group">
              <div className="nav-label">{group.label}</div>
              {group.items.map(({ to, label, icon: Icon, ...item }) => (
                <NavLink key={to} to={to} end={to === '/'} title={collapsed ? label : undefined} className={({ isActive }) => cn('nav-link', isActive && 'active')}>
                  <Icon size={17} />
                  <span className="nav-text">{label}</span>
                  {'badge' in item && openAlerts > 0 && <span className="count">{openAlerts > 99 ? '99+' : openAlerts}</span>}
                </NavLink>
              ))}
            </div>
          ))}
        </nav>
        <div className="sidebar-footer row"><Flame size={14} /> <span className="nav-text">v1.0 · Microservices</span></div>
      </aside>
      {menuOpen && <div className="backdrop" onClick={() => setMenuOpen(false)} />}

      <div className="main">
        <header className="topbar">
          <Button variant="ghost" iconOnly className="menu-btn" icon={<Menu size={18} />} aria-label={collapsed ? 'Expand sidebar' : 'Toggle sidebar'} title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'} onClick={toggleSidebar} />
          <span className="topbar-title">{TITLES[location.pathname] ?? 'Security Manager'}</span>
          <span className="spacer" />
          {!canWrite && <span className="live-pill" title="Your role has read-only access"><Eye size={13} /> Read-only</span>}
          <span className="live-pill hide-sm" title="Real-time alert stream">
            <span className={cn('health-dot', hub)} />
            {hub === 'live' ? 'Live' : hub === 'connecting' ? 'Connecting' : 'Offline'}
          </span>
          <NavLink to="/alerts" className="btn btn-ghost btn-icon" aria-label={`${openAlerts} open alerts`} style={{ position: 'relative' }}>
            <Bell size={18} />
            {openAlerts > 0 && <span className="health-dot" style={{ position: 'absolute', top: 8, right: 8, background: 'var(--danger)' }} />}
          </NavLink>
          <Button variant="ghost" iconOnly icon={theme === 'dark' ? <Sun size={18} /> : <Moon size={18} />} aria-label="Toggle theme" onClick={toggle} />
          <UserMenu />
        </header>
        <main className="content">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
