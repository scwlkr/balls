import { useEffect, useState } from "react";

import type {
  BrowserApi,
  RevitServerSetupInspectionDto,
  RevitServerSetupStatusDto,
} from "../api/browserApi";
import { toMessage } from "../presentation/toMessage";

export function RevitServerSetupPanel({ api }: { api: BrowserApi }) {
  const [busy, setBusy] = useState(false);
  const [fileName, setFileName] = useState<string | null>(null);
  const [result, setResult] = useState<RevitServerSetupInspectionDto | null>(
    null,
  );
  const [selectionId, setSelectionId] = useState<string | null>(null);
  const [approved, setApproved] = useState(false);
  const [status, setStatus] = useState<RevitServerSetupStatusDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let active = true;
    api.getRevitServerSetupStatus().then(
      (value) => active && setStatus(value),
      () => undefined,
    );
    return () => {
      active = false;
    };
  }, [api]);

  useEffect(() => {
    if (
      status?.stage !== "applying-prerequisites" &&
      status?.stage !== "verifying"
    ) {
      return;
    }
    const timer = window.setInterval(() => {
      api.getRevitServerSetupStatus().then(setStatus, () => undefined);
    }, 1000);
    return () => window.clearInterval(timer);
  }, [api, status?.stage]);

  async function chooseAndInspect() {
    if (busy) return;
    setBusy(true);
    setError(null);
    setResult(null);
    try {
      const selected = await api.selectRevitServerMedia();
      if (selected.status === "cancelled" || !selected.selectionId) {
        setFileName(null);
        setError("No installer was selected. Nothing changed.");
        return;
      }
      setFileName(selected.fileName);
      setSelectionId(selected.selectionId);
      setApproved(false);
      setResult(await api.inspectRevitServerSetup(selected.selectionId));
    } catch (reason) {
      setError(toMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  async function begin() {
    if (!selectionId || !result?.plan || !approved || busy) return;
    setBusy(true);
    setError(null);
    try {
      setStatus(
        await api.beginRevitServerSetup(selectionId, result.plan.planDigest),
      );
    } catch (reason) {
      setError(toMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  async function verify() {
    if (busy) return;
    setBusy(true);
    setError(null);
    try {
      setStatus(await api.verifyRevitServerSetup());
    } catch (reason) {
      setError(toMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  async function retry() {
    if (busy) return;
    setBusy(true);
    setError(null);
    try {
      setStatus(await api.retryRevitServerSetup());
    } catch (reason) {
      setError(toMessage(reason));
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="revit-setup" aria-labelledby="revit-setup-title">
      <div className="revit-setup-intro">
        <div>
          <p className="eyebrow">Development</p>
          <h2 id="revit-setup-title">Set up Revit Server 2027</h2>
          <p>
            Check this server and official Autodesk media, then review the exact
            Host + Admin plan. This step is read-only.
          </p>
        </div>
        <button type="button" disabled={busy} onClick={chooseAndInspect}>
          {busy ? "Inspecting…" : "Choose official installer"}
        </button>
      </div>

      {error ? (
        <p className="revit-setup-message" role="status">
          {error}
        </p>
      ) : null}
      {fileName ? (
        <p className="revit-media-name">Selected: {fileName}</p>
      ) : null}
      {result ? <InspectionResult result={result} /> : null}
      {result?.status === "ready" &&
      result.plan &&
      (!status?.attemptId ||
        status.stage === "blocked" ||
        status.stage === "failed") ? (
        <div className="revit-setup-consent">
          <label>
            <input
              type="checkbox"
              checked={approved}
              onChange={(event) => setApproved(event.target.checked)}
            />
            I reviewed the Host + Admin plan and approve these Windows changes.
          </label>
          <button type="button" disabled={!approved || busy} onClick={begin}>
            Prepare Windows and open Autodesk setup
          </button>
        </div>
      ) : null}
      {status?.attemptId ? (
        <SetupProgress
          status={status}
          busy={busy}
          verify={verify}
          retry={retry}
        />
      ) : null}
    </section>
  );
}

function SetupProgress({
  status,
  busy,
  verify,
  retry,
}: {
  status: RevitServerSetupStatusDto;
  busy: boolean;
  verify(): void;
  retry(): void;
}) {
  const awaiting = status.stage === "awaiting-autodesk";
  const complete = status.stage === "ready-for-handoff";
  const retryable = status.stage === "incomplete";
  return (
    <div
      className="revit-setup-progress"
      data-stage={status.stage}
      role="status"
    >
      <strong>{complete ? "Healthy" : stageLabel(status.stage)}</strong>
      <p>{status.summary}</p>
      {awaiting ? (
        <div className="revit-autodesk-card">
          <h3>In Autodesk setup</h3>
          <ul>
            <li>Product: Revit Server 2027</li>
            <li>Roles: Host + Admin</li>
            <li>Accelerator: Off</li>
            {status.plan?.dataPaths.slice(1).map((path) => (
              <li key={path}>{path}</li>
            ))}
          </ul>
          <p>
            Accept Autodesk’s terms yourself and confirm its configuration page.
          </p>
          <button type="button" disabled={busy} onClick={verify}>
            Autodesk setup is finished — verify
          </button>
        </div>
      ) : null}
      {status.checks.length > 0 ? (
        <ul aria-label="Revit Server health checks">
          {status.checks.map((check) => (
            <li key={check.id} data-status={check.status}>
              {check.summary}
            </li>
          ))}
        </ul>
      ) : null}
      {retryable ? (
        <button type="button" disabled={busy} onClick={retry}>
          Open Autodesk setup again
        </button>
      ) : null}
    </div>
  );
}

function stageLabel(stage: RevitServerSetupStatusDto["stage"]) {
  switch (stage) {
    case "applying-prerequisites":
      return "Preparing Windows";
    case "prerequisites-applied":
      return "Windows prepared";
    case "awaiting-autodesk":
      return "Your turn in Autodesk setup";
    case "verifying":
      return "Verifying";
    case "incomplete":
      return "Incomplete";
    case "failed":
      return "Failed";
    case "blocked":
      return "Blocked";
    case "ready-for-handoff":
      return "Healthy";
    default:
      return "Not started";
  }
}

function InspectionResult({
  result,
}: {
  result: RevitServerSetupInspectionDto;
}) {
  const ready = result.status === "ready" && result.plan !== null;
  return (
    <div
      className="revit-inspection"
      data-status={ready ? "ready" : "blocked"}
      aria-label="Revit Server setup inspection"
    >
      <div className="revit-result-heading">
        <strong>{ready ? "Ready" : "Blocked"}</strong>
        <p>{result.summary}</p>
      </div>
      <ul className="revit-checks" aria-label="Readiness checks">
        {result.checks.map((check) => (
          <li key={check.id} data-status={check.status}>
            <strong>{check.status === "ready" ? "Ready" : "Blocked"}</strong>
            <span>{check.summary}</span>
          </li>
        ))}
      </ul>
      {result.plan ? <SetupPlan plan={result.plan} /> : null}
    </div>
  );
}

function SetupPlan({
  plan,
}: {
  plan: NonNullable<RevitServerSetupInspectionDto["plan"]>;
}) {
  const sections: Array<[string, string[]]> = [
    ["Roles", [`Host + Admin`, `Accelerator off`]],
    ["Data paths", plan.dataPaths],
    ["Windows and IIS prerequisites", plan.windowsPrerequisites],
    ["Folder access", plan.aclIntent],
    ["Default Web Site", plan.defaultWebSiteEffects],
    ["Server-local RSN.ini", plan.rsnIni],
    ["Private network only", plan.firewallEffects],
    ["Verification after installation", plan.verificationActions],
    ["Balls-owned state", plan.ballsOwnedState],
    ["Autodesk-owned state", plan.autodeskOwnedState],
  ];
  return (
    <div className="revit-plan" aria-label="Exact Host and Admin setup plan">
      <div className="revit-plan-identity">
        <div>
          <span>Server</span>
          <strong>{plan.machine}</strong>
          <small>{plan.windows}</small>
        </div>
        <div>
          <span>Verified media</span>
          <strong>{plan.media}</strong>
          <small>SHA-256 {plan.mediaSha256}</small>
        </div>
      </div>
      {sections.map(([heading, items]) => (
        <section key={heading}>
          <h3>{heading}</h3>
          <ul>
            {items.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </section>
      ))}
      <p className="revit-plan-digest">
        Plan identity <code>{plan.planDigest}</code>
      </p>
      <p className="revit-read-only">
        Review only — any machine, media, hostname, path, role, network, or
        prerequisite change requires a fresh inspection.
      </p>
    </div>
  );
}
