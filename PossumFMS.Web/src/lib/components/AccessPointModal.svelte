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
		class="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-xs"
		role="dialog"
		aria-modal="true"
	>
		<div
			class="w-full max-w-lg rounded-2xl border border-slate-700 bg-slate-900 p-6 text-white shadow-2xl"
		>
			<div class="flex items-center justify-between">
				<h2 class="text-xl font-black tracking-tight">Access Point & WPA Key Setup</h2>
				<button
					type="button"
					onclick={() => (isOpen = false)}
					class="cursor-pointer text-slate-400 hover:text-white"
				>
					✕
				</button>
			</div>

			<p class="mt-1 text-xs text-slate-400">
				Set custom WPA security keys per station. If blank, default key ("possum2718") will be used.
			</p>

			{#if apMessage}
				<div class="mt-3 rounded-lg bg-slate-800 px-3 py-2 text-xs font-semibold text-emerald-400">
					{apMessage}
				</div>
			{/if}

			<div class="mt-4 space-y-2">
				{#each inputs as input, idx}
					<div
						class="grid grid-cols-[100px_1fr_1fr] items-center gap-2 rounded bg-slate-800/80 px-3 py-2 text-xs"
					>
						<span class="font-bold text-slate-300">{stationLabel(idx)}</span>
						<div>
							<span class="text-[10px] text-slate-400">Team:</span>
							<span class="ml-1 font-bold text-white">{input.team || '—'}</span>
						</div>
						<div>
							<input
								type="text"
								placeholder="WPA Key"
								bind:value={input.wpa}
								class="w-full rounded border border-slate-700 bg-slate-900 px-2 py-1 text-xs text-white placeholder-slate-500 focus:border-emerald-500 focus:outline-hidden"
							/>
						</div>
					</div>
				{/each}
			</div>

			<div class="mt-6 flex items-center justify-end gap-2">
				<button
					type="button"
					onclick={() => (isOpen = false)}
					class="cursor-pointer rounded-lg px-4 py-2 text-xs font-bold text-slate-400 hover:bg-slate-800 hover:text-white"
				>
					Close
				</button>
				<button
					type="button"
					disabled={isConfiguring}
					onclick={pushApConfig}
					class="cursor-pointer rounded-lg bg-emerald-600 px-4 py-2 text-xs font-bold text-white shadow-md hover:bg-emerald-500 disabled:opacity-50"
				>
					{isConfiguring ? 'Applying…' : 'Apply & Configure AP'}
				</button>
			</div>
		</div>
	</div>
{/if}
