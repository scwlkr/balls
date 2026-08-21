import { useEffect, useRef, useState, type FormEvent } from "react";

import { browserApi, type BrowserApi } from "./api/browserApi";
import type {
  CircleDetailsDto,
  CircleMessageDto,
  CircleSummaryDto,
  StatusDto,
} from "./api/localControl";
import { BrandMark } from "./components/BrandMark";
import { CircleTopology } from "./components/CircleTopology";
import { StatusBanner } from "./components/StatusBanner";
import type { DashboardSnapshot } from "./presentation/DashboardSnapshot";

interface AppProps {
  api?: BrowserApi;
}

interface WorkspaceState {
  status: StatusDto;
  circles: CircleSummaryDto[];
  selected: CircleDetailsDto | null;
  messages: CircleMessageDto[];
}

export function App({ api = browserApi }: AppProps) {
  const started = useRef(false);
  const [{ capability, initialError }] = useState(() => {
    const value = readLaunchCapability(window.location.hash);
    return {
      capability: value,
      initialError: value
        ? null
        : "Run balls ui again to open a fresh local workspace.",
    };
  });
  const [workspace, setWorkspace] = useState<WorkspaceState | null>(null);
  const [error, setError] = useState<string | null>(initialError);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (started.current) return;
    started.current = true;
    if (!capability) return;

    let active = true;
    void (async () => {
      try {
        await api.exchangeLaunchCapability(capability);
        window.history.replaceState(
          null,
          "",
          window.location.pathname + window.location.search,
        );
        const [status, circleList] = await Promise.all([
          api.getStatus(),
          api.listCircles(),
        ]);
        const [selected, messageList] = circleList.circles[0]
          ? await Promise.all([
              api.getCircle(circleList.circles[0].id),
              api.getMessages(circleList.circles[0].id),
            ])
          : [null, null];
        if (active) {
          setWorkspace({
            status,
            circles: circleList.circles,
            selected,
            messages: messageList?.messages ?? [],
          });
        }
      } catch (reason) {
        if (active) setError(toMessage(reason));
      }
    })();
    return () => {
      active = false;
    };
  }, [api, capability]);

  async function createCircle(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!workspace || busy) return;
    const data = new FormData(event.currentTarget);
    const name = String(data.get("circleName") ?? "");
    const owner = String(data.get("ownerName") ?? "");
    setBusy(true);
    setError(null);
    try {
      const selected = await api.createCircle(name, owner);
      setWorkspace({
        ...workspace,
        circles: mergeCircle(workspace.circles, selected.circle),
        selected,
        messages: [],
      });
    } catch (reason) {
      setError(toMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  async function selectCircle(circleId: string) {
    if (!workspace || workspace.selected?.circle.id === circleId) return;
    setBusy(true);
    setError(null);
    try {
      const [selected, messageList] = await Promise.all([
        api.getCircle(circleId),
        api.getMessages(circleId),
      ]);
      setWorkspace({ ...workspace, selected, messages: messageList.messages });
    } catch (reason) {
      setError(toMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <a className="skip-link" href="#main-content">
        Skip to Circle
      </a>
      <div className="app-shell">
        <Masthead
          hasWorkspace={workspace !== null}
          hasCircle={workspace?.selected != null}
        />
        <main id="main-content">
          {!workspace && !error ? (
            <section className="loading-state" role="status">
              <p className="eyebrow">Local workspace</p>
              <h1>Opening Balls…</h1>
            </section>
          ) : null}

          {error && !workspace ? (
            <section className="error-state" role="alert">
              <p className="eyebrow">Local session unavailable</p>
              <h1>Run balls ui again</h1>
              <p>{error}</p>
            </section>
          ) : null}

          {workspace ? (
            <Workspace
              workspace={workspace}
              error={error}
              busy={busy}
              onCreate={createCircle}
              onSelect={selectCircle}
            />
          ) : null}
        </main>

        <footer>
          <span>Circle-first. Local by default.</span>
          {workspace ? <code>{workspace.status.node.id}</code> : null}
        </footer>
      </div>
    </>
  );
}

function Masthead({
  hasWorkspace,
  hasCircle,
}: {
  hasWorkspace: boolean;
  hasCircle: boolean;
}) {
  return (
    <header className="masthead">
      <a className="brand" href="#main-content" aria-label="Balls home">
        <BrandMark />
        <span>balls</span>
      </a>
      {hasWorkspace ? (
        <nav aria-label="Circle navigation">
          <a href="#circle" aria-current="page">
            Circle
          </a>
          {hasCircle ? <a href="#people">People</a> : null}
          {hasCircle ? <a href="#nodes">Nodes</a> : null}
          {hasCircle ? <a href="#messages">Messages</a> : null}
        </nav>
      ) : null}
      <span className="local-label" data-ready={hasWorkspace}>
        Local workspace
      </span>
    </header>
  );
}

interface WorkspaceProps {
  workspace: WorkspaceState;
  error: string | null;
  busy: boolean;
  onCreate: (event: FormEvent<HTMLFormElement>) => void;
  onSelect: (circleId: string) => void;
}

function Workspace({
  workspace,
  error,
  busy,
  onCreate,
  onSelect,
}: WorkspaceProps) {
  const dashboard = workspace.selected
    ? toDashboard(workspace.status, workspace.selected)
    : null;
  const statusSnapshot = {
    productVersion: workspace.status.productVersion,
    protocolVersion: Number(workspace.status.protocolVersion),
    localNode: {
      id: workspace.status.node.id,
      name: workspace.status.node.displayName,
      createdAtUtc: workspace.status.node.createdAtUtc,
    },
  };

  return (
    <>
      <StatusBanner snapshot={statusSnapshot} />
      {workspace.circles.length > 0 ? (
        <nav
          className="circle-switcher"
          aria-label="Your Circles"
          aria-busy={busy}
        >
          <span>Circles</span>
          {workspace.circles.map((circle) => (
            <button
              type="button"
              key={circle.id}
              aria-current={
                workspace.selected?.circle.id === circle.id ? "page" : undefined
              }
              disabled={busy}
              onClick={() => void onSelect(circle.id)}
            >
              {circle.name}
            </button>
          ))}
          {busy ? (
            <span className="switching-label" role="status">
              Switching Circle…
            </span>
          ) : null}
        </nav>
      ) : null}
      {error ? (
        <p className="inline-error" role="alert">
          {error}
        </p>
      ) : null}

      {dashboard ? (
        <CircleWorkspace dashboard={dashboard} messages={workspace.messages} />
      ) : (
        <EmptyWorkspace busy={busy} onCreate={onCreate} />
      )}
    </>
  );
}

function CircleWorkspace({
  dashboard,
  messages,
}: {
  dashboard: DashboardSnapshot;
  messages: CircleMessageDto[];
}) {
  const { circle } = dashboard;
  return (
    <>
      <section
        className="circle-intro"
        id="circle"
        aria-labelledby="circle-title"
      >
        <div>
          <p className="eyebrow">Circle home</p>
          <h1 id="circle-title">{circle.name}</h1>
          <p className="circle-thesis">
            A shared digital place owned by the people inside it.
          </p>
        </div>
        <dl className="circle-identity">
          <div>
            <dt>Circle ID</dt>
            <dd>{circle.id}</dd>
          </div>
          <div>
            <dt>Created</dt>
            <dd>
              <time dateTime={circle.createdAtUtc}>
                {formatDate(circle.createdAtUtc)}
              </time>
            </dd>
          </div>
        </dl>
      </section>
      <CircleTopology circle={circle} />
      <MessageHistory dashboard={dashboard} messages={messages} />
    </>
  );
}

function MessageHistory({
  dashboard,
  messages,
}: {
  dashboard: DashboardSnapshot;
  messages: CircleMessageDto[];
}) {
  const members = new Map(
    dashboard.circle.members.map((member) => [member.id, member.name]),
  );
  const nodes = new Map(
    dashboard.circle.nodes.map((node) => [node.id, node.name]),
  );
  return (
    <section
      className="message-history"
      id="messages"
      aria-labelledby="messages-title"
    >
      <div className="section-heading">
        <div>
          <p className="eyebrow">Durable Circle history</p>
          <h2 id="messages-title">Messages</h2>
        </div>
        <p>Authored by a Member on an admitted Node.</p>
      </div>
      {messages.length === 0 ? (
        <p className="message-empty">No messages yet.</p>
      ) : (
        <ol className="message-list">
          {messages.map((message) => (
            <li key={message.id}>
              <header>
                <strong>
                  {members.get(message.authorMemberId) ??
                    message.authorMemberId}
                  {" · "}
                  {nodes.get(message.authorNodeId) ?? message.authorNodeId}
                </strong>
                <span>#{message.sequence}</span>
              </header>
              <p>{message.text}</p>
              <time dateTime={message.authoredAtUtc}>
                {new Date(message.authoredAtUtc).toLocaleString()}
              </time>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}

function EmptyWorkspace({
  busy,
  onCreate,
}: {
  busy: boolean;
  onCreate: (event: FormEvent<HTMLFormElement>) => void;
}) {
  return (
    <section className="empty-workspace" id="circle">
      <div className="empty-intro">
        <BrandMark />
        <p className="eyebrow">Your first shared place</p>
        <h1>Create your first Circle</h1>
        <p>
          Start a shared digital place for your people. This Node keeps the
          Circle available on this device.
        </p>
      </div>
      <form aria-label="Create a Circle" aria-busy={busy} onSubmit={onCreate}>
        <label htmlFor="circle-name">Circle name</label>
        <input id="circle-name" name="circleName" maxLength={100} required />
        <label htmlFor="owner-name">Your display name</label>
        <input id="owner-name" name="ownerName" maxLength={100} required />
        <button type="submit" disabled={busy}>
          {busy ? "Creating…" : "Create Circle"}
        </button>
      </form>
    </section>
  );
}

function readLaunchCapability(fragment: string) {
  const prefix = "#launch=";
  if (!fragment.startsWith(prefix) || fragment.length <= prefix.length) {
    return null;
  }
  try {
    return decodeURIComponent(fragment.slice(prefix.length));
  } catch {
    return null;
  }
}

function mergeCircle(circles: CircleSummaryDto[], circle: CircleSummaryDto) {
  return [circle, ...circles.filter((candidate) => candidate.id !== circle.id)];
}

function toDashboard(
  status: StatusDto,
  details: CircleDetailsDto,
): DashboardSnapshot {
  return {
    productVersion: status.productVersion,
    protocolVersion: Number(status.protocolVersion),
    localNode: {
      id: status.node.id,
      name: status.node.displayName,
      createdAtUtc: status.node.createdAtUtc,
    },
    circle: {
      id: details.circle.id,
      name: details.circle.name,
      createdAtUtc: details.circle.createdAtUtc,
      members: details.members.map((member) => ({
        id: member.id,
        name: member.displayName,
        role: member.role,
        joinedAtUtc: member.joinedAtUtc,
      })),
      nodes: details.nodes.map((node) => ({
        id: node.id,
        name: node.displayName,
        joinedAtUtc: node.joinedAtUtc,
        isLocal: node.id === status.node.id,
      })),
    },
  };
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    year: "numeric",
    month: "long",
    day: "numeric",
  }).format(new Date(value));
}

function toMessage(reason: unknown) {
  return reason instanceof Error
    ? reason.message
    : "The local workspace could not be loaded.";
}
