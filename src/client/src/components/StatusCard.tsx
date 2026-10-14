import type { ReactNode } from 'react';

interface StatusCardProps {
  title: string;
  value: string | number;
  subtitle?: string;
  icon: ReactNode;
  color?: 'blue' | 'green' | 'yellow' | 'red' | 'purple';
}

export default function StatusCard({
  title,
  value,
  subtitle,
  icon,
  color = 'blue',
}: StatusCardProps) {
  return (
    <div className={`status-card status-card--${color}`}>
      <div className="status-card__icon">{icon}</div>
      <div className="status-card__content">
        <span className="status-card__title">{title}</span>
        <span className="status-card__value">{value}</span>
        {subtitle && <span className="status-card__subtitle">{subtitle}</span>}
      </div>
    </div>
  );
}
