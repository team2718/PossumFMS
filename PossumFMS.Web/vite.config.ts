import tailwindcss from '@tailwindcss/vite';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

export default defineConfig({
	plugins: [tailwindcss(), sveltekit()],
	server: {
		proxy: {
			// Forward SignalR hub and its WebSocket negotiation to the .NET backend
			'/fmshub': {
				target: 'http://localhost:5000',
				ws: true,
				changeOrigin: true
			}
		}
	}
});
