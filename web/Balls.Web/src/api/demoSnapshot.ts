import type { CircleDetailsDto, StatusDto } from "./localControl";
import type { DashboardSnapshot } from "../presentation/DashboardSnapshot";

const status = {
  productVersion: "0.1.0-alpha.2",
  protocolVersion: 1,
  node: {
    id: "0198f2cc-6a50-7a08-aacb-298f4ebdf616",
    displayName: "Alice-PC",
    createdAtUtc: "2026-08-19T12:00:00.0000000+00:00",
  },
} satisfies StatusDto;

const circle = {
  circle: {
    id: "0198f2cc-6a50-7a08-aacb-298f4ebdf620",
    name: "Example Studio",
    createdAtUtc: "2026-08-19T12:05:00.0000000+00:00",
    memberCount: 3,
    nodeCount: 2,
  },
  members: [
    {
      id: "0198f2cc-6a50-7a08-aacb-298f4ebdf621",
      displayName: "Alice Morgan",
      role: "owner",
      joinedAtUtc: "2026-08-19T12:05:00.0000000+00:00",
    },
    {
      id: "0198f2cc-6a50-7a08-aacb-298f4ebdf622",
      displayName: "Bob Chen",
      role: "member",
      joinedAtUtc: "2026-08-19T12:08:00.0000000+00:00",
    },
    {
      id: "0198f2cc-6a50-7a08-aacb-298f4ebdf623",
      displayName: "Casey Rivera",
      role: "member",
      joinedAtUtc: "2026-08-19T12:11:00.0000000+00:00",
    },
  ],
  nodes: [
    {
      id: status.node.id,
      displayName: status.node.displayName,
      joinedAtUtc: "2026-08-19T12:05:00.0000000+00:00",
    },
    {
      id: "0198f2cc-6a50-7a08-aacb-298f4ebdf624",
      displayName: "Office-Server",
      joinedAtUtc: "2026-08-19T12:09:00.0000000+00:00",
    },
  ],
} satisfies CircleDetailsDto;

export const demoDashboard: DashboardSnapshot = {
  productVersion: status.productVersion,
  protocolVersion: Number(status.protocolVersion),
  localNode: {
    id: status.node.id,
    name: status.node.displayName,
    createdAtUtc: status.node.createdAtUtc,
  },
  circle: {
    id: circle.circle.id,
    name: circle.circle.name,
    createdAtUtc: circle.circle.createdAtUtc,
    members: circle.members.map((member) => ({
      id: member.id,
      name: member.displayName,
      role: member.role,
      joinedAtUtc: member.joinedAtUtc,
    })),
    nodes: circle.nodes.map((node) => ({
      id: node.id,
      name: node.displayName,
      joinedAtUtc: node.joinedAtUtc,
      isLocal: node.id === status.node.id,
    })),
  },
};
