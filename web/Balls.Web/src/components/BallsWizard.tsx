import { useEffect, useState, type FormEvent } from "react";

import type {
  BrowserApi,
  BrowserBallsWizardChatMessageDto,
  BrowserBallsWizardStatusDto,
} from "../api/browserApi";
import { toMessage } from "../presentation/toMessage";

interface BallsWizardProps {
  api: BrowserApi;
  localRole: "owner" | "member" | "none";
}

interface ChatEntry {
  role: "user" | "assistant";
  content: string;
  sources?: Array<{ id: string; title: string }>;
}

const greeting: ChatEntry = {
  role: "assistant",
  content:
    "Greetings! I’m Balls Wizard — currently hovering, doing a little magic, and ready to help you find your way around Balls.",
};

export function BallsWizard({ api, localRole }: BallsWizardProps) {
  const [status, setStatus] = useState<BrowserBallsWizardStatusDto | null>(
    null,
  );
  const [open, setOpen] = useState(false);
  const [dismissed, setDismissed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [messages, setMessages] = useState<ChatEntry[]>([greeting]);
  const [question, setQuestion] = useState("");
  useEffect(() => {
    let active = true;
    void api
      .getWizardStatus()
      .then((next) => {
        if (active) setStatus(next);
      })
      .catch((reason) => {
        if (active) setError(toMessage(reason));
      });
    return () => {
      active = false;
    };
  }, [api]);

  useEffect(() => {
    if (!status?.canCancel) return;
    const timer = window.setInterval(() => {
      void api
        .getWizardStatus()
        .then(setStatus)
        .catch((reason) => setError(toMessage(reason)));
    }, 750);
    return () => window.clearInterval(timer);
  }, [api, status?.canCancel]);

  async function runStatusChange(
    action: () => Promise<BrowserBallsWizardStatusDto>,
  ) {
    if (busy) return;
    setBusy(true);
    setError(null);
    try {
      setStatus(await action());
    } catch (reason) {
      setError(toMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  async function ask(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const content = question.trim();
    if (!content || busy) return;
    const user: ChatEntry = { role: "user", content };
    const nextMessages = [...messages, user];
    setMessages(nextMessages);
    setQuestion("");
    setBusy(true);
    setError(null);
    try {
      const history = nextMessages
        .filter((entry) => entry !== greeting)
        .map(
          (entry) =>
            ({
              role: entry.role,
              content: entry.content,
            }) satisfies BrowserBallsWizardChatMessageDto,
        )
        .slice(-12);
      const answer = await api.chatWithWizard(localRole, history);
      setMessages((current) => [
        ...current,
        {
          role: "assistant",
          content: answer.answer,
          sources: answer.sources,
        },
      ]);
    } catch (reason) {
      setError(toMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  if (!status && !error) return null;

  const installed = status?.installation === "installed";
  const unavailable = status?.support !== "supported";
  const totalDownloadBytes = toNumber(status?.totalDownloadBytes ?? 0);
  const downloadedBytes = toNumber(status?.downloadedBytes ?? 0);
  const progress = totalDownloadBytes
    ? Math.min(100, (downloadedBytes / totalDownloadBytes) * 100)
    : 0;

  if (!installed && !open && dismissed) return null;

  return (
    <div className="wizard-layer" data-installed={installed}>
      {!open ? (
        installed ? (
          <button
            type="button"
            className="wizard-launcher"
            aria-label="Open Balls Wizard"
            onClick={() => setOpen(true)}
          >
            <img src="/balls-wizard.png" alt="" />
          </button>
        ) : (
          <aside className="wizard-offer" aria-label="Balls Wizard offer">
            <img src="/balls-wizard.png" alt="" />
            <div>
              <strong>
                {unavailable ? "Wizard unavailable" : "Download Wizard"}
              </strong>
              <p>{status?.message ?? error}</p>
            </div>
            <div className="wizard-offer-actions">
              <button type="button" onClick={() => setOpen(true)}>
                {unavailable ? "Details" : "Take a look"}
              </button>
              <button type="button" onClick={() => setDismissed(true)}>
                Not now
              </button>
            </div>
          </aside>
        )
      ) : (
        <aside className="wizard-panel" aria-label="Balls Wizard">
          <header>
            <div>
              <img src="/balls-wizard.png" alt="" />
              <div>
                <span>Local product guide</span>
                <h2>Balls Wizard</h2>
              </div>
            </div>
            <button
              type="button"
              className="wizard-close"
              aria-label="Close Balls Wizard"
              onClick={() => setOpen(false)}
            >
              ×
            </button>
          </header>

          {installed ? (
            <>
              <div className="wizard-chat" aria-live="polite">
                {messages.map((message, index) => (
                  <article
                    className={`wizard-message wizard-message-${message.role}`}
                    key={`${message.role}-${index}`}
                  >
                    <span>
                      {message.role === "assistant" ? "Wizard" : "You"}
                    </span>
                    <p>{message.content}</p>
                    {message.sources?.length ? (
                      <details>
                        <summary>Sources</summary>
                        <ul>
                          {message.sources.map((source) => (
                            <li key={source.id}>{source.title}</li>
                          ))}
                        </ul>
                      </details>
                    ) : null}
                  </article>
                ))}
                {busy ? (
                  <p className="wizard-thinking" role="status">
                    Consulting the tiny spellbook inside this computer…
                  </p>
                ) : null}
              </div>
              {error ? (
                <p className="wizard-error" role="alert">
                  {error}
                </p>
              ) : null}
              <form className="wizard-form" onSubmit={ask}>
                <label htmlFor="wizard-question">Ask about Balls</label>
                <div>
                  <textarea
                    id="wizard-question"
                    value={question}
                    maxLength={2000}
                    rows={2}
                    disabled={busy}
                    placeholder="How do I share a folder?"
                    onChange={(event) => setQuestion(event.target.value)}
                  />
                  <button type="submit" disabled={busy || !question.trim()}>
                    Ask
                  </button>
                </div>
              </form>
              <WizardDetails status={status} localRole={localRole} />
              <div className="wizard-management">
                <button
                  type="button"
                  onClick={() => {
                    setMessages([greeting]);
                    setError(null);
                  }}
                >
                  Clear conversation
                </button>
                <button
                  type="button"
                  className="wizard-remove"
                  disabled={busy}
                  onClick={() =>
                    void runStatusChange(async () => {
                      const next = await api.removeWizard();
                      setMessages([greeting]);
                      setOpen(false);
                      setDismissed(false);
                      return next;
                    })
                  }
                >
                  Remove Wizard
                </button>
              </div>
            </>
          ) : (
            <div className="wizard-install">
              <p className="wizard-intro">
                A helpful little local model for Balls. It stays on this
                computer, keeps no chat history, and has precisely zero
                authority to wave a wand at your Circle.
              </p>
              {status ? (
                <WizardDetails status={status} localRole={localRole} />
              ) : null}
              {status?.canCancel ? (
                <div
                  className="wizard-progress"
                  aria-label="Wizard download progress"
                >
                  <div>
                    <span style={{ width: `${progress}%` }} />
                  </div>
                  <p role="status">
                    {status.stage} · {formatBytes(status.downloadedBytes)} of{" "}
                    {formatBytes(status.totalDownloadBytes)}
                  </p>
                  <button
                    type="button"
                    disabled={busy}
                    onClick={() =>
                      void runStatusChange(() => api.cancelWizardInstall())
                    }
                  >
                    Pause download
                  </button>
                </div>
              ) : (
                <button
                  type="button"
                  className="wizard-download"
                  disabled={!status?.canInstall || busy}
                  onClick={() =>
                    void runStatusChange(() => api.installWizard())
                  }
                >
                  {status?.installation === "partial"
                    ? "Resume Wizard download"
                    : `Download ${formatBytes(status?.totalDownloadBytes ?? 0)}`}
                </button>
              )}
              {error ? (
                <p className="wizard-error" role="alert">
                  {error}
                </p>
              ) : null}
            </div>
          )}
        </aside>
      )}
    </div>
  );
}

function WizardDetails({
  status,
  localRole,
}: {
  status: BrowserBallsWizardStatusDto;
  localRole: "owner" | "member" | "none";
}) {
  return (
    <div className="wizard-details">
      <details>
        <summary>What Wizard can see</summary>
        <dl>
          <div>
            <dt>Balls role</dt>
            <dd>{localRole}</dd>
          </div>
          <div>
            <dt>Windows</dt>
            <dd>{status.systemContext.operatingSystem}</dd>
          </div>
          <div>
            <dt>Architecture</dt>
            <dd>
              {status.systemContext.operatingSystemArchitecture} /{" "}
              {status.systemContext.processArchitecture}
            </dd>
          </div>
          <div>
            <dt>CPU</dt>
            <dd>{status.systemContext.cpu}</dd>
          </div>
          <div>
            <dt>GPU</dt>
            <dd>{status.systemContext.gpus.join(", ")}</dd>
          </div>
          <div>
            <dt>Memory</dt>
            <dd>
              {formatBytes(status.systemContext.availableMemoryBytes)} available
              of {formatBytes(status.systemContext.totalMemoryBytes)}
            </dd>
          </div>
          <div>
            <dt>Wizard storage</dt>
            <dd>{formatBytes(status.systemContext.freeStorageBytes)} free</dd>
          </div>
        </dl>
        <p>
          No names, hostname, serials, network addresses, Circle content, files,
          or saved conversations.
        </p>
      </details>
      <details>
        <summary>Model, storage, and sources</summary>
        <p>
          {status.wizardVersion} needs{" "}
          {formatBytes(status.requiredStorageBytes)} free. Downloads are pinned
          and verified before use.
        </p>
        <ul>
          {status.artifacts.map((artifact) => (
            <li key={artifact.id}>
              <strong>{artifact.displayName}</strong>
              <span>
                {artifact.version} · {formatBytes(artifact.sizeBytes)} ·{" "}
                {artifact.license}
              </span>
              <a href={artifact.source} target="_blank" rel="noreferrer">
                Official source
              </a>
            </li>
          ))}
        </ul>
      </details>
    </div>
  );
}

function toNumber(value: string | number) {
  return typeof value === "number" ? value : Number(value);
}

function formatBytes(value: string | number) {
  value = toNumber(value);
  if (!Number.isFinite(value) || value <= 0) return "0 B";
  const units = ["B", "KiB", "MiB", "GiB"];
  const index = Math.min(Math.floor(Math.log(value) / Math.log(1024)), 3);
  return `${(value / 1024 ** index).toFixed(index >= 3 ? 2 : 1)} ${units[index]}`;
}
