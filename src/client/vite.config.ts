import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // SW_API_URL can be set in the environment or in .env (see .env.example).
  const env = loadEnv(mode, process.cwd(), '')
  const apiUrl = env.SW_API_URL || 'http://localhost:5000'

  return {
    plugins: [react()],
    build: {
      rollupOptions: {
        output: {
          // Separate, long-cacheable vendor chunks instead of one 1 MB bundle.
          manualChunks: {
            react: ['react', 'react-dom', 'react-router-dom'],
            msal: ['@azure/msal-browser', '@azure/msal-react'],
            charts: ['recharts'],
          },
        },
      },
    },
    server: {
      // Forward API and runtime config requests to the local API, so the browser
      // talks to a single origin in development just like in production.
      proxy: {
        '/api': apiUrl,
        '/config.json': apiUrl,
      },
    },
  }
})
