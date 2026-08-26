import type {
  DashboardSnapshot,
  WorkspaceMessage,
} from "../presentation/DashboardSnapshot";

export function MessageHistory({
  dashboard,
  messages,
}: {
  dashboard: DashboardSnapshot;
  messages: WorkspaceMessage[];
}) {
  const members = new Map(
    dashboard.circle.members.map((member) => [member.id, member.name]),
  );
  const nodes = new Map(
    dashboard.circle.nodes.map((node) => [node.id, node.name]),
  );
  return (
    <section
      className="message-history"
      id="messages"
      aria-labelledby="messages-title"
    >
      <div className="section-heading">
        <div>
          <p className="eyebrow">Durable Circle history</p>
          <h2 id="messages-title">Messages</h2>
        </div>
        <p>Authored by a Member on an admitted Node.</p>
      </div>
      {messages.length === 0 ? (
        <p className="message-empty">No messages yet.</p>
      ) : (
        <ol className="message-list">
          {messages.map((message) => (
            <li key={message.id}>
              <header>
                <strong>
                  {members.get(message.authorMemberId) ??
                    message.authorMemberId}
                  {" · "}
                  {nodes.get(message.authorNodeId) ?? message.authorNodeId}
                </strong>
                <span>#{message.sequence}</span>
              </header>
              <p>{message.text}</p>
              <time dateTime={message.authoredAtUtc}>
                {new Date(message.authoredAtUtc).toLocaleString()}
              </time>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
