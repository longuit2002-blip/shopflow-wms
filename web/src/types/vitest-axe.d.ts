/**
 * Module augmentation for vitest-axe — Sprint-6 plan U13.
 *
 * `vitest-axe@0.1.0` ships type augmentation against the obsolete
 * `Vi.Assertion` namespace (Vitest 0.x), but the runtime + test suite
 * runs on Vitest 2.x where the assertion interface lives in the
 * `vitest` module. The package's `extend-expect.d.ts` is therefore a
 * no-op for type-checking; this file re-declares the augmentation
 * against the right interface so `expect(axeResults).toHaveNoViolations()`
 * type-checks.
 *
 * Sprint-7 follow-up: file a PR against vitest-axe to update its
 * augmentation OR migrate to a maintained fork; remove this shim.
 */

import 'vitest';

interface NoViolationsMatcher {
  /** Asserts the axe-core results object contains zero violations. */
  toHaveNoViolations(): void;
}

declare module 'vitest' {
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type
  interface Assertion extends NoViolationsMatcher {}
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type
  interface AsymmetricMatchersContaining extends NoViolationsMatcher {}
}
