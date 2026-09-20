/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// Same-site cookie delivery, locally: the SPA and the API are ONE origin because Vite forwards
// `/api` to Kestrel UNREWRITTEN. This is the same rule CloudFront applies in production, and it
// is what lets the session cookie attach with no CORS policy and no antiforgery token. Do not
// "simplify" it into a separate API origin.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    // 5173 unless the launcher assigns another (PORT), as when a second project already holds it.
    // The proxy below is what same-site delivery rests on, and it works from any port.
    port: Number(process.env.PORT) || 5173,
    // A taken port must fail loudly: a silent shift to 5174 would break the proxy assumption.
    strictPort: true,
    proxy: {
      "/api": {
        target: "http://localhost:5111",
        changeOrigin: false,
      },
    },
  },
  test: {
    environment: "jsdom",
    globals: false,
    setupFiles: ["./src/test/setup.ts"],
    include: ["src/**/*.test.{ts,tsx}"],
  },
});
