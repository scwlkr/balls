import { useEffect, useRef, useState, type FormEvent } from "react";

import { browserApi, type BrowserApi } from "./api/browserApi";
import {
  decodeInvitationCode,
  encodeInvitationCode,
} from "./api/invitationCode";
import type {
  CircleDetailsDto,
  CircleMessageDto,
  CircleSummaryDto,
  CircleViewerDto,
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
  viewer: CircleViewerDto | null;
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
        const [selected, messageList, viewer] = circleList.circles[0]
          ? await Promise.all([
              api.getCircle(circleList.circles[0].id),
              api.getMessages(circleList.circles[0].id),
              api.getViewer(circleList.circles[0].id),
            ])
          : [null, null, null];
        if (active) {
          setWorkspace({
            status,
            circles: circleList.circles,
            selected,
            viewer,
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
      const viewer = await api.getViewer(selected.circle.id);
      setWorkspace({
        ...workspace,
        circles: mergeCircle(workspace.circles, selected.circle),
        selected,
        viewer,
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
      const [selected, messageList, viewer] = await Promise.all([
        api.getCircle(circleId),
        api.getMessages(circleId),
        api.getViewer(circleId),
      ]);
      setWorkspace({
        ...workspace,
        selected,
        viewer,
        messages: messageList.messages,
      });
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
        invitation.provider,
        invitation.endpoint,
        invitation.syncEndpoint,
        name,
      );
      const viewer = await api.getViewer(selected.circle.id);
      setWorkspace({
        ...workspace,
        circles: mergeCircle(workspace.circles, selected.circle),
        selected,
        viewer,
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
  onCreate: (event: FormEvent<HTMLFormElement>) => void;
  onJoin: (event: FormEvent<HTMLFormElement>) => void;
  onSelect: (circleId: string) => void;
}

function Workspace({
  api,
  workspace,
  error,
  busy,
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

      {dashboard && workspace.viewer ? (
        <CircleWorkspace
          api={api}
          dashboard={dashboard}
          viewer={workspace.viewer}
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
  viewer,
  messages,
  api,
}: {
  dashboard: DashboardSnapshot;
  viewer: CircleViewerDto;
  messages: CircleMessageDto[];
  api: BrowserApi;
}) {
  const { circle } = dashboard;
  const [filesRevision, setFilesRevision] = useState(0);
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
      {viewer.role === "owner" ? (
        <>
          <OwnerContributionPanel
            api={api}
            circleId={circle.id}
            onContributed={() => setFilesRevision((value) => value + 1)}
          />
          <OwnerGrantPanel
            key={`${circle.id}:${filesRevision}`}
            api={api}
            dashboard={dashboard}
            circleId={circle.id}
          />
          <InvitationPanel api={api} circleId={circle.id} />
        </>
      ) : null}
      {viewer.role === "member" ? (
        <FilesMappingPanel
          key={`${circle.id}:${filesRevision}`}
          api={api}
          viewer={viewer}
          circleId={circle.id}
        />
      ) : null}
      <MessageHistory dashboard={dashboard} messages={messages} />
    </>
  );
}

function OwnerGrantPanel({
  api,
  dashboard,
  circleId,
}: {
  api: BrowserApi;
  dashboard: DashboardSnapshot;
  circleId: string;
}) {
  const members = dashboard.circle.members.filter(
    (member) => member.role === "member",
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
    setPreview(undefined);
    setStatus(null);
    setError(null);
  }

  function changeMember(value: string) {
    setMemberName(value);
    setPreview(undefined);
    setStatus(null);
    setError(null);
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
      {folders.length === 0 || members.length === 0 ? (
        <p className="message-empty">
          {folders.length === 0
            ? "Contribute a folder before sharing it."
            : "Invite someone and wait for them to join before sharing the folder."}
        </p>
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

function OwnerContributionPanel({
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
        !result.folderPath ||
        !result.displayName
      ) {
        setSelection(null);
        setStatus("No folder was selected. Nothing changed.");
        return;
      }
      setSelection({
        requestId: crypto.randomUUID(),
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
        selection.folderPath,
        selection.displayName,
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

function InvitationPanel({
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

function FilesMappingPanel({
  api,
  viewer,
  circleId,
}: {
  api: BrowserApi;
  viewer: CircleViewerDto;
  circleId: string;
}) {
  const [contributions, setContributions] = useState<
    Awaited<ReturnType<BrowserApi["listFilesContributions"]>>["contributions"]
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
          if (available.length === 0) {
            retry();
          }
        }
      } catch (reason) {
        if (!active) return;
        if (shouldSynchronize && attempt < 15) {
          retry();
        } else {
          setPanelError(toMessage(reason));
        }
      }
    }

    void load();
    return () => {
      active = false;
      if (timer !== undefined) window.clearTimeout(timer);
    };
  }, [api, circleId, refreshRequest, viewer.memberId, viewer.role]);

  async function chooseContribution(value: string) {
    setContributionId(value);
    setGrantId("");
    setPanelError(null);
    if (!value) return;
    try {
      const result = await api.listFilesGrants(circleId, value);
      const available = result.grants.filter(
        (grant) =>
          viewer.role === "owner" || grant.memberId === viewer.memberId,
      );
      setGrantId(available[0]?.id ?? "");
    } catch (reason) {
      setPanelError(toMessage(reason));
    }
  }

  async function openSharedFolder() {
    if (!contributionId || !grantId || panelBusy) return;
    setPanelBusy(true);
    setPanelError(null);
    setMappingStatus(null);
    try {
      const available = await api.previewFilesMapping(
        circleId,
        contributionId,
        grantId,
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
        selectedDrive,
      );
      await api.mapFiles(
        circleId,
        contributionId,
        grantId,
        selectedDrive,
        exactPlan.planId,
      );
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
              onClick={() => setRefreshRequest((value) => value + 1)}
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
          <label htmlFor="files-contribution">Approved folder</label>
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
