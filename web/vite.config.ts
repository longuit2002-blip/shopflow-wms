import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react-swc';
import { TanStackRouterVite } from '@tanstack/router-plugin/vite';
import path from 'node:path';

// ShopFlow WMS web frontend — Sprint-6 plan U1 + U6.
// SWC instead of Babel (faster); React 19; TanStack Router file-based
// routing (U6 wires the routes tree).
//
// Dev server proxies `/api/*` and `/auth/*` to the Gateway so JWT + tenant
// routing flow through the real backend even in dev mode.
export default defineConfig({
  plugins: [
    TanStackRouterVite({
      target: 'react',
      autoCodeSplitting: true,
      // Sprint-7.5 U7: route-schema tests live next to their routes
      // (e.g., `inventory.test.tsx` beside `inventory.tsx`). Tell the
      // generator to skip `.test.` files so they don't get treated as
      // routes themselves.
      routeFileIgnorePattern: '\\.(test|spec)\\.[tj]sx?$',
    }),
    react(),
  ],
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
