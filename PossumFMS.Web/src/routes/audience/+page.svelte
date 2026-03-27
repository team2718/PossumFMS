<script lang="ts">
	import { fly } from 'svelte/transition';
	import { fms } from '$lib/fms.svelte';
	import type { MatchResultRecord, MatchResultRankingPoints } from '$lib/fms.svelte';

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
	const allianceOrder = $derived(matchState?.allianceOrder ?? 'blueLeft');
	const leftAlliance = $derived<'blue' | 'red'>(allianceOrder === 'blueLeft' ? 'blue' : 'red');
	const rightAlliance = $derived<'blue' | 'red'>(allianceOrder === 'blueLeft' ? 'red' : 'blue');
	const leftStations = $derived(allianceOrder === 'blueLeft' ? blueStations : redStations);
	const rightStations = $derived(allianceOrder === 'blueLeft' ? redStations : blueStations);
	const leftTeams = $derived(allianceOrder === 'blueLeft' ? blueTeams : redTeams);
	const rightTeams = $derived(allianceOrder === 'blueLeft' ? redTeams : blueTeams);
	const leftScore = $derived(allianceOrder === 'blueLeft' ? blueScore : redScore);
	const rightScore = $derived(allianceOrder === 'blueLeft' ? redScore : blueScore);
	const leftHubActive = $derived(allianceOrder === 'blueLeft' ? blueHubActive : redHubActive);
	const rightHubActive = $derived(allianceOrder === 'blueLeft' ? redHubActive : blueHubActive);
	const leftFuelCombined = $derived(allianceOrder === 'blueLeft' ? blueFuelCombined : redFuelCombined);
	const rightFuelCombined = $derived(allianceOrder === 'blueLeft' ? redFuelCombined : blueFuelCombined);
	const leftTowerCombined = $derived(allianceOrder === 'blueLeft' ? blueTowerCombined : redTowerCombined);
	const rightTowerCombined = $derived(allianceOrder === 'blueLeft' ? redTowerCombined : blueTowerCombined);

	const soundFiles = [
		'/sounds/match-start.wav',
		'/sounds/auto-end.wav',
		'/sounds/teleop-start.wav',
		'/sounds/match-end.wav',
		'/sounds/match-abort.wav',
		'/sounds/match_result.wav',
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

	// Track audience view transitions so Match Results has its own stinger.
	let prevAudienceView: string | null = null;
	let audienceViewInitialized = false;

	$effect(() => {
		const view = audienceView;

		if (!audienceViewInitialized) {
			prevAudienceView = view;
			audienceViewInitialized = true;
			return;
		}

		if (view === prevAudienceView) return;

		if (view === 'matchResults') {
			playSound('/sounds/match_result.wav');
		}

		prevAudienceView = view;
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
	isMiddle: boolean,
	avatar: string | null
)}
	<div
		class="flex items-center gap-2 {alliance === 'blue'
			? isMiddle
				? 'alliance-blue-bg-dark'
				: 'alliance-blue-bg-darker'
			: isMiddle
				? 'alliance-red-bg-dark'
				: 'alliance-red-bg-darker'} px-2 py-2 text-sm font-semibold md:text-4xl"
	>
		<img
			src={avatar ? `data:image/png;base64,${avatar}` : '/first-default-avatar.png'}
			alt=""
			class="h-8 w-8 shrink-0 object-contain md:h-10 md:w-10"
		/>
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

{#snippet matchResultsTeamRow(teamNum: number, nickname: string, avatar: string | null)}
	<div class="flex items-center gap-3 border-t border-black/20 px-3 py-2.5">
		<img
			src={avatar ? `data:image/png;base64,${avatar}` : '/first-default-avatar.png'}
			alt=""
			class="h-12 w-12 shrink-0 object-contain md:h-14 md:w-14"
		/>
		<div class="min-w-0 flex-1">
			<div class="text-5xl font-black leading-tight md:text-5xl">
				{teamNum > 0 ? teamNum : '----'}
			</div>
			<!-- {#if nickname}
				<div class="truncate text-xl font-semibold opacity-70">{nickname}</div>
			{/if} -->
		</div>
	</div>
{/snippet}

{#snippet rpIcons(rp: MatchResultRankingPoints, alliance: 'red' | 'blue')}
	{@const earnedBg = alliance === 'red' ? 'alliance-red-bg' : 'alliance-blue-bg'}
	{@const unearnedBg = alliance === 'red' ? 'alliance-red-bg-darker' : 'alliance-blue-bg-darker'}
	<div class="mt-auto border-t border-black/20 px-3 py-3">
		<div class="mb-2 text-[10px] font-black uppercase tracking-widest opacity-50">
			Ranking Points
		</div>
		<div class="flex flex-wrap items-center gap-2">
			{#each [
				{ icon: '/single_fuel.png', earned: rp.energized, label: 'Energized' },
				{ icon: '/multiple_fuel.png', earned: rp.supercharged, label: 'Supercharged' },
				{ icon: '/tower.png', earned: rp.traversal, label: 'Traversal' }
			] as item}
				<div
					class="flex h-13 w-13 items-center justify-center rounded shadow-md shadow-black/60 {item.earned
						? earnedBg
						: unearnedBg}"
					title={item.label}
				>
					<img
						src={item.icon}
						alt={item.label}
						class="h-9 w-9 object-contain {item.earned ? 'opacity-100' : 'opacity-30'}"
					/>
				</div>
			{/each}
			{#each Array.from({ length: 3 }, (_, idx) => idx) as idx}
				<div
					class="flex h-13 w-13 items-center justify-center rounded shadow-md shadow-black/60 {idx < rp.winTie
						? earnedBg
						: unearnedBg}"
					title="Win"
				>
					<img
						src="/trophy.png"
						alt="Win"
						class="h-9 w-9 object-contain {idx < rp.winTie ? 'opacity-100' : 'opacity-30'}"
					/>
				</div>
			{/each}
		</div>
	</div>
{/snippet}

{#snippet matchResultsView(match: MatchResultRecord | null)}
	<div class="absolute inset-0 flex flex-col">
		{#if match}
			{@const winner =
				match.redScore > match.blueScore
					? 'red'
					: match.blueScore > match.redScore
						? 'blue'
						: 'tie'}
			{@const isBlueLeft = allianceOrder === 'blueLeft'}
			{@const lAlliance = isBlueLeft ? 'blue' : 'red'}
			{@const rAlliance = isBlueLeft ? 'red' : 'blue'}
			{@const lTeams = isBlueLeft ? match.blueTeams : match.redTeams}
			{@const rTeams = isBlueLeft ? match.redTeams : match.blueTeams}
			{@const lNicknames = isBlueLeft ? match.blueTeamNicknames : match.redTeamNicknames}
			{@const rNicknames = isBlueLeft ? match.redTeamNicknames : match.blueTeamNicknames}
			{@const lAvatars = isBlueLeft ? match.blueTeamAvatars : match.redTeamAvatars}
			{@const rAvatars = isBlueLeft ? match.redTeamAvatars : match.blueTeamAvatars}
			{@const lScore = isBlueLeft ? match.blueScore : match.redScore}
			{@const rScore = isBlueLeft ? match.redScore : match.blueScore}
			{@const lBreakdown = isBlueLeft ? match.blueBreakdown : match.redBreakdown}
			{@const rBreakdown = isBlueLeft ? match.redBreakdown : match.blueBreakdown}
			{@const lRp = isBlueLeft ? match.blueRankingPoints : match.redRankingPoints}
			{@const rRp = isBlueLeft ? match.redRankingPoints : match.blueRankingPoints}

			<!-- Match title -->
			<div class="bg-gray-800 text-center text-xl font-black drop-shadow md:text-3xl">
				{match.matchType} Match {match.matchNumber}
				<span class="ml-2 text-sm font-semibold uppercase tracking-widest opacity-60">Results</span>
			</div>

			<!-- Main 3-column layout -->
			<div class="flex min-h-0 flex-1 overflow-hidden">
				<!-- Left alliance column -->
				<div
					class="{lAlliance === 'blue'
						? 'alliance-blue-bg-darker'
						: 'alliance-red-bg-darker'} flex w-[28%] flex-col text-white"
				>
					{#if winner === lAlliance}
						<div
							class="flex items-center justify-center gap-2 bg-yellow-400 px-3 py-4 text-4xl font-black uppercase tracking-wide text-black md:text-4xl"
						>
							<img src="/trophy.png" alt="" class="h-8 w-8 object-contain invert" />
							<span>Winner</span>
						</div>
					{:else if winner === 'tie'}
						<div
							class="flex items-center justify-center bg-yellow-400 px-3 py-4 text-4xl font-black uppercase text-black/80 md:text-3xl"
						>
							Tie
						</div>
					{:else}
						<div class="px-3 py-4 text-2xl font-black opacity-0 md:text-4xl">Winner</div>
					{/if}
					{#each lTeams as team, i}
						{@render matchResultsTeamRow(team, lNicknames[i], lAvatars[i])}
					{/each}
					{@render rpIcons(lRp, lAlliance)}
				</div>

				<!-- CENTER: scores + score breakdown -->
				<div class="flex flex-1 flex-col bg-neutral-200 text-black">
					<div class="flex">
						<div
							class="{lAlliance === 'blue'
								? 'alliance-blue-bg'
								: 'alliance-red-bg'} flex flex-1 flex-col items-center justify-center py-3 text-white"
						>
							<div class="text-xs font-black uppercase tracking-widest opacity-80 md:text-sm">
								{lAlliance === 'blue' ? 'Blue' : 'Red'}
							</div>
							<div class="text-5xl font-black leading-none md:text-7xl">{lScore}</div>
						</div>
						<div
							class="{rAlliance === 'blue'
								? 'alliance-blue-bg'
								: 'alliance-red-bg'} flex flex-1 flex-col items-center justify-center py-3 text-white"
						>
							<div class="text-xs font-black uppercase tracking-widest opacity-80 md:text-sm">
								{rAlliance === 'blue' ? 'Blue' : 'Red'}
							</div>
							<div class="text-5xl font-black leading-none md:text-7xl">{rScore}</div>
						</div>
					</div>
					<table class="w-full flex-1 border-collapse">
						<tbody>
							<tr class="bg-neutral-400">
								<th
									colspan="3"
									class="py-1.5 text-center text-xl font-black uppercase tracking-wider"
									>Auto</th
								>
							</tr>
							{#each [
								['Fuel', lBreakdown.autoFuelPoints, rBreakdown.autoFuelPoints],
								['Tower', lBreakdown.autoTowerPoints, rBreakdown.autoTowerPoints]
							] as [label, left, right]}
								<tr class="border-t border-neutral-300 bg-neutral-200">
									<td class="w-2/5 py-2 pr-4 text-right text-xl font-black md:text-5xl"
										>{left}</td
									>
									<td class="py-2 text-center text-xl font-black uppercase tracking-wider"
										>{label}</td
									>
									<td class="w-2/5 py-2 pl-4 text-left text-xl font-black md:text-5xl"
										>{right}</td
									>
								</tr>
							{/each}
							<tr class="border-t-2 border-neutral-400 bg-neutral-400">
								<th
									colspan="3"
									class="py-1.5 text-center text-xl font-black uppercase tracking-wider"
									>Teleop</th
								>
							</tr>
							{#each [
								['Fuel', lBreakdown.teleopFuelPoints, rBreakdown.teleopFuelPoints],
								['Tower', lBreakdown.teleopTowerPoints, rBreakdown.teleopTowerPoints]
							] as [label, left, right]}
								<tr class="border-t border-neutral-300 bg-neutral-200">
									<td class="w-2/5 py-2 pr-4 text-right text-xl font-black md:text-5xl"
										>{left}</td
									>
									<td class="py-2 text-center text-xl font-black uppercase tracking-wider"
										>{label}</td
									>
									<td class="w-2/5 py-2 pl-4 text-left text-xl font-black md:text-5xl"
										>{right}</td
									>
								</tr>
							{/each}
						</tbody>
					</table>
				</div>

				<!-- Right alliance column -->
				<div
					class="{rAlliance === 'blue'
						? 'alliance-blue-bg-darker'
						: 'alliance-red-bg-darker'} flex w-[28%] flex-col text-white"
				>
					{#if winner === rAlliance}
						<div
							class="flex items-center justify-center gap-2 bg-yellow-400 px-3 py-4 text-4xl font-black uppercase tracking-wide text-black md:text-4xl"
						>
							<img src="/trophy.png" alt="" class="h-8 w-8 object-contain invert" />
							<span>Winner</span>
						</div>
					{:else if winner === 'tie'}
						<div
							class="flex items-center justify-center bg-yellow-400 px-3 py-4 text-4xl font-black uppercase text-black/80 md:text-3xl"
						>
							Tie
						</div>
					{:else}
						<div class="px-3 py-4 text-2xl font-black opacity-0 md:text-4xl">Winner</div>
					{/if}
					{#each rTeams as team, i}
						{@render matchResultsTeamRow(team, rNicknames[i], rAvatars[i])}
					{/each}
					{@render rpIcons(rRp, rAlliance)}
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
						<!-- Left alliance hub active indicator -->
						{@render hubIndicator('left', leftHubActive)}
						<!-- Right alliance hub active indicator -->
						{@render hubIndicator('right', rightHubActive)}
						<div
							class="grid grid-cols-[1fr_1fr_1fr_1fr_1fr_1fr_1fr_1fr_1fr] overflow-hidden rounded-lg border border-black/30 shadow-md shadow-black/70"
						>
							<!-- Left Team 1 -->
						{@render allianceTeamCell(leftTeams[0], !!leftStations[0]?.robotLinked, leftAlliance, false, leftStations[0]?.avatarBase64 ?? null)}
						<!-- Left Team 2 (middle) -->
						{@render allianceTeamCell(leftTeams[1], !!leftStations[1]?.robotLinked, leftAlliance, true, leftStations[1]?.avatarBase64 ?? null)}
						<!-- Left Team 3 -->
						{@render allianceTeamCell(leftTeams[2], !!leftStations[2]?.robotLinked, leftAlliance, false, leftStations[2]?.avatarBase64 ?? null)}
							<!-- Left Score -->
							<div
								class="{leftAlliance === 'blue' ? 'alliance-blue-bg' : 'alliance-red-bg'} flex items-center justify-center px-5 text-4xl font-black md:text-6xl"
							>
								{leftScore}
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
							<!-- Right Score -->
							<div
								class="{rightAlliance === 'blue' ? 'alliance-blue-bg' : 'alliance-red-bg'} flex items-center justify-center px-5 text-4xl font-black md:text-6xl"
							>
								{rightScore}
							</div>
							<!-- Right Team 1 -->
						{@render allianceTeamCell(rightTeams[0], !!rightStations[0]?.robotLinked, rightAlliance, false, rightStations[0]?.avatarBase64 ?? null)}
						<!-- Right Team 2 (middle) -->
						{@render allianceTeamCell(rightTeams[1], !!rightStations[1]?.robotLinked, rightAlliance, true, rightStations[1]?.avatarBase64 ?? null)}
						<!-- Right Team 3 -->
						{@render allianceTeamCell(rightTeams[2], !!rightStations[2]?.robotLinked, rightAlliance, false, rightStations[2]?.avatarBase64 ?? null)}
						</div>
					</div>
					<div class="mt-2 grid grid-cols-1 gap-2 md:grid-cols-2">
					{@render rankingProgress(leftAlliance, leftFuelCombined, leftTowerCombined)}
					{@render rankingProgress(rightAlliance, rightFuelCombined, rightTowerCombined)}
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
