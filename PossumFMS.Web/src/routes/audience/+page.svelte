<script lang="ts">
	import { fms } from '$lib/fms.svelte';

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
				? 'bg-[#003151]'
				: 'bg-[#004270]'
			: isMiddle
				? 'bg-[#620a0c]'
				: 'bg-[#850e12]'} px-3 py-3 text-sm font-semibold md:text-5xl"
	>
		{#if alliance === 'blue'}
			<span class="h-2 w-2 shrink-0 rounded-full {linked ? 'bg-emerald-300' : 'bg-white/25'}"
			></span>
			<span>{team > 0 ? team : '----'}</span>
		{:else}
			<span>{team > 0 ? team : '----'}</span>
			<span class="h-2 w-2 shrink-0 rounded-full {linked ? 'bg-emerald-300' : 'bg-white/25'}"
			></span>
		{/if}
	</div>
{/snippet}

{#snippet rankingProgress(alliance: 'red' | 'blue', fuelCombined: number, towerCombined: number)}
	<div
		class="rounded-lg {alliance === 'blue' ? 'bg-[#003151]/90' : 'bg-[#620a0c]/90'} {alliance ===
		'red'
			? 'text-right'
			: ''} px-4 py-2 text-sm font-semibold md:text-xl shadow-md shadow-black/70"
	>
		<span class="mr-4 {matchState?.rankingPoints[alliance].energized
				? 'text-teal-400'
				: 'text-slate-400'}"
			>Energized {Math.min(fuelCombined, 100)}/100</span
		>
		<span class="mr-4 {matchState?.rankingPoints[alliance].supercharged
				? 'text-teal-400'
				: 'text-slate-400'}"
			>Supercharged {Math.min(fuelCombined, 360)}/360</span
		>
		<span class="{matchState?.rankingPoints[alliance].traversal
				? 'text-teal-400'
				: 'text-slate-400'}"
			>Traversal {Math.min(towerCombined, 50)}/50</span
		>
	</div>
{/snippet}

<div class="relative h-screen w-full overflow-hidden bg-transparent text-white">
	<div class="pointer-events-none absolute inset-0">
		<div class="absolute top-4 left-1/2 w-[95%] -translate-x-1/2 md:w-[88%]">
			<div class="relative">
				<!-- Blue hub active indicator (left of panel, arrow pointing toward left screen edge) -->
				{@render hubIndicator('left', blueHubActive)}
				<!-- Red hub active indicator (right of panel, arrow pointing toward right screen edge) -->
				{@render hubIndicator('right', redHubActive)}
				<div
					class="grid grid-cols-[1fr_1fr_1fr_auto_auto_auto_1fr_1fr_1fr] overflow-hidden rounded-lg border border-black/30 shadow-md shadow-black/70"
				>
					<!-- Blue Team 1 -->
					{@render allianceTeamCell(blueTeams[0], !!blueStations[0]?.robotLinked, 'blue', false)}
					<!-- Blue Team 2 (middle) -->
					{@render allianceTeamCell(blueTeams[1], !!blueStations[1]?.robotLinked, 'blue', true)}
					<!-- Blue Team 3 -->
					{@render allianceTeamCell(blueTeams[2], !!blueStations[2]?.robotLinked, 'blue', false)}
					<!-- Blue Score -->
					<div
						class="flex items-center justify-center bg-[#0066b3] px-5 text-4xl font-black md:text-6xl"
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
						class="flex items-center justify-center bg-[#ec1d23] px-5 text-4xl font-black md:text-6xl"
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
	</div>
</div>
