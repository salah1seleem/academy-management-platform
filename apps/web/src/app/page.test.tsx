import { cleanup, render, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import HomePage from "./page";

const replace = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ replace }) }));

beforeEach(() => replace.mockReset());
afterEach(cleanup);

describe("auth-aware root route", () => {
  it("redirects an unauthenticated visitor to login", async () => {
    global.fetch = vi.fn().mockResolvedValue({ status: 401, ok: false });
    render(<HomePage />);
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/login"));
  });

  it("redirects a Guardian to the Guardian home", async () => {
    global.fetch = vi.fn().mockResolvedValue({ status: 200, ok: true, json: async () => ({ role: "Guardian" }) });
    render(<HomePage />);
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/guardian"));
  });

  it("redirects a staff role to the Dashboard", async () => {
    global.fetch = vi.fn().mockResolvedValue({ status: 200, ok: true, json: async () => ({ role: "AcademyAdmin" }) });
    render(<HomePage />);
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/dashboard"));
  });
});
