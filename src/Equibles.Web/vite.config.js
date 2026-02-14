import { defineConfig } from 'vite'
import { resolve } from 'path'

export default defineConfig({
    build: {
        outDir: 'wwwroot/dist',
        emptyOutDir: true,
        rollupOptions: {
            input: resolve(__dirname, 'src/index.js'),
            output: {
                entryFileNames: 'bundle.js',
                assetFileNames: (assetInfo) => {
                    if (assetInfo.name?.endsWith('.css')) {
                        return 'main.css'
                    }
                    return '[name][extname]'
                }
            }
        }
    }
})
