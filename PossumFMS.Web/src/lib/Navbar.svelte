<script lang="ts">
	import { fms } from '$lib/fms.svelte';
	import { page } from '$app/state';

	const matchState = $derived(fms.matchState);

	const navLinks = [
		{ href: '/', label: 'Match Play' },
		{ href: '/scoring', label: 'Scoring' },
		{ href: '/fta', label: 'FTA' },
		{ href: '/stops', label: 'Stops' },
		{ href: '/audience', label: 'Audience' }
	];
</script>

<div class="app-neutral-bg border-b border-slate-300">
	<div class="mx-auto flex max-w-[1700px] items-end gap-0.5 overflow-x-auto px-3 pt-2 text-sm">
		{#each navLinks as link}
			<a
				href={link.href}
				class="rounded-t-md border border-b-0 px-3 py-2 font-bold whitespace-nowrap {page.url.pathname ===
				link.href
					? 'border-slate-300 bg-white text-slate-900'
					: 'border-transparent text-slate-500 hover:text-slate-800'}"
				style={page.url.pathname === link.href
					? 'box-shadow: inset 0 3px 0 0 var(--color-secondary);'
					: ''}
			>
				{link.label}
			</a>
		{/each}
		<div class="ml-auto flex shrink-0 items-center gap-3 px-2 pb-1 text-xs text-slate-600">
			<span class="inline-flex items-center gap-1">
				<span
					class="h-2.5 w-2.5 rounded-full {fms.connected ? 'bg-emerald-500' : 'bg-rose-500'}"
				></span>{fms.connected ? 'Connected' : 'Connecting'}</span
			>
			<span>{matchState?.matchType ?? 'None'} #{matchState?.matchNumber ?? 0}</span>
		</div>
	</div>
</div>
