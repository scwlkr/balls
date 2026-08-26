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
