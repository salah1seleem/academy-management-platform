import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { HelpVideoPlayer, type HelpVideo } from "./help-video";
import { HelpCenter } from "./help-center";
const root = "/api/v1/help/lessons/family-renewal/media/00000000-0000-0000-0000-000000000001/";
const video: HelpVideo = { id: "00000000-0000-0000-0000-000000000001", title: "شرح الأسرة", description: "شرح اصطناعي", alternativeText: "ملخص التجديد", version: 2, durationSeconds: 2, url: root + "video", thumbnailUrl: root + "thumbnail" };
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it("renders only the exact private revision with controls, poster and a text alternative, without autoplay", () => {
  render(<HelpVideoPlayer lesson="family-renewal" video={video}/>);
  const player = document.querySelector("video")!; expect(player).toHaveAttribute("src", root + "video"); expect(player).toHaveAttribute("poster", root + "thumbnail"); expect(player).toHaveAttribute("controls"); expect(player).not.toHaveAttribute("autoplay"); expect(screen.getByText("ملخص التجديد")).toBeVisible();
  fireEvent.error(player); expect(document.querySelector("video")).toBeNull(); expect(screen.getByRole("status")).toHaveTextContent("تعذر تشغيل");
});
it("rejects an external or mismatched lesson media URL", () => {
  render(<HelpVideoPlayer lesson="family-renewal" video={{ ...video, url: "https://untrusted.invalid/video.mp4" }}/>); expect(document.querySelector("video")).toBeNull(); expect(screen.getByRole("status")).toHaveTextContent("تعذر تشغيل");
});
it("published detail shows real media alongside written steps then removes both after access is revoked", async () => {
  global.fetch = vi.fn().mockResolvedValueOnce(Response.json({ id: "family-renewal", title: { ar: "تجديد" }, summary: { ar: "راجع الإجمالي" }, section: "subscriptions", contentKind: "WrittenGuide", version: 1, videoStatus: "Published", video, steps: [{ ar: "راجع الباقة" }] })).mockResolvedValue(Response.json({}, { status: 404 }));
  render(<HelpCenter guardian id="family-renewal"/>); await screen.findByText("شرح الأسرة"); expect(document.querySelector("video")).not.toBeNull(); expect(screen.getByRole("list")).toHaveTextContent("راجع الباقة"); fireEvent(window, new Event("focus")); await screen.findByRole("alert"); expect(document.querySelector("video")).toBeNull(); expect(screen.queryByRole("list")).not.toBeInTheDocument();
});
