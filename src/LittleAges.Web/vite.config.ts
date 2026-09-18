import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { loadEnv } from 'vite'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', '')
  const serverUrl = env.LITTLE_AGES_SERVER_URL || 'http://127.0.0.1:5274'

  return {
    plugins: [react()],
    server: {
      proxy: {
        '/api': serverUrl,
        '/hubs': { target: serverUrl, ws: true },
      },
    },
    test: {
      environment: 'jsdom',
      setupFiles: './src/test/setup.ts',
    },
  }
})
