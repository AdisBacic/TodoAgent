import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The dev server proxies /api to the .NET host so the browser sees one origin.
// SSE works through the proxy because http-proxy streams responses as they arrive.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": {
        target: "http://localhost:5080",
        changeOrigin: true,
      },
    },
  },
});
