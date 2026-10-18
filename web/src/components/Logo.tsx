/** App mark: a mouse silhouette with a lit scroll wheel on an ice-blue tile. */
export function Logo({ size = 22 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 32 32" fill="none" aria-hidden>
      <defs>
        <linearGradient id="nc-tile" x1="4" y1="2" x2="28" y2="30" gradientUnits="userSpaceOnUse">
          <stop stopColor="#8be0ff" />
          <stop offset="1" stopColor="#2f9fe8" />
        </linearGradient>
        <linearGradient id="nc-body" x1="16" y1="6" x2="16" y2="27" gradientUnits="userSpaceOnUse">
          <stop stopColor="#14161a" />
          <stop offset="1" stopColor="#0b0c0e" />
        </linearGradient>
      </defs>
      <rect width="32" height="32" rx="9" fill="url(#nc-tile)" />
      {/* body */}
      <path d="M16 6.5c-4.4 0-7 3-7 7.6v4.6c0 4.6 2.9 7.8 7 7.8s7-3.2 7-7.8v-4.6c0-4.6-2.6-7.6-7-7.6Z" fill="url(#nc-body)" />
      {/* button split */}
      <path d="M16 6.8v6.4M9.2 13.4h13.6" stroke="#8be0ff" strokeOpacity=".35" strokeWidth="1" />
      {/* wheel */}
      <rect x="14.9" y="8.6" width="2.2" height="3.6" rx="1.1" fill="#8be0ff" />
    </svg>
  )
}
