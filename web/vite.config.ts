import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import tailwind from '@tailwindcss/vite';
export default defineConfig({
  plugins: [react(), tailwind()],
  build: { rollupOptions: { output: { manualChunks: { realtime: ['@microsoft/signalr'], markdown: ['react-markdown'] } } } },
  server: { port: 5173, strictPort: true, proxy: { '/api': 'http://localhost:5080', '/hubs': { target: 'http://localhost:5080', ws: true } } },
  test: { environment: 'jsdom', setupFiles: ['./src/test/setup.ts'], include: ['src/**/*.test.{ts,tsx}'] },
});
