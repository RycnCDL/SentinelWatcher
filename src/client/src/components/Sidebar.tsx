import { NavLink } from 'react-router-dom';
import { useMsal } from '@azure/msal-react';
import {
  LayoutDashboard,
  ShieldAlert,
  Database,
  Cable,
  FileInput,
  HeartPulse,
  Sun,
  Moon,
  LogOut,
} from 'lucide-react';
import WorkspaceSelector from './WorkspaceSelector';
import { isDcrPageEnabled } from '../config/runtimeConfig';

const allNavItems = [
  { to: '/', icon: LayoutDashboard, label: 'Dashboard' },
  { to: '/analytics-rules', icon: ShieldAlert, label: 'Analytics Rules' },
  { to: '/ingestion', icon: Database, label: 'Log Ingestion' },
  { to: '/data-connectors', icon: Cable, label: 'Data Connectors' },
  { to: '/dcr', icon: FileInput, label: 'Collection Rules' },
  { to: '/sentinel-health', icon: HeartPulse, label: 'Sentinel Health' },
];

interface SidebarProps {
  theme: 'dark' | 'light';
  onToggleTheme: () => void;
}

export default function Sidebar({ theme, onToggleTheme }: SidebarProps) {
  const { instance, accounts } = useMsal();
  const account = accounts[0];
  // Read at render time: the runtime config is loaded after this module is imported.
  const navItems = allNavItems.filter((item) => item.to !== '/dcr' || isDcrPageEnabled());

  return (
    <aside className="sidebar">
      <div className="sidebar-header">
        <ShieldAlert size={26} className="sidebar-header__icon" />
        <h1>Sentinel Watcher</h1>
      </div>

      <WorkspaceSelector />

      <nav>
        {navItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.to === '/'}
            className={({ isActive }) =>
              `nav-item ${isActive ? 'active' : ''}`
            }
          >
            <item.icon size={20} className="nav-icon" />
            <span>{item.label}</span>
          </NavLink>
        ))}
      </nav>

      <div className="sidebar-footer">
        {account && (
          <div className="sidebar-user">
            <span className="sidebar-user__name">{account.name || account.username}</span>
            <button
              className="sidebar-user__logout"
              onClick={() => instance.logoutRedirect()}
              title="Sign out"
            >
              <LogOut size={16} />
            </button>
          </div>
        )}

        <button className="theme-toggle" onClick={onToggleTheme} aria-label="Toggle theme">
          <div className="theme-toggle__track">
            <div className="theme-toggle__thumb">
              {theme === 'dark' ? <Moon size={12} /> : <Sun size={12} />}
            </div>
          </div>
          <span className="theme-toggle__label">
            {theme === 'dark' ? 'Dark' : 'Light'}
          </span>
        </button>
      </div>
    </aside>
  );
}
