import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { readFile } from "node:fs/promises";
import test from "node:test";
import { fileURLToPath } from "node:url";

const siteRoot = new URL("../", import.meta.url);

async function render(path = "/") {
  const html = await readFile(
    new URL("../dist/client/index.html", import.meta.url),
  );
  const workerUrl = new URL("../dist/server/index.js", import.meta.url);
  workerUrl.searchParams.set("test", `${process.pid}-${Date.now()}`);
  const { default: worker } = await import(workerUrl.href);

  return worker.fetch(
    new Request(new URL(path, "https://balls.wlkrlabs.com")),
    {
      ASSETS: {
        fetch: async (request) => {
          const url = new URL(request.url);
          return url.pathname === "/"
            ? new Response(html, {
                headers: { "content-type": "text/html; charset=utf-8" },
              })
            : new Response("Not found", { status: 404 });
        },
      },
    },
  );
}

test("presents one stable command for every supported delivery lane", async () => {
  const response = await render();
  assert.equal(response.status, 200);
  assert.match(response.headers.get("content-type") ?? "", /^text\/html\b/i);

  const html = await response.text();
  assert.match(html, /<title>Download Balls<\/title>/i);
  assert.match(html, /One command\. The newest published Balls Alpha\./i);
  assert.match(html, /Windows/i);
  assert.match(html, /Linux/i);
  assert.match(html, /macOS/i);
  assert.match(html, /og\.png/i);
  assert.match(html, /https:\/\/balls\.wlkrlabs\.com\/install\.ps1/);
  assert.match(html, /https:\/\/balls\.wlkrlabs\.com\/install\.sh/);
  assert.match(html, /https:\/\/balls\.wlkrlabs\.com\/source\.sh/);
  assert.match(
    html,
    /Windows x64 · Windows PowerShell 5\.1\+[\s\S]*· runtime checked from manifest/i,
  );
  assert.match(html, /powershell\.exe -NoLogo -NoProfile -File/);
  assert.doesNotMatch(html, /\bpwsh\b/);
  assert.match(html, /unsigned prerelease/i);
  assert.match(html, /macOS is source-only/i);
  assert.match(html, /Testing software\. It may be broken\./i);
  assert.match(html, /Previous versions/i);
  assert.match(html, /newest ten Development builds/i);
  assert.match(html, /https:\/\/github\.com\/scwlkr\/balls\/releases/);
  assert.match(html, /\/versions\/0\.3\.0-alpha\.1\.json/);
  assert.doesNotMatch(html, /automatic updates/i);
  assert.doesNotMatch(html, /codex-preview/i);
});

test("enforces the public security policy through the Worker", async () => {
  const response = await render();

  assert.equal(
    response.headers.get("content-security-policy"),
    "default-src 'self'; base-uri 'none'; connect-src 'self'; font-src 'self'; form-action 'none'; frame-ancestors 'none'; img-src 'self'; object-src 'none'; script-src 'self'; style-src 'self'",
  );
  assert.equal(
    response.headers.get("cross-origin-opener-policy"),
    "same-origin",
  );
  assert.equal(
    response.headers.get("permissions-policy"),
    "clipboard-write=(self)",
  );
  assert.equal(response.headers.get("referrer-policy"), "no-referrer");
  assert.equal(
    response.headers.get("strict-transport-security"),
    "max-age=31536000; includeSubDomains",
  );
  assert.equal(response.headers.get("x-content-type-options"), "nosniff");
  assert.equal(response.headers.get("x-frame-options"), "DENY");
});

function assertPackageManifest(manifest, expectedChannel) {
  assert.equal(manifest.schemaVersion, 1);
  assert.equal(manifest.channel, expectedChannel);
  assert.match(manifest.release.tag, /^[0-9A-Za-z][0-9A-Za-z._-]{0,127}$/);
  assert.match(manifest.release.commit, /^[0-9a-f]{40}$/);
  assert.equal(manifest.release.unsigned, true);
  assert.equal(
    manifest.release.url,
    `https://github.com/scwlkr/balls/releases/tag/${manifest.release.tag}`,
  );

  const windows = manifest.platforms["windows-x64"];
  assert.equal(windows.delivery, "package");
  assert.deepEqual(windows.identity, {
    product: "Balls",
    version: windows.identity.version,
    commit: manifest.release.commit,
    platform: "windows",
    architecture: "x64",
  });
  assert.match(windows.identity.version, /^[0-9A-Za-z][0-9A-Za-z._-]{0,127}$/);
  const windowsRuntime = windows.runtime;
  assert.match(windowsRuntime.kind, /^(framework-dependent|self-contained)$/);
  assert.equal(windowsRuntime.architecture, "x64");
  if (windowsRuntime.kind === "framework-dependent") {
    assert.ok(windowsRuntime.frameworks.length > 0);
    for (const framework of windowsRuntime.frameworks) {
      assert.match(framework.name, /^[A-Za-z][A-Za-z0-9.]{0,127}$/);
      assert.ok(Number.isInteger(framework.major));
      assert.ok(framework.major >= 1 && framework.major <= 999);
    }
  }

  for (const [platform, delivery] of Object.entries(manifest.platforms)) {
    if (delivery.delivery !== "package") {
      continue;
    }
    assert.equal(delivery.identity.product, "Balls");
    assert.equal(delivery.identity.commit, manifest.release.commit);
    assert.equal(
      delivery.identity.platform,
      platform === "windows-x64" ? "windows" : "linux",
    );
    assert.equal(delivery.identity.architecture, "x64");
    assert.match(
      delivery.archive.name,
      new RegExp(
        `^balls-${delivery.identity.version.replaceAll(".", "\\.")}-canary-${delivery.identity.platform}-x64-${manifest.release.commit.slice(0, 12)}\\.zip$`,
      ),
    );
    for (const asset of [
      delivery.archive,
      delivery.checksum,
      delivery.installer,
    ]) {
      assert.match(asset.sha256, /^[0-9a-f]{64}$/);
      const url = new URL(asset.url);
      assert.equal(url.protocol, "https:");
      assert.equal(url.hostname, "github.com");
      assert.match(
        url.pathname,
        new RegExp(`^/scwlkr/balls/releases/download/${manifest.release.tag}/`),
      );
    }
  }
}

test("pins moving and immutable manifests plus the release catalog", async () => {
  const catalog = JSON.parse(
    await readFile(new URL("public/releases.json", siteRoot), "utf8"),
  );
  assert.equal(catalog.schemaVersion, 1);
  assert.equal(
    catalog.completeHistory,
    "https://github.com/scwlkr/balls/releases",
  );
  assert.deepEqual(
    catalog.accepted.map((release) => release.tag),
    ["0.3.0-alpha.1", "0.2.0-alpha.1", "0.1.0-alpha.2"],
  );
  assert.deepEqual(catalog.development, []);

  for (const release of catalog.accepted) {
    assert.equal(release.manifest, `/versions/${release.tag}.json`);
    const manifest = JSON.parse(
      await readFile(new URL(`public${release.manifest}`, siteRoot), "utf8"),
    );
    assert.equal(manifest.release.tag, release.tag);
    assertPackageManifest(manifest, "alpha");
  }

  const alpha = JSON.parse(
    await readFile(new URL("public/channels/alpha.json", siteRoot), "utf8"),
  );
  assertPackageManifest(alpha, "alpha");
  assert.equal(alpha.platforms["macos-arm64"].delivery, "source-only");

  const developmentFixture = JSON.parse(
    await readFile(
      new URL("tests/fixtures/development.json", siteRoot),
      "utf8",
    ),
  );
  assertPackageManifest(developmentFixture, "development");
  assert.equal(
    developmentFixture.platforms["windows-x64"].runtime.kind,
    "self-contained",
  );
  assert.notEqual(
    developmentFixture.release.tag,
    developmentFixture.platforms["windows-x64"].identity.version,
  );
});

test("bootstraps verify local files without pipe-to-shell or policy bypasses", async () => {
  const [powershell, linux, macos] = await Promise.all([
    readFile(new URL("public/install.ps1", siteRoot), "utf8"),
    readFile(new URL("public/install.sh", siteRoot), "utf8"),
    readFile(new URL("public/source.sh", siteRoot), "utf8"),
  ]);

  assert.match(powershell, /Get-FileHash/);
  assert.match(powershell, /Install-BallsCanary\\?\.ps1/);
  assert.match(powershell, /PackageManifest\.commit/);
  assert.match(powershell, /commit\.Substring\(0, 12\)/);
  assert.match(powershell, /--list-runtimes/);
  assert.match(powershell, /DOTNET_ROOT_X64/);
  assert.match(powershell, /RegistryView\]::Registry64/);
  assert.match(powershell, /ProgramW6432/);
  assert.doesNotMatch(powershell, /Get-Command\s+dotnet/);
  assert.doesNotMatch(
    powershell,
    /requires the x64 \.NET 10 and ASP\.NET Core 10 runtimes/,
  );
  const preflight = powershell.lastIndexOf(
    "Assert-RuntimeRequirements $delivery.runtime",
  );
  assert.ok(preflight > powershell.indexOf("Invoke-RestMethod"));
  assert.ok(preflight < powershell.indexOf("$temporaryRoot"));
  assert.match(powershell, /#Requires -Version 5\.1/);
  assert.match(powershell, /@\('alpha', 'development'\)/);
  assert.match(powershell, /installation\.json/);
  assert.match(powershell, /WScript\.Shell/);
  assert.match(powershell, /Assert-InternalChecksums/);
  assert.match(powershell, /Opened the local Balls workspace\./);
  assert.match(powershell, /previousShortcutBytes/);
  assert.match(powershell, /previousRecordBytes/);
  assert.match(powershell, /installationCommitted/);
  assert.doesNotMatch(powershell, /\$IsWindows/);
  assert.doesNotMatch(powershell, /\.ArgumentList/);
  assert.doesNotMatch(powershell, /Invoke-Expression|\biex\b|ExecutionPolicy/i);

  assert.match(linux, /sha256sum/);
  assert.match(linux, /Install-BallsCanary\\?\.sh/);
  assert.match(linux, /package_manifest\.get\("commit"\)/);
  assert.match(linux, /re\.escape\(commit\[:12\]\)/);
  assert.doesNotMatch(linux, /curl[^\n]*\|/);

  assert.match(macos, /git .*rev-parse HEAD/);
  assert.match(macos, /source-only/i);
  assert.doesNotMatch(macos, /curl[^\n]*\|/);
});

test(
  "executes the Windows runtime preflight unit tests",
  { skip: process.platform !== "win32" },
  () => {
    const result = spawnSync(
      "powershell.exe",
      [
        "-NoLogo",
        "-NoProfile",
        "-NonInteractive",
        "-File",
        fileURLToPath(new URL("install-runtime.test.ps1", import.meta.url)),
      ],
      { encoding: "utf8" },
    );

    assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`);
  },
);

test("copies the public channel and bootstrap files into the deployment", async () => {
  for (const path of [
    "channels/alpha.json",
    "releases.json",
    "install.ps1",
    "install.sh",
    "source.sh",
    "versions/0.3.0-alpha.1.json",
    "versions/0.2.0-alpha.1.json",
    "versions/0.1.0-alpha.2.json",
  ]) {
    const source = await readFile(new URL(`public/${path}`, siteRoot));
    const built = await readFile(new URL(`dist/client/${path}`, siteRoot));
    assert.deepEqual(built, source);
  }

  const [, wranglerJson] = await Promise.all([
    readFile(new URL("dist/.openai/hosting.json", siteRoot)),
    readFile(new URL("dist/server/wrangler.json", siteRoot), "utf8"),
  ]);
  const wrangler = JSON.parse(wranglerJson);
  assert.equal(wrangler.assets.run_worker_first, true);
});
