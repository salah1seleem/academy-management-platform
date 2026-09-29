import { describe, expect, it } from "vitest";
import manifest from "./manifest";

describe("PWA manifest", () => {
  it("starts from the login route", () => expect(manifest().start_url).toBe("/login"));
  it("keeps Arabic RTL metadata", () => expect(manifest()).toMatchObject({ lang: "ar", dir: "rtl", display: "standalone" }));
});
