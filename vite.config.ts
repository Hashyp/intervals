import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

declare const process: {
  env: Record<string, string | undefined>;
};

const apiTarget =
  process.env.services__api__http__0 ?? process.env.services__api__https__0;

export default defineConfig({
  base: "/intervals/",
  plugins: [react()],
  server: {
    proxy: apiTarget
      ? {
          "/api": {
            target: apiTarget,
            changeOrigin: true,
            secure: false,
          },
        }
      : undefined,
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: "./src/test/setup.ts",
  },
});
