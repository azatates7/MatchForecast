import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// /api istekleri geliştirme sırasında .NET API'ye yönlendirilir (CORS gerekmez).
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
})
