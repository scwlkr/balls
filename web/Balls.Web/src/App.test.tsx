import { act, fireEvent, render, screen, within } from "@testing-library/react";

import { App } from "./App";
import type { BrowserApi } from "./api/browserApi";
import {
  decodeInvitationCode,
  encodeInvitationCode,
} from "./api/invitationCode";
import type {
  CircleDetailsDto,
  CircleListDto,
  CircleMessageListDto,
  StatusDto,
} from "./api/localControl";

const status = {
  productVersion: "0.3.0-alpha.1",
  protocolVersion: 1,
  node: {
    id: "0198f2cc-6a50-7a08-aacb-298f4ebdf616",
    displayName: "Alice-PC",
    createdAtUtc: "2026-08-19T12:00:00.0000000+00:00",
  },
} satisfies StatusDto;

const details = {
  circle: {
    id: "0198f2cc-6a50-7a08-aacb-298f4ebdf620",
    name: "Example Studio",
    createdAtUtc: "2026-08-19T12:05:00.0000000+00:00",
    memberCount: 1,
    nodeCount: 1,
  },
  members: [
    {
      id: "0198f2cc-6a50-7a08-aacb-298f4ebdf621",
      displayName: "Alice Morgan",
      role: "owner",
      joinedAtUtc: "2026-08-19T12:05:00.0000000+00:00",
    },
  ],
  nodes: [
    {
      id: status.node.id,
      displayName: status.node.displayName,
      joinedAtUtc: "2026-08-19T12:05:00.0000000+00:00",
    },
  ],
} satisfies CircleDetailsDto;

const secondDetails = {
  ...details,
  circle: {
    ...details.circle,
    id: "0198f2cc-6a50-7a08-aacb-298f4ebdf630",
    name: "Neighborhood Circle",
  },
} satisfies CircleDetailsDto;

const messages = {
  circleId: details.circle.id,
  messages: [
    {
      id: "0198f2cc-6a50-7a08-aacb-298f4ebdf640",
      circleId: details.circle.id,
      authorMemberId: details.members[0].id,
      authorNodeId: details.nodes[0].id,
      text: "Hello from Alice's Node.",
      authoredAtUtc: "2026-08-21T18:00:00+00:00",
      sequence: 1,
      acceptedAtUtc: "2026-08-21T18:00:01+00:00",
    },
  ],
} satisfies CircleMessageListDto;

describe("Balls browser workspace", () => {
  beforeEach(() => {
    window.history.replaceState(null, "", "/#launch=test-capability");
    window.sessionStorage.clear();
  });

  it("exchanges the fragment and presents an accessible empty local workspace", async () => {
    render(<App api={createApi({ circles: [] })} />);

    expect(
      await screen.findByRole("heading", {
        level: 1,
        name: "Create your first Circle",
      }),
    ).toBeInTheDocument();
    expect(screen.getByRole("banner")).toBeInTheDocument();
    expect(screen.getByRole("main")).toHaveAttribute("id", "main-content");
    expect(screen.getByLabelText("Local Node status")).toHaveTextContent(
      "Alice-PC",
    );
    expect(
      screen.getByRole("form", { name: "Create a Circle" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Local workspace", { selector: ".local-label" }),
    ).toHaveAttribute("data-ready", "true");
    expect(window.location.hash).toBe("");
  });

  it("uses the Balls brandmark in the home link instead of a placeholder letter", () => {
    render(<App api={createApi({ circles: [] })} />);

    const home = screen.getByRole("link", { name: "Balls home" });
    const mark = home.querySelector("svg");
    expect(mark).toHaveAttribute("aria-hidden", "true");
    expect(mark).toHaveAttribute("viewBox", "0 0 64 64");
    expect(home.querySelector(".brand-mark")).not.toHaveTextContent("B");
  });

  it("offers the read-only Revit Server setup before any Circle exists", async () => {
    render(<App api={createApi({ circles: [] })} />);

    expect(
      await screen.findByRole("heading", {
        level: 2,
        name: "Set up Revit Server 2027",
      }),
    ).toBeInTheDocument();
    expect(screen.getByText(/This step is read-only\./)).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Choose official installer" }),
    ).toBeInTheDocument();
  });

  it("renders one exact Ready Host and Admin plan without exposing the media path", async () => {
    const api = createApi({ circles: [] });
    api.selectRevitServerMedia = async () => ({
      status: "selected",
      selectionId: "selection-1",
      fileName: "Revit_Server_2027_win_db.sfx.exe",
    });
    api.inspectRevitServerSetup = async () => ({
      status: "ready",
      summary:
        "Ready to review the exact Host + Admin setup plan. Nothing has changed yet.",
      checks: [
        {
          id: "windows-server",
          status: "ready",
          code: "windows_server_2022_desktop_ready",
          summary: "Windows Server 2022 Desktop Experience is supported.",
        },
      ],
      plan: {
        planDigest: "a".repeat(64),
        machine: "BALLS-RS27",
        windows: "Windows Server 2022 Standard build 20348 (Server)",
        media:
          "Autodesk, Inc. — Autodesk Revit Server 2027 27.0.4.412 (Revit_Server_2027_win_db.sfx.exe)",
        mediaSha256: "b".repeat(64),
        mediaFileName: "Revit_Server_2027_win_db.sfx.exe",
        mediaPublisher: "Autodesk, Inc.",
        mediaProduct: "Autodesk Revit Server 2027",
        mediaVersion: "27.0.4.412",
        enabledRoles: ["Host", "Admin"],
        forbiddenRoles: ["Accelerator"],
        dataPaths: [String.raw`D:\RevitServer\2027\Projects`],
        windowsPrerequisites: ["Web Server (IIS)"],
        aclIntent: ["NETWORK SERVICE: Full control"],
        defaultWebSiteEffects: ["Keep Default Web Site"],
        rsnIni: ["Write BALLS-RS27 as the only line"],
        firewallEffects: [
          "Allow TCP 80 and TCP 808 from LocalSubnet on the Private profile only",
        ],
        verificationActions: ["Verify exactly Host + Admin"],
        ballsOwnedState: ["Windows prerequisites"],
        autodeskOwnedState: ["Autodesk installed product"],
      },
    });

    render(<App api={api} />);
    fireEvent.click(
      await screen.findByRole("button", { name: "Choose official installer" }),
    );

    const result = await screen.findByLabelText(
      "Revit Server setup inspection",
    );
    expect(result).toHaveTextContent("Ready");
    expect(result).toHaveTextContent("Host + Admin");
    expect(result).toHaveTextContent("Accelerator off");
    expect(result).toHaveTextContent(String.raw`D:\RevitServer\2027\Projects`);
    expect(result).toHaveTextContent("TCP 80 and TCP 808");
    expect(result).toHaveTextContent("Review only");
    expect(result).not.toHaveTextContent(String.raw`C:\Users\Alice`);
  });

  it("shows a plain Blocked action and no approvable plan", async () => {
    const api = createApi({ circles: [] });
    api.selectRevitServerMedia = async () => ({
      status: "selected",
      selectionId: "selection-2",
      fileName: "substituted.exe",
    });
    api.inspectRevitServerSetup = async () => ({
      status: "blocked",
      summary:
        "Setup is blocked. Resolve the listed items, then inspect again. Nothing was changed.",
      checks: [
        {
          id: "installer",
          status: "blocked",
          code: "installer_signature_untrusted",
          summary: "Choose official Autodesk Revit Server 2027 media.",
        },
      ],
      plan: null,
    });

    render(<App api={api} />);
    fireEvent.click(
      await screen.findByRole("button", { name: "Choose official installer" }),
    );

    const result = await screen.findByLabelText(
      "Revit Server setup inspection",
    );
    expect(result).toHaveTextContent("Blocked");
    expect(result).toHaveTextContent("Choose official Autodesk");
    expect(
      screen.queryByLabelText("Exact Host and Admin setup plan"),
    ).toBeNull();
  });

  it("requires consent, shows the exact Autodesk handoff, and verifies health", async () => {
    const api = createApi({ circles: [] });
    const plan = {
      planDigest: "a".repeat(64),
      machine: "BALLS-RS27",
      windows: "Windows Server 2022 Standard build 20348 (Server)",
      media: "Autodesk Revit Server 2027 27.0.4.412",
      mediaSha256: "b".repeat(64),
      mediaFileName: "Revit_Server_2027_win_db.sfx.exe",
      mediaPublisher: "Autodesk, Inc.",
      mediaProduct: "Autodesk Revit Server 2027",
      mediaVersion: "27.0.4.412",
      enabledRoles: ["Host", "Admin"],
      forbiddenRoles: ["Accelerator"],
      dataPaths: [
        String.raw`D:\RevitServer\2027`,
        String.raw`D:\RevitServer\2027\Projects`,
        String.raw`D:\RevitServer\2027\Cache`,
      ],
      windowsPrerequisites: ["Web Server (IIS)"],
      aclIntent: ["NETWORK SERVICE: Full control"],
      defaultWebSiteEffects: ["Keep Default Web Site"],
      rsnIni: ["Write BALLS-RS27 as the only line"],
      firewallEffects: ["Private LocalSubnet only"],
      verificationActions: ["Verify exactly Host + Admin"],
      ballsOwnedState: ["Windows prerequisites"],
      autodeskOwnedState: ["Autodesk installed product"],
    };
    api.selectRevitServerMedia = async () => ({
      status: "selected",
      selectionId: "selection-3",
      fileName: "Revit_Server_2027_win_db.sfx.exe",
    });
    api.inspectRevitServerSetup = async () => ({
      status: "ready",
      summary: "Ready. Nothing changed.",
      checks: [],
      plan,
    });
    api.beginRevitServerSetup = async () => ({
      stage: "awaiting-autodesk",
      summary: "Complete Autodesk setup.",
      attemptId: "attempt-1",
      plan,
      checks: [],
    });
    api.verifyRevitServerSetup = async () => ({
      stage: "ready-for-handoff",
      summary:
        "Revit Server 2027 Host + Admin is healthy on this local server.",
      attemptId: "attempt-1",
      plan,
      checks: [
        {
          id: "roles",
          status: "ready",
          code: "roles_exact",
          summary: "Host + Admin are enabled and Accelerator is off.",
        },
      ],
      wallClockSeconds: 600,
      humanInterventionSeconds: 120,
    });
    let exported = false;
    api.exportRevitServerHandoff = async () => {
      exported = true;
      return {
        fileName: "revit-server-2027-setup-bundle.zip",
        contentType: "application/zip",
        bundleBase64: "UEs=",
        bundleSha256: "c".repeat(64),
        outcome: "PASS",
        wallClockSeconds: 601,
      };
    };
    api.getRevitServerSetupStatus = async () =>
      exported
        ? {
            stage: "ready-for-handoff",
            summary:
              "PASS — Revit Server 2027 Host+Admin installation and Administrator surface are healthy in the disposable QEMU/KVM lab.",
            attemptId: "attempt-1",
            plan,
            checks: [],
            wallClockSeconds: 601,
            humanInterventionSeconds: 120,
            outcome: "PASS",
            bundleSha256: "c".repeat(64),
          }
        : {
            stage: "not-started",
            summary: "Choose the official Autodesk installer.",
            attemptId: null,
            plan: null,
            checks: [],
          };

    render(<App api={api} />);
    fireEvent.click(
      await screen.findByRole("button", { name: "Choose official installer" }),
    );
    const begin = await screen.findByRole("button", {
      name: "Prepare Windows and open Autodesk setup",
    });
    expect(begin).toBeDisabled();
    fireEvent.click(screen.getByRole("checkbox"));
    fireEvent.click(begin);

    expect(await screen.findByText("In Autodesk setup")).toBeInTheDocument();
    expect(screen.getByText("Roles: Host + Admin")).toBeInTheDocument();
    expect(screen.getByText("Accelerator: Off")).toBeInTheDocument();
    expect(screen.queryByText(/--|powershell|msiexec/i)).toBeNull();
    fireEvent.click(
      screen.getByRole("button", {
        name: "Autodesk setup is finished — verify",
      }),
    );
    expect(await screen.findByText("Ready for handoff")).toBeInTheDocument();
    expect(screen.getByText(/Host \+ Admin are enabled/)).toBeInTheDocument();
    fireEvent.click(
      screen.getByRole("button", { name: "Export boss handoff" }),
    );
    expect(await screen.findByText("Healthy")).toBeInTheDocument();
    expect(screen.getByText(/Bundle SHA-256/)).toBeInTheDocument();
  });

  it("announces the workspace loading state", () => {
    const api = createApi({ circles: [] });
    api.exchangeLaunchCapability = () => new Promise(() => undefined);

    render(<App api={api} />);

    expect(screen.getByRole("status")).toHaveTextContent("Opening Balls…");
    expect(
      screen.queryByRole("navigation", { name: "Circle navigation" }),
    ).toBeNull();
    expect(
      screen.getByText("Local workspace", { selector: ".local-label" }),
    ).toHaveAttribute("data-ready", "false");
  });

  it("creates a Circle and renders its Member and Node through live API results", async () => {
    render(<App api={createApi({ circles: [] })} />);
    const form = await screen.findByRole("form", { name: "Create a Circle" });

    fireEvent.change(within(form).getByLabelText("Circle name"), {
      target: { value: "Example Studio" },
    });
    fireEvent.change(within(form).getByLabelText("Your display name"), {
      target: { value: "Alice Morgan" },
    });
    fireEvent.submit(form);

    expect(
      await screen.findByRole("heading", { level: 1, name: "Example Studio" }),
    ).toBeInTheDocument();
    const members = screen.getByRole("region", { name: "Members" });
    const nodes = screen.getByRole("region", { name: "Nodes" });
    expect(within(members).getByText("Alice Morgan")).toBeInTheDocument();
    expect(within(members).getByText("owner")).toBeInTheDocument();
    expect(within(nodes).getByText("Alice-PC")).toBeInTheDocument();
    expect(within(nodes).getByText("This device")).toBeInTheDocument();
  });

  it("cancels the Windows folder picker without previewing or mutating", async () => {
    const api = createApi({ circles: [details.circle] });
    let applyCount = 0;
    api.contributeFilesFolder = async () => {
      applyCount += 1;
      throw new Error("The contribution must not run.");
    };

    render(<App api={api} />);
    fireEvent.click(
      await screen.findByRole("button", { name: "Choose existing folder" }),
    );

    expect(await screen.findByRole("status")).toHaveTextContent(
      "No folder was selected. Nothing changed.",
    );
    expect(screen.queryByLabelText("Contribution preview")).toBeNull();
    expect(applyCount).toBe(0);
  });

  it("previews the exact existing folder and retries one idempotent contribution", async () => {
    const api = createApi({ circles: [details.circle] });
    const selectionId = "0198f2cc-6a50-7a08-aacb-298f4ebdf689";
    const requests: Array<[string, string, string]> = [];
    api.selectFilesFolder = async () => ({
      status: "selected",
      selectionId,
      folderPath: String.raw`C:\BallsDemo\Projects`,
      displayName: "Projects",
    });
    api.contributeFilesFolder = async (...request) => {
      requests.push(request);
      if (requests.length === 1) {
        throw new Error("Windows did not finish the approved hosting change.");
      }
      return {
        status: "applied",
        contributionId: "0198f2cc-6a50-7a08-aacb-298f4ebdf690",
        displayName: "Projects",
        folderPath: String.raw`C:\BallsDemo\Projects`,
      };
    };

    render(<App api={api} />);
    fireEvent.click(
      await screen.findByRole("button", { name: "Choose existing folder" }),
    );

    const preview = await screen.findByLabelText("Contribution preview");
    expect(preview).toHaveTextContent(String.raw`C:\BallsDemo\Projects`);
    expect(preview).toHaveTextContent("Existing files stay in place");
    expect(requests).toHaveLength(0);
    fireEvent.click(
      within(preview).getByRole("button", { name: "Contribute Projects" }),
    );
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Windows did not finish",
    );
    fireEvent.click(
      within(preview).getByRole("button", { name: "Contribute Projects" }),
    );

    expect(await screen.findByRole("status")).toHaveTextContent(
      "Projects is ready to share with Circle members.",
    );
    expect(requests).toHaveLength(2);
    expect(requests[0]).toEqual(requests[1]);
    expect(requests[0]?.[0]).toBe(details.circle.id);
    expect(requests[0]?.[2]).toBe(selectionId);
  });

  it("creates a single shareable invitation without exposing connection setup", async () => {
    const api = createApi({ circles: [details.circle] });
    const invitationRequests: string[] = [];
    api.getCircle = async () => ({
      ...details,
      nodes: [
        {
          id: "0198f2cc-6a50-7a08-aacb-298f4ebdf679",
          displayName: "Different first node",
          joinedAtUtc: "2026-08-19T12:00:00Z",
        },
        ...details.nodes,
      ],
    });
    api.createInvitation = async (circleId) => {
      invitationRequests.push(circleId);
      return {
        circleId: details.circle.id,
        invitationId: "0198f2cc-6a50-7a08-aacb-298f4ebdf670",
        expiresAtUtc: "2026-08-25T13:00:00Z",
        package: '{"signed":"original"}',
        provider: "lan-tcp-v1",
        endpoint: "192.168.1.20:43120",
        syncEndpoint: "192.168.1.20:43155",
      };
    };

    render(<App api={api} />);
    fireEvent.click(
      await screen.findByRole("button", { name: "Create invitation" }),
    );

    const invitation = await screen.findByLabelText(
      "Invitation to send privately",
    );
    expect(
      decodeInvitationCode((invitation as HTMLTextAreaElement).value),
    ).toEqual({
      version: 1,
      provider: "lan-tcp-v1",
      endpoint: "192.168.1.20:43120",
      syncEndpoint: "192.168.1.20:43155",
      package: '{"signed":"original"}',
    });
    expect(
      screen.getByRole("button", { name: "Copy invitation" }),
    ).toBeInTheDocument();
    expect(invitationRequests).toEqual([details.circle.id]);
    expect(screen.queryByText(/network settings/i)).toBeNull();
    expect(screen.queryByLabelText(/address/i)).toBeNull();
    expect(screen.queryByLabelText(/port/i)).toBeNull();
  });

  it("reviews and safely retries one human Read/write Capability grant", async () => {
    const member = {
      id: "0198f2cc-6a50-7a08-aacb-298f4ebdf671",
      displayName: "Bob",
      role: "member",
      joinedAtUtc: "2026-08-25T12:00:00Z",
    } as const;
    const joined = {
      ...details,
      circle: { ...details.circle, memberCount: 2, nodeCount: 2 },
      members: [...details.members, member],
    } satisfies CircleDetailsDto;
    const api = createApi({ circles: [joined.circle] });
    api.getCircle = async () => joined;
    api.listFilesContributions = async (circleId) => ({
      circleId,
      contributions: [
        {
          id: "0198f2cc-6a50-7a08-aacb-298f4ebdf672",
          circleId,
          provider: {
            id: "0198f2cc-6a50-7a08-aacb-298f4ebdf673",
            nodeId: details.nodes[0].id,
          },
          displayName: "Projects",
          lifecycle: "defined",
          generation: 1,
          createdAtUtc: "2026-08-25T12:00:00Z",
          authorizedByMemberId: details.members[0].id,
          authorityGeneration: 1,
          authorizedAtUtc: "2026-08-25T12:00:00Z",
        },
        {
          id: "0198f2cc-6a50-7a08-aacb-298f4ebdf674",
          circleId,
          provider: {
            id: "0198f2cc-6a50-7a08-aacb-298f4ebdf675",
            nodeId: details.nodes[0].id,
          },
          displayName: "Projects",
          lifecycle: "defined",
          generation: 1,
          createdAtUtc: "2026-08-25T12:01:00Z",
          authorizedByMemberId: details.members[0].id,
          authorityGeneration: 1,
          authorizedAtUtc: "2026-08-25T12:01:00Z",
        },
      ],
    });
    const previews: Array<[string, string, string]> = [];
    api.previewFilesGrant = async (...request) => {
      previews.push(request);
      return {
        folderName: "Projects",
        folderPath: String.raw`C:\BallsDemo\Projects`,
        memberName: "Bob",
        access: "Read/write",
        summary: String.raw`Give Bob Read/write access to Projects (C:\BallsDemo\Projects).`,
      };
    };
    let applyCount = 0;
    api.applyFilesGrant = async () => {
      applyCount += 1;
      if (applyCount === 1) {
        throw new Error("Windows could not complete the Member access change.");
      }
      return {
        status: "applied",
        folderName: "Projects",
        memberName: "Bob",
        access: "Read/write",
        message: "Projects is now a Circle Capability for Bob.",
      };
    };

    render(<App api={api} />);
    const form = await screen.findByRole("form", {
      name: "Share a Circle Capability",
    });
    expect(within(form).getByLabelText("Folder")).toHaveValue("Projects");
    expect(
      within(form).getByLabelText("Folder").querySelectorAll("option"),
    ).toHaveLength(1);
    expect(within(form).getByLabelText("Member")).toHaveValue("Bob");
    expect(within(form).getByLabelText("Access")).toHaveValue("read-write");
    fireEvent.click(
      within(form).getByRole("button", { name: "Review access" }),
    );

    const preview = await screen.findByLabelText("Access preview");
    expect(preview).toHaveTextContent(
      String.raw`Give Bob Read/write access to Projects (C:\BallsDemo\Projects).`,
    );
    expect(previews).toEqual([[details.circle.id, "Projects", "Bob"]]);
    for (const forbidden of [
      "SMB",
      "account",
      "password",
      "plan",
      "address",
      "port",
    ]) {
      expect(preview).not.toHaveTextContent(new RegExp(forbidden, "i"));
    }

    fireEvent.click(
      within(preview).getByRole("button", { name: "Share this Capability" }),
    );
    expect(await within(form).findByRole("alert")).toHaveTextContent(
      "Windows could not complete",
    );
    fireEvent.click(
      within(preview).getByRole("button", { name: "Share this Capability" }),
    );
    expect(await within(form).findByRole("status")).toHaveTextContent(
      "Projects is now a Circle Capability for Bob.",
    );
    expect(applyCount).toBe(2);
  });

  it("refreshes joined Members in place so an Owner can select a new Member", async () => {
    const member = {
      id: "0198f2cc-6a50-7a08-aacb-298f4ebdf681",
      displayName: "Bob",
      role: "member",
      joinedAtUtc: "2026-08-25T12:00:00Z",
    } as const;
    const joined = {
      ...details,
      circle: { ...details.circle, memberCount: 2 },
      members: [...details.members, member],
    } satisfies CircleDetailsDto;
    const api = createApi({ circles: [details.circle] });
    let memberHasJoined = false;
    api.getCircle = async () => (memberHasJoined ? joined : details);
    api.listFilesContributions = async (circleId) => ({
      circleId,
      contributions: [
        {
          id: "0198f2cc-6a50-7a08-aacb-298f4ebdf682",
          circleId,
          provider: {
            id: "0198f2cc-6a50-7a08-aacb-298f4ebdf683",
            nodeId: details.nodes[0].id,
          },
          displayName: "Projects",
          lifecycle: "defined",
          generation: 1,
          createdAtUtc: "2026-08-25T12:00:00Z",
          authorizedByMemberId: details.members[0].id,
          authorityGeneration: 1,
          authorizedAtUtc: "2026-08-25T12:00:00Z",
        },
      ],
    });

    render(<App api={api} />);
    expect(
      await screen.findByText(
        "Invite someone and wait for them to join before sharing the folder.",
      ),
    ).toBeInTheDocument();

    memberHasJoined = true;
    fireEvent.click(screen.getByRole("button", { name: "Refresh members" }));

    const form = await screen.findByRole("form", {
      name: "Share a Circle Capability",
    });
    expect(within(form).getByLabelText("Member")).toHaveValue("Bob");
    expect(screen.getByRole("status")).toHaveTextContent(
      "Member list updated.",
    );
  });

  it("joins with a pasted invitation and opens shared files through one no-input action", async () => {
    const api = createApi({ circles: [] });
    const joined = {
      ...details,
      circle: { ...details.circle, memberCount: 2, nodeCount: 2 },
      members: [
        ...details.members,
        {
          id: "0198f2cc-6a50-7a08-aacb-298f4ebdf672",
          displayName: "Bob",
          role: "member",
          joinedAtUtc: "2026-08-25T12:00:00Z",
        },
      ],
      nodes: [
        {
          id: "0198f2cc-6a50-7a08-aacb-298f4ebdf673",
          displayName: "Office server",
          joinedAtUtc: "2026-08-19T12:05:00Z",
        },
        ...details.nodes,
      ],
    } satisfies CircleDetailsDto;
    const contributionId = "0198f2cc-6a50-7a08-aacb-298f4ebdf674";
    const grantId = "0198f2cc-6a50-7a08-aacb-298f4ebdf675";
    let joinedRequest: [string, string, string, string, string] | undefined;
    const openedRequests: [string][] = [];
    let syncRequest: [string] | undefined;
    let syncAttempts = 0;
    api.joinCircle = async (...request) => {
      joinedRequest = request;
      return joined;
    };
    api.getViewer = async () => ({
      memberId: joined.members[1].id,
      role: "member",
    });
    api.syncFiles = async (...request) => {
      syncRequest = request;
      syncAttempts += 1;
      return {
        circleId: joined.circle.id,
        importedGrantCount: syncAttempts > 1 ? 1 : 0,
      };
    };
    api.listFilesContributions = async (circleId) => ({
      circleId,
      contributions:
        syncAttempts < 2
          ? []
          : [
              {
                id: contributionId,
                circleId,
                provider: {
                  id: "0198f2cc-6a50-7a08-aacb-298f4ebdf676",
                  nodeId: joined.nodes[0].id,
                },
                displayName: "Project Files",
                lifecycle: "defined",
                generation: 1,
                createdAtUtc: "2026-08-25T12:00:00Z",
                authorizedByMemberId: joined.members[0].id,
                authorityGeneration: 1,
                authorizedAtUtc: "2026-08-25T12:00:00Z",
              },
            ],
    });
    api.listFilesGrants = async (circleId, contribution) => ({
      circleId,
      contributionId: contribution,
      grants: [
        {
          id: "0198f2cc-6a50-7a08-aacb-298f4ebdf678",
          circleId,
          contributionId: contribution,
          memberId: joined.members[0].id,
          access: "read-write",
          lifecycle: "defined",
          generation: 1,
          createdAtUtc: "2026-08-25T12:00:00Z",
          authorizedByMemberId: joined.members[0].id,
          authorityGeneration: 1,
          authorizedAtUtc: "2026-08-25T12:00:00Z",
        },
        {
          id: grantId,
          circleId,
          contributionId: contribution,
          memberId: joined.members[1].id,
          access: "read-write",
          lifecycle: "defined",
          generation: 1,
          createdAtUtc: "2026-08-25T12:00:00Z",
          authorizedByMemberId: joined.members[0].id,
          authorityGeneration: 1,
          authorizedAtUtc: "2026-08-25T12:00:00Z",
        },
      ],
    });
    api.openFiles = async (...request) => {
      openedRequests.push(request);
      if (openedRequests.length === 1) {
        throw new Error(
          "The shared folder is offline. Check that the Circle owner's computer is on, then try again.",
        );
      }
      return {
        status: "opened",
        folderName: "Project Files",
        message: "Opened Project Files in File Explorer.",
      };
    };

    render(<App api={api} />);
    fireEvent.click(
      await screen.findByRole("button", { name: "Join a Circle" }),
    );
    const join = screen.getByRole("form", { name: "Join a Circle" });
    fireEvent.change(within(join).getByLabelText("Your invitation"), {
      target: {
        value: encodeInvitationCode({
          package: '{"signed":"original"}',
          provider: "lan-tcp-v1",
          endpoint: "192.168.1.20:43120",
          syncEndpoint: "192.168.1.20:43155",
        }),
      },
    });
    fireEvent.change(within(join).getByLabelText("Your name"), {
      target: { value: "Bob" },
    });
    fireEvent.submit(join);

    expect(
      await screen.findByText(
        "Waiting for your Circle owner to finish sharing the project folder.",
      ),
    ).toBeInTheDocument();
    const activeMembership = screen.getByLabelText("Active Circle membership");
    expect(activeMembership).toHaveTextContent("Example Studio");
    expect(activeMembership).toHaveTextContent("Bob");
    const files = await screen.findByRole(
      "form",
      { name: "Open Circle Capability" },
      { timeout: 3000 },
    );
    const open = await within(files).findByRole("button", {
      name: "Open shared folder in Explorer",
    });
    expect(joinedRequest).toEqual([
      '{"signed":"original"}',
      "lan-tcp-v1",
      "192.168.1.20:43120",
      "192.168.1.20:43155",
      "Bob",
    ]);
    expect(syncRequest).toEqual([details.circle.id]);
    expect(syncAttempts).toBe(2);
    expect(
      within(files).queryByLabelText("Private host IPv4 address"),
    ).toBeNull();
    expect(within(files).queryByLabelText("Grant")).toBeNull();
    expect(within(files).queryByLabelText("Approved folder")).toBeNull();
    expect(
      within(files).queryByText(/drive|endpoint|provider|plan/i),
    ).toBeNull();
    expect(
      screen.queryByRole("button", { name: "Create invitation" }),
    ).toBeNull();
    fireEvent.click(open);

    expect(await within(files).findByRole("alert")).toHaveTextContent(
      "The shared folder is offline",
    );
    fireEvent.click(open);

    expect(await within(files).findByRole("status")).toHaveTextContent(
      "Opened Project Files in File Explorer.",
    );
    expect(openedRequests).toEqual([[details.circle.id], [details.circle.id]]);
  });

  it("restores a joined Member Capability after a fresh render without Web Storage", async () => {
    const memberId = "0198f2cc-6a50-7a08-aacb-298f4ebdf654";
    const joined = {
      ...details,
      members: [
        ...details.members,
        {
          id: memberId,
          displayName: "Bob",
          role: "member",
          joinedAtUtc: "2026-08-22T12:00:00Z",
        },
      ],
    } satisfies CircleDetailsDto;
    const api = createApi({ circles: [joined.circle] });
    api.getCircle = async () => joined;
    api.getViewer = async () => ({ memberId, role: "member" });
    api.listFilesContributions = async (circleId) => ({
      circleId,
      contributions: [
        {
          id: "0198f2cc-6a50-7a08-aacb-298f4ebdf650",
          circleId,
          provider: {
            id: "0198f2cc-6a50-7a08-aacb-298f4ebdf651",
            nodeId: details.nodes[0].id,
          },
          displayName: "Project Files",
          lifecycle: "defined",
          generation: 1,
          createdAtUtc: "2026-08-22T12:00:00Z",
          authorizedByMemberId: details.members[0].id,
          authorityGeneration: 1,
          authorizedAtUtc: "2026-08-22T12:00:00Z",
        },
      ],
    });
    api.listFilesGrants = async (circleId, contributionId) => ({
      circleId,
      contributionId,
      grants: [
        {
          id: "0198f2cc-6a50-7a08-aacb-298f4ebdf652",
          circleId,
          contributionId,
          memberId,
          access: "read-write",
          lifecycle: "defined",
          generation: 1,
          createdAtUtc: "2026-08-22T12:00:00Z",
          authorizedByMemberId: details.members[0].id,
          authorityGeneration: 1,
          authorizedAtUtc: "2026-08-22T12:00:00Z",
        },
      ],
    });
    const storageRead = vi
      .spyOn(Storage.prototype, "getItem")
      .mockImplementation(() => {
        throw new Error("Web Storage must not be read.");
      });
    const storageWrite = vi
      .spyOn(Storage.prototype, "setItem")
      .mockImplementation(() => {
        throw new Error("Web Storage must not be written.");
      });

    const first = render(<App api={api} />);
    expect(
      await screen.findByRole("heading", { level: 1, name: "Example Studio" }),
    ).toBeInTheDocument();
    expect(
      await screen.findByRole("button", {
        name: "Open shared folder in Explorer",
      }),
    ).toBeInTheDocument();
    first.unmount();
    window.history.replaceState(null, "", "/#launch=fresh-capability");
    render(<App api={api} />);

    expect(
      await screen.findByRole("button", {
        name: "Open shared folder in Explorer",
      }),
    ).toBeInTheDocument();
    expect(storageRead).not.toHaveBeenCalled();
    expect(storageWrite).not.toHaveBeenCalled();
    storageRead.mockRestore();
    storageWrite.mockRestore();
  });

  it("announces when Circle creation is busy", async () => {
    let finishCreate: ((value: CircleDetailsDto) => void) | undefined;
    const api = createApi({ circles: [] });
    api.createCircle = () =>
      new Promise((resolve) => {
        finishCreate = resolve;
      });

    render(<App api={api} />);
    const form = await screen.findByRole("form", { name: "Create a Circle" });
    fireEvent.change(within(form).getByLabelText("Circle name"), {
      target: { value: "Example Studio" },
    });
    fireEvent.change(within(form).getByLabelText("Your display name"), {
      target: { value: "Alice Morgan" },
    });
    fireEvent.submit(form);

    expect(form).toHaveAttribute("aria-busy", "true");
    expect(
      within(form).getByRole("button", { name: "Creating…" }),
    ).toBeDisabled();

    await act(async () => finishCreate?.(details));
    expect(
      await screen.findByRole("heading", { level: 1, name: "Example Studio" }),
    ).toBeInTheDocument();
  });

  it("loads the first persisted Circle and exposes the Circle list", async () => {
    render(
      <App
        api={createApi({
          circles: [details.circle],
        })}
      />,
    );

    expect(
      await screen.findByRole("heading", { level: 1, name: "Example Studio" }),
    ).toBeInTheDocument();
    const circles = screen.getByRole("navigation", { name: "Your Circles" });
    expect(
      within(circles).getByRole("button", { name: "Example Studio" }),
    ).toHaveAttribute("aria-current", "page");
  });

  it("shows durable Circle messages with Member and Node attribution", async () => {
    const api = createApi({ circles: [details.circle] });
    api.getMessages = async () => messages;

    render(<App api={api} />);

    const region = await screen.findByRole("region", { name: "Messages" });
    expect(
      within(region).getByText("Hello from Alice's Node."),
    ).toBeInTheDocument();
    expect(
      within(region).getByText("Alice Morgan · Alice-PC"),
    ).toBeInTheDocument();
    expect(within(region).getByText("#1")).toBeInTheDocument();
  });

  it("announces a busy Circle switch and moves selection after loading", async () => {
    let finishSwitch: ((value: CircleDetailsDto) => void) | undefined;
    const api = createApi({ circles: [details.circle, secondDetails.circle] });
    api.getCircle = (circleId) => {
      if (circleId === details.circle.id) return Promise.resolve(details);
      return new Promise((resolve) => {
        finishSwitch = resolve;
      });
    };

    render(<App api={api} />);
    await screen.findByRole("heading", { level: 1, name: "Example Studio" });
    const circles = screen.getByRole("navigation", { name: "Your Circles" });
    fireEvent.click(
      within(circles).getByRole("button", { name: "Neighborhood Circle" }),
    );

    expect(circles).toHaveAttribute("aria-busy", "true");
    expect(within(circles).getByRole("status")).toHaveTextContent(
      "Switching Circle…",
    );
    expect(
      within(circles).getByRole("button", { name: "Neighborhood Circle" }),
    ).toBeDisabled();
    expect(
      screen.getByRole("heading", { level: 1, name: "Example Studio" }),
    ).toBeInTheDocument();

    await act(async () => finishSwitch?.(secondDetails));
    expect(
      await screen.findByRole("heading", {
        level: 1,
        name: "Neighborhood Circle",
      }),
    ).toBeInTheDocument();
    expect(
      within(circles).getByRole("button", { name: "Neighborhood Circle" }),
    ).toHaveAttribute("aria-current", "page");
  });

  it("fails closed when no launch capability is present", async () => {
    window.history.replaceState(null, "", "/");

    render(<App api={createApi({ circles: [] })} />);

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Run balls ui again",
    );
    expect(screen.queryByRole("form", { name: "Create a Circle" })).toBeNull();
  });
});

function createApi(circleList: CircleListDto): BrowserApi {
  return {
    exchangeLaunchCapability: async () => undefined,
    getStatus: async () => status,
    listCircles: async () => circleList,
    getCircle: async () => details,
    getViewer: async () => ({ memberId: details.members[0].id, role: "owner" }),
    getMessages: async (circleId) => ({ circleId, messages: [] }),
    createCircle: async () => details,
    createInvitation: async () => {
      throw new Error("No invitation test configured.");
    },
    joinCircle: async () => details,
    syncFiles: async (circleId) => ({ circleId, importedGrantCount: 0 }),
    listFilesContributions: async (circleId) => ({
      circleId,
      contributions: [],
    }),
    listFilesGrants: async (circleId, contributionId) => ({
      circleId,
      contributionId,
      grants: [],
    }),
    selectFilesFolder: async () => ({
      status: "cancelled",
      selectionId: null,
      folderPath: null,
      displayName: null,
    }),
    contributeFilesFolder: async () => {
      throw new Error("No contribution test configured.");
    },
    previewFilesGrant: async () => {
      throw new Error("No Member access preview configured.");
    },
    applyFilesGrant: async () => {
      throw new Error("No Member access apply configured.");
    },
    openFiles: async () => {
      throw new Error("No shared-folder open test configured.");
    },
    selectRevitServerMedia: async () => ({
      status: "cancelled",
      selectionId: null,
      fileName: null,
    }),
    inspectRevitServerSetup: async () => {
      throw new Error("No Revit Server inspection test configured.");
    },
    getRevitServerSetupStatus: async () => ({
      stage: "not-started",
      summary:
        "Choose the official Autodesk installer and inspect this server.",
      attemptId: null,
      plan: null,
      checks: [],
    }),
    beginRevitServerSetup: async () => {
      throw new Error("No Revit Server setup begin test configured.");
    },
    verifyRevitServerSetup: async () => {
      throw new Error("No Revit Server verification test configured.");
    },
    retryRevitServerSetup: async () => {
      throw new Error("No Revit Server retry test configured.");
    },
    exportRevitServerHandoff: async () => {
      throw new Error("No Revit Server handoff export test configured.");
    },
  };
}
