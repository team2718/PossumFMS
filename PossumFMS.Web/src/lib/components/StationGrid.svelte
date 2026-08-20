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

	const readinessPositiveLabel = $derived(isMatchStartingOrRunning ? 'READY' : 'LINKED');
	const readinessNegativeLabel = $derived(isMatchStartingOrRunning ? 'NOT READY' : 'NOT LINKED');

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
		class="hidden items-center gap-1 px-1 py-1 text-center font-bold text-slate-600 sm:grid sm:grid-cols-[68px_66px_minmax(128px,1fr)_48px_44px_44px_88px_80px]"
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
		class="mx-auto flex h-7 w-10 items-center justify-center rounded font-bold text-white {online
			? 'bg-emerald-600'
			: 'bg-rose-700'}"
	>
		{online ? 'OK' : 'X'}
	</div>
{/snippet}

{#snippet robotEnabledCell(estop: boolean, astop: boolean, robotLinked: boolean, bypassed: boolean)}
	{@const state = estop
		? { label: 'E-Stopped', classes: 'bg-rose-700 text-white' }
		: astop
			? { label: 'A-Stopped', classes: 'bg-rose-700 text-white' }
			: bypassed
				? { label: 'Bypassed', classes: 'bg-slate-500 text-white' }
				: !robotLinked
					? { label: 'No Robot', classes: 'bg-slate-500 text-white' }
					: { label: 'Enabled', classes: 'bg-emerald-600 text-white' }}
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

<div class="rounded border border-slate-300 bg-white shadow-xs">
	<!-- Team Configuration Toolbar -->
	<div class="flex flex-wrap items-center justify-between gap-2 border-b border-slate-200 bg-slate-50 px-3 py-2 text-xs">
		<div class="flex items-center gap-2">
			<span class="font-bold text-slate-700 uppercase tracking-wider">Field Stations & Teams</span>
			{#if configureWarning}
				<span class="rounded bg-rose-100 px-2 py-0.5 font-semibold text-rose-800">{configureWarning}</span>
			{/if}
			{#if configureSuccess}
				<span class="rounded bg-emerald-100 px-2 py-0.5 font-semibold text-emerald-800">{configureSuccess}</span>
			{/if}
		</div>
		<div class="flex items-center gap-2">
			{#if onOpenWpaModal}
				<button
					type="button"
					onclick={onOpenWpaModal}
					class="cursor-pointer rounded border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700 hover:bg-slate-100"
				>
					Edit WPA Keys
				</button>
			{/if}
			<button
				type="button"
				onclick={clearAllTeams}
				disabled={phase !== 'Idle' || isConfiguring}
				class="cursor-pointer rounded border border-slate-300 bg-white px-2.5 py-1 text-xs font-semibold text-slate-700 hover:bg-slate-100 disabled:opacity-50"
			>
				Clear Teams
			</button>
			<button
				type="button"
				onclick={configureAccessPoint}
				disabled={phase !== 'Idle' || isConfiguring}
				class="brand-secondary-bg cursor-pointer rounded px-3 py-1 text-xs font-bold text-white hover:opacity-90 disabled:opacity-50"
			>
				{isConfiguring ? 'Configuring AP…' : 'Assign Teams & Configure AP'}
			</button>
		</div>
	</div>

	<!-- Readiness Matrix -->
	<div class="grid grid-cols-1 xl:grid-cols-[1fr_170px_1fr]">
		<!-- Blue Alliance -->
		<div class="alliance-blue-bg-soft border-b border-slate-300 xl:border-r xl:border-b-0">
			<div class="alliance-blue-border-soft flex items-center justify-between border-b px-3 py-2">
				<span class="alliance-blue-text text-sm font-bold tracking-wide">BLUE ALLIANCE</span>
				<span
					class="rounded px-2 py-0.5 text-xs font-bold text-white {blueReady
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
						class="alliance-blue-border-soft mt-1 grid grid-cols-[68px_66px_minmax(128px,1fr)_48px_44px_44px_88px_80px] items-center gap-1 rounded border bg-white/75 px-1.5 py-1.5"
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
								class="h-7 w-full min-w-[8rem] rounded border border-slate-300 bg-white px-2 text-xs"
							/>
						</div>
						<input
							type="checkbox"
							checked={s.bypassed}
							disabled={phase !== 'Idle'}
							onchange={() => fms.bypassStation(idx, !s.bypassed)}
							class="mx-auto h-4 w-4 cursor-pointer rounded border-slate-300"
						/>
						{@render readinessStatusCell(s.dsLinked)}
						{@render readinessStatusCell(s.robotLinked)}
						{@render robotEnabledCell(s.estop, s.astop, s.robotLinked, s.bypassed)}
						{@render stopButton('E', s.estop, idx)}
					</div>
				{/each}
			</div>
		</div>

		<!-- Center Column: Match Status -->
		<div
			class="order-first flex flex-col items-center justify-center gap-2 border-b border-slate-300 bg-slate-50 px-4 py-4 text-center xl:order-none xl:border-b-0"
		>
			<div class="text-xs font-bold tracking-widest text-slate-500 uppercase">Match Status</div>
			<div class="text-xl font-black tracking-tight text-slate-800">
				{matchState?.matchType ?? 'Test'} Match {matchState?.matchNumber ?? 1}
			</div>

			<div
				class="inline-flex items-center gap-2 rounded-full border border-slate-300 bg-white px-3 py-1 shadow-xs"
			>
				<span class="text-[11px] font-bold tracking-wider text-slate-600 uppercase">
					{displayPhase}
				</span>
			</div>
			<div class="font-mono text-2xl font-black text-slate-800 tabular-nums">
				{matchState ? formatTime(matchState.timeRemaining) : '0:00'}
			</div>
		</div>

		<!-- Red Alliance -->
		<div class="alliance-red-bg-soft">
			<div class="alliance-red-border-soft flex items-center justify-between border-b px-3 py-2">
				<span
					class="rounded px-2 py-0.5 text-xs font-bold text-white {redReady
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
						class="alliance-red-border-soft mt-1 grid grid-cols-[68px_66px_minmax(128px,1fr)_48px_44px_44px_88px_80px] items-center gap-1 rounded border bg-white/75 px-1.5 py-1.5"
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
								class="h-7 w-full min-w-[8rem] rounded border border-slate-300 bg-white px-2 text-xs"
							/>
						</div>
						<input
							type="checkbox"
							checked={s.bypassed}
							disabled={phase !== 'Idle'}
							onchange={() => fms.bypassStation(idx, !s.bypassed)}
							class="mx-auto h-4 w-4 cursor-pointer rounded border-slate-300"
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

