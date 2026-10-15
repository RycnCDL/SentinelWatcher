import { useWorkspace } from '../auth/useWorkspace';
import { ChevronDown } from 'lucide-react';
import { useState, useRef, useEffect } from 'react';

export default function WorkspaceSelector() {
  const { workspaces, selectedWorkspace, switchWorkspace, loading, error } = useWorkspace();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, []);

  if (loading) {
    return <div className="workspace-selector workspace-selector--loading">Loading workspaces...</div>;
  }

  if (error) {
    return <div className="workspace-selector workspace-selector--empty">{error}</div>;
  }

  if (workspaces.length === 0) {
    return <div className="workspace-selector workspace-selector--empty">No workspaces configured</div>;
  }

  return (
    <div className="workspace-selector" ref={ref}>
      <button className="workspace-selector__trigger" onClick={() => setOpen(!open)}>
        <div className="workspace-selector__info">
          <span className="workspace-selector__label">Workspace</span>
          <span className="workspace-selector__name">
            {selectedWorkspace?.workspaceName || 'Select...'}
          </span>
        </div>
        <ChevronDown size={16} className={`workspace-selector__chevron ${open ? 'open' : ''}`} />
      </button>

      {open && (
        <div className="workspace-selector__dropdown">
          {workspaces.map((ws) => (
            <button
              key={ws.resourceId}
              className={`workspace-selector__option ${ws.resourceId === selectedWorkspace?.resourceId ? 'active' : ''}`}
              onClick={() => { switchWorkspace(ws); setOpen(false); }}
            >
              <span className="workspace-selector__option-name">{ws.workspaceName}</span>
              <span className="workspace-selector__option-sub">
                {ws.resourceGroup}
              </span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
