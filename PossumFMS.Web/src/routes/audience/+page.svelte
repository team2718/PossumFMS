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

	const redStations = $derived(matchState?.stations.slice(0, 3) ?? []);
	const blueStations = $derived(matchState?.stations.slice(3, 6) ?? []);

	const redTeams = $derived(
		redStations.map((s) => s.teamNumber)
	);
	
	const blueTeams = $derived(
		blueStations.map((s) => s.teamNumber)
	);

	const matchPhaseText = $derived(
		`${blueStations.filter((s) => s.robotLinked).length} / 6 : ${redStations.filter((s) => s.robotLinked).length}`
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

<div class="relative h-screen w-full overflow-hidden bg-transparent text-white">
	<div class="absolute inset-0 pointer-events-none">
		<div class="absolute left-1/2 top-4 w-[95%] -translate-x-1/2 rounded-lg shadow-2xl shadow-black/70 md:w-[88%]">
			<div class="grid grid-cols-[1fr_auto_1fr] overflow-hidden rounded-lg border border-black/30">
				<div class="bg-[#1168d7]">
					<div class="flex items-stretch border-b border-white/15">
						<div class="flex flex-1 items-center justify-between px-3">
							<span class="text-sm font-black tracking-wide md:text-lg">Blue Alliance</span>
							<div class="rounded bg-[#0e4da2] px-4 py-1 text-3xl font-black md:text-5xl">{blueScore}</div>
						</div>
					</div>
					<div class="grid grid-cols-3 gap-1 bg-[#0e4da2]/70 p-1.5 text-xs md:text-sm">
						{#each blueStations as station, i}
							<div class="flex items-center justify-between rounded bg-[#0d3f83] px-2 py-1 font-semibold">
								<span>{blueTeams[i] > 0 ? blueTeams[i] : '----'}</span>
								<span class="h-2.5 w-2.5 rounded-full {station?.robotLinked ? 'bg-emerald-300' : 'bg-slate-300/50'}"></span>
							</div>
						{/each}
					</div>
				</div>

				<div class="flex min-w-44 flex-col items-center justify-center bg-white px-4 py-2 text-black md:min-w-56">
					<div class="font-mono text-4xl font-black md:text-6xl">
						{matchState ? formatTime(matchState.timeRemaining) : '0:00'}
					</div>
					<div class="text-xs font-bold tracking-wide md:text-sm">{matchPhaseText}</div>
				</div>

				<div class="bg-[#cb2f33]">
					<div class="flex items-stretch border-b border-white/15">
						<div class="flex flex-1 items-center justify-between px-3">
							<div class="rounded bg-[#9c1f23] px-4 py-1 text-3xl font-black md:text-5xl">{redScore}</div>
							<span class="text-sm font-black tracking-wide md:text-lg">Red Alliance</span>
						</div>
					</div>
					<div class="grid grid-cols-3 gap-1 bg-[#9c1f23]/70 p-1.5 text-xs md:text-sm">
						{#each redStations as station, i}
							<div class="flex items-center justify-between rounded bg-[#80191d] px-2 py-1 font-semibold">
								<span>{redTeams[i] > 0 ? redTeams[i] : '----'}</span>
								<span class="h-2.5 w-2.5 rounded-full {station?.robotLinked ? 'bg-emerald-300' : 'bg-slate-300/50'}"></span>
							</div>
						{/each}
					</div>
				</div>
			</div>
		</div>
	</div>
</div>
