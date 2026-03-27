<script lang="ts">
	import { fly } from 'svelte/transition';
	import { fms } from '$lib/fms.svelte';
	import type { MatchResultRecord } from '$lib/fms.svelte';

	// Connect to the FMS hub to receive live match state
	$effect(() => {
		fms.connect();
	});

	// Format seconds as M:SS
	function formatTime(secs: number): string {
		const total = Math.ceil(secs);
		const m = Math.floor(total / 60);
		const s = total % 60;
		return `${m}:${s.toString().padStart(2, '0')}`;
	}

	const matchState = $derived(fms.matchState);
	const redScore = $derived(matchState?.redScore ?? 0);
	const blueScore = $derived(matchState?.blueScore ?? 0);

	const redStations = $derived(matchState?.stations.slice(0, 3) ?? []);
	const blueStations = $derived(matchState?.stations.slice(3, 6) ?? []);

	const redTeams = $derived(redStations.map((s) => s.teamNumber));

	const blueTeams = $derived(blueStations.map((s) => s.teamNumber));

	const teleopPeriodIndicator = $derived(
		(() => {
			if (matchState?.phase !== 'Teleop') return null;

			const timeRemaining = Math.max(0, Math.ceil(matchState.timeRemaining));

			if (timeRemaining > 130) return `1/6 :${(timeRemaining - 130).toString().padStart(2, '0')}`;
			if (timeRemaining > 105) return `2/6 :${(timeRemaining - 105).toString().padStart(2, '0')}`;
			if (timeRemaining > 80) return `3/6 :${(timeRemaining - 80).toString().padStart(2, '0')}`;
			if (timeRemaining > 55) return `4/6 :${(timeRemaining - 55).toString().padStart(2, '0')}`;
			if (timeRemaining > 30) return `5/6 :${(timeRemaining - 30).toString().padStart(2, '0')}`;
			return `6/6 :${timeRemaining.toString().padStart(2, '0')}`;
		})()
	);

	const redFuelCombined = $derived(matchState?.redBreakdown.fuelCombined ?? 0);
	const blueFuelCombined = $derived(matchState?.blueBreakdown.fuelCombined ?? 0);
	const redTowerCombined = $derived(matchState?.redBreakdown.towerCombined ?? 0);
	const blueTowerCombined = $derived(matchState?.blueBreakdown.towerCombined ?? 0);
	const blueHubActive = $derived(matchState?.hubActive?.blue ?? false);
	const redHubActive = $derived(matchState?.hubActive?.red ?? false);

	// Audience view state
	const audienceView = $derived(matchState?.audienceView ?? 'live');
	const lastCommittedMatch = $derived<MatchResultRecord | null>(matchState?.lastCommittedMatch ?? null);

	const soundFiles = [
		'/sounds/match-start.wav',
		'/sounds/auto-end.wav',
		'/sounds/teleop-start.wav',
		'/sounds/match-end.wav',
		'/sounds/match-abort.wav',
		'/sounds/auto-end-carter.wav',
		'/sounds/match-abort-carter.wav',
		'/sounds/alliance_shift.wav',
		'/sounds/steam_whistle.wav'
	] as const;

	type SoundFile = (typeof soundFiles)[number];

	// Lazily created and cached so each file is only loaded once.
	const audioCache = new Map<SoundFile, HTMLAudioElement>();

	function playSound(file: SoundFile) {
		if (typeof Audio === 'undefined') return;
		let audio = audioCache.get(file);
		if (!audio) {
			audio = new Audio(file);
			audio.preload = 'auto';
			audioCache.set(file, audio);
		}
		audio.currentTime = 0;
		audio.play().catch(() => {
			// Browsers may block autoplay until the user interacts with the page.
		});
	}

	// Preload all sounds once the component mounts so they're ready instantly.
	$effect(() => {
		for (const file of soundFiles) {
			if (!audioCache.has(file)) {
				const audio = new Audio(file);
				audio.preload = 'auto';
				audioCache.set(file, audio);
			}
		}
	});

	// Track phase transitions to fire the right sound.
	// prevPhase is a plain (non-reactive) variable so reading it inside $effect
	// does not add it as a dependency — only matchState changes trigger the effect.
	let prevPhase: string | null = null;
	let phaseInitialized = false;

	$effect(() => {
		const phase = matchState?.phase ?? null;
		const aborted = matchState?.wasAborted ?? false;

		if (phase === null) return;

		// First state received — snapshot current phase without playing anything,
		// so opening the page mid-match doesn't trigger a sound.
		if (!phaseInitialized) {
			prevPhase = phase;
			phaseInitialized = true;
			return;
		}

		if (phase === prevPhase) return;

		switch (phase) {
			case 'Auto':
				playSound('/sounds/match-start.wav');
				break;
			case 'AutoToTeleopTransition':
				playSound('/sounds/auto-end-carter.wav');
				break;
			case 'Teleop':
				playSound('/sounds/teleop-start.wav');
				break;
			case 'PostMatch':
				playSound(aborted ? '/sounds/match-abort-carter.wav' : '/sounds/match-end.wav');
				break;
		}

		prevPhase = phase;
	});

	// Track TeleopPeriod transitions to fire the right sound for shift changes.
	// prevTeleopPeriod is a plain (non-reactive) variable so reading it inside $effect
	// does not add it as a dependency — only matchState changes trigger the effect.
	let prevTeleopPeriod: string | null = null;
	let teleopPeriodInitialized = false;

	$effect(() => {
		const teleopPeriod = matchState?.currentTeleopPeriod ?? null;

		if (teleopPeriod === null || matchState?.phase !== 'Teleop') return;

		// First state received — snapshot current period without playing anything.
		if (!teleopPeriodInitialized) {
			prevTeleopPeriod = teleopPeriod;
			teleopPeriodInitialized = true;
			return;
		}

		if (teleopPeriod === prevTeleopPeriod) return;

		// Determine which sound to play based on the transition
		const soundToPlay = (() => {
			// Shift 4 → EndGame transition
			if (prevTeleopPeriod === 'Shift4' && teleopPeriod === 'EndGame') {
				return '/sounds/steam_whistle.wav';
			}

			// All other shifts (TransitionShift → Shift1, Shift1 → Shift2, Shift2 → Shift3, Shift3 → Shift4)
			if (
				(prevTeleopPeriod === 'TransitionShift' && teleopPeriod === 'Shift1') ||
				(prevTeleopPeriod === 'Shift1' && teleopPeriod === 'Shift2') ||
				(prevTeleopPeriod === 'Shift2' && teleopPeriod === 'Shift3') ||
				(prevTeleopPeriod === 'Shift3' && teleopPeriod === 'Shift4')
			) {
				return '/sounds/alliance_shift.wav';
			}

			return null;
		})();

		if (soundToPlay) {
			playSound(soundToPlay);
		}

		prevTeleopPeriod = teleopPeriod;
	});
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
			font-family: Calibri, 'Gill Sans MT', 'Trebuchet MS', Arial, sans-serif;
		}
	</style>
</svelte:head>

{#snippet hubIndicator(side: 'left' | 'right', active: boolean)}
	<div
		class="absolute inset-y-0 {side === 'left'
			? 'right-full pr-3'
			: 'left-full pl-3'} flex items-center {active ? '' : 'invisible'}"
	>
		<div
			class="flex h-16 w-16 items-center justify-center bg-[#fbf700] text-5xl leading-none font-black text-black"
		>
			{side === 'left' ? '🡨' : '🡪'}
		</div>
	</div>
{/snippet}

{#snippet allianceTeamCell(
	team: number,
	linked: boolean,
	alliance: 'blue' | 'red',
	isMiddle: boolean
)}
	<div
		class="flex items-center justify-between {alliance === 'blue'
			? isMiddle
				? 'alliance-blue-bg-dark'
				: 'alliance-blue-bg-darker'
			: isMiddle
				? 'alliance-red-bg-dark'
				: 'alliance-red-bg-darker'} px-3 py-3 text-sm font-semibold md:text-4xl"
	>
		<span>{team > 0 ? team : '----'}</span>
	</div>
{/snippet}

{#snippet rankingProgress(alliance: 'red' | 'blue', fuelCombined: number, towerCombined: number)}
	<div
		class="rounded-lg {alliance === 'blue'
			? 'alliance-blue-bg-dark-transparent'
			: 'alliance-red-bg-dark-transparent text-right'} px-4 py-2 text-sm font-semibold shadow-md shadow-black/70 md:text-xl"
	>
		<span
			class="mr-4 {matchState?.rankingPoints[alliance].energized
				? 'text-white-glow'
				: 'text-slate-300'}">Energized {Math.min(fuelCombined, 100)}/100</span
		>
		<span
			class="mr-4 {matchState?.rankingPoints[alliance].supercharged
				? 'text-white-glow'
				: 'text-slate-300'}">Supercharged {Math.min(fuelCombined, 360)}/360</span
		>
		<span
			class={matchState?.rankingPoints[alliance].traversal
				? 'text-white-glow'
				: 'text-slate-300'}>Traversal {Math.min(towerCombined, 50)}/50</span
		>
	</div>
{/snippet}

{#snippet teamAvatarOrPlaceholder(avatarBase64: string | null | undefined)}
	{#if avatarBase64}
		<img
			src="data:image/png;base64,{avatarBase64}"
			alt="Team avatar"
			class="h-10 w-10 rounded object-contain"
		/>
	{:else}
		<svg
			viewBox="0 0 40 40"
			xmlns="http://www.w3.org/2000/svg"
			class="h-10 w-10 opacity-40"
			fill="currentColor"
			aria-hidden="true"
		>
			<!-- Simple robot silhouette placeholder -->
			<rect x="11" y="10" width="18" height="12" rx="3" />
			<circle cx="16" cy="16" r="2.5" fill="black" opacity="0.5" />
			<circle cx="24" cy="16" r="2.5" fill="black" opacity="0.5" />
			<rect x="9" y="24" width="22" height="12" rx="3" />
			<rect x="4" y="26" width="5" height="8" rx="2" />
			<rect x="31" y="26" width="5" height="8" rx="2" />
		</svg>
	{/if}
{/snippet}

{#snippet matchResultsAlliancePanel(
	alliance: 'blue' | 'red',
	teams: number[],
	nicknames: string[],
	avatars: (string | null)[],
	score: number
)}
	<div
		class="flex-1 overflow-hidden rounded-lg border border-black/30 shadow-md shadow-black/70 {alliance ===
		'blue'
			? 'alliance-blue-bg-dark'
			: 'alliance-red-bg-dark'}"
	>
		<!-- Alliance header with score -->
		<div
			class="flex items-center justify-between px-4 py-3 {alliance === 'blue'
				? 'alliance-blue-bg-darker'
				: 'alliance-red-bg-darker'}"
		>
			<div class="text-sm font-black tracking-wider uppercase opacity-80">
				{alliance === 'blue' ? 'Blue Alliance' : 'Red Alliance'}
			</div>
			<div class="text-5xl font-black md:text-6xl">{score}</div>
		</div>
		<!-- Team rows -->
		{#each teams as teamNum, i}
			<div class="flex items-center gap-3 border-t border-black/20 px-4 py-2">
				{@render teamAvatarOrPlaceholder(avatars[i])}
				<div class="min-w-0 flex-1">
					<div class="text-2xl font-black leading-tight md:text-3xl">
						{teamNum > 0 ? teamNum : '----'}
					</div>
					{#if nicknames[i]}
						<div class="truncate text-sm font-semibold opacity-75">{nicknames[i]}</div>
					{/if}
				</div>
			</div>
		{/each}
	</div>
{/snippet}

{#snippet matchResultsView(match: MatchResultRecord | null)}
	<div class="absolute top-4 left-1/2 w-[95%] -translate-x-1/2 md:w-[88%]">
		{#if match === null}
			<div
				class="rounded-lg border border-black/30 bg-black/60 px-8 py-10 text-center shadow-md shadow-black/70"
			>
				<div class="text-2xl font-black opacity-60">No match results available</div>
				<div class="mt-2 text-sm opacity-40">Commit a match result from the FMS to display here.</div>
			</div>
		{:else}
			<!-- Match header -->
			<div class="mb-3 text-center">
				<div class="text-2xl font-black tracking-wide md:text-4xl">
					{match.matchType} Match {match.matchNumber}
				</div>
				<div class="mt-0.5 text-sm font-semibold uppercase tracking-widest opacity-60">Results</div>
			</div>

			<!-- Alliance panels side by side -->
			<div class="flex gap-3">
				{@render matchResultsAlliancePanel(
					'blue',
					match.blueTeams,
					match.blueTeamNicknames,
					match.blueTeamAvatars,
					match.blueScore
				)}
				{@render matchResultsAlliancePanel(
					'red',
					match.redTeams,
					match.redTeamNicknames,
					match.redTeamAvatars,
					match.redScore
				)}
			</div>

			<!-- Score breakdown + ranking points -->
			<div class="mt-3 grid grid-cols-2 gap-3">
				<!-- Score breakdown -->
				<div
					class="rounded-lg border border-black/30 bg-black/60 px-4 py-3 shadow-md shadow-black/70"
				>
					<div class="mb-2 text-xs font-black tracking-wider uppercase opacity-60">
						Score Breakdown
					</div>
					<table class="w-full text-sm">
						<thead>
							<tr class="border-b border-white/20 text-xs font-black uppercase opacity-60">
								<th class="pb-1 text-left font-semibold"></th>
								<th class="pb-1 text-center font-semibold">
									<span class="alliance-blue-text font-black">Blue</span>
								</th>
								<th class="pb-1 text-center font-semibold">
									<span class="alliance-red-text font-black">Red</span>
								</th>
							</tr>
						</thead>
						<tbody class="divide-y divide-white/10">
							{#each [
								['Auto Fuel', match.blueBreakdown.autoFuelPoints, match.redBreakdown.autoFuelPoints],
								['Auto Tower', match.blueBreakdown.autoTowerPoints, match.redBreakdown.autoTowerPoints],
								['Teleop Fuel', match.blueBreakdown.teleopFuelPoints, match.redBreakdown.teleopFuelPoints],
								['Teleop Tower', match.blueBreakdown.teleopTowerPoints, match.redBreakdown.teleopTowerPoints]
							] as [label, blue, red]}
								<tr>
									<td class="py-1 pr-2 text-left text-xs font-semibold opacity-75">{label}</td>
									<td class="py-1 text-center font-black">{blue}</td>
									<td class="py-1 text-center font-black">{red}</td>
								</tr>
							{/each}
							<tr class="border-t-2 border-white/40">
								<td class="pt-1.5 pr-2 text-left text-xs font-black uppercase tracking-wide"
									>Total</td
								>
								<td class="pt-1.5 text-center text-xl font-black">{match.blueScore}</td>
								<td class="pt-1.5 text-center text-xl font-black">{match.redScore}</td>
							</tr>
						</tbody>
					</table>
				</div>

				<!-- Ranking points -->
				<div
					class="rounded-lg border border-black/30 bg-black/60 px-4 py-3 shadow-md shadow-black/70"
				>
					<div class="mb-2 text-xs font-black tracking-wider uppercase opacity-60">
						Ranking Points
					</div>
					<table class="w-full text-sm">
						<thead>
							<tr class="border-b border-white/20 text-xs uppercase opacity-60">
								<th class="pb-1 text-left font-semibold"></th>
								<th class="pb-1 text-center font-semibold">
									<span class="alliance-blue-text font-black">Blue</span>
								</th>
								<th class="pb-1 text-center font-semibold">
									<span class="alliance-red-text font-black">Red</span>
								</th>
							</tr>
						</thead>
						<tbody class="divide-y divide-white/10">
							{#each [
								['Energized', match.blueRankingPoints.energized, match.redRankingPoints.energized],
								['Supercharged', match.blueRankingPoints.supercharged, match.redRankingPoints.supercharged],
								['Traversal', match.blueRankingPoints.traversal, match.redRankingPoints.traversal]
							] as [label, blue, red]}
								<tr>
									<td class="py-1 pr-2 text-left text-xs font-semibold opacity-75">{label}</td>
									<td class="py-1 text-center font-black {blue ? 'text-white-glow' : 'opacity-30'}"
										>{blue ? '✓' : '✗'}</td
									>
									<td class="py-1 text-center font-black {red ? 'text-white-glow' : 'opacity-30'}"
										>{red ? '✓' : '✗'}</td
									>
								</tr>
							{/each}
							<tr>
								<td class="py-1 pr-2 text-left text-xs font-semibold opacity-75">Win / Tie</td>
								<td class="py-1 text-center font-black">{match.blueRankingPoints.winTie}</td>
								<td class="py-1 text-center font-black">{match.redRankingPoints.winTie}</td>
							</tr>
							<tr class="border-t-2 border-white/40">
								<td class="pt-1.5 pr-2 text-left text-xs font-black uppercase tracking-wide"
									>Total RP</td
								>
								<td class="pt-1.5 text-center text-xl font-black">{match.blueRankingPoints.total}</td>
								<td class="pt-1.5 text-center text-xl font-black">{match.redRankingPoints.total}</td>
							</tr>
						</tbody>
					</table>
				</div>
			</div>
		{/if}
	</div>
{/snippet}

<div class="relative h-screen w-full overflow-hidden bg-transparent text-white">
	{#key audienceView}
		<div
			in:fly={{ y: 40, duration: 500, opacity: 1 }}
			out:fly={{ y: -40, duration: 300, opacity: 1 }}
			class="pointer-events-none absolute inset-0"
		>
			{#if audienceView === 'live'}
				<!--
					Live scoreboard overlay: team numbers, timer, scores, ranking points progress.
				-->
				<div class="absolute top-4 left-1/2 w-[95%] -translate-x-1/2 md:w-[88%]">
					<div class="relative">
						<!-- Blue hub active indicator (left of panel, arrow pointing toward left screen edge) -->
						{@render hubIndicator('left', blueHubActive)}
						<!-- Red hub active indicator (right of panel, arrow pointing toward right screen edge) -->
						{@render hubIndicator('right', redHubActive)}
						<div
							class="grid grid-cols-[1fr_1fr_1fr_1fr_1fr_1fr_1fr_1fr_1fr] overflow-hidden rounded-lg border border-black/30 shadow-md shadow-black/70"
						>
							<!-- Blue Team 1 -->
							{@render allianceTeamCell(blueTeams[0], !!blueStations[0]?.robotLinked, 'blue', false)}
							<!-- Blue Team 2 (middle) -->
							{@render allianceTeamCell(blueTeams[1], !!blueStations[1]?.robotLinked, 'blue', true)}
							<!-- Blue Team 3 -->
							{@render allianceTeamCell(blueTeams[2], !!blueStations[2]?.robotLinked, 'blue', false)}
							<!-- Blue Score -->
							<div
								class="alliance-blue-bg flex items-center justify-center px-5 text-4xl font-black md:text-6xl"
							>
								{blueScore}
							</div>
							<!-- Timer -->
							<div class="flex flex-col items-center justify-center bg-white px-6 py-3 text-black">
								<div class="text-4xl font-black md:text-6xl">
									{matchState?.phase === 'AutoToTeleopTransition'
										? '0:00'
										: matchState
											? formatTime(matchState.timeRemaining)
											: '0:00'}
								</div>
								<div
									class="-mt-1 text-sm font-black md:text-2xl {teleopPeriodIndicator
										? ''
										: 'invisible'}"
								>
									{teleopPeriodIndicator ?? '\u00a0'}
								</div>
							</div>
							<!-- Red Score -->
							<div
								class="alliance-red-bg flex items-center justify-center px-5 text-4xl font-black md:text-6xl"
							>
								{redScore}
							</div>
							<!-- Red Team 1 -->
							{@render allianceTeamCell(redTeams[0], !!redStations[0]?.robotLinked, 'red', false)}
							<!-- Red Team 2 (middle) -->
							{@render allianceTeamCell(redTeams[1], !!redStations[1]?.robotLinked, 'red', true)}
							<!-- Red Team 3 -->
							{@render allianceTeamCell(redTeams[2], !!redStations[2]?.robotLinked, 'red', false)}
						</div>
					</div>
					<div class="mt-2 grid grid-cols-1 gap-2 md:grid-cols-2">
						{@render rankingProgress('blue', blueFuelCombined, blueTowerCombined)}
						{@render rankingProgress('red', redFuelCombined, redTowerCombined)}
					</div>
				</div>
			{:else if audienceView === 'matchResults'}
				<!--
					Match results view: committed scores, breakdown, and ranking points.
				-->
				{@render matchResultsView(lastCommittedMatch)}
			{:else}
				<!--
					Fallback for future views (e.g. prematch, break screen).
					Add {:else if audienceView === 'prematch'} blocks above this one.
				-->
			{/if}
		</div>
	{/key}
</div>
