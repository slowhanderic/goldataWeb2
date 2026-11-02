import { existsSync, readFileSync } from 'node:fs'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// Local settings come from the same ONE file as the API: deploy/env/local.env (API_URL).
// In UAT/prod nginx serves the build and proxies /api and /mcp (deploy/nginx.conf), so nothing here applies.
function localEnv(key: string, fallback: string): string {
  const file = new URL('../deploy/env/local.env', import.meta.url)
  if (!existsSync(file)) return fallback
  const line = readFileSync(file, 'utf8').split(/\r?\n/).find((l) => l.startsWith(`${key}=`))
  return line ? line.slice(key.length + 1).trim() : fallback
}
const apiUrl = process.env.API_URL ?? localEnv('API_URL', 'http://localhost:5080')

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': apiUrl,
      '/mcp': apiUrl,
      '/health': apiUrl,
    },
  },
  build: { outDir: 'dist', sourcemap: false, chunkSizeWarningLimit: 1500 },
})
