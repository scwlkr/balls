import { act, fireEvent, render, screen, within } from "@testing-library/react";

import { FilesMappingPanel, type FilesMappingApi } from "./FilesMappingPanel";

describe("Member Circle Files", () => {
  it("clears a stale synchronization error when Check again succeeds", async () => {
    vi.useFakeTimers();
    try {
      const circleId = "0198f2cc-6a50-7a08-aacb-298f4ebdf620";
      const memberId = "0198f2cc-6a50-7a08-aacb-298f4ebdf621";
      const contributionId = "0198f2cc-6a50-7a08-aacb-298f4ebdf622";
      let ownerOnline = false;
      const api = {
        syncFiles: async () => {
          if (!ownerOnline) {
            throw new Error(
              "The Circle owner's device could not be reached on your local network.",
            );
          }
          return { circleId, importedGrantCount: 1 };
        },
        listFilesContributions: async () => ({
          circleId,
          contributions: ownerOnline
            ? [
                {
                  id: contributionId,
                  circleId,
                  provider: {
                    id: "0198f2cc-6a50-7a08-aacb-298f4ebdf623",
                    nodeId: "0198f2cc-6a50-7a08-aacb-298f4ebdf624",
                  },
                  displayName: "Projects",
                  lifecycle: "defined" as const,
                  generation: 1,
                  createdAtUtc: "2026-08-26T12:00:00Z",
                  authorizedByMemberId: memberId,
                  authorityGeneration: 1,
                  authorizedAtUtc: "2026-08-26T12:00:00Z",
                },
              ]
            : [],
        }),
        listFilesGrants: async () => ({
          circleId,
          contributionId,
          grants: [
            {
              id: "0198f2cc-6a50-7a08-aacb-298f4ebdf625",
              circleId,
              contributionId,
              memberId,
              access: "read-write" as const,
              lifecycle: "defined" as const,
              generation: 1,
              createdAtUtc: "2026-08-26T12:00:00Z",
              authorizedByMemberId: memberId,
              authorityGeneration: 1,
              authorizedAtUtc: "2026-08-26T12:00:00Z",
            },
          ],
        }),
        openFiles: async () => {
          throw new Error("The test does not open Explorer.");
        },
      } satisfies FilesMappingApi;

      render(
        <FilesMappingPanel
          api={api}
          viewer={{ memberId, role: "member" }}
          circleId={circleId}
        />,
      );
      await act(async () => {
        await vi.runAllTimersAsync();
      });

      expect(screen.getByRole("alert")).toHaveTextContent(
        "could not be reached",
      );
      ownerOnline = true;
      await act(async () => {
        fireEvent.click(screen.getByRole("button", { name: "Check again" }));
      });

      const form = screen.getByRole("form", {
        name: "Open Circle Capability",
      });
      expect(within(form).queryByRole("alert")).toBeNull();
      expect(
        within(form).getByRole("button", {
          name: "Open shared folder in Explorer",
        }),
      ).toBeEnabled();
    } finally {
      vi.useRealTimers();
    }
  });
});
