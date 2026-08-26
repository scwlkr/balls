# ADR 0013 — Use the Office Server Node as the First Anchor

- **Status:** Accepted
- **Date:** 2026-08-26

The Office Server Node is the Office Circle's first Anchor so its live membership, authorization,
invitation, catalog, and coordination state remain available when an Owner's personal computer is
offline. The Circle Authority and accepted recovery material must also have a separately protected,
tested recovery copy; the server is the first durable home of Circle state, not the permanent owner
or only recoverable copy of the Circle.

One Office Server Node is sufficient for the initial two- or three-person deployment. Automatic
failover and a second live server are not launch requirements; planned downtime or server failure
temporarily removes the capabilities hosted there.
