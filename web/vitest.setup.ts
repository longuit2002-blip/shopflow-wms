import '@testing-library/jest-dom/vitest';
// Type augmentation for vitest-axe's `toHaveNoViolations` matcher — the
// `.d.ts` at `vitest-axe/extend-expect` augments the global Vi.Assertion
// interface. The runtime equivalent (the .js) is empty in this build,
// so we still register the matchers manually below.
import 'vitest-axe/extend-expect';
import { cleanup } from '@testing-library/react';
import { afterEach, expect } from 'vitest';
import * as axeMatchers from 'vitest-axe/matchers';

expect.extend(axeMatchers);

afterEach(() => {
  cleanup();
});
