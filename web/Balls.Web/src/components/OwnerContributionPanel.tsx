import { useState } from "react";

import type { BrowserApi } from "../api/browserApi";
import { toMessage } from "../presentation/toMessage";

export function OwnerContributionPanel({
  api,
  circleId,
  onContributed,
}: {
  api: BrowserApi;
  circleId: string;
  onContributed: () => void;
}) {
  const [selection, setSelection] = useState<{
    requestId: string;
    selectionId: string;
    folderPath: string;
    displayName: string;
  } | null>(null);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function chooseFolder() {
    if (busy) return;
    setBusy(true);
    setStatus(null);
    setError(null);
    try {
      const result = await api.selectFilesFolder(circleId);
      if (
        result.status === "cancelled" ||
        !result.selectionId ||
        !result.folderPath ||
        !result.displayName
      ) {
        setSelection(null);
        setStatus("No folder was selected. Nothing changed.");
        return;
      }
      setSelection({
        requestId: crypto.randomUUID(),
        selectionId: result.selectionId,
        folderPath: result.folderPath,
        displayName: result.displayName,
      });
    } catch (reason) {
      setError(toMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  async function contributeFolder() {
    if (!selection || busy) return;
    setBusy(true);
    setStatus(null);
    setError(null);
    try {
      const result = await api.contributeFilesFolder(
        circleId,
        selection.requestId,
        selection.selectionId,
      );
      setStatus(`${result.displayName} is ready to share with Circle members.`);
      setSelection(null);
      onContributed();
    } catch (reason) {
      setError(toMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section
      className="message-history contribution-panel"
      id="contribute"
      aria-labelledby="contribute-title"
    >
      <div className="section-heading">
        <div>
          <p className="eyebrow">Circle Files</p>
          <h2 id="contribute-title">Share an existing folder</h2>
        </div>
        <p>Choose work that is already on this Windows computer.</p>
      </div>
      <div className="contribution-actions" aria-busy={busy}>
        <button
          type="button"
          disabled={busy}
          onClick={() => void chooseFolder()}
        >
          {busy && !selection
            ? "Opening folder picker…"
            : "Choose existing folder"}
        </button>
        {selection ? (
          <div
            className="contribution-preview"
            aria-label="Contribution preview"
          >
            <p>You chose</p>
            <strong>{selection.folderPath}</strong>
            <p>
              Balls will make this exact folder available as Circle Files.
              Existing files stay in place. Windows may ask the Owner to approve
              the hosting change.
            </p>
            <button
              type="button"
              disabled={busy}
              onClick={() => void contributeFolder()}
            >
              {busy
                ? "Contributing folder…"
                : `Contribute ${selection.displayName}`}
            </button>
          </div>
        ) : null}
        {status ? <p role="status">{status}</p> : null}
        {error ? (
          <p className="inline-error" role="alert">
            {error}
          </p>
        ) : null}
      </div>
    </section>
  );
}
