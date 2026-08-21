export function BrandMark() {
  return (
    <svg
      className="brand-mark"
      viewBox="0 0 64 64"
      aria-hidden="true"
      focusable="false"
    >
      <g className="brand-mark-core">
        <path d="m10 18 19-11 18 11M10 18v22l19 11M29 7v44" />
        <path d="m10 18 19 11 18-11M10 40l19-11m-19 11 19 11" />
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
