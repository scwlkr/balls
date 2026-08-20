import { fireEvent, render, screen, within } from "@testing-library/react";

import { App } from "./App";
import type { BrowserApi } from "./api/browserApi";
import type {
  CircleDetailsDto,
  CircleListDto,
  StatusDto,
} from "./api/localControl";

const status = {
  productVersion: "0.2.0-alpha.1",
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

describe("Balls browser workspace", () => {
  beforeEach(() => {
    window.history.replaceState(null, "", "/#launch=test-capability");
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
    expect(window.location.hash).toBe("");
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
    createCircle: async () => details,
  };
}
