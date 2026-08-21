# Balls browser workspace

The root [`balls-brand.png`](../../balls-brand.png) is the canonical visual source. The browser
uses a production SVG component derived from its connected-node brandmark; do not embed the full
brand board or invent a second logo.

## Visual foundation

- Carbon `#111317`, graphite `#3A3D45`, signal indigo `#6366F1`, and mist `#E6E8EB` are the core
  palette. Semantic success and error colors communicate state; they are not alternate brand
  accents.
- Manrope carries product language and hierarchy. JetBrains Mono is reserved for status,
  identifiers, compact labels, and protocol facts.
- The connected-node mark and trust thread are the signature. Keep surrounding layout quiet,
  structural, and Circle-first.
- Reuse the tokens in `src/styles.css` and the `BrandMark` component. Add a component only when
  real browser behavior repeats; this is not a general-purpose design-system package.

## Interaction rules

- Keep Circle application behavior behind the typed browser API. React presents loading, empty,
  selected, switching, busy, and error states; it does not own durable product state.
- Preserve semantic landmarks, one page heading, keyboard order, visible `:focus-visible` styles,
  `aria-current`, `aria-busy`, live status, and alert roles.
- Motion may clarify a connection once. Every animation must collapse under
  `prefers-reduced-motion: reduce` without hiding content.
- At narrow widths, stack the Circle topology, keep the Circle switcher horizontally scrollable,
  and never introduce document-level horizontal overflow.
