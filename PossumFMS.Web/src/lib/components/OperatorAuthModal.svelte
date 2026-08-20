<script lang="ts">
	import { fms } from '$lib/fms.svelte';

	let { isOpen = $bindable(false) } = $props<{ isOpen: boolean }>();

	let passwordInput = $state('');
	let authErrorMessage = $state('');
	let isAuthenticating = $state(false);

	async function authenticateOperator() {
		authErrorMessage = '';
		isAuthenticating = true;

		try {
			await fms.signIn(passwordInput);
			passwordInput = '';
			isOpen = false;
		} catch (error) {
			authErrorMessage =
				error instanceof Error ? error.message : 'Authentication failed. Please try again.';
		} finally {
			isAuthenticating = false;
		}
	}
</script>

{#if isOpen}
	<div
		class="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/60 p-4 backdrop-blur-xs"
		role="dialog"
		aria-modal="true"
	>
		<div
			class="w-full max-w-md rounded-xl border border-slate-300 bg-white p-6 text-slate-900 shadow-2xl transition-colors dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100"
		>
			<div
				class="flex items-center justify-between border-b border-slate-200 pb-3 dark:border-slate-700"
			>
				<h2 class="text-lg font-black tracking-tight text-slate-900 dark:text-slate-100">
					Operator Authentication
				</h2>
				<button
					type="button"
					onclick={() => {
						authErrorMessage = '';
						passwordInput = '';
						isOpen = false;
					}}
					class="cursor-pointer text-base font-bold text-slate-400 hover:text-slate-700 dark:text-slate-500 dark:hover:text-slate-300"
				>
					✕
				</button>
			</div>

			<p class="mt-2 text-xs text-slate-600 dark:text-slate-400">
				Protected arena operations require field operator authentication.
			</p>

			{#if authErrorMessage}
				<div
					class="mt-3 rounded border border-rose-300 bg-rose-50 px-3 py-2 text-xs font-semibold text-rose-800 dark:border-rose-700 dark:bg-rose-950/80 dark:text-rose-300"
				>
					{authErrorMessage}
				</div>
			{/if}

			<form
				onsubmit={(e) => {
					e.preventDefault();
					authenticateOperator();
				}}
				class="mt-4 space-y-4"
			>
				<div>
					<label
						for="operatorPassword"
						class="block text-xs font-bold tracking-wider text-slate-700 uppercase dark:text-slate-300"
					>
						Operator Password
					</label>
					<input
						id="operatorPassword"
						type="password"
						bind:value={passwordInput}
						placeholder="Enter operator password…"
						class="mt-1 w-full rounded border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 placeholder-slate-400 focus:border-teal-600 focus:outline-hidden dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100"
						autocomplete="current-password"
						required
					/>
				</div>

				<div
					class="flex items-center justify-end gap-2 border-t border-slate-200 pt-4 dark:border-slate-700"
				>
					<button
						type="button"
						onclick={() => {
							authErrorMessage = '';
							passwordInput = '';
							isOpen = false;
						}}
						class="cursor-pointer rounded px-3 py-1.5 text-xs font-bold text-slate-600 hover:bg-slate-100 hover:text-slate-900 dark:text-slate-400 dark:hover:bg-slate-700 dark:hover:text-slate-100"
					>
						Cancel
					</button>
					<button
						type="submit"
						disabled={isAuthenticating}
						class="brand-secondary-bg cursor-pointer rounded px-4 py-1.5 text-xs font-bold text-white shadow-xs hover:opacity-90 disabled:opacity-50"
					>
						{isAuthenticating ? 'Authenticating…' : 'Sign In'}
					</button>
				</div>
			</form>
		</div>
	</div>
{/if}
