import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  build: {
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (!id.includes("node_modules")) return undefined;
          if (id.includes("@firebase/auth") || id.includes("firebase/auth")) return "firebase-auth";
          if (id.includes("@firebase/firestore") || id.includes("firebase/firestore")) return "firebase-firestore";
          if (id.includes("@firebase/functions") || id.includes("firebase/functions")) return "firebase-functions";
          if (id.includes("@firebase/storage") || id.includes("firebase/storage")) return "firebase-storage";
          if (id.includes("firebase") || id.includes("@firebase") || id.includes("google-gax")) return "firebase-core";
          if (id.includes("react") || id.includes("scheduler")) return "react";
          if (id.includes("lucide-react")) return "icons";
          return "vendor";
        }
      }
    }
  },
  server: {
    port: 5173
  }
});
