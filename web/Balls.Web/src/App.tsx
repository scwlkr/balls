import { demoDashboard } from "./api/demoSnapshot";
import { CircleTopology } from "./components/CircleTopology";
import { StatusBanner } from "./components/StatusBanner";

export function App() {
  const { circle } = demoDashboard;

  return (
    <>
      <a className="skip-link" href="#main-content">
        Skip to Circle
      </a>
      <div className="app-shell">
        <header className="masthead">
          <a className="brand" href="#main-content" aria-label="Balls home">
            <span className="brand-mark" aria-hidden="true">
              B
            </span>
            <span>Balls</span>
          </a>
          <nav aria-label="Circle navigation">
            <a href="#circle" aria-current="page">
              Home
            </a>
            <a href="#people">People</a>
            <a href="#nodes">Nodes</a>
          </nav>
          <span className="local-label">Local workspace</span>
        </header>

        <main id="main-content">
          <StatusBanner snapshot={demoDashboard} />

          <section
            className="circle-intro"
            id="circle"
            aria-labelledby="circle-title"
          >
            <div>
              <p className="eyebrow">Your Circle</p>
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
                  <time dateTime={circle.createdAtUtc}>August 19, 2026</time>
                </dd>
              </div>
            </dl>
          </section>

          <CircleTopology circle={circle} />
        </main>

        <footer>
          <span>One local API. One Circle view.</span>
          <code>{demoDashboard.localNode.id}</code>
        </footer>
      </div>
    </>
  );
}
