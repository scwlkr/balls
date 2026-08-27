import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

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
  assert.match(html, /Loading verified install command/);
  assert.match(html, /href="\/bootstrap\/windows-x64\.json"/);
  assert.match(html, /https:\/\/balls\.wlkrlabs\.com\/install\.sh/);
  assert.match(html, /https:\/\/balls\.wlkrlabs\.com\/source\.sh/);
  assert.match(
    html,
    /Windows x64 · Windows PowerShell 5\.1\+[\s\S]*· runtime checked from manifest/i,
  );
  assert.doesNotMatch(html, /powershell\.exe -NoLogo -NoProfile -File/);
  assert.doesNotMatch(html, /\.ps1\b/i);
  assert.doesNotMatch(
    html,
    /ExecutionPolicy|Unblock-File|Invoke-Expression|\biex\b/i,
  );
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
  assert.deepEqual(
    catalog.development.map((release) => release.tag),
    [
      "development-20260827T045203Z-39cd15e5ffdf",
      "development-20260826T223620Z-72f6fa983b4c",
      "development-20260826T212044Z-1218b57d8d37",
    ],
  );

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
  assert.equal(alpha.release.tag, "development-20260827T045203Z-39cd15e5ffdf");
  assert.deepEqual(Object.keys(alpha.platforms), ["windows-x64"]);
  assert.equal(alpha.platforms["windows-x64"].runtime.kind, "self-contained");

  const legacyCrossPlatform = JSON.parse(
    await readFile(
      new URL("public/legacy/0.3.0-alpha.1-cross-platform.json", siteRoot),
      "utf8",
    ),
  );
  assertPackageManifest(legacyCrossPlatform, "alpha");
  assert.equal(legacyCrossPlatform.release.tag, "0.3.0-alpha.1");
  assert.equal(legacyCrossPlatform.platforms["linux-x64"].delivery, "package");
  assert.equal(
    legacyCrossPlatform.platforms["macos-arm64"].delivery,
    "source-only",
  );

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

  const development = JSON.parse(
    await readFile(
      new URL("public/channels/development.json", siteRoot),
      "utf8",
    ),
  );
  const immutableDevelopment = JSON.parse(
    await readFile(
      new URL(
        "public/versions/development-20260827T045203Z-39cd15e5ffdf.json",
        siteRoot,
      ),
      "utf8",
    ),
  );
  assert.deepEqual(development, immutableDevelopment);
  assertPackageManifest(development, "development");
  assert.deepEqual(alpha.release, development.release);
  assert.deepEqual(
    alpha.platforms["windows-x64"],
    development.platforms["windows-x64"],
  );

  const bootstrap = JSON.parse(
    await readFile(
      new URL("public/bootstrap/windows-x64.json", siteRoot),
      "utf8",
    ),
  );
  const immutableBootstrap = JSON.parse(
    await readFile(
      new URL(
        "public/bootstrap/versions/development-20260827T045203Z-39cd15e5ffdf.json",
        siteRoot,
      ),
      "utf8",
    ),
  );
  assert.deepEqual(bootstrap, immutableBootstrap);
  assert.equal(bootstrap.release.commit, development.release.commit);
});

test("bootstraps verify local files without pipe-to-shell or policy bypasses", async () => {
  const [windowsSource, linux, macos] = await Promise.all([
    readFile(new URL("src/main.ts", siteRoot), "utf8"),
    readFile(new URL("public/install.sh", siteRoot), "utf8"),
    readFile(new URL("public/source.sh", siteRoot), "utf8"),
  ]);

  assert.match(windowsSource, /bootstrap\/windows-x64\.json/);
  assert.match(windowsSource, /Get-FileHash/);
  assert.match(windowsSource, /balls-bootstrap-windows-x64/);
  assert.match(windowsSource, /--manifest-uri/);
  assert.doesNotMatch(windowsSource, /install\.ps1|\s-File\s|\.ps1\b/i);
  assert.doesNotMatch(
    windowsSource,
    /Invoke-Expression|\biex\b|ExecutionPolicy|Unblock-File/i,
  );

  assert.match(linux, /sha256sum/);
  assert.match(linux, /legacy\/0\.3\.0-alpha\.1-cross-platform\.json/);
  assert.doesNotMatch(linux, /channels\/alpha\.json/);
  assert.match(linux, /Install-BallsCanary\\?\.sh/);
  assert.match(linux, /package_manifest\.get\("commit"\)/);
  assert.match(linux, /re\.escape\(commit\[:12\]\)/);
  assert.doesNotMatch(linux, /curl[^\n]*\|/);

  assert.match(macos, /git .*rev-parse HEAD/);
  assert.match(macos, /legacy\/0\.3\.0-alpha\.1-cross-platform\.json/);
  assert.doesNotMatch(macos, /channels\/alpha\.json/);
  assert.match(macos, /source-only/i);
  assert.doesNotMatch(macos, /curl[^\n]*\|/);
});

test("copies the public channel and bootstrap files into the deployment", async () => {
  for (const path of [
    "channels/alpha.json",
    "channels/development.json",
    "bootstrap/windows-x64.json",
    "bootstrap/versions/development-20260827T045203Z-39cd15e5ffdf.json",
    "bootstrap/versions/development-20260826T223620Z-72f6fa983b4c.json",
    "bootstrap/versions/development-20260826T212044Z-1218b57d8d37.json",
    "releases.json",
    "install.sh",
    "source.sh",
    "legacy/0.3.0-alpha.1-cross-platform.json",
    "versions/0.3.0-alpha.1.json",
    "versions/0.2.0-alpha.1.json",
    "versions/0.1.0-alpha.2.json",
    "versions/development-20260827T045203Z-39cd15e5ffdf.json",
    "versions/development-20260826T223620Z-72f6fa983b4c.json",
    "versions/development-20260826T212044Z-1218b57d8d37.json",
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
