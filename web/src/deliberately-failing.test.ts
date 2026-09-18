import { expect, test } from "vitest";

test("deliberately fails to prove the PR gate goes red (issue #66)", () => {
  expect(1).toBe(2);
});
