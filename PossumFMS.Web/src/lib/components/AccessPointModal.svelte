<script lang="ts">
	import { fms } from '$lib/fms.svelte';
	import { stationLabel } from '$lib/utils/formatters';

	let { isOpen = $bindable(false), inputs = $bindable() } = $props<{
		isOpen: boolean;
		inputs: Array<{ team: string; wpa: string }>;
	}>();

	let apMessage = $state('');
	let isConfiguring = $state(false);

	async function pushApConfig() {
		apMessage = '';
		isConfiguring = true;

		try {
			await fms.assignTeams(
				inputs.map((entry: { team: string; wpa: string }) => ({
					teamNumber: parseInt(entry.team) || 0,
					wpaKey: entry.wpa
				}))
			);
			await fms.configureAccessPoint();
			apMessage = 'Access Point configured successfully.';
		} catch (error) {
			apMessage = error instanceof Error ? error.message : 'Failed to configure Access Point.';
		} finally {
			isConfiguring = false;
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
			class="w-full max-w-lg rounded-xl border border-slate-300 bg-white p-6 text-slate-900 shadow-2xl transition-colors dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100"
		>
			<div
				class="flex items-center justify-between border-b border-slate-200 pb-3 dark:border-slate-700"
			>
				<h2 class="text-lg font-black tracking-tight text-slate-900 dark:text-slate-100">
					Access Point & WPA Key Setup
				</h2>
				<button
					type="button"
					onclick={() => (isOpen = false)}
					class="cursor-pointer text-base font-bold text-slate-400 hover:text-slate-700 dark:text-slate-500 dark:hover:text-slate-300"
				>
					✕
				</button>
			</div>

			<p class="mt-2 text-xs text-slate-600 dark:text-slate-400">
				Set custom WPA security keys per station. If left blank, the default key (<span
					class="font-mono font-semibold text-slate-800 dark:text-slate-200">possum2718</span
				>) will be used.
			</p>

			{#if apMessage}
				<div
					class="mt-3 rounded border border-emerald-300 bg-emerald-50 px-3 py-2 text-xs font-semibold text-emerald-800 dark:border-emerald-700 dark:bg-emerald-950/80 dark:text-emerald-300"
				>
					{apMessage}
				</div>
			{/if}

			<div class="mt-4 space-y-2">
				{#each inputs as input, idx}
					<div
						class="grid grid-cols-[90px_1fr_1.5fr] items-center gap-2 rounded border border-slate-200 bg-slate-50 px-3 py-2 text-xs dark:border-slate-700 dark:bg-slate-900/60"
					>
						<span class="font-bold {idx < 3 ? 'alliance-red-text' : 'alliance-blue-text'}">
							{stationLabel(idx)}
						</span>
						<div>
							<span class="text-[11px] text-slate-500 dark:text-slate-400">Team:</span>
							<span class="ml-1 font-bold text-slate-900 dark:text-slate-100"
								>{input.team || '—'}</span
							>
						</div>
						<div>
							<input
								type="text"
								placeholder="WPA Key"
								bind:value={input.wpa}
								class="w-full rounded border border-slate-300 bg-white px-2 py-1 text-xs text-slate-900 placeholder-slate-400 focus:border-teal-600 focus:outline-hidden dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100"
							/>
						</div>
					</div>
				{/each}
			</div>

			<div
				class="mt-6 flex items-center justify-end gap-2 border-t border-slate-200 pt-4 dark:border-slate-700"
			>
				<button
					type="button"
					onclick={() => (isOpen = false)}
					class="cursor-pointer rounded px-3 py-1.5 text-xs font-bold text-slate-600 hover:bg-slate-100 hover:text-slate-900 dark:text-slate-400 dark:hover:bg-slate-700 dark:hover:text-slate-100"
				>
					Close
				</button>
				<button
					type="button"
					disabled={isConfiguring}
					onclick={pushApConfig}
					class="brand-secondary-bg cursor-pointer rounded px-4 py-1.5 text-xs font-bold text-white shadow-xs hover:opacity-90 disabled:opacity-50"
				>
					{isConfiguring ? 'Applying…' : 'Apply & Configure AP'}
				</button>
			</div>
		</div>
	</div>
{/if}
