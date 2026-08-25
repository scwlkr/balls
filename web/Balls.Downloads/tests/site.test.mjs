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
    /Windows x64 · PowerShell 7 · \.NET 10 \+ ASP\.NET Core 10/i,
  );
  assert.match(html, /unsigned prerelease/i);
  assert.match(html, /macOS is source-only/i);
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

test("pins the accepted Alpha and every packaged asset by SHA-256", async () => {
  const manifest = JSON.parse(
    await readFile(new URL("public/channels/alpha.json", siteRoot), "utf8"),
  );

  assert.equal(manifest.schemaVersion, 1);
  assert.equal(manifest.channel, "alpha");
  assert.match(manifest.release.tag, /^\d+\.\d+\.\d+-alpha\.\d+$/);
  assert.match(manifest.release.commit, /^[0-9a-f]{40}$/);
  assert.equal(manifest.release.unsigned, true);
  assert.equal(manifest.platforms["macos-arm64"].delivery, "source-only");
  assert.deepEqual(manifest.platforms["windows-x64"].runtime, {
    kind: "framework-dependent",
    architecture: "x64",
    frameworks: [
      { name: "Microsoft.NETCore.App", major: 10 },
      { name: "Microsoft.AspNetCore.App", major: 10 },
    ],
  });

  for (const platform of ["windows-x64", "linux-x64"]) {
    const delivery = manifest.platforms[platform];
    assert.equal(delivery.delivery, "package");
    assert.match(
      delivery.archive.name,
      new RegExp(`${manifest.release.commit.slice(0, 12)}\\.zip$`),
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
});

test("bootstraps verify local files without pipe-to-shell or policy bypasses", async () => {
  const [powershell, linux, macos] = await Promise.all([
    readFile(new URL("public/install.ps1", siteRoot), "utf8"),
    readFile(new URL("public/install.sh", siteRoot), "utf8"),
    readFile(new URL("public/source.sh", siteRoot), "utf8"),
  ]);

  assert.match(powershell, /Get-FileHash/);
  assert.match(powershell, /Install-BallsCanary\\?\.ps1/);
  assert.match(powershell, /packageManifest\.commit/);
  assert.match(powershell, /commit\.Substring\(0, 12\)/);
  assert.match(powershell, /--list-runtimes/);
  assert.match(powershell, /DOTNET_ROOT_X64/);
  assert.match(powershell, /RegistryView\]::Registry64/);
  assert.match(powershell, /ProgramW6432/);
  assert.doesNotMatch(powershell, /Get-Command\s+dotnet/);
  const preflight = powershell.lastIndexOf(
    "Assert-RuntimeRequirements $delivery.runtime",
  );
  assert.ok(preflight > powershell.indexOf("Invoke-RestMethod"));
  assert.ok(preflight < powershell.indexOf("$temporaryRoot"));
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
      "pwsh",
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
    "install.ps1",
    "install.sh",
    "source.sh",
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
