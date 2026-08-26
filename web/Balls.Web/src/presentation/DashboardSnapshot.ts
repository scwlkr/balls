export interface DashboardSnapshot {
  productVersion: string;
  protocolVersion: number;
  localNode: {
    id: string;
    name: string;
    createdAtUtc: string;
  };
  circle: {
    id: string;
    name: string;
    createdAtUtc: string;
    members: Array<{
      id: string;
      name: string;
      role: string;
      joinedAtUtc: string;
    }>;
    nodes: Array<{
      id: string;
      name: string;
      joinedAtUtc: string;
      isLocal: boolean;
    }>;
  };
}

export interface WorkspaceViewer {
  memberId: string;
  role: string;
}

export interface WorkspaceMessage {
  id: string;
  sequence: string | number;
  authorMemberId: string;
  authorNodeId: string;
  text: string;
  authoredAtUtc: string;
}
