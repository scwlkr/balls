import { useState } from "react";

import type { BrowserApi } from "../api/browserApi";
import { encodeInvitationCode } from "../api/invitationCode";
import { toMessage } from "../presentation/toMessage";

export function InvitationPanel({
  api,
  circleId,
}: {
  api: BrowserApi;
  circleId: string;
}) {
  const [invitationCode, setInvitationCode] = useState("");
  const [expiresAt, setExpiresAt] = useState("");
  const [busy, setBusy] = useState(false);
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function createInvitation() {
    if (busy) return;
    setBusy(true);
    setCopied(false);
    setError(null);
    try {
      const invitation = await api.createInvitation(circleId);
      setInvitationCode(encodeInvitationCode(invitation));
      setExpiresAt(invitation.expiresAtUtc);
    } catch (reason) {
      setError(toMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  async function copyInvitation() {
    try {
      await navigator.clipboard.writeText(invitationCode);
      setCopied(true);
    } catch {
      setError("Copy the invitation from the text box and send it privately.");
    }
  }

  return (
    <section
      className="message-history invitation-panel"
      id="invite"
      aria-labelledby="invite-title"
    >
      <div className="section-heading">
        <div>
          <p className="eyebrow">Private, single-use invitation</p>
          <h2 id="invite-title">Invite someone</h2>
        </div>
        <p>They install Balls, paste your invitation, and join this Circle.</p>
      </div>
      <div className="invitation-actions" aria-busy={busy}>
        <button
          type="button"
          disabled={busy}
          onClick={() => void createInvitation()}
        >
          {busy ? "Creating invitation…" : "Create invitation"}
        </button>
        {invitationCode ? (
          <div className="invitation-result">
            <label htmlFor="invitation-code">
              Invitation to send privately
            </label>
            <textarea
              id="invitation-code"
              readOnly
              value={invitationCode}
              rows={3}
            />
            <button type="button" onClick={() => void copyInvitation()}>
              {copied ? "Copied" : "Copy invitation"}
            </button>
            <p>
              Expires {new Date(expiresAt).toLocaleString()} and works once.
            </p>
          </div>
        ) : null}
        {error ? (
          <p className="inline-error" role="alert">
            {error}
          </p>
        ) : null}
      </div>
    </section>
  );
}
