import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
export default defineConfig({
    plugins: [react()],
    server: {
        port: 5173,
        proxy: {
            '/agent': {
                target: 'http://localhost:8090',
                changeOrigin: true,
                rewrite: (path) => path.replace(/^\/agent/, ''),
            },
            '/scope': {
                target: 'https://localhost:7146',
                changeOrigin: true,
                secure: false,
            }, 
            '/articles': {
                target: 'https://localhost:7146',
                changeOrigin: true,
                secure: false,
            },
            '/search': {
                target: 'https://localhost:7146',
                changeOrigin: true,
                secure: false,
            }
        }
    }
})
