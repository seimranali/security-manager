import { Link, Route, Routes } from 'react-router-dom';
import { Compass } from 'lucide-react';
import { AppLayout } from './components/layout/AppLayout';
import { RequireAuth } from './components/layout/RequireAuth';
import { EmptyState } from './components/ui/DataTable';
import LoginPage from './pages/LoginPage';
import DashboardPage from './pages/DashboardPage';
import PoliciesPage from './pages/firewall/PoliciesPage';
import RulesPage from './pages/firewall/RulesPage';
import TrafficPage from './pages/firewall/TrafficPage';
import IpListsPage from './pages/firewall/IpListsPage';
import EnforcementPage from './pages/firewall/EnforcementPage';
import GatewaysPage from './pages/network/GatewaysPage';
import GatewayDetailPage from './pages/network/GatewayDetailPage';
import ClustersPage from './pages/network/ClustersPage';
import EventsPage from './pages/security/EventsPage';
import DetectionsPage from './pages/security/DetectionsPage';
import DetectionRulesPage from './pages/security/DetectionRulesPage';
import TargetsPage from './pages/scanner/TargetsPage';
import ScansPage from './pages/scanner/ScansPage';
import SchedulesPage from './pages/scanner/SchedulesPage';
import FindingsPage from './pages/scanner/FindingsPage';
import AlertsPage from './pages/AlertsPage';
import UsersPage from './pages/UsersPage';
import AccountSecurityPage from './pages/AccountSecurityPage';
import LoginHistoryPage from './pages/LoginHistoryPage';
import NotificationsPage from './pages/NotificationsPage';
import BackupPage from './pages/BackupPage';
import ThreatFeedsPage from './pages/firewall/ThreatFeedsPage';

export default function App() {
  return (
    <Routes>
      <Route path="login" element={<LoginPage />} />
      <Route element={<RequireAuth><AppLayout /></RequireAuth>}>
        <Route index element={<DashboardPage />} />
        <Route path="firewall/policies" element={<PoliciesPage />} />
        <Route path="firewall/rules" element={<RulesPage />} />
        <Route path="firewall/traffic" element={<TrafficPage />} />
        <Route path="firewall/ip-lists" element={<IpListsPage />} />
        <Route path="firewall/enforcement" element={<EnforcementPage />} />
        <Route path="firewall/feeds" element={<ThreatFeedsPage />} />
        <Route path="account/security" element={<AccountSecurityPage />} />
        <Route path="network/gateways" element={<GatewaysPage />} />
        <Route path="network/gateways/:id" element={<GatewayDetailPage />} />
        <Route path="network/clusters" element={<ClustersPage />} />
        <Route path="security/events" element={<EventsPage />} />
        <Route path="security/detections" element={<DetectionsPage />} />
        <Route path="security/rules" element={<DetectionRulesPage />} />
        <Route path="scanner/targets" element={<TargetsPage />} />
        <Route path="scanner/scans" element={<ScansPage />} />
        <Route path="scanner/schedules" element={<SchedulesPage />} />
        <Route path="scanner/findings" element={<FindingsPage />} />
        <Route path="alerts" element={<AlertsPage />} />
        <Route path="admin/users" element={<RequireAuth roles={['Admin']}><UsersPage /></RequireAuth>} />
        <Route path="admin/sign-ins" element={<RequireAuth roles={['Admin']}><LoginHistoryPage /></RequireAuth>} />
        <Route path="admin/notifications" element={<RequireAuth roles={['Admin']}><NotificationsPage /></RequireAuth>} />
        <Route path="admin/backup" element={<RequireAuth roles={['Admin']}><BackupPage /></RequireAuth>} />
        <Route
          path="*"
          element={<EmptyState icon={<Compass size={22} />} title="Page not found" action={<Link to="/" className="btn btn-primary">Back to dashboard</Link>} />}
        />
      </Route>
    </Routes>
  );
}
