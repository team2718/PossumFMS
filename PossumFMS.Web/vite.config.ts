import tailwindcss from '@tailwindcss/vite';
import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

export default defineConfig({
	plugins: [tailwindcss(), sveltekit()],
	server: {
		// Windows accepts IPv4-mapped connections on this IPv6 wildcard socket.
		host: '::',
		proxy: {
			// Forward SignalR hub and its WebSocket negotiation to the .NET backend
			'/fmshub': {
				target: 'http://127.0.0.1:80',
				ws: true,
				changeOrigin: true
			},
			// The operator session is issued by ASP.NET Core, not SvelteKit.
			'/auth': {
				target: 'http://127.0.0.1:80',
				changeOrigin: true
			}
		}
	}
});
