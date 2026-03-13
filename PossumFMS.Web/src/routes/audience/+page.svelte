<script lang="ts">
	import { fms } from '$lib/fms.svelte';

	// Connect to the FMS hub to receive live match state
	$effect(() => {
		fms.connect();
	});

	// Format seconds as M:SS
	function formatTime(secs: number): string {
		const m = Math.floor(secs / 60);
		const s = Math.floor(secs % 60);
		return `${m}:${s.toString().padStart(2, '0')}`;
	}

	const matchState = $derived(fms.matchState);

	// -------------------------------------------------------------------------
	// TODO: Score tracking
	//
	// Right now the FMS backend doesn't send score data in MatchState.
	// When it does, you'll add score fields here like:
	//
	//   const redScore = $derived(matchState?.redScore ?? 0);
	//   const blueScore = $derived(matchState?.blueScore ?? 0);
	//
	// For now they're just hardcoded to 0 as placeholders.
	// -------------------------------------------------------------------------
	let redScore = $state(0);
	let blueScore = $state(0);

	const redTeams = $derived(
		matchState?.stations.slice(0, 3).map((s) => s.teamNumber).filter((n) => n > 0) ?? []
	);
	const blueTeams = $derived(
		matchState?.stations.slice(3, 6).map((s) => s.teamNumber).filter((n) => n > 0) ?? []
	);
</script>

<!--
	Make the page body transparent so OBS can composite it as a Browser Source overlay.
	The styles below target the <html> and <body> elements that wrap this page.
-->
<svelte:head>
	<style>
		html,
		body {
			background: transparent !important;
		}
	</style>
</svelte:head>

<!--
	Outer wrapper — transparent background, full screen.
	Everything is positioned at the bottom so it sits like a scoreboard overlay.
-->
<div class="flex h-screen flex-col justify-end bg-transparent p-4">

	<!-- ===== SCOREBOARD BAR ===== -->
	<div class="flex items-stretch gap-0 overflow-hidden rounded-xl shadow-2xl shadow-black/60">

		<!-- Red Alliance Panel -->
		<div class="flex flex-1 flex-col items-center bg-red-900/90 px-6 py-4">
			<span class="font-mono text-7xl font-bold text-white">{redScore}</span>

			<!-- Team numbers (or names once you implement team name lookup) -->
			<span class="mt-1 text-sm text-red-300">
				{#if redTeams.length > 0}
					{redTeams.join(' · ')}
				{/if}
			</span>
		</div>

		<!-- Center: Timer + Match Info -->
		<div class="flex flex-col items-center justify-center bg-gray-900/90 px-8 py-4">
			<!-- Match phase label -->
			{#if matchState}
				<span class="mb-1 text-xs font-semibold uppercase tracking-widest text-gray-400">
					{matchState.matchType}
					{#if matchState.matchNumber > 0}#{matchState.matchNumber}{/if}
				</span>
			{/if}
			<span class="font-mono text-6xl font-bold text-white">
				{matchState ? formatTime(matchState.timeRemaining) : '—:——'}
			</span>

			<!-- Phase displayed below the timer -->
			{#if matchState}
				<span class="mt-1 text-xs text-gray-500">{matchState.phase}</span>
			{/if}
		</div>

		<!-- Blue Alliance Panel -->
		<div class="flex flex-1 flex-col items-center bg-blue-900/90 px-6 py-4">
			<span class="font-mono text-7xl font-bold text-white">{blueScore}</span>

			<!-- Team numbers -->
			<span class="mt-1 text-sm text-blue-300">
				{#if blueTeams.length > 0}
					{blueTeams.join(' · ')}
				{/if}
			</span>
		</div>
	</div>
</div>
