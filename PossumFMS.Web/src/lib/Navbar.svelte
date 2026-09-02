<script lang="ts">
	import { fms } from '$lib/fms.svelte';
	import { theme } from '$lib/theme.svelte';
	import { page } from '$app/state';

	const matchState = $derived(fms.matchState);
	let password = $state('');
	let loginError = $state('');
	let isSigningIn = $state(false);

	async function signIn() {
		loginError = '';
		isSigningIn = true;
		try {
			await fms.signIn(password);
			password = '';
		} catch (error) {
			loginError = error instanceof Error ? error.message : 'Unable to sign in.';
		} finally {
			isSigningIn = false;
		}
	}

	const navLinks = [
		{ href: '/', label: 'Match Play' },
		{ href: '/scoring', label: 'Scoring' },
		{ href: '/referee', label: 'Referee' },
		{ href: '/fta', label: 'FTA' },
		{ href: '/stops', label: 'Stops' },
		{ href: '/audience', label: 'Audience' }
	];
</script>

<div class="app-neutral-bg border-b border-slate-300 transition-colors dark:border-slate-700">
	<div
		class="mx-auto flex max-w-425 flex-col gap-1.5 px-3 pt-2 text-sm md:flex-row md:items-end md:gap-0.5"
	>
		<!-- Utility controls (Theme, connection, match, operator login) -->
		<div
			class="flex flex-wrap items-center justify-between gap-2 pb-1 text-xs text-slate-600 md:order-2 md:ml-auto md:shrink-0 dark:text-slate-300"
		>
			<div class="flex items-center gap-2">
				<!-- Light / Dark Mode Toggle Button -->
				<button
					type="button"
					onclick={() => theme.toggle()}
					title={theme.isDark ? 'Switch to light mode' : 'Switch to dark mode'}
					aria-label="Toggle theme"
					class="flex size-7 cursor-pointer items-center justify-center rounded border border-slate-300 bg-white text-slate-700 shadow-xs transition hover:bg-slate-100 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-700"
				>
					{#if theme.isDark}
						<!-- Sun icon -->
						<svg
							class="size-4 text-amber-400"
							fill="none"
							viewBox="0 0 24 24"
							stroke="currentColor"
						>
							<path
								stroke-linecap="round"
								stroke-linejoin="round"
								stroke-width="2"
								d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364 6.364l-.707-.707M6.343 6.343l-.707-.707m12.728 0l-.707.707M6.343 17.657l-.707.707M16 12a4 4 0 11-8 0 4 4 0 018 0z"
							/>
						</svg>
					{:else}
						<!-- Moon icon -->
						<svg
							class="size-4 text-slate-600"
							fill="none"
							viewBox="0 0 24 24"
							stroke="currentColor"
						>
							<path
								stroke-linecap="round"
								stroke-linejoin="round"
								stroke-width="2"
								d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z"
							/>
						</svg>
					{/if}
				</button>

				<span class="inline-flex items-center gap-1">
					<span class="size-2.5 rounded-full {fms.connected ? 'bg-emerald-500' : 'bg-rose-500'}"
					></span>{fms.connected ? 'Connected' : 'Connecting'}
				</span>
				<span class="font-medium"
					>{matchState?.matchType ?? 'None'} #{matchState?.matchNumber ?? 0}</span
				>
			</div>

			<div class="flex items-center gap-2">
				{#if fms.operatorAuthenticated}
					<button
						type="button"
						onclick={() => void fms.signOut()}
						class="cursor-pointer rounded border border-emerald-700 bg-emerald-50 px-2 py-1 font-bold text-emerald-800 hover:bg-emerald-100 dark:bg-emerald-950/60 dark:text-emerald-300 dark:hover:bg-emerald-900/80"
					>
						Operator signed in
					</button>
				{:else}
					<form
						class="flex items-center gap-1"
						onsubmit={(event) => {
							event.preventDefault();
							void signIn();
						}}
					>
						<label class="sr-only" for="operator-password">Operator password</label>
						<input
							id="operator-password"
							type="password"
							bind:value={password}
							autocomplete="current-password"
							placeholder="Password"
							class="h-7 w-28 rounded border border-slate-400 bg-white px-2 text-xs text-slate-900 placeholder-slate-400 sm:w-36 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100"
						/>
						<button
							type="submit"
							disabled={isSigningIn || password.length === 0}
							class="brand-secondary-bg h-7 cursor-pointer rounded px-2 font-bold text-white hover:opacity-90 disabled:opacity-50"
						>
							{isSigningIn ? '...' : 'Sign in'}
						</button>
					</form>
				{/if}
				{#if loginError}<span class="max-w-48 text-rose-700 dark:text-rose-400">{loginError}</span
					>{/if}
			</div>
		</div>

		<!-- Nav Links: scrollable tabs -->
		<div class="-mx-3 flex items-end gap-0.5 overflow-x-auto px-3 md:order-1 md:mx-0 md:px-0">
			{#each navLinks as link}
				<a
					href={link.href}
					class="rounded-t-md border border-b-0 px-3 py-2 font-bold whitespace-nowrap transition-colors {page
						.url.pathname === link.href
						? 'border-slate-300 bg-white text-slate-900 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100'
						: 'border-transparent text-slate-500 hover:text-slate-800 dark:text-slate-400 dark:hover:text-slate-200'}"
					style={page.url.pathname === link.href
						? 'box-shadow: inset 0 3px 0 0 var(--color-secondary);'
						: ''}
				>
					{link.label}
				</a>
			{/each}
		</div>
	</div>
</div>
