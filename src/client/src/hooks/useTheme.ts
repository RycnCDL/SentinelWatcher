import { useState, useEffect } from 'react';

export function useTheme(): 'dark' | 'light' {
  const [theme, setTheme] = useState<'dark' | 'light'>(
    () => (document.documentElement.getAttribute('data-theme') as 'dark' | 'light') || 'dark'
  );

  useEffect(() => {
    const observer = new MutationObserver(() => {
      const t = document.documentElement.getAttribute('data-theme') as 'dark' | 'light';
      setTheme(t || 'dark');
    });
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
    return () => observer.disconnect();
  }, []);

  return theme;
}

export function useChartTheme() {
  const theme = useTheme();
  const isDark = theme === 'dark';

  return {
    tooltipStyle: isDark
      ? {
          backgroundColor: 'rgba(26, 28, 35, 0.95)',
          backdropFilter: 'blur(12px)',
          border: '1px solid rgba(255, 255, 255, 0.1)',
          borderRadius: '12px',
          color: '#FFFFFF',
          boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.8)',
        }
      : {
          backgroundColor: 'rgba(255, 255, 255, 0.97)',
          backdropFilter: 'blur(12px)',
          border: '1px solid rgba(0, 0, 0, 0.1)',
          borderRadius: '12px',
          color: '#0F172A',
          boxShadow: '0 8px 32px rgba(0, 0, 0, 0.12)',
        },
    gridStroke: isDark ? 'rgba(255, 255, 255, 0.04)' : 'rgba(0, 0, 0, 0.06)',
    axisStroke: isDark ? '#5A6470' : '#94A3B8',
    axisTickFill: isDark ? '#A0AAB5' : '#64748B',
  };
}
