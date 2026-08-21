import type { DashboardSnapshot } from "../presentation/DashboardSnapshot";
import { BrandMark } from "./BrandMark";

interface CircleTopologyProps {
  circle: DashboardSnapshot["circle"];
}

function initials(name: string) {
  return name
    .split(/[-\s]+/)
    .slice(0, 2)
    .map((part) => part[0])
    .join("")
    .toUpperCase();
}

export function CircleTopology({ circle }: CircleTopologyProps) {
  return (
    <section className="topology" aria-labelledby="topology-title">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Circle structure</p>
          <h2 id="topology-title">Together in {circle.name}</h2>
        </div>
        <p>People are first-class. Nodes make the Circle useful.</p>
      </div>

      <div className="trust-thread">
        <div className="circle-hub" aria-label={`${circle.name} summary`}>
          <BrandMark />
          <span>Circle</span>
          <strong>{circle.name}</strong>
          <small>
            {circle.members.length} people · {circle.nodes.length} Nodes
          </small>
        </div>

        <section
          className="roster-panel"
          id="people"
          aria-labelledby="people-title"
        >
          <div className="roster-header">
            <p className="eyebrow">People</p>
            <h3 id="people-title">Members</h3>
          </div>
          <ul className="roster-list">
            {circle.members.map((member) => (
              <li key={member.id}>
                <span className="avatar member-avatar" aria-hidden="true">
                  {initials(member.name)}
                </span>
                <span className="roster-name">
                  <strong>{member.name}</strong>
                  <span>{member.role}</span>
                </span>
                <code title={member.id}>{member.id.slice(-6)}</code>
              </li>
            ))}
          </ul>
        </section>

        <section
          className="roster-panel"
          id="nodes"
          aria-labelledby="nodes-title"
        >
          <div className="roster-header">
            <p className="eyebrow">Infrastructure</p>
            <h3 id="nodes-title">Nodes</h3>
          </div>
          <ul className="roster-list">
            {circle.nodes.map((node) => (
              <li key={node.id}>
                <span className="avatar node-avatar" aria-hidden="true">
                  {initials(node.name)}
                </span>
                <span className="roster-name">
                  <strong>{node.name}</strong>
                  <span>{node.isLocal ? "This device" : "Dedicated Node"}</span>
                </span>
                <span className="online-state">Online</span>
              </li>
            ))}
          </ul>
        </section>
      </div>
    </section>
  );
}
