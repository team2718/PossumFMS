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
		class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-xs"
		role="dialog"
		aria-modal="true"
	>
		<div
			class="w-full max-w-md rounded-2xl border border-slate-700 bg-slate-900 p-6 text-white shadow-2xl"
		>
			<h2 class="text-xl font-black tracking-tight">Operator Authentication</h2>
			<p class="mt-1 text-sm text-slate-400">
				Protected arena operations require field operator authentication.
			</p>

			{#if authErrorMessage}
				<div
					class="mt-4 rounded-lg border border-rose-800 bg-rose-950/80 px-3 py-2 text-xs font-semibold text-rose-200"
				>
					{authErrorMessage}
				</div>
			{/if}

			<form
				onsubmit={(e) => {
					e.preventDefault();
					authenticateOperator();
				}}
				class="mt-5 space-y-4"
			>
				<div>
					<label for="operatorPassword" class="block text-xs font-bold uppercase tracking-wider text-slate-300">
						Operator Password
					</label>
					<input
						id="operatorPassword"
						type="password"
						bind:value={passwordInput}
						placeholder="Enter operator password…"
						class="mt-1 w-full rounded-lg border border-slate-700 bg-slate-800 px-3 py-2 text-sm text-white placeholder-slate-500 focus:border-emerald-500 focus:outline-hidden"
						autocomplete="current-password"
						required
					/>
				</div>

				<div class="flex items-center justify-end gap-2 pt-2">
					<button
						type="button"
						onclick={() => {
							authErrorMessage = '';
							passwordInput = '';
							isOpen = false;
						}}
						class="cursor-pointer rounded-lg px-4 py-2 text-xs font-bold text-slate-400 hover:bg-slate-800 hover:text-white"
					>
						Cancel
					</button>
					<button
						type="submit"
						disabled={isAuthenticating}
						class="cursor-pointer rounded-lg bg-emerald-600 px-4 py-2 text-xs font-bold text-white shadow-md hover:bg-emerald-500 disabled:opacity-50"
					>
						{isAuthenticating ? 'Authenticating…' : 'Sign In'}
					</button>
				</div>
			</form>
		</div>
	</div>
{/if}

