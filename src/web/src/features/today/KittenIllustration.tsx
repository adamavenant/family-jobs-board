/** A small, original kitten face drawn for the board's pinned date bar. Purely decorative. */
export function KittenIllustration({ className }: { className?: string }) {
  return (
    <svg
      className={className}
      viewBox="0 0 64 64"
      width="40"
      height="40"
      aria-hidden="true"
      focusable="false"
    >
      <path className="kitten__fur" d="M9 30 14 5 30 19Z" />
      <path className="kitten__fur" d="M55 30 50 5 34 19Z" />
      <path className="kitten__ear" d="M14 24 16.5 12 25 19Z" />
      <path className="kitten__ear" d="M50 24 47.5 12 39 19Z" />
      <ellipse className="kitten__fur" cx="32" cy="37" rx="23" ry="20" />
      <path className="kitten__stripe" d="M32 18v7M25 19.5l2 6M39 19.5l-2 6" />
      <ellipse className="kitten__cheek" cx="18" cy="43" rx="4.5" ry="3" />
      <ellipse className="kitten__cheek" cx="46" cy="43" rx="4.5" ry="3" />
      <ellipse className="kitten__eye" cx="23.5" cy="35" rx="3.5" ry="4.5" />
      <ellipse className="kitten__eye" cx="40.5" cy="35" rx="3.5" ry="4.5" />
      <circle className="kitten__shine" cx="24.8" cy="33.4" r="1.3" />
      <circle className="kitten__shine" cx="41.8" cy="33.4" r="1.3" />
      <path className="kitten__nose" d="M29.5 41.5h5L32 44.5Z" />
      <path
        className="kitten__line"
        d="M32 44.5c0 3-3.5 4.5-6 2.5M32 44.5c0 3 3.5 4.5 6 2.5M15 40l-9-2M15 44H5.5M49 40l9-2M49 44h9.5"
      />
    </svg>
  );
}
