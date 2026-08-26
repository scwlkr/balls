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
  await expect(page.getByText("Windows", { exact: true })).toBeVisible();
  await expect(page.getByText("Linux", { exact: true })).toBeVisible();
  await expect(page.getByText("macOS", { exact: true })).toBeVisible();
  await expect(page.getByText(/macOS is source-only today/i)).toBeVisible();

  const copyButton = page.getByRole("button", { name: "Copy Bash command" });
  await copyButton.click();
  await expect(copyButton).toContainText("Copied");
  await expect(page.locator("#linux-command")).toContainText(
    "https://balls.wlkrlabs.com/install.sh",
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
