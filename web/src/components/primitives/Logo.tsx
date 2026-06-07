/**
 * Dot-matrix ShopFlow logo — 4×4 grid mark ported from the design canon
 * `app.jsx` Sidebar (~line 65). The original was an inline grid of <span>
 * elements; this component renders the same shapes as a single SVG so it
 * scales cleanly via the `size` prop and inherits color from currentColor.
 *
 * Bit pattern (1 = dot on, 0 = dot off):
 *   1 1 1 0     row 0
 *   1 0 0 1     row 1
 *   1 0 0 1     row 2
 *   1 1 1 0     row 3
 *
 * Spells out a stylized "S" / "P" hybrid in the original grid; the SVG
 * preserves the dot:gap ratio of 4:2 px → viewBox 22×22, dot 4×4, gap 2.
 */

const PATTERN: ReadonlyArray<readonly number[]> = [
  [1, 1, 1, 0],
  [1, 0, 0, 1],
  [1, 0, 0, 1],
  [1, 1, 1, 0],
];

const DOT = 4;
const GAP = 2;
const VIEW = DOT * 4 + GAP * 3; // 22

export interface LogoProps {
  /** Rendered pixel size of the (square) logo. Default: 22 (1:1 with viewBox). */
  size?: number;
  /** Optional title for assistive tech. Defaults to "ShopFlow logo". */
  title?: string;
  className?: string;
}

export function Logo({ size = 22, title = 'ShopFlow logo', className }: LogoProps) {
  return (
    <svg
      xmlns="http://www.w3.org/2000/svg"
      width={size}
      height={size}
      viewBox={`0 0 ${VIEW} ${VIEW}`}
      role="img"
      aria-label={title}
      className={className}
      style={{ flex: 'none' }}
    >
      <title>{title}</title>
      {PATTERN.flatMap((row, r) =>
        row.map((on, c) =>
          on ? (
            <rect
              key={`${r}-${c}`}
              x={c * (DOT + GAP)}
              y={r * (DOT + GAP)}
              width={DOT}
              height={DOT}
              rx={1}
              fill="currentColor"
            />
          ) : null,
        ),
      )}
    </svg>
  );
}
