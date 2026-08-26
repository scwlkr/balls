import { expect, test } from "@playwright/test";

test("shows every delivery lane and copies the complete Linux command", async ({
  page,
}) => {
  await page.goto("/");

  await expect(
    page.getByRole("heading", {
      level: 1,
      name: "One command. The newest published Balls Alpha.",
    }),
  ).toBeVisible();
  const downloads = page.locator("#downloads");
  await expect(downloads.getByText("Windows", { exact: true })).toBeVisible();
  await expect(downloads.getByText("Linux", { exact: true })).toBeVisible();
  await expect(downloads.getByText("macOS", { exact: true })).toBeVisible();
  await expect(page.getByText(/macOS is source-only today/i)).toBeVisible();
  await expect(
    page.getByRole("heading", {
      level: 2,
      name: "Testing software. It may be broken.",
    }),
  ).toBeVisible();
  await expect(page.locator("[data-development-release]")).toBeVisible();
  await expect(page.locator("[data-development-tag]")).toHaveText(
    "development-20260826T223620Z-72f6fa983b4c",
  );
  await expect(page.locator("#development-windows-command")).toContainText(
    "/channels/development.json",
  );
  await expect(
    page.getByRole("heading", { level: 2, name: "Previous versions" }),
  ).toBeVisible();

  const copyButton = page.getByRole("button", { name: "Copy Bash command" });
  await copyButton.click();
  await expect(copyButton).toContainText("Copied");
  await expect(page.locator("#linux-command")).toContainText(
    "https://balls.wlkrlabs.com/install.sh",
  );
  await expect(page.locator("#windows-command")).toContainText(
    "https://balls.wlkrlabs.com/bootstrap/windows-x64.json",
  );
  await expect(page.locator("#windows-command")).toContainText("Get-FileHash");
});

test("shows the latest warned Development build and limits history to ten", async ({
  page,
}) => {
  const development = Array.from({ length: 12 }, (_, index) => ({
    tag: `development-2026-08-${String(26 - index).padStart(2, "0")}`,
    publishedAt: new Date(Date.UTC(2026, 7, 26 - index, 12)).toISOString(),
    manifest: `/versions/development-2026-08-${String(26 - index).padStart(2, "0")}.json`,
    knownBroken: index === 0,
    note: index === 0 ? "Issue #92 test build; it may be broken." : undefined,
  }));
  await page.route("**/releases.json", async (route) => {
    await route.fulfill({
      json: {
        schemaVersion: 1,
        accepted: [
          {
            tag: "0.3.0-alpha.1",
            publishedAt: "2026-08-21T22:25:14Z",
            manifest: "/versions/0.3.0-alpha.1.json",
          },
          {
            tag: "0.2.0-alpha.1",
            publishedAt: "2026-08-20T17:22:28Z",
            manifest: "/versions/0.2.0-alpha.1.json",
          },
          {
            tag: "0.1.0-alpha.2",
            publishedAt: "2026-08-19T21:21:40Z",
            manifest: "/versions/0.1.0-alpha.2.json",
          },
        ],
        development,
        completeHistory: "https://github.com/scwlkr/balls/releases",
      },
    });
  });

  await page.goto("/");

  await expect(page.locator("[data-development-release]")).toBeVisible();
  await expect(page.locator("[data-development-tag]")).toHaveText(
    development[0].tag,
  );
  await expect(page.locator("[data-development-summary]")).toHaveText(
    "Issue #92 test build; it may be broken.",
  );
  await expect(page.locator("#development-windows-command")).toContainText(
    "/channels/development.json",
  );

  const historyRows = page.locator("[data-release-history] tr");
  await expect(historyRows).toHaveCount(13);
  await expect(
    page.getByText(development[9].tag, { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText(development[10].tag, { exact: true }),
  ).toHaveCount(0);
  await expect(historyRows.first().locator("details code")).toContainText(
    "/versions/0.3.0-alpha.1.json",
  );
});

test("renders Windows runtime versions from the channel manifest", async ({
  page,
}) => {
  await page.route("**/channels/alpha.json", async (route) => {
    const response = await route.fetch();
    const manifest = await response.json();
    manifest.platforms["windows-x64"].runtime = {
      kind: "framework-dependent",
      architecture: "x64",
      frameworks: [
        { name: "Microsoft.NETCore.App", major: 11 },
        { name: "Microsoft.AspNetCore.App", major: 11 },
      ],
    };
    await route.fulfill({ response, json: manifest });
  });

  await page.goto("/");
  await expect(page.locator("[data-windows-runtime-requirements]")).toHaveText(
    " · .NET 11 + ASP.NET Core 11",
  );
});

test("removes the Windows runtime note for a self-contained channel", async ({
  page,
}) => {
  await page.route("**/channels/alpha.json", async (route) => {
    const response = await route.fetch();
    const manifest = await response.json();
    manifest.platforms["windows-x64"].runtime = {
      kind: "self-contained",
      architecture: "x64",
    };
    await route.fulfill({ response, json: manifest });
  });

  await page.goto("/");
  await expect(page.locator("[data-windows-runtime-requirements]")).toHaveText(
    "",
  );
});
