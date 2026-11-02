import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it } from "vitest";
import { avatarPath, PersonPortrait } from "./person-portrait";

describe("shared illustrated portraits", () => {
  afterEach(cleanup);
  it("uses recorded gender and neutral unknowns", () => {
    expect(avatarPath(false, 1)).toBe("/avatars/child-male.svg");
    expect(avatarPath(true, "Female")).toBe("/avatars/adult-female.svg");
    expect(avatarPath(false, "اسم")).toBe("/avatars/child-neutral.svg");
  });
  it("keeps private photo precedence and recovers from a broken one", async () => {
    const photo = "/api/v1/assets/11111111-1111-1111-1111-111111111111";
    render(<PersonPortrait name="لاعب خيالي" photo={photo} gender={2} />);
    const image = screen.getByRole("img");
    expect(image.getAttribute("src")).toContain(photo);
    fireEvent.error(image);
    await waitFor(() => expect(image.getAttribute("src")).toContain("/avatars/child-female.svg"));
  });
  it("does not request an arbitrary remote portrait", () => {
    render(<PersonPortrait photo="https://example.test/private.jpg" adult />);
    expect(screen.getByRole("img").getAttribute("src")).toContain("/avatars/adult-neutral.svg");
  });
});
