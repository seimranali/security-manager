import type { ReactNode } from 'react';

export function EmptyState({ icon, title, description, action }: { icon: ReactNode; title: string; description?: string; action?: ReactNode }) {
  return (
    <div className="empty">
      <div className="empty-icon">{icon}</div>
      <div className="empty-title">{title}</div>
      {description && <p>{description}</p>}
      {action}
    </div>
  );
}

export function Spinner() {
  return <div className="loading-block"><div className="spinner" role="status" aria-label="Loading" /></div>;
}
