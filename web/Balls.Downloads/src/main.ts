import "@fontsource-variable/jetbrains-mono";
import "@fontsource-variable/manrope";
import "./style.css";

type RuntimeFramework = {
  name: string;
  major: number;
};

type RuntimeContract = {
  kind: "framework-dependent" | "self-contained";
  architecture: string;
  frameworks?: RuntimeFramework[];
};

type ChannelManifest = {
  platforms?: {
    "windows-x64"?: {
      runtime?: RuntimeContract;
    };
  };
};

type ReleaseEntry = {
  tag: string;
  publishedAt: string;
  manifest: string;
  knownBroken?: boolean;
  note?: string;
};

type ReleaseCatalog = {
  schemaVersion: number;
  accepted: ReleaseEntry[];
  development: ReleaseEntry[];
  completeHistory: string;
};

const releaseTagPattern = /^[0-9A-Za-z][0-9A-Za-z._-]{0,127}$/;

function isReleaseEntry(value: unknown): value is ReleaseEntry {
  if (!value || typeof value !== "object") {
    return false;
  }

  const entry = value as Partial<ReleaseEntry>;
  return (
    typeof entry.tag === "string" &&
    releaseTagPattern.test(entry.tag) &&
    typeof entry.publishedAt === "string" &&
    Number.isFinite(Date.parse(entry.publishedAt)) &&
    entry.manifest === `/versions/${entry.tag}.json` &&
    (entry.knownBroken === undefined ||
      typeof entry.knownBroken === "boolean") &&
    (entry.note === undefined ||
      (typeof entry.note === "string" && entry.note.length <= 240))
  );
}

function parseReleaseCatalog(value: unknown): ReleaseCatalog | null {
  if (!value || typeof value !== "object") {
    return null;
  }

  const catalog = value as Partial<ReleaseCatalog>;
  if (
    catalog.schemaVersion !== 1 ||
    !Array.isArray(catalog.accepted) ||
    !Array.isArray(catalog.development) ||
    !catalog.accepted.every(isReleaseEntry) ||
    !catalog.development.every(isReleaseEntry) ||
    catalog.completeHistory !== "https://github.com/scwlkr/balls/releases"
  ) {
    return null;
  }

  const identities = [...catalog.accepted, ...catalog.development].map(
    (entry) => entry.tag,
  );
  if (new Set(identities).size !== identities.length) {
    return null;
  }

  return catalog as ReleaseCatalog;
}

function windowsInstallCommand(manifestPath?: string): string {
  const manifestArgument = manifestPath
    ? ` -ManifestUri 'https://balls.wlkrlabs.com${manifestPath}'`
    : "";
  return `Invoke-WebRequest https://balls.wlkrlabs.com/install.ps1 -OutFile "$env:TEMP\\install-balls.ps1"; powershell.exe -NoLogo -NoProfile -File "$env:TEMP\\install-balls.ps1"${manifestArgument}`;
}

function appendHistoryRow(
  body: HTMLTableSectionElement,
  entry: ReleaseEntry,
  channel: "alpha" | "development",
): void {
  const row = body.insertRow();

  const versionCell = row.insertCell();
  const version = document.createElement("strong");
  version.textContent = entry.tag;
  versionCell.append(version);
  if (entry.knownBroken || entry.note) {
    const note = document.createElement("span");
    note.className = "known-broken";
    note.textContent = entry.note ?? "Known broken";
    versionCell.append(note);
  }

  const channelCell = row.insertCell();
  const badge = document.createElement("span");
  badge.className = `channel-badge ${channel}`;
  badge.textContent = channel === "alpha" ? "Accepted Alpha" : "Development";
  channelCell.append(badge);

  const dateCell = row.insertCell();
  const published = document.createElement("time");
  published.dateTime = entry.publishedAt;
  published.textContent = new Intl.DateTimeFormat("en-US", {
    dateStyle: "medium",
  }).format(new Date(entry.publishedAt));
  dateCell.append(published);

  const installCell = row.insertCell();
  const details = document.createElement("details");
  const summary = document.createElement("summary");
  summary.textContent = "Windows command";
  const command = document.createElement("code");
  command.textContent = windowsInstallCommand(entry.manifest);
  const manifest = document.createElement("a");
  manifest.href = entry.manifest;
  manifest.textContent = "Version manifest";
  details.append(summary, command, manifest);
  installCell.append(details);
}

function renderReleaseCatalog(catalog: ReleaseCatalog): void {
  const history = document.querySelector<HTMLTableSectionElement>(
    "[data-release-history]",
  );
  if (history) {
    history.replaceChildren();
    for (const entry of catalog.accepted) {
      appendHistoryRow(history, entry, "alpha");
    }
    for (const entry of catalog.development.slice(0, 10)) {
      appendHistoryRow(history, entry, "development");
    }
  }

  const historyLink = document.querySelector<HTMLAnchorElement>(
    "[data-complete-history]",
  );
  if (historyLink) {
    historyLink.href = catalog.completeHistory;
  }

  const latestDevelopment = catalog.development[0];
  if (!latestDevelopment) {
    return;
  }

  const empty = document.querySelector<HTMLElement>("[data-development-empty]");
  const release = document.querySelector<HTMLElement>(
    "[data-development-release]",
  );
  const tag = document.querySelector<HTMLElement>("[data-development-tag]");
  const summary = document.querySelector<HTMLElement>(
    "[data-development-summary]",
  );
  const command = document.getElementById("development-windows-command");
  const manifest = document.querySelector<HTMLAnchorElement>(
    "[data-development-manifest]",
  );
  if (!empty || !release || !tag || !summary || !command || !manifest) {
    return;
  }

  empty.hidden = true;
  release.hidden = false;
  tag.textContent = latestDevelopment.tag;
  summary.textContent =
    latestDevelopment.note ??
    "This identified build may be incomplete or broken.";
  command.textContent = windowsInstallCommand("/channels/development.json");
  manifest.href = latestDevelopment.manifest;
}

function describeWindowsRuntime(runtime?: RuntimeContract): string | null {
  if (!runtime || runtime.architecture !== "x64") {
    return null;
  }
  if (runtime.kind === "self-contained") {
    return "";
  }
  if (
    runtime.kind !== "framework-dependent" ||
    !Array.isArray(runtime.frameworks) ||
    runtime.frameworks.length === 0
  ) {
    return null;
  }

  const labels: string[] = [];
  for (const framework of runtime.frameworks) {
    if (
      !/^[A-Za-z][A-Za-z0-9.]{0,127}$/.test(framework.name) ||
      !Number.isInteger(framework.major) ||
      framework.major < 1 ||
      framework.major > 999
    ) {
      return null;
    }

    const displayName =
      framework.name === "Microsoft.NETCore.App"
        ? ".NET"
        : framework.name === "Microsoft.AspNetCore.App"
          ? "ASP.NET Core"
          : framework.name;
    labels.push(`${displayName} ${framework.major}`);
  }
  return ` · ${labels.join(" + ")}`;
}

const runtimeRequirements = document.querySelector<HTMLElement>(
  "[data-windows-runtime-requirements]",
);

if (runtimeRequirements) {
  void (async () => {
    try {
      const response = await fetch("/channels/alpha.json", {
        cache: "no-store",
        headers: { accept: "application/json" },
      });
      if (!response.ok) {
        return;
      }

      const manifest = (await response.json()) as ChannelManifest;
      const description = describeWindowsRuntime(
        manifest.platforms?.["windows-x64"]?.runtime,
      );
      if (description !== null) {
        runtimeRequirements.textContent = description;
      }
    } catch {
      // Keep the truthful generic fallback when the manifest is unavailable.
    }
  })();
}

void (async () => {
  try {
    const response = await fetch("/releases.json", {
      cache: "no-store",
      headers: { accept: "application/json" },
    });
    if (!response.ok) {
      return;
    }

    const catalog = parseReleaseCatalog(await response.json());
    if (catalog) {
      renderReleaseCatalog(catalog);
    }
  } catch {
    // The static accepted-release rows remain usable when the catalog is unavailable.
  }
})();

const copyButtons = document.querySelectorAll<HTMLButtonElement>(
  "button[data-copy-target]",
);

for (const button of copyButtons) {
  const targetId = button.dataset.copyTarget;
  const target = targetId ? document.getElementById(targetId) : null;
  const label = button.querySelector<HTMLElement>("[data-copy-label]");

  if (!target || !label) {
    continue;
  }

  button.addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(target.textContent ?? "");
      label.textContent = "Copied";
    } catch {
      const selection = window.getSelection();
      const range = document.createRange();
      range.selectNodeContents(target);
      selection?.removeAllRanges();
      selection?.addRange(range);
      label.textContent = "Select and copy";
    }
  });
}
