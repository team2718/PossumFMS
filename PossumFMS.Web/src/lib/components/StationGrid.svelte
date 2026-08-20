<script lang="ts">
	import { fms } from '$lib/fms.svelte';
	import type { Station } from '$lib/fms.svelte';
	import { formatTime, stationLabel } from '$lib/utils/formatters';

	let {
		inputs = $bindable(),
		isConfiguring = $bindable(false),
		configureWarning = $bindable(''),
		configureSuccess = $bindable(''),
		onOpenWpaModal
	} = $props<{
		inputs: Array<{ team: string; wpa: string }>;
		isConfiguring: boolean;
		configureWarning: string;
		configureSuccess: string;
		onOpenWpaModal?: () => void;
	}>();

	const matchState = $derived(fms.matchState);
	const phase = $derived(matchState?.phase ?? 'Disconnected');

	const redStations = $derived<Station[]>(matchState?.stations.slice(0, 3) ?? []);
	const blueStations = $derived<Station[]>(matchState?.stations.slice(3, 6) ?? []);

	const isMatchStartingOrRunning = $derived(
		phase === 'PreMatch' ||
			phase === 'Auto' ||
			phase === 'AutoToTeleopTransition' ||
			phase === 'Teleop'
	);

	const redReady = $derived(
		redStations.length === 3 &&
			redStations.every((s) => (isMatchStartingOrRunning ? s.isReadyInMatch : s.isReady))
	);

	const blueReady = $derived(
		blueStations.length === 3 &&
			blueStations.every((s) => (isMatchStartingOrRunning ? s.isReadyInMatch : s.isReady))
	);

	const readinessPositiveLabel = 'READY';
	const readinessNegativeLabel = 'NOT READY';

	const displayPhase = $derived(
		(() => {
			switch (phase) {
				case 'PreMatch':
					return 'Pre-Match';
				case 'Auto':
					return 'Autonomous';
				case 'AutoToTeleopTransition':
					return 'Transition';
				case 'Teleop':
					return 'Teleop';
				case 'PostMatch':
					return 'Post-Match';
				case 'Idle':
					return 'Idle';
				default:
					return phase;
			}
		})()
	);

	// Phase duration calculations for animated progress bar
	const autoDuration = $derived(matchState?.matchDurations?.autoSeconds ?? 20);
	const transDuration = $derived(matchState?.matchDurations?.autoToTeleopTransitionSeconds ?? 3);
	const teleopDuration = $derived(matchState?.matchDurations?.teleopSeconds ?? 140);
	const totalDuration = $derived(autoDuration + transDuration + teleopDuration || 163);

	const autoWidthPercent = $derived((autoDuration / totalDuration) * 100);
	const transWidthPercent = $derived((transDuration / totalDuration) * 100);
	const teleopWidthPercent = $derived((teleopDuration / totalDuration) * 100);

	const autoFillPercent = $derived(
		(() => {
			if (phase === 'Idle' || phase === 'PreMatch') return 0;
			if (phase === 'Auto') {
				const elapsed = Math.max(0, autoDuration - (matchState?.timeRemaining ?? 0));
				return Math.min(100, Math.max(0, (elapsed / (autoDuration || 1)) * 100));
			}
			return 100;
		})()
	);

	const transFillPercent = $derived(
		(() => {
			if (phase === 'Idle' || phase === 'PreMatch' || phase === 'Auto') return 0;
			if (phase === 'AutoToTeleopTransition') {
				const elapsed = Math.max(0, transDuration - (matchState?.timeRemaining ?? 0));
				return Math.min(100, Math.max(0, (elapsed / (transDuration || 1)) * 100));
			}
			return 100;
		})()
	);

	const teleopFillPercent = $derived(
		(() => {
			if (phase === 'Teleop') {
				const elapsed = Math.max(0, teleopDuration - (matchState?.timeRemaining ?? 0));
				return Math.min(100, Math.max(0, (elapsed / (teleopDuration || 1)) * 100));
			}
			if (phase === 'PostMatch') return 100;
			return 0;
		})()
	);

	function hasActiveEstopHardware(stationIndex: number): boolean {
		const station = fms.matchState?.stations?.[stationIndex];
		if (!station) return false;

		const allianceName = station.alliance.toLowerCase();
		const stationPosition = station.position;

		return (
			fms.matchState?.fieldDevices?.some(
				(d) =>
					d.type === 'Estop' &&
					d.status === 'Connected' &&
					!d.bypassed &&
					d.heartbeat?.kind === 'Estop' &&
					((d.heartbeat.field.toLowerCase() === allianceName &&
						(d.heartbeat.station === stationPosition || d.heartbeat.station === 0)) ||
						d.heartbeat.field.toLowerCase() === 'field')
			) ?? false
		);
	}

	// Calculate reasons why prestart/start might be blocked
	const unreadyReasons = $derived(
		(() => {
			if (!matchState) return ['Connecting to FMS…'];
			if (matchState.arenaEstop) return ['Arena E-Stop is active'];

			if (phase === 'PreMatch') {
				const issues: string[] = [];
				for (let i = 0; i < 6; i++) {
					const s = matchState.stations[i];
					if (!s || s.bypassed) continue;
					const name = stationLabel(i);

					if (s.estop) {
						issues.push(`${name} is E-Stopped`);
					} else if (s.astop) {
						issues.push(`${name} is A-Stopped`);
					} else if (!s.dsLinked) {
						issues.push(`${name} no DS link`);
					} else if (!s.robotLinked) {
						issues.push(`${name} no Robot link`);
					} else if (
						matchState.requireFieldEstopForMatchStart !== false &&
						!hasActiveEstopHardware(i)
					) {
						issues.push(`${name} no HW E-Stop`);
					}
				}

				if (issues.length === 0) return ['✓ All stations ready to start'];
				return issues;
			}

			if (phase === 'Idle') {
				if (matchState.freePracticeEnabled) return ['Free Practice Mode'];
				return ['Arena Idle — Ready for Prestart'];
			}

			return [];
		})()
	);

	const blueInputIndices = [3, 4, 5];
	const redInputIndices = [0, 1, 2];

	async function configureAccessPoint() {
		configureWarning = '';
		configureSuccess = '';

		if (phase !== 'Idle') {
			configureWarning = 'Team assignments can only be changed while the arena is idle.';
			return;
		}

		const seenTeams = new Map<number, number>();
		const teamsToAssign = new Array<number>(inputs.length).fill(0);

		for (let idx = 0; idx < inputs.length; idx++) {
			const raw = String(inputs[idx].team ?? '').trim();
			if (!raw) continue;

			const teamNumber = parseInt(raw);
			if (isNaN(teamNumber) || teamNumber <= 0) continue;
			teamsToAssign[idx] = teamNumber;

			const seenAt = seenTeams.get(teamNumber);
			if (seenAt !== undefined) {
				configureWarning = `Team ${teamNumber} is entered for both ${stationLabel(seenAt)} and ${stationLabel(idx)}.`;
				return;
			}

			seenTeams.set(teamNumber, idx);
		}

		isConfiguring = true;

		try {
			await fms.assignTeams(
				teamsToAssign.map((teamNumber, idx) => ({
					teamNumber,
					wpaKey: inputs[idx].wpa
				}))
			);

			await fms.configureAccessPoint();
			configureSuccess = 'Teams assigned and AP configuration requested.';
		} catch (error) {
			configureWarning =
				error instanceof Error ? error.message : 'Failed to configure. Please try again.';
		} finally {
			isConfiguring = false;
		}
	}

	async function clearAllTeams() {
		if (isConfiguring) return;

		configureWarning = '';
		configureSuccess = '';

		if (phase !== 'Idle') {
			configureWarning = 'Teams can only be cleared while the arena is idle.';
			return;
		}

		for (let i = 0; i < inputs.length; i++) {
			inputs[i].team = '';
			inputs[i].wpa = '';
		}

		isConfiguring = true;
		try {
			await fms.assignTeams(
				Array.from({ length: 6 }, () => ({
					teamNumber: 0,
					wpaKey: ''
				}))
			);
			await fms.configureAccessPoint();
			configureSuccess = 'Cleared all teams and reset access point.';
		} catch (error) {
			configureWarning =
				error instanceof Error ? error.message : 'Failed to clear teams. Please try again.';
		} finally {
			isConfiguring = false;
		}
	}
</script>

{#snippet readinessHeaderRow()}
	<div
		class="hidden items-center gap-1 border-b border-slate-200/80 px-1.5 py-1 text-center text-[10px] font-bold text-slate-500 uppercase sm:grid sm:grid-cols-[68px_66px_minmax(128px,1fr)_48px_44px_44px_88px_80px] dark:border-slate-700/80 dark:text-slate-400"
	>
		<div>E-Stop HW</div>
		<div>Station</div>
		<div>Team</div>
		<div>Bypass</div>
		<div>DS</div>
		<div>Robot</div>
		<div>Enabled</div>
		<div>E-Stop</div>
	</div>
{/snippet}

{#snippet readinessStatusCell(online: boolean)}
	<div
		class="mx-auto flex h-7 w-10 items-center justify-center rounded font-bold text-white shadow-xs {online
			? 'bg-emerald-600'
			: 'bg-rose-700'}"
	>
		{online ? 'OK' : 'X'}
	</div>
{/snippet}

{#snippet robotEnabledCell(estop: boolean, astop: boolean, robotLinked: boolean, bypassed: boolean)}
	{@const state = estop
		? { label: 'E-Stopped', classes: 'bg-rose-700 text-white shadow-xs' }
		: astop
			? { label: 'A-Stopped', classes: 'bg-rose-700 text-white shadow-xs' }
			: bypassed
				? { label: 'Bypassed', classes: 'bg-slate-500 text-white shadow-xs' }
				: !robotLinked
					? { label: 'No Robot', classes: 'bg-slate-500 text-white shadow-xs' }
					: { label: 'Enabled', classes: 'bg-emerald-600 text-white shadow-xs' }}
	<div
		class="mx-auto flex h-7 w-20 items-center justify-center rounded px-1 text-[10px] font-bold whitespace-nowrap {state.classes}"
	>
		{state.label}
	</div>
{/snippet}

{#snippet stopButton(type: 'E' | 'A', active: boolean, stationIndex: number)}
	<button
		type="button"
		onclick={() => (type === 'E' ? fms.estopStation(stationIndex) : fms.astopStation(stationIndex))}
		class="mx-auto h-7 w-14 cursor-pointer rounded border border-rose-900 px-1 text-[10px] font-black tracking-wide text-white shadow-xs transition active:translate-y-px {active
			? 'bg-rose-950'
			: 'bg-rose-700 hover:bg-rose-600'}"
	>
		{type}-Stop
	</button>
{/snippet}

<div
	class="rounded border border-slate-300 bg-white shadow-xs transition-colors dark:border-slate-700 dark:bg-slate-800"
>
	<!-- Team Configuration Toolbar -->
	<div
		class="flex flex-wrap items-center justify-between gap-2 border-b border-slate-200 bg-slate-50 px-3 py-2 text-xs dark:border-slate-700 dark:bg-slate-900/60"
	>
		<div class="flex items-center gap-2">
			<span class="font-bold tracking-wider text-slate-700 uppercase dark:text-slate-300">
				Field Stations & Teams
			</span>
			{#if phase !== 'Idle'}
				<span
					class="inline-flex items-center gap-1 rounded bg-slate-200 px-2 py-0.5 text-[10px] font-bold text-slate-700 dark:bg-slate-700 dark:text-slate-200"
				>
					<svg
						xmlns="http://www.w3.org/2000/svg"
						class="h-3 w-3"
						fill="none"
						viewBox="0 0 24 24"
						stroke="currentColor"
					>
						<path
							stroke-linecap="round"
							stroke-linejoin="round"
							stroke-width="2"
							d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z"
						/>
					</svg>
					Locked ({displayPhase})
				</span>
			{/if}
			{#if configureWarning}
				<span
					class="rounded bg-rose-100 px-2 py-0.5 font-semibold text-rose-800 dark:bg-rose-950/80 dark:text-rose-300"
				>
					{configureWarning}
				</span>
			{/if}
			{#if configureSuccess}
				<span
					class="rounded bg-emerald-100 px-2 py-0.5 font-semibold text-emerald-800 dark:bg-emerald-950/80 dark:text-emerald-300"
				>
					{configureSuccess}
				</span>
			{/if}
		</div>
		<div class="flex items-center gap-2">
			{#if onOpenWpaModal}
				<button
					type="button"
					onclick={onOpenWpaModal}
					class="cursor-pointer rounded border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700 shadow-xs hover:bg-slate-100 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-700"
				>
					Edit WPA Keys
				</button>
			{/if}
			<button
				type="button"
				onclick={clearAllTeams}
				disabled={phase !== 'Idle' || isConfiguring}
				class="cursor-pointer rounded border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700 shadow-xs hover:bg-slate-100 disabled:cursor-not-allowed disabled:opacity-40 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-700"
			>
				Clear Teams
			</button>
			<button
				type="button"
				onclick={configureAccessPoint}
				disabled={phase !== 'Idle' || isConfiguring}
				class="brand-secondary-bg cursor-pointer rounded px-3 py-1 text-xs font-bold text-white shadow-xs hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-40"
			>
				{isConfiguring ? 'Configuring AP…' : 'Assign Teams & Configure AP'}
			</button>
		</div>
	</div>

	<!-- Readiness Matrix -->
	<div class="grid grid-cols-1 xl:grid-cols-[1fr_210px_1fr]">
		<!-- Blue Alliance -->
		<div
			class="alliance-blue-bg-soft border-b border-slate-300 xl:border-r xl:border-b-0 dark:border-slate-700"
		>
			<div class="alliance-blue-border-soft flex items-center justify-between border-b px-3 py-2">
				<span class="alliance-blue-text text-sm font-bold tracking-wide">BLUE ALLIANCE</span>
				<span
					class="rounded px-2 py-0.5 text-xs font-bold text-white shadow-xs {blueReady
						? 'bg-emerald-700'
						: 'bg-rose-700'}"
				>
					{blueReady ? readinessPositiveLabel : readinessNegativeLabel}
				</span>
			</div>
			<div class="overflow-x-auto p-2 text-xs">
				{@render readinessHeaderRow()}
				{#each blueStations as s, i (s.index)}
					{@const idx = blueInputIndices[i]}
					<div
						class="alliance-blue-border-soft mt-1.5 grid grid-cols-[68px_66px_minmax(128px,1fr)_48px_44px_44px_88px_80px] items-center gap-1 rounded border bg-white/80 px-1.5 py-1.5 shadow-xs dark:bg-slate-900/70"
					>
						{@render readinessStatusCell(hasActiveEstopHardware(idx))}
						<div class="alliance-blue-text text-center font-bold">Station {i + 1}</div>
						<div class="flex items-center gap-1">
							<input
								type="text"
								inputmode="numeric"
								pattern="[0-9]*"
								placeholder="Team"
								bind:value={inputs[idx].team}
								disabled={phase !== 'Idle'}
								class="h-7 w-full min-w-[8rem] rounded border border-slate-300 bg-white px-2 text-xs text-slate-900 placeholder-slate-400 disabled:cursor-not-allowed disabled:bg-slate-100 disabled:opacity-60 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100 dark:disabled:bg-slate-900/60"
							/>
						</div>
						<input
							type="checkbox"
							checked={s.bypassed}
							disabled={phase !== 'Idle'}
							onchange={() => fms.bypassStation(idx, !s.bypassed)}
							class="mx-auto h-4 w-4 cursor-pointer rounded border-slate-300 disabled:cursor-not-allowed disabled:opacity-40 dark:border-slate-600"
						/>
						{@render readinessStatusCell(s.dsLinked)}
						{@render readinessStatusCell(s.robotLinked)}
						{@render robotEnabledCell(s.estop, s.astop, s.robotLinked, s.bypassed)}
						{@render stopButton('E', s.estop, idx)}
					</div>
				{/each}
			</div>
		</div>

		<!-- Center Column: Match Status & Diagnostics -->
		<div
			class="order-first flex flex-col items-center justify-center gap-2 border-b border-slate-300 bg-slate-50 px-3 py-4 text-center xl:order-none xl:border-b-0 dark:border-slate-700 dark:bg-slate-900/60"
		>
			<div
				class="text-[11px] font-bold tracking-widest text-slate-500 uppercase dark:text-slate-400"
			>
				Match Status
			</div>
			<div class="text-lg font-black tracking-tight text-slate-800 dark:text-slate-100">
				{matchState?.matchType ?? 'Test'} Match {matchState?.matchNumber ?? 1}
			</div>

			<!-- Phase Pill -->
			<div
				class="inline-flex items-center gap-1.5 rounded-full border border-slate-300 bg-white px-3 py-0.5 shadow-xs dark:border-slate-600 dark:bg-slate-800"
			>
				<span
					class="h-2 w-2 rounded-full {isMatchStartingOrRunning
						? 'animate-pulse bg-emerald-500'
						: 'bg-slate-400'}"
				></span>
				<span
					class="text-[11px] font-bold tracking-wider text-slate-700 uppercase dark:text-slate-200"
				>
					{displayPhase}
				</span>
			</div>

			<!-- Big Countdown Clock -->
			<div class="font-mono text-3xl font-black text-slate-900 tabular-nums dark:text-slate-100">
				{matchState ? formatTime(matchState.timeRemaining) : '0:00'}
			</div>

			<!-- Match Phase Segmented Progress Bar (Animated Proportional Fills) -->
			<div class="w-full max-w-[190px] space-y-1">
				<div
					class="flex h-2.5 w-full overflow-hidden rounded-full bg-slate-200 p-0.5 shadow-inner dark:bg-slate-700/80"
				>
					<!-- Auto Segment Track -->
					<div
						class="relative h-full overflow-hidden rounded-l-full bg-slate-300/40 dark:bg-slate-800/60"
						style="width: {autoWidthPercent}%;"
						title="Autonomous ({autoDuration}s)"
					>
						<div
							class="h-full bg-amber-500 transition-all duration-200 ease-linear"
							style="width: {autoFillPercent}%;"
						></div>
					</div>

					<!-- Transition Segment Track -->
					<div
						class="relative mx-0.5 h-full overflow-hidden bg-slate-300/40 dark:bg-slate-800/60"
						style="width: {transWidthPercent}%;"
						title="Transition ({transDuration}s)"
					>
						<div
							class="h-full bg-sky-400 transition-all duration-200 ease-linear"
							style="width: {transFillPercent}%;"
						></div>
					</div>

					<!-- Teleop Segment Track -->
					<div
						class="relative h-full overflow-hidden rounded-r-full bg-slate-300/40 dark:bg-slate-800/60"
						style="width: {teleopWidthPercent}%;"
						title="Teleop ({teleopDuration}s)"
					>
						<div
							class="h-full bg-emerald-500 transition-all duration-200 ease-linear"
							style="width: {teleopFillPercent}%;"
						></div>
					</div>
				</div>
				<div
					class="flex justify-between text-[9px] font-bold tracking-tight text-slate-400 dark:text-slate-500"
				>
					<span class={phase === 'Auto' ? 'font-black text-amber-600 dark:text-amber-400' : ''}
						>AUTO ({autoDuration}s)</span
					>
					<span
						class={phase === 'AutoToTeleopTransition'
							? 'font-black text-sky-600 dark:text-sky-400'
							: ''}>TRANS ({transDuration}s)</span
					>
					<span
						class={phase === 'Teleop' ? 'font-black text-emerald-600 dark:text-emerald-400' : ''}
						>TELEOP ({teleopDuration}s)</span
					>
				</div>
			</div>

			<!-- Readiness & Diagnostic Reason Pill -->
			<div class="mt-1 w-full px-1">
				{#if unreadyReasons.length > 0}
					<div
						class="rounded-md border px-2 py-1 text-[10px] leading-tight font-bold shadow-xs {unreadyReasons[0].startsWith(
							'✓'
						)
							? 'border-emerald-300 bg-emerald-50 text-emerald-800 dark:border-emerald-800 dark:bg-emerald-950/80 dark:text-emerald-300'
							: phase === 'PreMatch'
								? 'border-amber-300 bg-amber-50 text-amber-800 dark:border-amber-800 dark:bg-amber-950/80 dark:text-amber-300'
								: 'border-slate-200 bg-slate-100 text-slate-600 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-400'}"
					>
						{#each unreadyReasons.slice(0, 2) as reason}
							<div>{reason}</div>
						{/each}
						{#if unreadyReasons.length > 2}
							<div class="text-[9px] opacity-75">+{unreadyReasons.length - 2} more</div>
						{/if}
					</div>
				{/if}
			</div>
		</div>

		<!-- Red Alliance -->
		<div class="alliance-red-bg-soft">
			<div class="alliance-red-border-soft flex items-center justify-between border-b px-3 py-2">
				<span
					class="rounded px-2 py-0.5 text-xs font-bold text-white shadow-xs {redReady
						? 'bg-emerald-700'
						: 'bg-rose-700'}"
				>
					{redReady ? readinessPositiveLabel : readinessNegativeLabel}
				</span>
				<span class="alliance-red-text text-sm font-bold tracking-wide">RED ALLIANCE</span>
			</div>
			<div class="overflow-x-auto p-2 text-xs">
				{@render readinessHeaderRow()}
				{#each redStations as s, i (s.index)}
					{@const idx = redInputIndices[i]}
					<div
						class="alliance-red-border-soft mt-1.5 grid grid-cols-[68px_66px_minmax(128px,1fr)_48px_44px_44px_88px_80px] items-center gap-1 rounded border bg-white/80 px-1.5 py-1.5 shadow-xs dark:bg-slate-900/70"
					>
						{@render readinessStatusCell(hasActiveEstopHardware(idx))}
						<div class="alliance-red-text text-center font-bold">Station {i + 1}</div>
						<div class="flex items-center gap-1">
							<input
								type="text"
								inputmode="numeric"
								pattern="[0-9]*"
								placeholder="Team"
								bind:value={inputs[idx].team}
								disabled={phase !== 'Idle'}
								class="h-7 w-full min-w-[8rem] rounded border border-slate-300 bg-white px-2 text-xs text-slate-900 placeholder-slate-400 disabled:cursor-not-allowed disabled:bg-slate-100 disabled:opacity-60 dark:border-slate-600 dark:bg-slate-800 dark:text-slate-100 dark:disabled:bg-slate-900/60"
							/>
						</div>
						<input
							type="checkbox"
							checked={s.bypassed}
							disabled={phase !== 'Idle'}
							onchange={() => fms.bypassStation(idx, !s.bypassed)}
							class="mx-auto h-4 w-4 cursor-pointer rounded border-slate-300 disabled:cursor-not-allowed disabled:opacity-40 dark:border-slate-600"
						/>
						{@render readinessStatusCell(s.dsLinked)}
						{@render readinessStatusCell(s.robotLinked)}
						{@render robotEnabledCell(s.estop, s.astop, s.robotLinked, s.bypassed)}
						{@render stopButton('E', s.estop, idx)}
					</div>
				{/each}
			</div>
		</div>
	</div>
</div>
