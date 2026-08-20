<script lang="ts">
	import { fms } from '$lib/fms.svelte';
	import type { TowerEndgameLevel } from '$lib/fms.svelte';

	const matchState = $derived(fms.matchState);
	const redBreakdown = $derived(matchState?.redBreakdown);
	const blueBreakdown = $derived(matchState?.blueBreakdown);

	let scoreWarning = $state('');
	const fuelAdjustments = [10, 5, 1, -1, -5, -10];

	const blueScoreStationIndices = [3, 4, 5];
	const redScoreStationIndices = [0, 1, 2];

	function stationCode(idx: number): string {
		return idx < 3 ? `Red${idx + 1}` : `Blue${idx - 2}`;
	}

	async function adjustFuelPoints(alliance: 'Red' | 'Blue', isAuto: boolean, delta: number) {
		scoreWarning = '';
		try {
			await fms.adjustFuelPoints(alliance, isAuto, delta);
		} catch (error) {
			scoreWarning =
				error instanceof Error ? error.message : 'Failed to update fuel score. Please try again.';
		}
	}

	async function setAutoTowerClimb(stationIndex: number, climbed: boolean) {
		scoreWarning = '';
		try {
			await fms.setAutoTowerClimb(stationIndex, climbed);
		} catch (error) {
			scoreWarning =
				error instanceof Error
					? error.message
					: 'Failed to update auto tower climb. Please try again.';
		}
	}

	async function setEndgameTowerLevel(stationIndex: number, level: TowerEndgameLevel) {
		scoreWarning = '';
		try {
			await fms.setEndgameTowerLevel(stationIndex, level);
		} catch (error) {
			scoreWarning =
				error instanceof Error
					? error.message
					: 'Failed to update endgame tower level. Please try again.';
		}
	}
</script>

{#snippet fuelAdjustmentButtons(alliance: 'Red' | 'Blue', isAuto: boolean)}
	<div class="flex flex-wrap gap-1">
		{#each fuelAdjustments as delta}
			<button
				type="button"
				onclick={() => adjustFuelPoints(alliance, isAuto, delta)}
				class="cursor-pointer rounded px-2 py-1 text-xs font-bold text-white transition {delta > 0
					? 'bg-emerald-700 hover:bg-emerald-600'
					: 'bg-rose-700 hover:bg-rose-600'}"
			>
				{delta > 0 ? '+' : ''}{delta}
			</button>
		{/each}
	</div>
{/snippet}

{#snippet autoTowerClimbControls(indices: number[])}
	<div class="flex flex-wrap gap-3">
		{#each indices as idx}
			<label class="inline-flex cursor-pointer items-center gap-1 text-slate-700">
				<input
					type="checkbox"
					checked={matchState?.stationClimbs?.[idx]?.autoClimbed ?? false}
					onchange={(e) => setAutoTowerClimb(idx, (e.currentTarget as HTMLInputElement).checked)}
					class="h-4 w-4 rounded border-slate-300"
				/>
				<span class="text-xs font-semibold">{stationCode(idx)}</span>
			</label>
		{/each}
	</div>
{/snippet}

{#snippet endgameTowerLevelControls(indices: number[])}
	<div class="grid grid-cols-3 gap-2">
		{#each indices as idx}
			<div class="rounded border border-slate-200 bg-slate-50 px-2 py-1.5">
				<div class="mb-1 text-[10px] font-semibold uppercase text-slate-500">
					{stationCode(idx)}
				</div>
				<select
					value={matchState?.stationClimbs?.[idx]?.endgameLevel ?? 'None'}
					onchange={(e) =>
						setEndgameTowerLevel(
							idx,
							(e.currentTarget as HTMLSelectElement).value as TowerEndgameLevel
						)}
					class="w-full rounded border border-slate-300 bg-white px-1 py-1 text-xs text-slate-800"
				>
					<option value="None">None</option>
					<option value="L1">L1 (10)</option>
					<option value="L2">L2 (20)</option>
					<option value="L3">L3 (30)</option>
				</select>
			</div>
		{/each}
	</div>
{/snippet}

{#snippet rankingPointsSummary(alliance: 'red' | 'blue')}
	<div class="mt-1 border-t border-slate-200 pt-1 text-xs text-slate-600 space-y-0.5">
		<div>
			Energized RP (100 Fuel): <span class="font-bold text-slate-800"
				>{matchState?.rankingPoints[alliance].energized ? 'Yes' : 'No'}</span
			>
		</div>
		<div>
			Supercharged RP (360 Fuel): <span class="font-bold text-slate-800"
				>{matchState?.rankingPoints[alliance].supercharged ? 'Yes' : 'No'}</span
			>
		</div>
		<div>
			Traversal RP (50 Tower): <span class="font-bold text-slate-800"
				>{matchState?.rankingPoints[alliance].traversal ? 'Yes' : 'No'}</span
			>
		</div>
		<div>
			Win/Tie RP: <span class="font-bold text-slate-800">{matchState?.rankingPoints[alliance].winTie ?? 0}</span>
		</div>
		<div>
			Total RP: <span class="font-bold text-slate-800">{matchState?.rankingPoints[alliance].total ?? 0}</span>
		</div>
	</div>
{/snippet}

{#snippet scoreAlliancePanel(alliance: 'blue' | 'red', stationIndices: number[])}
	{@const breakdown = alliance === 'blue' ? blueBreakdown : redBreakdown}
	{@const allianceLabel = alliance === 'blue' ? 'Blue' : 'Red'}
	<div
		class="rounded border p-3 {alliance === 'blue'
			? 'alliance-blue-border-soft alliance-blue-bg-soft'
			: 'alliance-red-border-soft alliance-red-bg-soft'}"
	>
		<div class="mb-3 flex items-center justify-between">
			<div
				class="text-xs font-bold uppercase tracking-wider {alliance === 'blue'
					? 'alliance-blue-text'
					: 'alliance-red-text'}"
			>
				{allianceLabel} Alliance
			</div>
			<div
				class="rounded px-2 py-0.5 text-xs font-bold text-white {alliance === 'blue'
					? 'alliance-blue-bg'
					: 'alliance-red-bg'}"
			>
				Total {breakdown?.total ?? 0}
			</div>
		</div>

		<div
			class="mb-2 rounded border bg-white p-2 {alliance === 'blue'
				? 'alliance-blue-border-soft'
				: 'alliance-red-border-soft'}"
		>
			<div class="mb-2 flex items-center justify-between text-xs font-semibold text-slate-700">
				<span>Auto Fuel</span>
				<span class="font-bold">{breakdown?.autoFuelPoints ?? 0}</span>
			</div>
			{@render fuelAdjustmentButtons(allianceLabel as 'Red' | 'Blue', true)}
		</div>

		<div
			class="mb-2 rounded border bg-white p-2 {alliance === 'blue'
				? 'alliance-blue-border-soft'
				: 'alliance-red-border-soft'}"
		>
			<div class="mb-2 flex items-center justify-between text-xs font-semibold text-slate-700">
				<span>Teleop Fuel</span>
				<span class="font-bold">{breakdown?.teleopFuelPoints ?? 0}</span>
			</div>
			{@render fuelAdjustmentButtons(allianceLabel as 'Red' | 'Blue', false)}
		</div>

		<div
			class="mb-2 rounded border bg-white p-2 {alliance === 'blue'
				? 'alliance-blue-border-soft'
				: 'alliance-red-border-soft'}"
		>
			<div class="mb-2 text-xs font-semibold text-slate-700">Auto Tower Climb</div>
			{@render autoTowerClimbControls(stationIndices)}
		</div>

		<div
			class="mb-2 rounded border bg-white p-2 {alliance === 'blue'
				? 'alliance-blue-border-soft'
				: 'alliance-red-border-soft'}"
		>
			<div class="mb-2 text-xs font-semibold text-slate-700">Endgame Tower Level</div>
			{@render endgameTowerLevelControls(stationIndices)}
		</div>

		<div
			class="rounded border bg-white p-2 text-xs {alliance === 'blue'
				? 'alliance-blue-border-soft'
				: 'alliance-red-border-soft'}"
		>
			<div class="font-semibold text-slate-700">Scoring Breakdown</div>
			<div>Fuel Combined: <span class="font-bold">{breakdown?.fuelCombined ?? 0}</span></div>
			<div>Tower Combined: <span class="font-bold">{breakdown?.towerCombined ?? 0}</span></div>
			{@render rankingPointsSummary(alliance)}
		</div>
	</div>
{/snippet}

<div class="p-3">
	{#if scoreWarning}
		<div
			class="mb-3 rounded border border-rose-300 bg-rose-50 px-3 py-2 text-xs font-semibold text-rose-700"
		>
			{scoreWarning}
		</div>
	{/if}
	<div class="grid grid-cols-1 md:grid-cols-2 gap-3">
		{@render scoreAlliancePanel('blue', blueScoreStationIndices)}
		{@render scoreAlliancePanel('red', redScoreStationIndices)}
	</div>
</div>

