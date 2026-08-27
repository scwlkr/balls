import { useEffect, useState } from "react";

import type { BrowserApi } from "../api/browserApi";
import type { WorkspaceViewer } from "../presentation/DashboardSnapshot";
import { toMessage } from "../presentation/toMessage";

export type FilesMappingApi = Pick<
  BrowserApi,
  "syncFiles" | "listFilesContributions" | "listFilesGrants" | "openFiles"
>;

export function FilesMappingPanel({
  api,
  viewer,
  circleId,
}: {
  api: FilesMappingApi;
  viewer: WorkspaceViewer;
  circleId: string;
}) {
  const [contributions, setContributions] = useState<
    Awaited<
      ReturnType<FilesMappingApi["listFilesContributions"]>
    >["contributions"]
  >([]);
  const [contributionId, setContributionId] = useState("");
  const [grantId, setGrantId] = useState("");
  const [mappingStatus, setMappingStatus] = useState<string | null>(null);
  const [panelError, setPanelError] = useState<string | null>(null);
  const [panelBusy, setPanelBusy] = useState(false);
  const [refreshRequest, setRefreshRequest] = useState(0);

  useEffect(() => {
    let active = true;
    let timer: ReturnType<typeof window.setTimeout> | undefined;
    let attempt = 0;
    const shouldSynchronize = viewer.role === "member";

    function retry() {
      if (!active || !shouldSynchronize || attempt >= 15) return;
      const delay = Math.min(1000 * 2 ** attempt, 15000);
      attempt += 1;
      timer = window.setTimeout(() => void load(), delay);
    }

    async function load() {
      try {
        if (shouldSynchronize) {
          await api.syncFiles(circleId);
          if (!active) return;
        }

        const result = await api.listFilesContributions(circleId);
        if (!active) return;
        setContributions(result.contributions);
        const first = result.contributions[0];
        setContributionId(first?.id ?? "");
        if (!first) {
          retry();
          return;
        }

        const grantList = await api.listFilesGrants(circleId, first.id);
        if (active) {
          const available = grantList.grants.filter(
            (grant) =>
              viewer.role === "owner" || grant.memberId === viewer.memberId,
          );
          setGrantId(available[0]?.id ?? "");
          setPanelError(null);
          if (available.length === 0) retry();
        }
      } catch (reason) {
        if (!active) return;
        if (shouldSynchronize && attempt < 15) retry();
        else setPanelError(toMessage(reason));
      }
    }

    void load();
    return () => {
      active = false;
      if (timer !== undefined) window.clearTimeout(timer);
    };
  }, [api, circleId, refreshRequest, viewer.memberId, viewer.role]);

  async function openSharedFolder() {
    if (!contributionId || !grantId || panelBusy) return;
    setPanelBusy(true);
    setPanelError(null);
    setMappingStatus(null);
    try {
      const result = await api.openFiles(circleId);
      setMappingStatus(result.message);
    } catch (reason) {
      setPanelError(toMessage(reason));
    } finally {
      setPanelBusy(false);
    }
  }

  return (
    <section
      className="message-history"
      id="files"
      aria-labelledby="files-title"
    >
      <div className="section-heading">
        <div>
          <p className="eyebrow">Circle Capability</p>
          <h2 id="files-title">Open shared folder</h2>
        </div>
        <p>Only folders your Circle Owner approved for you appear here.</p>
      </div>
      {contributions.length === 0 ? (
        <div className="message-empty">
          <p>
            Waiting for your Circle owner to finish sharing the project folder.
          </p>
          {viewer.role === "member" ? (
            <button
              type="button"
              onClick={() => {
                setPanelError(null);
                setRefreshRequest((value) => value + 1);
              }}
            >
              Check again
            </button>
          ) : null}
          {panelError ? (
            <p className="inline-error" role="alert">
              {panelError}
            </p>
          ) : null}
        </div>
      ) : (
        <form
          aria-label="Open Circle Capability"
          aria-busy={panelBusy}
          onSubmit={(event) => event.preventDefault()}
        >
          <p>
            Approved folder: <strong>{contributions[0]?.displayName}</strong>
          </p>
          <button
            type="button"
            disabled={panelBusy || !grantId}
            onClick={() => void openSharedFolder()}
          >
            {panelBusy ? "Connecting…" : "Open shared folder in Explorer"}
          </button>
          {mappingStatus ? <p role="status">{mappingStatus}</p> : null}
          {panelError ? (
            <p className="inline-error" role="alert">
              {panelError}
            </p>
          ) : null}
        </form>
      )}
    </section>
  );
}
