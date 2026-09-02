<script lang="ts">
	import './layout.css';
	import favicon from '$lib/assets/favicon.svg';
	import { theme } from '$lib/theme.svelte';
	import { page } from '$app/state';

	let { children } = $props();

	$effect(() => {
		const isAudience = page.url.pathname.startsWith('/audience');
		if (isAudience) {
			document.documentElement.classList.remove('dark');
			document.documentElement.style.colorScheme = 'light';
		} else if (theme.isDark) {
			document.documentElement.classList.add('dark');
			document.documentElement.style.colorScheme = 'dark';
		} else {
			document.documentElement.classList.remove('dark');
			document.documentElement.style.colorScheme = 'light';
		}
	});
</script>

<svelte:head>
	<link rel="icon" href={favicon} />
</svelte:head>

{@render children()}
