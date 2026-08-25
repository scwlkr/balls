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
