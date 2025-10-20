import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'

// Fix for __dirname in ESM
const __filename = fileURLToPath(import.meta.url)
const __dirname = dirname(__filename)

export default defineConfig({
    plugins: [react()],
    server: {
        port: 5173,
        proxy: {
            '/api':
            {
                target: 'https://localhost:7146', // match your backend dev port
                changeOrigin: true,
                secure: false, 
            } 
        }
    },
    build: {
        outDir: resolve(__dirname, '../wwwroot'),
        emptyOutDir: true
    }
})
