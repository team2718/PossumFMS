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

	const redTeams = $derived(
		redStations.map((s) => s.teamNumber)
	);
	
	const blueTeams = $derived(
		blueStations.map((s) => s.teamNumber)
	);

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

	const matchPhaseText = $derived(
		`${blueStations.filter((s) => s.robotLinked).length} / 6 : ${redStations.filter((s) => s.robotLinked).length}`
	);

	// -------------------------------------------------------------------------
	// Sound effects
	//
	// Audio files belong in PossumFMS.Web/static/sounds/.
	// They will be served at /sounds/<filename>.
	//
	// Expected files:
	//   match-start.wav   — played when Auto begins
	//   auto-end.wav      — played when Auto ends (AutoToTeleopTransition)
	//   teleop-start.wav  — played when Teleop begins
	//   match-end.wav     — played when Teleop ends normally (PostMatch)
	//   match-abort.wav   — played when a match is aborted (PostMatch via abort)
	// -------------------------------------------------------------------------

	const soundFiles = [
		'/sounds/match-start.wav',
		'/sounds/auto-end.wav',
		'/sounds/teleop-start.wav',
		'/sounds/match-end.wav',
		'/sounds/match-abort.wav',
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
			// This is silently ignored — the operator simply needs to click anything
			// on the audience page before the first match.
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
				playSound('/sounds/auto-end.wav');
				break;
			case 'Teleop':
				playSound('/sounds/teleop-start.wav');
				break;
			case 'PostMatch':
				playSound(aborted ? '/sounds/match-abort.wav' : '/sounds/match-end.wav');
				break;
		}

		prevPhase = phase;
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

<div class="relative h-screen w-full overflow-hidden bg-transparent text-white">
	<div class="absolute inset-0 pointer-events-none">
		<div class="absolute left-1/2 top-4 w-[95%] -translate-x-1/2 shadow-2xl shadow-black/70 md:w-[88%]">
			<div class="relative">
				<!-- Blue hub active indicator (left of panel, arrow pointing toward left screen edge) -->
				<div class="absolute inset-y-0 right-full flex items-center pr-3 {blueHubActive ? '' : 'invisible'}">
					<div class="flex items-center">
						<div style="width:0;height:0;border-top:28px solid transparent;border-bottom:28px solid transparent;border-right:36px solid black"></div>
						<div class="h-16 w-16 bg-[#fbf700]"></div>
					</div>
				</div>
				<!-- Red hub active indicator (right of panel, arrow pointing toward right screen edge) -->
				<div class="absolute inset-y-0 left-full flex items-center pl-3 {redHubActive ? '' : 'invisible'}">
					<div class="flex items-center">
						<div class="h-16 w-16 bg-[#fbf700]"></div>
						<div style="width:0;height:0;border-top:28px solid transparent;border-bottom:28px solid transparent;border-left:36px solid black"></div>
					</div>
				</div>
			<div class="grid grid-cols-[1fr_1fr_1fr_auto_auto_auto_1fr_1fr_1fr] overflow-hidden rounded-lg border border-black/30">
				<!-- Blue Team 1 -->
				<div class="flex items-center justify-between bg-[#004270] px-3 py-3 text-sm font-semibold md:text-5xl">
					<span class="h-2 w-2 shrink-0 rounded-full {blueStations[0]?.robotLinked ? 'bg-emerald-300' : 'bg-white/25'}"></span>
					<span>{blueTeams[0] > 0 ? blueTeams[0] : '----'}</span>
				</div>
				<!-- Blue Team 2 (middle) -->
				<div class="flex items-center justify-between bg-[#003151] px-3 py-3 text-4xl font-semibold md:text-5xl">
					<span class="h-2 w-2 shrink-0 rounded-full {blueStations[1]?.robotLinked ? 'bg-emerald-300' : 'bg-white/25'}"></span>
					<span>{blueTeams[1] > 0 ? blueTeams[1] : '----'}</span>
				</div>
				<!-- Blue Team 3 -->
				<div class="flex items-center justify-between bg-[#004270] px-3 py-3 text-sm font-semibold md:text-5xl">
					<span class="h-2 w-2 shrink-0 rounded-full {blueStations[2]?.robotLinked ? 'bg-emerald-300' : 'bg-white/25'}"></span>
					<span>{blueTeams[2] > 0 ? blueTeams[2] : '----'}</span>
				</div>
				<!-- Blue Score -->
				<div class="flex items-center justify-center bg-[#0066b3] px-5 text-4xl font-black md:text-6xl">
					{blueScore}
				</div>
				<!-- Timer -->
				<div class="flex flex-col items-center justify-center bg-white px-6 py-3 text-black">
					<div class="text-4xl font-black md:text-6xl">
						{matchState?.phase === 'AutoToTeleopTransition' ? '0:00' : matchState ? formatTime(matchState.timeRemaining) : '0:00'}
					</div>
					<div class="-mt-1 text-sm font-black md:text-2xl {teleopPeriodIndicator ? '' : 'invisible'}">
						{teleopPeriodIndicator ?? '\u00a0'}
					</div>
				</div>
				<!-- Red Score -->
				<div class="flex items-center justify-center bg-[#ec1d23] px-5 text-4xl font-black md:text-6xl">
					{redScore}
				</div>
				<!-- Red Team 1 -->
				<div class="flex items-center justify-between bg-[#850e12] px-3 py-3 text-sm font-semibold md:text-5xl">
					<span>{redTeams[0] > 0 ? redTeams[0] : '----'}</span>
					<span class="h-2 w-2 shrink-0 rounded-full {redStations[0]?.robotLinked ? 'bg-emerald-300' : 'bg-white/25'}"></span>
				</div>
				<!-- Red Team 2 (middle) -->
				<div class="flex items-center justify-between bg-[#620a0c] px-3 py-3 text-sm font-semibold md:text-5xl">
					<span>{redTeams[1] > 0 ? redTeams[1] : '----'}</span>
					<span class="h-2 w-2 shrink-0 rounded-full {redStations[1]?.robotLinked ? 'bg-emerald-300' : 'bg-white/25'}"></span>
				</div>
				<!-- Red Team 3 -->
				<div class="flex items-center justify-between bg-[#850e12] px-3 py-3 text-sm font-semibold md:text-5xl">
					<span>{redTeams[2] > 0 ? redTeams[2] : '----'}</span>
					<span class="h-2 w-2 shrink-0 rounded-full {redStations[2]?.robotLinked ? 'bg-emerald-300' : 'bg-white/25'}"></span>
				</div>
			</div>
			</div>
			<div class="mt-2 grid grid-cols-1 gap-2 md:grid-cols-2">
				<div class="rounded-lg border bg-[#003151]/90 px-4 py-2 text-sm font-semibold md:text-xl">
					<span class="mr-4">Energized {Math.min(blueFuelCombined, 100)}/100{matchState?.rankingPoints.blue.energized ? ' ✓' : ''}</span>
					<span class="mr-4">Supercharged {Math.min(blueFuelCombined, 360)}/360{matchState?.rankingPoints.blue.supercharged ? ' ✓' : ''}</span>
					<span>Traversal {Math.min(blueTowerCombined, 50)}/50{matchState?.rankingPoints.blue.traversal ? ' ✓' : ''}</span>
				</div>
				<div class="rounded-lg text-right border bg-[#620a0c]/90 px-4 py-2 text-sm font-semibold md:text-xl">
					<span class="mr-4">Energized {Math.min(redFuelCombined, 100)}/100{matchState?.rankingPoints.red.energized ? ' ✓' : ''}</span>
					<span class="mr-4">Supercharged {Math.min(redFuelCombined, 360)}/360{matchState?.rankingPoints.red.supercharged ? ' ✓' : ''}</span>
					<span>Traversal {Math.min(redTowerCombined, 50)}/50{matchState?.rankingPoints.red.traversal ? ' ✓' : ''}</span>
				</div>
			</div>
		</div>
	</div>
</div>
