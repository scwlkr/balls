import { useState } from "react";

import type {
  BrowserApi,
  RevitServerSetupInspectionDto,
} from "../api/browserApi";
import { toMessage } from "../presentation/toMessage";

export function RevitServerSetupPanel({ api }: { api: BrowserApi }) {
  const [busy, setBusy] = useState(false);
  const [fileName, setFileName] = useState<string | null>(null);
  const [result, setResult] = useState<RevitServerSetupInspectionDto | null>(
    null,
  );
  const [error, setError] = useState<string | null>(null);

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
      setResult(await api.inspectRevitServerSetup(selected.selectionId));
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
    </section>
  );
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
