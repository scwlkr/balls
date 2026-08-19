import { spawn, type ChildProcessWithoutNullStreams } from "node:child_process";
import { once } from "node:events";
import { createInterface } from "node:readline";
import { fileURLToPath } from "node:url";

import { expect, test } from "@playwright/test";

const readyPrefix = "BALLS_BROWSER_READY ";
const repositoryRoot = fileURLToPath(new URL("../../..", import.meta.url));
const harnessPath = fileURLToPath(
  new URL(
    "../../../eng/Balls.BrowserHarness/bin/Release/net10.0/Balls.BrowserHarness.dll",
    import.meta.url,
  ),
);

test("launches, creates, lists, and survives a daemon restart", async ({
  page,
}) => {
  const harness = startHarness();
  try {
    await page.goto(await harness.nextLaunch());
    await expect(
      page.getByRole("heading", { level: 1, name: "Create your first Circle" }),
    ).toBeVisible();
    await expect(page).toHaveURL((url) => url.hash === "");

    await page.getByLabel("Circle name").fill("Browser Circle");
    await page.getByLabel("Your display name").fill("Alice");
    await page.getByRole("button", { name: "Create Circle" }).click();

    await expect(
      page.getByRole("heading", { level: 1, name: "Browser Circle" }),
    ).toBeVisible();
    await expect(page.getByRole("region", { name: "Members" })).toContainText(
      "Alice",
    );
    await expect(page.getByRole("region", { name: "Nodes" })).toContainText(
      "Browser-PC",
    );
    await expect(
      page.getByRole("navigation", { name: "Your Circles" }),
    ).toContainText("Browser Circle");

    const restartedLaunch = await harness.restart();
    await page.goto(restartedLaunch);
    await expect(
      page.getByRole("heading", { level: 1, name: "Browser Circle" }),
    ).toBeVisible();
    await expect(page).toHaveURL((url) => url.hash === "");
  } finally {
    await harness.stop();
  }
});

interface Harness {
  nextLaunch(): Promise<string>;
  restart(): Promise<string>;
  stop(): Promise<void>;
}

function startHarness(): Harness {
  const child = spawn("dotnet", [harnessPath], {
    cwd: repositoryRoot,
    stdio: ["pipe", "pipe", "pipe"],
    windowsHide: true,
  });
  const launches: string[] = [];
  const waiters: Array<(value: string) => void> = [];
  let stderr = "";
  createInterface({ input: child.stdout }).on("line", (line) => {
    if (!line.startsWith(readyPrefix)) return;
    const launch = line.slice(readyPrefix.length);
    const waiter = waiters.shift();
    if (waiter) waiter(launch);
    else launches.push(launch);
  });
  child.stderr.on("data", (chunk: Buffer) => {
    stderr = (stderr + chunk.toString()).slice(-4_000);
  });

  async function nextLaunch() {
    const queued = launches.shift();
    if (queued) return queued;
    return await new Promise<string>((resolve, reject) => {
      const timeout = setTimeout(
        () => reject(new Error(`Browser harness did not start. ${stderr}`)),
        20_000,
      );
      waiters.push((value) => {
        clearTimeout(timeout);
        resolve(value);
      });
      child.once("exit", (code) => {
        clearTimeout(timeout);
        reject(new Error(`Browser harness exited with ${code}. ${stderr}`));
      });
    });
  }

  return {
    nextLaunch,
    async restart() {
      child.stdin.write("restart\n");
      return await nextLaunch();
    },
    async stop() {
      if (child.exitCode !== null) return;
      child.stdin.write("quit\n");
      await waitForExit(child);
    },
  };
}

async function waitForExit(child: ChildProcessWithoutNullStreams) {
  const graceful = once(child, "exit");
  const timeout = new Promise<never>((_, reject) => {
    setTimeout(
      () => reject(new Error("Browser harness did not stop.")),
      10_000,
    );
  });
  try {
    await Promise.race([graceful, timeout]);
  } catch (error) {
    child.kill();
    throw error;
  }
}
