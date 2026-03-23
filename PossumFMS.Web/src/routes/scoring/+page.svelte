<script lang="ts">
	import { fms } from '$lib/fms.svelte';
	import Navbar from '$lib/Navbar.svelte';
	import type { TowerEndgameLevel } from '$lib/fms.svelte';

	$effect(() => {
		fms.connect();
	});

	const matchState = $derived(fms.matchState);
	const phase = $derived(matchState?.phase ?? 'Disconnected');

	const redScoreStationIndices = [0, 1, 2];
	const blueScoreStationIndices = [3, 4, 5];
	const fuelAdjustments = [10, 5, 1, -1, -5, -10];

	let scoreWarning = $state('');

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
				onclick={() => adjustFuelPoints(alliance, isAuto, delta)}
				class="rounded px-2 py-1 text-xs font-bold text-white {delta > 0
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
			<label class="inline-flex items-center gap-1 text-slate-700">
				<input
					type="checkbox"
					checked={matchState?.stationClimbs?.[idx]?.autoClimbed ?? false}
					onchange={(e) => setAutoTowerClimb(idx, (e.currentTarget as HTMLInputElement).checked)}
				/>
				<span>{stationCode(idx)}</span>
			</label>
		{/each}
	</div>
{/snippet}

{#snippet endgameTowerLevelControls(indices: number[])}
	<div class="grid grid-cols-3 gap-2">
		{#each indices as idx}
			<div class="rounded border border-slate-200 bg-slate-50 px-2 py-1.5">
				<div class="mb-1 text-[10px] font-semibold text-slate-500 uppercase">
					{stationCode(idx)}
				</div>
				<select
					value={matchState?.stationClimbs?.[idx]?.endgameLevel ?? 'None'}
					onchange={(e) =>
						setEndgameTowerLevel(
							idx,
							(e.currentTarget as HTMLSelectElement).value as TowerEndgameLevel
						)}
					class="w-full rounded border border-slate-300 bg-white px-1 py-1 text-xs"
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
	<div class="mt-1 border-t border-slate-200 pt-1">
		<div>
			Energized RP (100 Fuel): <span class="font-bold"
				>{matchState?.rankingPoints[alliance].energized ? 'Yes' : 'No'}</span
			>
		</div>
		<div>
			Supercharged RP (360 Fuel): <span class="font-bold"
				>{matchState?.rankingPoints[alliance].supercharged ? 'Yes' : 'No'}</span
			>
		</div>
		<div>
			Traversal RP (50 Tower): <span class="font-bold"
				>{matchState?.rankingPoints[alliance].traversal ? 'Yes' : 'No'}</span
			>
		</div>
		<div>
			Win/Tie RP: <span class="font-bold">{matchState?.rankingPoints[alliance].winTie ?? 0}</span>
		</div>
		<div>
			Total RP: <span class="font-bold">{matchState?.rankingPoints[alliance].total ?? 0}</span>
		</div>
	</div>
{/snippet}

{#snippet scoreAlliancePanel(alliance: 'blue' | 'red', stationIndices: number[])}
	{@const breakdown = alliance === 'blue' ? matchState?.blueBreakdown : matchState?.redBreakdown}
	{@const allianceLabel = alliance === 'blue' ? 'Blue' : 'Red'}
	<div
		class="rounded border p-3 {alliance === 'blue'
			? 'alliance-blue-border-soft alliance-blue-bg-soft'
			: 'alliance-red-border-soft alliance-red-bg-soft'}"
	>
		<div class="mb-3 flex items-center justify-between">
			<div
				class="text-xs font-bold tracking-wider uppercase {alliance === 'blue'
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
				<span>{breakdown?.autoFuelPoints ?? 0}</span>
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
				<span>{breakdown?.teleopFuelPoints ?? 0}</span>
			</div>
			{@render fuelAdjustmentButtons(allianceLabel as 'Red' | 'Blue', false)}
		</div>

		<div
			class="mb-2 rounded border bg-white p-2 text-xs {alliance === 'blue'
				? 'alliance-blue-border-soft'
				: 'alliance-red-border-soft'}"
		>
			<div class="mb-1 font-semibold text-slate-700">Auto Tower Climb (15 pts each)</div>
			{@render autoTowerClimbControls(stationIndices)}
		</div>

		<div
			class="mb-2 rounded border bg-white p-2 text-xs {alliance === 'blue'
				? 'border-blue-200'
				: 'border-rose-200'}"
		>
			<div class="mb-1 font-semibold text-slate-700">Endgame Tower Level</div>
			{@render endgameTowerLevelControls(stationIndices)}
		</div>

		<div
			class="rounded border bg-white p-2 text-xs text-slate-700 {alliance === 'blue'
				? 'alliance-blue-border-soft'
				: 'alliance-red-border-soft'}"
		>
			<div>Fuel Combined: <span class="font-bold">{breakdown?.fuelCombined ?? 0}</span></div>
			<div>Tower Combined: <span class="font-bold">{breakdown?.towerCombined ?? 0}</span></div>
			{@render rankingPointsSummary(alliance)}
		</div>
	</div>
{/snippet}

<div class="app-neutral-bg min-h-screen text-slate-900">
	<Navbar />

	<main class="mx-auto flex max-w-[1700px] flex-col gap-3 px-3 py-3">
		<!-- Phase banner -->
		<div class="flex items-center gap-3 rounded border border-slate-200 bg-white px-4 py-2 shadow-sm">
			<span class="text-sm font-bold text-slate-700">Scoring</span>
			<span class="rounded-full border border-slate-300 bg-slate-50 px-3 py-0.5 text-xs font-semibold text-slate-600">
				{phase}
			</span>
			{#if matchState}
				<span class="text-xs text-slate-500">{matchState.matchType} #{matchState.matchNumber}</span>
			{/if}
		</div>

		{#if !matchState}
			<div class="rounded border border-slate-200 bg-white px-4 py-8 text-center text-sm text-slate-500 shadow-sm">
				Waiting for FMS connection…
			</div>
		{:else}
			{#if scoreWarning}
				<div class="rounded border border-rose-300 bg-rose-50 px-3 py-2 text-xs font-semibold text-rose-700">
					{scoreWarning}
				</div>
			{/if}
			<div class="grid grid-cols-1 gap-3 lg:grid-cols-2">
				{@render scoreAlliancePanel('blue', blueScoreStationIndices)}
				{@render scoreAlliancePanel('red', redScoreStationIndices)}
			</div>
		{/if}
	</main>
</div>
