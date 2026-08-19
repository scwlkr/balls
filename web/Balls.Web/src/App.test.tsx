import { render, screen, within } from "@testing-library/react";

import { demoDashboard } from "./api/demoSnapshot";
import { App } from "./App";

describe("Balls Web shell", () => {
  it("presents the local Node and Circle as one accessible workspace", () => {
    render(<App />);

    expect(screen.getByRole("banner")).toBeInTheDocument();
    expect(
      screen.getByRole("navigation", { name: "Circle navigation" }),
    ).toBeInTheDocument();
    expect(screen.getByRole("main")).toHaveAttribute("id", "main-content");
    expect(
      screen.getByRole("heading", { level: 1, name: "Example Studio" }),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("Local Node status")).toHaveTextContent(
      "ballsd is ready on this device",
    );
  });

  it("renders typed synthetic Member and Node states without duplicating protocol DTOs", () => {
    render(<App />);

    const members = screen.getByRole("region", { name: "Members" });
    const nodes = screen.getByRole("region", { name: "Nodes" });

    expect(within(members).getAllByRole("listitem")).toHaveLength(3);
    expect(within(members).getByText("Alice Morgan")).toBeInTheDocument();
    expect(within(members).getByText("owner")).toBeInTheDocument();
    expect(within(nodes).getAllByRole("listitem")).toHaveLength(2);
    expect(within(nodes).getByText("Alice-PC")).toBeInTheDocument();
    expect(within(nodes).getByText("Office-Server")).toBeInTheDocument();
  });

  it("preserves generated protocol identifiers at the presentation edge", () => {
    expect(demoDashboard.circle.nodes[0].id).toBe(demoDashboard.localNode.id);
    expect(demoDashboard.circle.nodes[0].isLocal).toBe(true);
    expect(demoDashboard.protocolVersion).toBe(1);
  });
});
