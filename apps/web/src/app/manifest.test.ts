import { describe, expect, it } from "vitest";
import manifest from "./manifest";

describe("PWA manifest", () => {
  it("starts from the login route", () => expect(manifest().start_url).toBe("/login"));
  it("keeps Arabic RTL metadata", () => expect(manifest()).toMatchObject({ lang: "ar", dir: "rtl", display: "standalone" }));
  it("uses the approved academy identity for build-time install assets", () => expect(manifest()).toMatchObject({ name: "The Professional Academy", theme_color: "#D71935", icons: [{ src: "/branding/icon-192.png", sizes: "192x192", type: "image/png" }, { src: "/branding/icon-512.png", sizes: "512x512", type: "image/png" }] }));
});
