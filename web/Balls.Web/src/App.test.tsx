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

  it("creates a single shareable invitation without exposing connection setup", async () => {
    const api = createApi({ circles: [details.circle] });
    api.createInvitation = async () => ({
      circleId: details.circle.id,
      invitationId: "0198f2cc-6a50-7a08-aacb-298f4ebdf670",
      expiresAtUtc: "2026-08-25T13:00:00Z",
      package: '{"signed":"original"}',
      endpoint: "192.168.1.20:43120",
    });

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
      endpoint: "192.168.1.20:43120",
      package: '{"signed":"original"}',
    });
    expect(
      screen.getByRole("button", { name: "Copy invitation" }),
    ).toBeInTheDocument();
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
    let joinedRequest: [string, string, string] | undefined;
    let mappedRequest:
      [string, string, string, string, string, string] | undefined;
    api.joinCircle = async (...request) => {
      joinedRequest = request;
      return joined;
    };
    api.listFilesContributions = async (circleId) => ({
      circleId,
      contributions: [
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
    api.previewFilesMapping = async (
      _circle,
      _contribution,
      _grant,
      _host,
      drive,
    ) => mappingPlan(drive);
    api.mapFiles = async (...request) => {
      mappedRequest = request;
      return { status: "mapped", plan: mappingPlan(request[4]) };
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
          endpoint: "192.168.1.20:43120",
        }),
      },
    });
    fireEvent.change(within(join).getByLabelText("Your name"), {
      target: { value: "Bob" },
    });
    fireEvent.submit(join);

    const files = await screen.findByRole("form", { name: "Map Circle Files" });
    const open = await within(files).findByRole("button", {
      name: "Open shared folder in Explorer",
    });
    expect(joinedRequest).toEqual([
      '{"signed":"original"}',
      "192.168.1.20:43120",
      "Bob",
    ]);
    expect(
      within(files).queryByLabelText("Private host IPv4 address"),
    ).toBeNull();
    expect(within(files).queryByLabelText("Grant")).toBeNull();
    fireEvent.click(open);

    expect(await within(files).findByRole("status")).toHaveTextContent(
      "Shared folder ready in File Explorer (P:).",
    );
    expect(mappedRequest).toEqual([
      details.circle.id,
      contributionId,
      grantId,
      "192.168.1.20",
      "P",
      "a".repeat(64),
    ]);
  });

  it("discovers drive letters before mapping through the shared browser application", async () => {
    const api = createApi({ circles: [details.circle] });
    api.listFilesContributions = async () => ({
      circleId: details.circle.id,
      contributions: [
        {
          id: "0198f2cc-6a50-7a08-aacb-298f4ebdf650",
          circleId: details.circle.id,
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
          memberId: details.members[0].id,
          access: "read-write",
          lifecycle: "defined",
          generation: 1,
          createdAtUtc: "2026-08-22T12:00:00Z",
          authorizedByMemberId: details.members[0].id,
          authorityGeneration: 1,
          authorizedAtUtc: "2026-08-22T12:00:00Z",
        },
        {
          id: "0198f2cc-6a50-7a08-aacb-298f4ebdf653",
          circleId,
          contributionId,
          memberId: "0198f2cc-6a50-7a08-aacb-298f4ebdf654",
          access: "read-only",
          lifecycle: "defined",
          generation: 1,
          createdAtUtc: "2026-08-22T12:00:00Z",
          authorizedByMemberId: details.members[0].id,
          authorityGeneration: 1,
          authorizedAtUtc: "2026-08-22T12:00:00Z",
        },
      ],
    });
    const mappingPlan = (driveLetter: string) => ({
      contractVersion: 1,
      planId: "a".repeat(64),
      endpoint: "192.168.1.20",
      uncPath: String.raw`\\192.168.1.20\balls-example`,
      credentialTarget: "192.168.1.20",
      driveLetter,
      friendlyName: "Example Studio",
      ownershipId: "b".repeat(64),
      availableDriveLetters: ["M", "N"],
      actions: ["Map exact share."],
    });
    api.previewFilesMapping = async (
      _circle,
      _contribution,
      _grant,
      _endpoint,
      drive,
    ) => mappingPlan(drive);
    api.mapFiles = async (
      _circle,
      _contribution,
      _grant,
      _endpoint,
      drive,
    ) => ({
      status: "mapped",
      plan: mappingPlan(drive),
    });

    render(<App api={api} />);
    const form = await screen.findByRole("form", { name: "Map Circle Files" });
    fireEvent.change(within(form).getByLabelText("Private host IPv4 address"), {
      target: { value: "192.168.1.20" },
    });
    fireEvent.click(
      within(form).getByRole("button", {
        name: "Find available drive letters",
      }),
    );
    const drive = await within(form).findByLabelText("Drive letter");
    expect(
      within(drive).getByRole("option", { name: "M:" }),
    ).toBeInTheDocument();
    fireEvent.change(drive, { target: { value: "M" } });
    fireEvent.click(
      within(form).getByRole("button", { name: "Preview exact mapping" }),
    );
    expect(await within(form).findByText(/M: →/)).toBeInTheDocument();
    fireEvent.change(drive, { target: { value: "N" } });
    expect(
      within(form).queryByRole("button", { name: "Unmap" }),
    ).not.toBeInTheDocument();
    fireEvent.click(
      within(form).getByRole("button", { name: "Preview exact mapping" }),
    );
    expect(await within(form).findByText(/N: →/)).toBeInTheDocument();
    fireEvent.click(
      within(form).getByRole("button", { name: "Map in Explorer" }),
    );
    expect(await within(form).findByRole("status")).toHaveTextContent("mapped");
    fireEvent.change(within(form).getByLabelText("Grant"), {
      target: { value: "0198f2cc-6a50-7a08-aacb-298f4ebdf653" },
    });
    expect(
      within(form).queryByRole("button", { name: "Unmap" }),
    ).not.toBeInTheDocument();
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
    getMessages: async (circleId) => ({ circleId, messages: [] }),
    createCircle: async () => details,
    createInvitation: async () => {
      throw new Error("No invitation test configured.");
    },
    joinCircle: async () => details,
    listFilesContributions: async (circleId) => ({
      circleId,
      contributions: [],
    }),
    listFilesGrants: async (circleId, contributionId) => ({
      circleId,
      contributionId,
      grants: [],
    }),
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
