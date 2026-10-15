import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { useState, useEffect, lazy, Suspense } from 'react';
import AuthGuard from './auth/AuthGuard';
import { WorkspaceProvider } from './auth/WorkspaceContext';
import AccessGate from './auth/AccessGate';
import Sidebar from './components/Sidebar';
import { isDcrPageEnabled } from './config/runtimeConfig';
import './App.css';

// Pages are loaded on demand, so the first load only fetches the page that is opened.
const Dashboard = lazy(() => import('./pages/Dashboard'));
const AnalyticsRules = lazy(() => import('./pages/AnalyticsRules'));
const Ingestion = lazy(() => import('./pages/Ingestion'));
const DataConnectors = lazy(() => import('./pages/DataConnectors'));
const DataCollectionRules = lazy(() => import('./pages/DataCollectionRules'));
const SentinelHealth = lazy(() => import('./pages/SentinelHealth'));

function App() {
  const [theme, setTheme] = useState<'dark' | 'light'>(() => {
    return (localStorage.getItem('sw-theme') as 'dark' | 'light') || 'dark';
  });

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('sw-theme', theme);
  }, [theme]);

  function toggleTheme() {
    setTheme((t) => (t === 'dark' ? 'light' : 'dark'));
  }

  return (
    <AuthGuard>
      <WorkspaceProvider>
        <AccessGate>
          <BrowserRouter>
            <div className="app-layout">
              <Sidebar theme={theme} onToggleTheme={toggleTheme} />
              <main className="main-content">
                <Suspense fallback={<div className="loading">Loading...</div>}>
                  <Routes>
                    <Route path="/" element={<Dashboard />} />
                    <Route path="/analytics-rules" element={<AnalyticsRules />} />
                    <Route path="/ingestion" element={<Ingestion />} />
                    <Route path="/data-connectors" element={<DataConnectors />} />
                    {isDcrPageEnabled() && <Route path="/dcr" element={<DataCollectionRules />} />}
                    <Route path="/sentinel-health" element={<SentinelHealth />} />
                  </Routes>
                </Suspense>
              </main>
            </div>
          </BrowserRouter>
        </AccessGate>
      </WorkspaceProvider>
    </AuthGuard>
  );
}

export default App;
