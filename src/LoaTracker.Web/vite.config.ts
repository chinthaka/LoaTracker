import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

const apiTarget = process.env.API_HTTPS || process.env.API_HTTP;

export default defineConfig({
  plugins: [react()],
  server: {
    host: true,
    strictPort: true,
    proxy: apiTarget
      ? {
          "/loa": {
            target: apiTarget,
            changeOrigin: true,
            secure: false,
          },
        }
      : undefined,
  },
});
