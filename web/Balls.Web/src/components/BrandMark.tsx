export function BrandMark() {
  return (
    <svg
      className="brand-mark"
      viewBox="0 0 64 64"
      aria-hidden="true"
      focusable="false"
    >
      <g className="brand-mark-core">
        <path d="M10 18 29 7l18 11v22L29 51 10 40Z" />
        <path d="M29 7v22m-19-11 19 11 18-11M10 40l19-11v22" />
        <circle cx="10" cy="18" r="4.5" />
        <circle cx="29" cy="7" r="4.5" />
        <circle cx="47" cy="18" r="4.5" />
        <circle cx="10" cy="40" r="4.5" />
        <circle cx="29" cy="29" r="4.5" />
        <circle cx="29" cy="51" r="4.5" />
      </g>
      <g className="brand-mark-signal">
        <path d="m29 51 20-11V29" />
        <circle cx="49" cy="40" r="4.5" />
        <circle cx="49" cy="29" r="4.5" />
      </g>
    </svg>
  );
}
