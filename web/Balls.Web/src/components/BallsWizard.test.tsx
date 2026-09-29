import { fireEvent, render, screen, within } from "@testing-library/react";

import type {
  BrowserApi,
  BrowserBallsWizardStatusDto,
} from "../api/browserApi";
import { BallsWizard } from "./BallsWizard";

const absent = {
  support: "supported",
  installation: "absent",
  stage: "idle",
  code: "wizard_supported",
  message: "This Node is ready to download Balls Wizard.",
  wizardVersion: "wizard-v0",
  downloadedBytes: 0,
  totalDownloadBytes: 3_368_023_179,
  requiredStorageBytes: 5_368_709_120,
  canInstall: true,
  canCancel: false,
  canChat: false,
  canRemove: false,
  systemContext: {
    operatingSystem: "Microsoft Windows 11 Pro 10.0.26100",
    operatingSystemArchitecture: "X64",
    processArchitecture: "X64",
    cpu: "Example 8 Core CPU",
    gpus: ["Example GPU"],
    totalMemoryBytes: 17_179_869_184,
    availableMemoryBytes: 10_737_418_240,
    freeStorageBytes: 107_374_182_400,
  },
  artifacts: [
    {
      id: "runtime",
      displayName: "Balls-managed llama.cpp runtime",
      version: "b10516",
      source:
        "https://github.com/ggml-org/llama.cpp/releases/download/b10516/runtime.zip",
      sizeBytes: 18_506_923,
      sha256: "a".repeat(64),
      license: "MIT",
    },
    {
      id: "model",
      displayName: "Google Gemma 4 E2B instruction model (QAT Q4, text only)",
      version: "revision",
      source: "https://huggingface.co/google/model",
      sizeBytes: 3_349_516_256,
      sha256: "b".repeat(64),
      license: "Apache-2.0",
    },
  ],
} satisfies BrowserBallsWizardStatusDto;

const installed = {
  ...absent,
  installation: "installed",
  stage: "ready",
  message: "The local Wizard is installed and ready to wake up.",
  downloadedBytes: absent.totalDownloadBytes,
  canInstall: false,
  canChat: true,
  canRemove: true,
} satisfies BrowserBallsWizardStatusDto;

const circleId = "0198c2d8-b000-7000-8000-000000000501";

describe("Balls Wizard", () => {
  it("offers an explicit download and lets Not now hide it for this render", async () => {
    const api = createWizardApi(absent);
    render(<BallsWizard api={api} circleId={circleId} localRole="owner" />);

    const offer = await screen.findByLabelText("Balls Wizard offer");
    expect(offer).toHaveTextContent("Download Wizard");
    expect(api.installWizard).not.toHaveBeenCalled();
    fireEvent.click(within(offer).getByRole("button", { name: "Not now" }));
    expect(screen.queryByLabelText("Balls Wizard offer")).toBeNull();
  });

  it("discloses exact local context and official sources before installation", async () => {
    const api = createWizardApi(absent);
    render(<BallsWizard api={api} circleId={circleId} localRole="member" />);
    fireEvent.click(
      within(await screen.findByLabelText("Balls Wizard offer")).getByRole(
        "button",
        { name: "Take a look" },
      ),
    );

    const panel = screen.getByLabelText("Balls Wizard");
    fireEvent.click(within(panel).getByText("What Wizard can see"));
    expect(panel).toHaveTextContent("member");
    expect(panel).toHaveTextContent("Microsoft Windows 11 Pro");
    expect(panel).toHaveTextContent("Example 8 Core CPU");
    expect(panel).toHaveTextContent("No names, hostname, serials");
    fireEvent.click(within(panel).getByText("Model, storage, and sources"));
    expect(
      within(panel).getAllByRole("link", { name: "Official source" }),
    ).toHaveLength(2);
    expect(panel).toHaveTextContent("3.14 GiB");
    fireEvent.click(
      within(panel).getByRole("button", { name: /Download 3\.14 GiB/ }),
    );
    expect(api.installWizard).toHaveBeenCalledTimes(1);
  });

  it("keeps chat ephemeral, shows optional Sources, and removes only Wizard", async () => {
    const api = createWizardApi(installed);
    api.chatWithWizard.mockResolvedValue({
      answer:
        "A flick of the hat: choose Open shared folder in Explorer from the Circle workspace.",
      sources: [
        {
          id: "open-circle-files-as-a-member",
          title: "Open Circle Files as a Member",
        },
      ],
    });
    api.removeWizard.mockResolvedValue(absent);
    render(<BallsWizard api={api} circleId={circleId} localRole="member" />);

    fireEvent.click(
      await screen.findByRole("button", { name: "Open Balls Wizard" }),
    );
    const panel = screen.getByLabelText("Balls Wizard");
    expect(panel).toHaveTextContent("currently hovering");
    fireEvent.change(within(panel).getByLabelText("Ask about Balls"), {
      target: { value: "How do I open the shared folder?" },
    });
    fireEvent.click(within(panel).getByRole("button", { name: "Ask" }));

    expect(
      await within(panel).findByText(/choose Open shared folder in Explorer/),
    ).toBeInTheDocument();
    fireEvent.click(within(panel).getByText("Sources"));
    expect(panel).toHaveTextContent("Open Circle Files as a Member");
    expect(api.chatWithWizard).toHaveBeenCalledWith(circleId, [
      { role: "user", content: "How do I open the shared folder?" },
    ]);

    fireEvent.click(
      within(panel).getByRole("button", { name: "Clear conversation" }),
    );
    expect(panel).not.toHaveTextContent("How do I open the shared folder?");
    fireEvent.click(
      within(panel).getByRole("button", { name: "Remove Wizard" }),
    );
    expect(
      await screen.findByLabelText("Balls Wizard offer"),
    ).toHaveTextContent("Download Wizard");
    expect(api.removeWizard).toHaveBeenCalledTimes(1);
  });

  it("refreshes into a retryable install state after chat integrity failure", async () => {
    const retryable = {
      ...absent,
      installation: "partial",
      stage: "failed",
      code: "wizard_integrity_failed",
      message: "Retry the Wizard download.",
    } satisfies BrowserBallsWizardStatusDto;
    const api = createWizardApi(installed);
    api.getWizardStatus
      .mockResolvedValueOnce(installed)
      .mockResolvedValue(retryable);
    api.chatWithWizard.mockRejectedValue(
      new Error("A Wizard artifact failed verification."),
    );
    render(<BallsWizard api={api} circleId={circleId} localRole="member" />);

    fireEvent.click(
      await screen.findByRole("button", { name: "Open Balls Wizard" }),
    );
    const panel = screen.getByLabelText("Balls Wizard");
    fireEvent.change(within(panel).getByLabelText("Ask about Balls"), {
      target: { value: "Can you help?" },
    });
    fireEvent.click(within(panel).getByRole("button", { name: "Ask" }));

    expect(
      await within(panel).findByRole("button", {
        name: "Resume Wizard download",
      }),
    ).toBeEnabled();
    expect(panel).toHaveTextContent("failed verification");
  });

  it("shows an honest unavailable state without an enabled download", async () => {
    const unsupported = {
      ...absent,
      support: "unsupported",
      code: "windows_11_x64_required",
      message: "Balls Wizard v0 needs Windows 11 x64; this Node is Linux.",
      totalDownloadBytes: 0,
      requiredStorageBytes: 0,
      canInstall: false,
      artifacts: [],
    } satisfies BrowserBallsWizardStatusDto;
    const api = createWizardApi(unsupported);
    render(<BallsWizard api={api} circleId={null} localRole="none" />);

    const offer = await screen.findByLabelText("Balls Wizard offer");
    expect(offer).toHaveTextContent("Wizard unavailable");
    fireEvent.click(within(offer).getByRole("button", { name: "Details" }));
    expect(screen.getByRole("button", { name: "Download 0 B" })).toBeDisabled();
    expect(api.installWizard).not.toHaveBeenCalled();
  });
});

function createWizardApi(status: BrowserBallsWizardStatusDto) {
  return {
    getWizardStatus: vi.fn().mockResolvedValue(status),
    installWizard: vi.fn().mockResolvedValue({ ...status, canCancel: true }),
    cancelWizardInstall: vi.fn().mockResolvedValue(status),
    removeWizard: vi.fn().mockResolvedValue(absent),
    chatWithWizard: vi.fn(),
  } as unknown as BrowserApi & {
    getWizardStatus: ReturnType<typeof vi.fn>;
    installWizard: ReturnType<typeof vi.fn>;
    cancelWizardInstall: ReturnType<typeof vi.fn>;
    removeWizard: ReturnType<typeof vi.fn>;
    chatWithWizard: ReturnType<typeof vi.fn>;
  };
}
