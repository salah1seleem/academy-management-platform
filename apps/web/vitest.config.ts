import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    setupFiles: ["./vitest.setup.ts"],
    // Bound jsdom memory/CPU: deterministic defaults on local and CI runners.
    maxWorkers: 2,
  },
});
