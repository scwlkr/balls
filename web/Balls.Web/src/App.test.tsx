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
    const requests: Array<[string, string, string, string]> = [];
    api.selectFilesFolder = async () => ({
      status: "selected",
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
    expect(requests[0]?.[2]).toBe(String.raw`C:\BallsDemo\Projects`);
    expect(requests[0]?.[3]).toBe("Projects");
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

  it("joins with a pasted invitation and connects shared files without IP or grant choices", async () => {
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
    let mappedRequest: [string, string, string, string, string] | undefined;
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
    const mappingPlan = (driveLetter: string) => ({
      contractVersion: 1,
      planId: "a".repeat(64),
      endpoint: "192.168.1.20",
      uncPath: String.raw`\\192.168.1.20\balls-projects`,
      credentialTarget: "192.168.1.20",
      driveLetter,
      friendlyName: "Project Files",
      ownershipId: "b".repeat(64),
      availableDriveLetters: ["M", "P"],
      actions: ["Map exact share."],
    });
    api.previewFilesMapping = async (_circle, _contribution, _grant, drive) =>
      mappingPlan(drive);
    api.mapFiles = async (...request) => {
      mappedRequest = request;
      return { status: "mapped", plan: mappingPlan(request[3]) };
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
    expect(
      screen.queryByRole("button", { name: "Create invitation" }),
    ).toBeNull();
    fireEvent.click(open);

    expect(await within(files).findByRole("status")).toHaveTextContent(
      "Shared folder ready in File Explorer (P:).",
    );
    expect(mappedRequest).toEqual([
      details.circle.id,
      contributionId,
      grantId,
      "P",
      "a".repeat(64),
    ]);
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
    previewFilesMapping: async () => {
      throw new Error("No mapping test configured.");
    },
    mapFiles: async () => {
      throw new Error("No mapping test configured.");
    },
    inspectFilesMapping: async () => {
      throw new Error("No mapping test configured.");
    },
    unmapFiles: async () => {
      throw new Error("No mapping test configured.");
    },
  };
}
