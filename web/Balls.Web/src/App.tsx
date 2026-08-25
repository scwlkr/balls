import { useEffect, useRef, useState, type FormEvent } from "react";

import { browserApi, type BrowserApi } from "./api/browserApi";
import {
  decodeInvitationCode,
  encodeInvitationCode,
  invitationHostAddress,
} from "./api/invitationCode";
import type {
  CircleFilesMemberMappingPlanDto,
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
  const [fileHosts, setFileHosts] = useState<Record<string, string>>({});

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

  async function joinCircle(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!workspace || busy) return;
    const data = new FormData(event.currentTarget);
    const invitationCode = String(data.get("invitationCode") ?? "");
    const name = String(data.get("memberName") ?? "");
    setBusy(true);
    setError(null);
    try {
      const invitation = decodeInvitationCode(invitationCode);
      const selected = await api.joinCircle(
        invitation.package,
        invitation.endpoint,
        name,
      );
      const host = invitationHostAddress(invitation.endpoint);
      if (host) {
        setFileHosts((current) => ({ ...current, [selected.circle.id]: host }));
        window.sessionStorage.setItem(
          `balls:file-host:${selected.circle.id}`,
          host,
        );
      }
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
              api={api}
              workspace={workspace}
              error={error}
              busy={busy}
              fileHosts={fileHosts}
              onCreate={createCircle}
              onJoin={joinCircle}
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
  api: BrowserApi;
  workspace: WorkspaceState;
  error: string | null;
  busy: boolean;
  fileHosts: Record<string, string>;
  onCreate: (event: FormEvent<HTMLFormElement>) => void;
  onJoin: (event: FormEvent<HTMLFormElement>) => void;
  onSelect: (circleId: string) => void;
}

function Workspace({
  api,
  workspace,
  error,
  busy,
  fileHosts,
  onCreate,
  onJoin,
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
        <CircleWorkspace
          api={api}
          dashboard={dashboard}
          fileHost={fileHosts[dashboard.circle.id]}
          messages={workspace.messages}
        />
      ) : (
        <EmptyWorkspace busy={busy} onCreate={onCreate} onJoin={onJoin} />
      )}
    </>
  );
}

function CircleWorkspace({
  dashboard,
  fileHost,
  messages,
  api,
}: {
  dashboard: DashboardSnapshot;
  fileHost?: string;
  messages: CircleMessageDto[];
  api: BrowserApi;
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
      {circle.nodes[0]?.id === dashboard.localNode.id ? (
        <InvitationPanel api={api} circleId={circle.id} />
      ) : null}
      <FilesMappingPanel
        key={circle.id}
        api={api}
        dashboard={dashboard}
        circleId={circle.id}
        initialEndpoint={
          fileHost ??
          window.sessionStorage.getItem(`balls:file-host:${circle.id}`) ??
          ""
        }
      />
      <MessageHistory dashboard={dashboard} messages={messages} />
    </>
  );
}

function InvitationPanel({
  api,
  circleId,
}: {
  api: BrowserApi;
  circleId: string;
}) {
  const [hostAddress, setHostAddress] = useState("");
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
      const invitation = await api.createInvitation(circleId, hostAddress);
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
        <details className="invitation-advanced">
          <summary>Advanced network settings</summary>
          <label htmlFor="invitation-host-address">
            Reachable private host address
          </label>
          <input
            id="invitation-host-address"
            value={hostAddress}
            placeholder="192.168.1.20"
            disabled={busy}
            onChange={(event) => setHostAddress(event.target.value)}
          />
          <p>
            Only set this when the server is behind a VM or port-forwarding
            rule.
          </p>
        </details>
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

function FilesMappingPanel({
  api,
  dashboard,
  circleId,
  initialEndpoint,
}: {
  api: BrowserApi;
  dashboard: DashboardSnapshot;
  circleId: string;
  initialEndpoint: string;
}) {
  const [contributions, setContributions] = useState<
    Awaited<ReturnType<BrowserApi["listFilesContributions"]>>["contributions"]
  >([]);
  const [grants, setGrants] = useState<
    Awaited<ReturnType<BrowserApi["listFilesGrants"]>>["grants"]
  >([]);
  const [contributionId, setContributionId] = useState("");
  const [grantId, setGrantId] = useState("");
  const [endpoint, setEndpoint] = useState(initialEndpoint);
  const [driveLetter, setDriveLetter] = useState("");
  const [plan, setPlan] = useState<CircleFilesMemberMappingPlanDto | null>(
    null,
  );
  const [planContext, setPlanContext] = useState<{
    contributionId: string;
    grantId: string;
    endpoint: string;
    driveLetter: string;
  } | null>(null);
  const [mappingStatus, setMappingStatus] = useState<string | null>(null);
  const [panelError, setPanelError] = useState<string | null>(null);
  const [panelBusy, setPanelBusy] = useState(false);
  const guided = initialEndpoint.length > 0;
  const memberNames = new Map(
    dashboard.circle.members.map((member) => [member.id, member.name]),
  );

  useEffect(() => {
    let active = true;
    void api
      .listFilesContributions(circleId)
      .then(async (result) => {
        if (!active) return;
        setContributions(result.contributions);
        const first = result.contributions[0];
        setContributionId(first?.id ?? "");
        if (first) {
          const grantList = await api.listFilesGrants(circleId, first.id);
          if (active) {
            setGrants(grantList.grants);
            setGrantId(grantList.grants[0]?.id ?? "");
          }
        }
      })
      .catch((reason) => {
        if (active) setPanelError(toMessage(reason));
      });
    return () => {
      active = false;
    };
  }, [api, circleId]);

  async function chooseContribution(value: string) {
    setContributionId(value);
    setGrantId("");
    setPlan(null);
    setPlanContext(null);
    setPanelError(null);
    if (!value) return;
    try {
      const result = await api.listFilesGrants(circleId, value);
      setGrants(result.grants);
      setGrantId(result.grants[0]?.id ?? "");
    } catch (reason) {
      setPanelError(toMessage(reason));
    }
  }

  async function discover() {
    if (!contributionId || !grantId || !endpoint || panelBusy) return;
    setPanelBusy(true);
    setPanelError(null);
    setPlan(null);
    setPlanContext(null);
    setDriveLetter("");
    try {
      const result = await api.previewFilesMapping(
        circleId,
        contributionId,
        grantId,
        endpoint,
        "",
      );
      setPlan(result);
      setPlanContext({ contributionId, grantId, endpoint, driveLetter: "" });
    } catch (reason) {
      setPanelError(toMessage(reason));
    } finally {
      setPanelBusy(false);
    }
  }

  async function previewSelected() {
    if (!driveLetter || panelBusy) return;
    setPanelBusy(true);
    setPanelError(null);
    try {
      const result = await api.previewFilesMapping(
        circleId,
        contributionId,
        grantId,
        endpoint,
        driveLetter,
      );
      setPlan(result);
      setPlanContext({ contributionId, grantId, endpoint, driveLetter });
      setMappingStatus("ready to map");
    } catch (reason) {
      setPanelError(toMessage(reason));
    } finally {
      setPanelBusy(false);
    }
  }

  async function mutate(operation: "map" | "inspect" | "unmap") {
    if (
      !plan ||
      !driveLetter ||
      panelBusy ||
      plan.driveLetter !== driveLetter ||
      planContext?.contributionId !== contributionId ||
      planContext.grantId !== grantId ||
      planContext.endpoint !== endpoint ||
      planContext.driveLetter !== driveLetter
    )
      return;
    setPanelBusy(true);
    setPanelError(null);
    try {
      const result =
        operation === "map"
          ? await api.mapFiles(
              circleId,
              contributionId,
              grantId,
              endpoint,
              driveLetter,
              plan.planId,
            )
          : operation === "inspect"
            ? await api.inspectFilesMapping(
                circleId,
                contributionId,
                grantId,
                endpoint,
                driveLetter,
              )
            : await api.unmapFiles(
                circleId,
                contributionId,
                grantId,
                endpoint,
                driveLetter,
              );
      setMappingStatus(result.status);
    } catch (reason) {
      setPanelError(toMessage(reason));
    } finally {
      setPanelBusy(false);
    }
  }

  async function openSharedFolder() {
    if (!contributionId || !grantId || !endpoint || panelBusy) return;
    setPanelBusy(true);
    setPanelError(null);
    setMappingStatus(null);
    try {
      const available = await api.previewFilesMapping(
        circleId,
        contributionId,
        grantId,
        endpoint,
        "",
      );
      const selectedDrive = available.availableDriveLetters.includes("P")
        ? "P"
        : available.availableDriveLetters[0];
      if (!selectedDrive) {
        throw new Error("No drive letters are available on this computer.");
      }
      const exactPlan = await api.previewFilesMapping(
        circleId,
        contributionId,
        grantId,
        endpoint,
        selectedDrive,
      );
      await api.mapFiles(
        circleId,
        contributionId,
        grantId,
        endpoint,
        selectedDrive,
        exactPlan.planId,
      );
      setDriveLetter(selectedDrive);
      setMappingStatus(
        `Shared folder ready in File Explorer (${selectedDrive}:).`,
      );
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
          <p className="eyebrow">Circle Files</p>
          <h2 id="files-title">Open in Explorer</h2>
        </div>
        <p>The limited password stays inside this device.</p>
      </div>
      {contributions.length === 0 ? (
        <p className="message-empty">
          No contributed folders are available yet.
        </p>
      ) : (
        <form
          aria-label="Map Circle Files"
          aria-busy={panelBusy}
          onSubmit={(event) => event.preventDefault()}
        >
          <label htmlFor="files-contribution">Folder</label>
          <select
            id="files-contribution"
            disabled={panelBusy}
            value={contributionId}
            onChange={(event) => void chooseContribution(event.target.value)}
          >
            {contributions.map((value) => (
              <option key={value.id} value={value.id}>
                {value.displayName}
              </option>
            ))}
          </select>
          {!guided || grants.length > 1 ? (
            <>
              <label htmlFor="files-grant">Grant</label>
              <select
                id="files-grant"
                disabled={panelBusy}
                value={grantId}
                onChange={(event) => {
                  setGrantId(event.target.value);
                  setPlan(null);
                  setPlanContext(null);
                  setMappingStatus(null);
                }}
              >
                {grants.map((value) => (
                  <option key={value.id} value={value.id}>
                    {value.access} ·{" "}
                    {memberNames.get(value.memberId) ?? "Circle member"}
                  </option>
                ))}
              </select>
            </>
          ) : null}
          {guided ? (
            <button
              type="button"
              disabled={panelBusy || !grantId}
              onClick={() => void openSharedFolder()}
            >
              {panelBusy ? "Connecting…" : "Open shared folder in Explorer"}
            </button>
          ) : (
            <>
              <label htmlFor="files-endpoint">Private host IPv4 address</label>
              <input
                id="files-endpoint"
                value={endpoint}
                placeholder="192.168.1.20"
                required
                disabled={panelBusy}
                onChange={(event) => {
                  setEndpoint(event.target.value);
                  setPlan(null);
                  setPlanContext(null);
                  setMappingStatus(null);
                }}
              />
              <button
                type="button"
                disabled={panelBusy || !grantId || !endpoint}
                onClick={() => void discover()}
              >
                Find available drive letters
              </button>
            </>
          )}
          {!guided && plan ? (
            <>
              <label htmlFor="files-drive">Drive letter</label>
              <select
                id="files-drive"
                required
                disabled={panelBusy}
                value={driveLetter}
                onChange={(event) => {
                  setDriveLetter(event.target.value);
                  setPlan((current) =>
                    current ? { ...current, driveLetter: "" } : current,
                  );
                  setPlanContext(null);
                  setMappingStatus(null);
                }}
              >
                <option value="">Choose a drive letter</option>
                {plan.availableDriveLetters.map((value) => (
                  <option key={value} value={value}>
                    {value}:
                  </option>
                ))}
              </select>
              <button
                type="button"
                disabled={panelBusy || !driveLetter}
                onClick={() => void previewSelected()}
              >
                Preview exact mapping
              </button>
              {plan.driveLetter &&
              plan.driveLetter === driveLetter &&
              planContext?.contributionId === contributionId &&
              planContext.grantId === grantId &&
              planContext.endpoint === endpoint &&
              planContext.driveLetter === driveLetter ? (
                <div>
                  <p>
                    <strong>
                      {plan.driveLetter}: → {plan.uncPath}
                    </strong>
                  </p>
                  <button
                    type="button"
                    disabled={panelBusy}
                    onClick={() => void mutate("map")}
                  >
                    Map in Explorer
                  </button>{" "}
                  <button
                    type="button"
                    disabled={panelBusy}
                    onClick={() => void mutate("inspect")}
                  >
                    Inspect
                  </button>{" "}
                  <button
                    type="button"
                    disabled={panelBusy}
                    onClick={() => void mutate("unmap")}
                  >
                    Unmap
                  </button>
                </div>
              ) : null}
            </>
          ) : null}
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
  onJoin,
}: {
  busy: boolean;
  onCreate: (event: FormEvent<HTMLFormElement>) => void;
  onJoin: (event: FormEvent<HTMLFormElement>) => void;
}) {
  const [mode, setMode] = useState<"create" | "join">("create");
  const joining = mode === "join";
  return (
    <section className="empty-workspace" id="circle">
      <div className="empty-intro">
        <BrandMark />
        <p className="eyebrow">Your first shared place</p>
        <h1>{joining ? "Join your Circle" : "Create your first Circle"}</h1>
        <p>
          {joining
            ? "Paste the private invitation you received to connect with your people and shared files."
            : "Start a shared digital place for your people. This Node keeps the Circle available on this device."}
        </p>
        <div
          className="onboarding-choice"
          aria-label="Choose how to get started"
        >
          <button
            type="button"
            aria-pressed={!joining}
            disabled={busy}
            onClick={() => setMode("create")}
          >
            Create a Circle
          </button>
          <button
            type="button"
            aria-pressed={joining}
            disabled={busy}
            onClick={() => setMode("join")}
          >
            Join a Circle
          </button>
        </div>
      </div>
      {joining ? (
        <form aria-label="Join a Circle" aria-busy={busy} onSubmit={onJoin}>
          <label htmlFor="join-invitation">Your invitation</label>
          <textarea
            id="join-invitation"
            name="invitationCode"
            rows={4}
            required
            placeholder="Paste the invitation you received"
          />
          <label htmlFor="join-member-name">Your name</label>
          <input
            id="join-member-name"
            name="memberName"
            maxLength={100}
            required
          />
          <button type="submit" disabled={busy}>
            {busy ? "Joining…" : "Join Circle"}
          </button>
        </form>
      ) : (
        <form aria-label="Create a Circle" aria-busy={busy} onSubmit={onCreate}>
          <label htmlFor="circle-name">Circle name</label>
          <input id="circle-name" name="circleName" maxLength={100} required />
          <label htmlFor="owner-name">Your display name</label>
          <input id="owner-name" name="ownerName" maxLength={100} required />
          <button type="submit" disabled={busy}>
            {busy ? "Creating…" : "Create Circle"}
          </button>
        </form>
      )}
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
