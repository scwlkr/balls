import { useState } from "react";

import type { BrowserApi } from "../api/browserApi";
import type {
  DashboardSnapshot,
  WorkspaceMessage,
  WorkspaceViewer,
} from "../presentation/DashboardSnapshot";
import { CircleTopology } from "./CircleTopology";
import { FilesMappingPanel } from "./FilesMappingPanel";
import { InvitationPanel } from "./InvitationPanel";
import { MessageHistory } from "./MessageHistory";
import { OwnerContributionPanel } from "./OwnerContributionPanel";
import { OwnerGrantPanel } from "./OwnerGrantPanel";

export function CircleWorkspace({
  dashboard,
  viewer,
  messages,
  api,
}: {
  dashboard: DashboardSnapshot;
  viewer: WorkspaceViewer;
  messages: WorkspaceMessage[];
  api: BrowserApi;
}) {
  const { circle } = dashboard;
  const [filesRevision, setFilesRevision] = useState(0);
  const activeMember = circle.members.find(
    (member) => member.id === viewer.memberId,
  );

  return (
    <>
      <section
        className="circle-intro"
        id="circle"
        aria-labelledby="circle-title"
      >
        <div>
          <p className="eyebrow">Circle home</p>
          <h1 id="circle-title">{circle.name}</h1>
          <p className="circle-thesis">
            A shared digital place owned by the people inside it.
          </p>
        </div>
        <dl className="circle-identity">
          <div>
            <dt>Circle ID</dt>
            <dd>{circle.id}</dd>
          </div>
          <div>
            <dt>Created</dt>
            <dd>
              <time dateTime={circle.createdAtUtc}>
                {formatDate(circle.createdAtUtc)}
              </time>
            </dd>
          </div>
        </dl>
      </section>
      <dl className="active-membership" aria-label="Active Circle membership">
        <div>
          <dt>Active Circle</dt>
          <dd>{circle.name}</dd>
        </div>
        <div>
          <dt>Joined as</dt>
          <dd data-member-id={viewer.memberId}>
            {activeMember?.name ?? viewer.memberId}
          </dd>
        </div>
      </dl>
      <CircleTopology circle={circle} />
      {viewer.role === "owner" ? (
        <>
          <OwnerContributionPanel
            api={api}
            circleId={circle.id}
            onContributed={() => setFilesRevision((value) => value + 1)}
          />
          <OwnerGrantPanel
            key={`${circle.id}:${filesRevision}`}
            api={api}
            dashboard={dashboard}
            circleId={circle.id}
          />
          <InvitationPanel api={api} circleId={circle.id} />
        </>
      ) : null}
      {viewer.role === "member" ? (
        <FilesMappingPanel
          key={`${circle.id}:${filesRevision}`}
          api={api}
          viewer={viewer}
          circleId={circle.id}
        />
      ) : null}
      <MessageHistory dashboard={dashboard} messages={messages} />
    </>
  );
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat(undefined, {
    year: "numeric",
    month: "long",
    day: "numeric",
  }).format(new Date(value));
}
