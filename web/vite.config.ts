import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react-swc';
import path from 'node:path';

// ShopFlow WMS web frontend — Sprint-6 plan U1.
// SWC instead of Babel (faster); React 19.
// TanStack Router plugin is wired in U6 once the `src/routes/` tree exists.
// Dev server proxies `/api/*` and `/auth/*` to the Gateway so JWT + tenant
// routing flow through the real backend even in dev mode.
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  server: {
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: 'http://localhost:8080',
        changeOrigin: true,
      },
      '/auth': {
        target: 'http://localhost:8080',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
    target: 'es2022',
  },
});
