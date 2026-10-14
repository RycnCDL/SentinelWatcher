import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { useState, useEffect } from 'react';
import AuthGuard from './auth/AuthGuard';
import { WorkspaceProvider } from './auth/WorkspaceContext';
import Sidebar from './components/Sidebar';
import Dashboard from './pages/Dashboard';
import AnalyticsRules from './pages/AnalyticsRules';
import Ingestion from './pages/Ingestion';
import DataConnectors from './pages/DataConnectors';
import DataCollectionRules from './pages/DataCollectionRules';
import SentinelHealth from './pages/SentinelHealth';
import './App.css';

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
        <BrowserRouter>
          <div className="app-layout">
            <Sidebar theme={theme} onToggleTheme={toggleTheme} />
            <main className="main-content">
              <Routes>
                <Route path="/" element={<Dashboard />} />
                <Route path="/analytics-rules" element={<AnalyticsRules />} />
                <Route path="/ingestion" element={<Ingestion />} />
                <Route path="/data-connectors" element={<DataConnectors />} />
                <Route path="/dcr" element={<DataCollectionRules />} />
                <Route path="/sentinel-health" element={<SentinelHealth />} />
              </Routes>
            </main>
          </div>
        </BrowserRouter>
      </WorkspaceProvider>
    </AuthGuard>
  );
}

export default App;
