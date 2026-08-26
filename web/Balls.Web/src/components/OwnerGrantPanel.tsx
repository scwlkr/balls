import { useEffect, useState } from "react";

import type { BrowserApi } from "../api/browserApi";
import type { DashboardSnapshot } from "../presentation/DashboardSnapshot";
import { toMessage } from "../presentation/toMessage";

type DashboardMember = DashboardSnapshot["circle"]["members"][number];

export function OwnerGrantPanel({
  api,
  dashboard,
  circleId,
}: {
  api: BrowserApi;
  dashboard: DashboardSnapshot;
  circleId: string;
}) {
  const [members, setMembers] = useState<DashboardMember[]>(() =>
    joinedMembers(dashboard),
  );
  const [folders, setFolders] = useState<string[]>([]);
  const [folderName, setFolderName] = useState("");
  const [memberName, setMemberName] = useState(members[0]?.name ?? "");
  const [preview, setPreview] = useState<
    Awaited<ReturnType<BrowserApi["previewFilesGrant"]>> | undefined
  >();
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    void api
      .listFilesContributions(circleId)
      .then((result) => {
        if (!active) return;
        const names = result.contributions.map((value) => value.displayName);
        setFolders(names);
        setFolderName(names[0] ?? "");
      })
      .catch((reason) => {
        if (active) setError(toMessage(reason));
      });
    return () => {
      active = false;
    };
  }, [api, circleId]);

  function changeFolder(value: string) {
    setFolderName(value);
    clearApproval();
  }

  function changeMember(value: string) {
    setMemberName(value);
    clearApproval();
  }

  function clearApproval() {
    setPreview(undefined);
    setStatus(null);
    setError(null);
  }

  async function refreshMembers() {
    if (busy) return;
    setBusy(true);
    setStatus(null);
    setError(null);
    setPreview(undefined);
    try {
      const current = await api.getCircle(circleId);
      const refreshed = current.members
        .filter((member) => member.role === "member")
        .map((member) => ({
          id: member.id,
          name: member.displayName,
          role: member.role,
          joinedAtUtc: member.joinedAtUtc,
        }));
      setMembers(refreshed);
      setMemberName((selected) =>
        refreshed.some((member) => member.name === selected)
          ? selected
          : (refreshed[0]?.name ?? ""),
      );
      setStatus(
        refreshed.length === 0
          ? "No joined Members yet. You can check again without reopening Balls."
          : "Member list updated.",
      );
    } catch (reason) {
      setError(toMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  async function reviewAccess() {
    if (!folderName || !memberName || busy) return;
    setBusy(true);
    setStatus(null);
    setError(null);
    try {
      setPreview(await api.previewFilesGrant(circleId, folderName, memberName));
    } catch (reason) {
      setPreview(undefined);
      setError(toMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  async function applyAccess() {
    if (!preview || busy) return;
    setBusy(true);
    setStatus(null);
    setError(null);
    try {
      const result = await api.applyFilesGrant(circleId);
      setStatus(result.message);
      setPreview(undefined);
    } catch (reason) {
      setError(toMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section
      className="message-history contribution-panel"
      id="share-capability"
      aria-labelledby="share-capability-title"
    >
      <div className="section-heading">
        <div>
          <p className="eyebrow">Circle Capability</p>
          <h2 id="share-capability-title">Share with a Member</h2>
        </div>
        <p>Choose exactly who can change the approved folder.</p>
      </div>
      <div className="member-refresh-actions">
        <button
          type="button"
          disabled={busy}
          onClick={() => void refreshMembers()}
        >
          {busy ? "Refreshing members…" : "Refresh members"}
        </button>
      </div>
      {folders.length === 0 || members.length === 0 ? (
        <div className="message-empty">
          <p>
            {folders.length === 0
              ? "Contribute a folder before sharing it."
              : "Invite someone and wait for them to join before sharing the folder."}
          </p>
          {status ? <p role="status">{status}</p> : null}
          {error ? (
            <p className="inline-error" role="alert">
              {error}
            </p>
          ) : null}
        </div>
      ) : (
        <form
          aria-label="Share a Circle Capability"
          aria-busy={busy}
          onSubmit={(event) => event.preventDefault()}
        >
          <label htmlFor="grant-folder">Folder</label>
          <select
            id="grant-folder"
            value={folderName}
            disabled={busy}
            onChange={(event) => changeFolder(event.target.value)}
          >
            {folders.map((folder) => (
              <option key={folder} value={folder}>
                {folder}
              </option>
            ))}
          </select>
          <label htmlFor="grant-member">Member</label>
          <select
            id="grant-member"
            value={memberName}
            disabled={busy}
            onChange={(event) => changeMember(event.target.value)}
          >
            {members.map((member) => (
              <option key={member.id} value={member.name}>
                {member.name}
              </option>
            ))}
          </select>
          <label htmlFor="grant-access">Access</label>
          <select id="grant-access" value="read-write" disabled={busy}>
            <option value="read-write">Read/write</option>
          </select>
          <button
            type="button"
            disabled={busy || !folderName || !memberName}
            onClick={() => void reviewAccess()}
          >
            {busy && !preview ? "Reviewing…" : "Review access"}
          </button>
          {preview ? (
            <div className="contribution-preview" aria-label="Access preview">
              <strong>{preview.summary}</strong>
              <p>
                Balls will create only the limited Windows access this Member
                needs. Private credential material stays out of the browser.
              </p>
              <button
                type="button"
                disabled={busy}
                onClick={() => void applyAccess()}
              >
                {busy ? "Sharing…" : "Share this Capability"}
              </button>
            </div>
          ) : null}
          {status ? <p role="status">{status}</p> : null}
          {error ? (
            <p className="inline-error" role="alert">
              {error}
            </p>
          ) : null}
        </form>
      )}
    </section>
  );
}

function joinedMembers(dashboard: DashboardSnapshot) {
  return dashboard.circle.members.filter((member) => member.role === "member");
}
