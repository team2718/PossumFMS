import adapter from '@sveltejs/adapter-static';

/** @type {import('@sveltejs/kit').Config} */
const config = {
	// Force runes mode for all project files.
	// This is read by both the Vite plugin (build) and the Svelte Language Server (editor).
	compilerOptions: {
		runes: true
	},
	kit: { adapter: adapter() }
};

export default config;
