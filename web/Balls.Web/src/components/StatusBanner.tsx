import type { DashboardSnapshot } from "../presentation/DashboardSnapshot";

interface StatusBannerProps {
  snapshot: Pick<
    DashboardSnapshot,
    "localNode" | "productVersion" | "protocolVersion"
  >;
}

export function StatusBanner({ snapshot }: StatusBannerProps) {
  return (
    <aside className="status-banner" aria-label="Local Node status">
      <span className="status-dot" aria-hidden="true" />
      <div>
        <strong>{snapshot.localNode.name}</strong>
        <span>Local Node ready</span>
      </div>
      <dl className="status-facts">
        <div>
          <dt>Control</dt>
          <dd>v{snapshot.protocolVersion}</dd>
        </div>
        <div>
          <dt>Build</dt>
          <dd>{snapshot.productVersion}</dd>
        </div>
      </dl>
    </aside>
  );
}
