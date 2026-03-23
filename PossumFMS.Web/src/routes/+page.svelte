<script lang="ts">
	import { fms } from '$lib/fms.svelte';
	import type {
		FieldDeviceDiagnostics,
		LogSeverity,
		RecentLogEntry,
		TowerEndgameLevel
	} from '$lib/fms.svelte';

	// Connect to the FMS hub when the page loads
	$effect(() => {
		fms.connect();
	});

	// Local inputs for team number and WPA key — one per station (index 0–5)
	let inputs = $state(Array.from({ length: 6 }, () => ({ team: '', wpa: '' })));

	// Pre-fill inputs from the first MatchState broadcast so existing assignments show up
	let hasInitialized = $state(false);
	$effect(() => {
		if (!hasInitialized && fms.matchState?.stations) {
			for (let i = 0; i < 6; i++) {
				const t = fms.matchState.stations[i].teamNumber;
				if (t > 0) inputs[i].team = String(t);
			}
			hasInitialized = true;
		}
	});

	// Format seconds as M:SS (e.g. 2:05)
	function formatTime(secs: number): string {
		const total = Math.ceil(secs);
		const m = Math.floor(total / 60);
		const s = total % 60;
		return `${m}:${s.toString().padStart(2, '0')}`;
	}

	function formatLastRobotLink(secondsSinceLink: number, robotLinked: boolean): string {
		if (robotLinked) return 'Now';
		if (secondsSinceLink >= 300) return '>5 mins ago';

		const wholeSeconds = Math.max(0, Math.round(secondsSinceLink));
		return `${wholeSeconds} ${wholeSeconds === 1 ? 'second' : 'seconds'} ago`;
	}

	function formatTimestamp(value: string): string {
		const timestamp = new Date(value);
		if (Number.isNaN(timestamp.getTime())) return '—';
		return timestamp.toLocaleString();
	}

	function formatLogTimestamp(value: string): string {
		const timestamp = new Date(value);
		if (Number.isNaN(timestamp.getTime())) return value;
		return timestamp.toLocaleString();
	}

	function formatAgo(seconds: number): string {
		if (seconds < 1) return '<1s ago';
		if (seconds < 60) return `${Math.round(seconds)}s ago`;
		if (seconds < 3600) return `${Math.floor(seconds / 60)}m ${Math.round(seconds % 60)}s ago`;
		const hours = Math.floor(seconds / 3600);
		const minutes = Math.round((seconds % 3600) / 60);
		return `${hours}h ${minutes}m ago`;
	}

	function statusBadgeClasses(status: string): string {
		return status === 'Connected'
			? 'bg-emerald-100 text-emerald-800'
			: status === 'Error'
				? 'bg-rose-100 text-rose-800'
				: 'bg-slate-200 text-slate-700';
	}

	function logEntryClasses(level: LogSeverity): string {
		return level === 'Critical'
			? 'border-red-950 bg-red-950 text-white'
			: level === 'Error'
				? 'border-rose-300 bg-rose-100 text-rose-950'
				: level === 'Warning'
					? 'border-amber-300 bg-amber-100 text-amber-950'
					: level === 'Information'
						? 'border-sky-200 bg-sky-50 text-sky-950'
						: level === 'Debug'
							? 'border-slate-300 bg-slate-100 text-slate-900'
							: 'border-slate-200 bg-white text-slate-700';
	}

	function deviceSpecificValues(
		device: FieldDeviceDiagnostics
	): Array<{ label: string; value: string }> {
		if (!device.heartbeat) return [{ label: 'Values', value: 'No parsed heartbeat yet' }];

		if (device.heartbeat.kind === 'Hub') {
			return [
				{ label: 'Alliance', value: device.heartbeat.alliance.toUpperCase() },
				{ label: 'Fuel Delta', value: String(device.heartbeat.fuelDelta) },
				{ label: 'Heartbeat', value: formatTimestamp(device.heartbeat.receivedUtc) }
			];
		}

		return [
			{ label: 'Field', value: device.heartbeat.field.toUpperCase() },
			{ label: 'Station', value: String(device.heartbeat.station) },
			{ label: 'A-Stop', value: device.heartbeat.astopActivated ? 'Active' : 'Clear' },
			{ label: 'E-Stop', value: device.heartbeat.estopActivated ? 'Active' : 'Clear' },
			{ label: 'Heartbeat', value: formatTimestamp(device.heartbeat.receivedUtc) }
		];
	}

	// Assign the team from the input field, or 0 if the field is blank
	let configureWarning = $state('');
	let configureSuccess = $state('');
	let isConfiguring = $state(false);

	type StationStatus = {
		teamNumber: number;
		estop: boolean;
		astop: boolean;
		bypassed: boolean;
		wrongStation: string | boolean;
		battery: number;
		robotLinked: boolean;
		dsLinked: boolean;
		radioLinked: boolean;
		rioLinked: boolean;
		isReady: boolean;
		isReadyInMatch: boolean;
		tripTimeMs: number;
		missedPackets: number;
		secondsSinceLastRobotLink: number;
		wifi?: {
			snr: number;
			rxRateMbps: number;
			txRateMbps: number;
			bandwidthMbps: number;
			radioLinked: boolean;
		} | null;
	};

	function stationLabel(idx: number): string {
		return idx < 3 ? `Red ${idx + 1}` : `Blue ${idx - 2}`;
	}

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
			configureWarning = 'Team assignments can only be changed while the arena is idle.';
			return;
		}

		isConfiguring = true;

		try {
			for (let idx = 0; idx < inputs.length; idx++) {
				inputs[idx].team = '';
				inputs[idx].wpa = '';
			}

			await fms.assignTeams(
				Array.from({ length: inputs.length }, () => ({
					teamNumber: 0,
					wpaKey: ''
				}))
			);

			configureSuccess = 'All teams were cleared.';
		} catch (error) {
			configureWarning =
				error instanceof Error ? error.message : 'Failed to clear teams. Please try again.';
		} finally {
			isConfiguring = false;
		}
	}

	// Derived helpers so the template isn't doing repeated fms.state?.... lookups
	const matchState = $derived(fms.matchState);
	$effect(() => {
		if (configureSuccess && matchState?.accessPoint.status === 'ACTIVE') {
			configureSuccess = '';
		}
	});
	const phase = $derived(matchState?.phase ?? 'Disconnected');
	const displayPhase = $derived(
		matchState?.freePracticeEnabled && matchState.phase === 'Idle' ? 'Free Practice' : phase
	);
	const blueStations = $derived(matchState?.stations.slice(3, 6) ?? []);
	const redStations = $derived(
		matchState ? [matchState.stations[2], matchState.stations[1], matchState.stations[0]] : []
	);

	const blueInputIndices = [3, 4, 5];
	const redInputIndices = [2, 1, 0];
	const redScoreStationIndices = [0, 1, 2];
	const blueScoreStationIndices = [3, 4, 5];
	const fieldDevices = $derived(matchState?.fieldDevices ?? []);
	const fuelAdjustments = [10, 5, 1, -1, -5, -10];

	function isEstopHardwareMappedToStation(
		device: FieldDeviceDiagnostics,
		stationIndex: number
	): boolean {
		if (device.bypassed || device.status !== 'Connected') return false;
		if (!device.heartbeat || device.heartbeat.kind !== 'Estop') return false;

		const stationAlliance = stationIndex < 3 ? 'red' : 'blue';
		const stationNumber = stationIndex < 3 ? stationIndex + 1 : stationIndex - 2;

		const deviceAlliance = device.heartbeat.field.toLowerCase();
		const deviceStation = device.heartbeat.station;

		if (deviceAlliance === 'field') return true;
		if (deviceAlliance !== stationAlliance) return false;

		return deviceStation === 0 || deviceStation === stationNumber;
	}

	function hasActiveEstopHardware(stationIndex: number): boolean {
		return fieldDevices.some((device) => isEstopHardwareMappedToStation(device, stationIndex));
	}

	// A station is ready if it is bypassed, OR if both DS and robot are linked
	const isMatchInProgress = $derived(
		phase === 'Auto' || phase === 'AutoToTeleopTransition' || phase === 'Teleop'
	);
	const readinessPositiveLabel = $derived(isMatchInProgress ? 'ACTIVE' : 'READY');
	const readinessNegativeLabel = $derived(isMatchInProgress ? 'INACTIVE' : 'NOT READY');

	// Readiness state comes from backend DriverStationConnection derived properties
	// so frontend and backend readiness logic cannot drift.
	const blueReady = $derived(
		blueStations.every((s) => (isMatchInProgress ? s.isReadyInMatch : s.isReady))
	);
	const redReady = $derived(
		redStations.every((s) => (isMatchInProgress ? s.isReadyInMatch : s.isReady))
	);

	let activeTab = $state('status');
	let scoreWarning = $state('');
	let optionsWarning = $state('');
	let optionsSuccess = $state('');
	let isTogglingFreePractice = $state(false);
	let isSavingMatchDurations = $state(false);
	let autoDurationSecondsInput = $state('20');
	let autoToTeleopTransitionDurationSecondsInput = $state('3');
	let teleopDurationSecondsInput = $state('140');
	let hasInitializedMatchDurations = $state(false);
	let logSearch = $state('');
	const logSeverityOptions: LogSeverity[] = [
		'Trace',
		'Debug',
		'Information',
		'Warning',
		'Error',
		'Critical'
	];
	let selectedLogSeverities = $state<LogSeverity[]>(['Warning', 'Error', 'Critical']);

	function stationCode(idx: number): string {
		return idx < 3 ? `Red${idx + 1}` : `Blue${idx - 2}`;
	}

	function toggleLogSeverity(level: LogSeverity, enabled: boolean) {
		if (enabled) {
			if (!selectedLogSeverities.includes(level)) {
				selectedLogSeverities = [...selectedLogSeverities, level];
			}
			return;
		}

		selectedLogSeverities = selectedLogSeverities.filter((value) => value !== level);
	}

	const filteredLogEntries = $derived.by(() => {
		const selectedLevels = new Set(selectedLogSeverities);
		const keyword = logSearch.trim().toLowerCase();

		return [...fms.logEntries].reverse().filter((entry: RecentLogEntry) => {
			if (!selectedLevels.has(entry.level)) return false;

			if (!keyword) return true;

			const haystack = `${entry.level} ${entry.category} ${entry.message}`.toLowerCase();
			return haystack.includes(keyword);
		});
	});

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

	async function setFreePracticeEnabled(enabled: boolean) {
		optionsWarning = '';
		optionsSuccess = '';
		isTogglingFreePractice = true;

		try {
			await fms.setFreePracticeEnabled(enabled);
		} catch (error) {
			optionsWarning =
				error instanceof Error
					? error.message
					: 'Failed to update Free Practice. Please try again.';
		} finally {
			isTogglingFreePractice = false;
		}
	}

	$effect(() => {
		if (!matchState?.matchDurations) return;

		if (!hasInitializedMatchDurations && !isSavingMatchDurations) {
			autoDurationSecondsInput = String(matchState.matchDurations.autoSeconds);
			autoToTeleopTransitionDurationSecondsInput = String(
				matchState.matchDurations.autoToTeleopTransitionSeconds
			);
			teleopDurationSecondsInput = String(matchState.matchDurations.teleopSeconds);
			hasInitializedMatchDurations = true;
		}
	});

	function parseNonNegativeSeconds(value: string): number | null {
		const parsed = Number(value);
		if (!Number.isFinite(parsed) || parsed < 0) return null;
		return parsed;
	}

	async function saveMatchDurations() {
		optionsWarning = '';
		optionsSuccess = '';

		if (phase !== 'Idle') {
			optionsWarning = 'Match durations can only be changed while the arena is idle.';
			return;
		}

		const autoSeconds = parseNonNegativeSeconds(autoDurationSecondsInput);
		const transitionSeconds = parseNonNegativeSeconds(autoToTeleopTransitionDurationSecondsInput);
		const teleopSeconds = parseNonNegativeSeconds(teleopDurationSecondsInput);

		if (autoSeconds === null || transitionSeconds === null || teleopSeconds === null) {
			optionsWarning = 'All durations must be non-negative numbers (0 is allowed).';
			return;
		}

		isSavingMatchDurations = true;

		try {
			await fms.setMatchDurations(autoSeconds, transitionSeconds, teleopSeconds);
			optionsSuccess = 'Match durations updated.';
		} catch (error) {
			optionsWarning =
				error instanceof Error
					? error.message
					: 'Failed to update match durations. Please try again.';
		} finally {
			isSavingMatchDurations = false;
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
		<!-- <div>A-Stop</div> -->
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
		onclick={() => (type === 'E' ? fms.estopStation(stationIndex) : fms.astopStation(stationIndex))}
		class="mx-auto h-7 w-14 cursor-pointer rounded border border-rose-900 px-1 text-[10px] font-black tracking-wide text-white shadow-sm transition active:translate-y-px {active
			? 'bg-rose-950'
			: 'bg-rose-700 hover:bg-rose-600'}">{active ? 'LOCK' : `${type}-Stop`}</button
	>
{/snippet}

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

{#snippet stationStatusCard(s: StationStatus, stationNumber: number, alliance: 'blue' | 'red')}
	<div
		class="mb-2 rounded border p-2 text-xs {s.estop
			? alliance === 'red'
				? 'alliance-red-border-soft alliance-red-bg'
				: 'alliance-red-border-soft alliance-red-bg'
			: alliance === 'red'
				? 'alliance-red-border-soft bg-white'
				: 'alliance-blue-border-soft bg-white'}"
	>
		<div class="mb-1.5 flex items-center justify-between">
			<span
				class="font-black {s.estop
					? 'text-white'
					: alliance === 'blue'
						? 'alliance-blue-text'
						: 'alliance-red-text'}">Station {stationNumber} — Team {s.teamNumber || '—'}</span
			>
			<div class="flex gap-1">
				{#if s.estop}<span
						class="rounded bg-rose-700 px-1.5 py-0.5 text-[10px] font-bold text-white">E-STOP</span
					>{/if}
				{#if s.astop}<span
						class="rounded bg-orange-600 px-1.5 py-0.5 text-[10px] font-bold text-white"
						>A-STOP</span
					>{/if}
				{#if s.bypassed}<span
						class="rounded bg-slate-500 px-1.5 py-0.5 text-[10px] font-bold text-white">BYPASS</span
					>{/if}
				{#if s.wrongStation}<span
						class="rounded bg-yellow-600 px-1.5 py-0.5 text-[10px] font-bold text-white"
						>WRONG STN</span
					>{/if}
			</div>
		</div>
		{#if s.wrongStation}
			<div class="mb-1 rounded bg-yellow-50 px-1.5 py-1 text-[10px] font-semibold text-yellow-800">
				Expected station: {s.wrongStation}
			</div>
		{/if}
		<div class="mt-1 flex flex-wrap items-center gap-1">
			<span class="rounded px-1.5 py-0.5 text-[10px] font-bold text-white {s.dsLinked ? 'bg-emerald-600' : 'bg-slate-400'}">DS</span>
			<span class="rounded px-1.5 py-0.5 text-[10px] font-bold text-white {s.robotLinked ? 'bg-emerald-600' : 'bg-slate-400'}">Robot</span>
			<span class="rounded px-1.5 py-0.5 text-[10px] font-bold text-white {s.radioLinked ? 'bg-emerald-600' : 'bg-slate-400'}">Radio</span>
			<span class="rounded px-1.5 py-0.5 text-[10px] font-bold text-white {s.rioLinked ? 'bg-emerald-600' : 'bg-slate-400'}">RIO</span>
			<span class="ml-1 rounded px-1.5 py-0.5 text-[10px] font-bold {s.isReady ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-100 text-slate-600'}">Ready</span>
			<span class="rounded px-1.5 py-0.5 text-[10px] font-bold {s.isReadyInMatch ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-100 text-slate-600'}">In-Match</span>
		</div>
		<div class="mt-1.5 grid grid-cols-4 gap-1 text-slate-600">
			<div class="rounded bg-slate-50 px-1.5 py-1">
				<div class="text-[10px] text-slate-400">Battery</div>
				<div class="font-semibold {s.battery < 11 && s.robotLinked ? 'text-yellow-600' : ''}">
					{s.robotLinked ? s.battery.toFixed(2) + 'V' : '—'}
				</div>
			</div>
			<div class="rounded bg-slate-50 px-1.5 py-1">
				<div class="text-[10px] text-slate-400">Trip</div>
				<div class="font-semibold">{s.dsLinked ? s.tripTimeMs + ' ms' : '—'}</div>
			</div>
			<div class="rounded bg-slate-50 px-1.5 py-1">
				<div class="text-[10px] text-slate-400">Lost Pkts</div>
				<div class="font-semibold {s.missedPackets > 0 ? 'text-yellow-600' : ''}">
					{s.missedPackets}
				</div>
			</div>
			<div class="rounded bg-slate-50 px-1.5 py-1">
				<div class="text-[10px] text-slate-400">Last Robot Link</div>
				<div class="font-semibold {s.secondsSinceLastRobotLink > 3 ? 'text-yellow-600' : ''}">
					{formatLastRobotLink(s.secondsSinceLastRobotLink, s.robotLinked)}
				</div>
			</div>
		</div>
		{#if s.wifi}
			<div class="mt-1 grid grid-cols-5 gap-1 text-slate-600">
				<div class="rounded bg-slate-50 px-1.5 py-1">
					<div class="text-[10px] text-slate-400">SNR</div>
					<div class="font-semibold">{s.wifi.snr}</div>
				</div>
				<div class="rounded bg-slate-50 px-1.5 py-1">
					<div class="text-[10px] text-slate-400">Rx Mbps</div>
					<div class="font-semibold">{s.wifi.rxRateMbps.toFixed(1)}</div>
				</div>
				<div class="rounded bg-slate-50 px-1.5 py-1">
					<div class="text-[10px] text-slate-400">Tx Mbps</div>
					<div class="font-semibold">{s.wifi.txRateMbps.toFixed(1)}</div>
				</div>
				<div class="rounded bg-slate-50 px-1.5 py-1">
					<div class="text-[10px] text-slate-400">BW Mbps</div>
					<div class="font-semibold">{s.wifi.bandwidthMbps.toFixed(2)}</div>
				</div>
				<div class="rounded bg-slate-50 px-1.5 py-1">
					<div class="text-[10px] text-slate-400">WiFi Link</div>
					<div class="font-semibold {s.wifi.radioLinked ? 'text-emerald-700' : 'text-rose-700'}">
						{s.wifi.radioLinked ? 'Linked' : 'Not Linked'}
					</div>
				</div>
			</div>
		{/if}
	</div>
{/snippet}

{#snippet fieldDeviceReplyTimeCell(device: FieldDeviceDiagnostics)}
	<div class="space-y-1">
		<div class="font-semibold text-slate-800">{device.lastReplyTimeMs} ms</div>
		<div class="text-[10px] leading-tight text-slate-600">
			Samples {device.replyTimeStats.sampleCount}
		</div>
		<div class="text-[10px] leading-tight text-slate-600">
			Avg {device.replyTimeStats.avgMs.toFixed(1)} ms · StdDev {device.replyTimeStats.stdDevMs.toFixed(
				1
			)} ms
		</div>
		<div class="text-[10px] leading-tight text-slate-600">
			Min {device.replyTimeStats.minMs} ms · Max {device.replyTimeStats.maxMs} ms
		</div>
	</div>
{/snippet}

{#snippet fieldDeviceSpecificValuesCell(device: FieldDeviceDiagnostics)}
	<div class="space-y-0.5 text-[11px] text-slate-700">
		{#each deviceSpecificValues(device) as metric}
			<div><span class="font-semibold">{metric.label}:</span> {metric.value}</div>
		{/each}
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
	<div class="app-neutral-bg border-b border-slate-300">
		<div class="mx-auto flex max-w-[1700px] items-end gap-1 px-3 pt-2 text-sm">
			<div
				class="rounded-t-md border border-b-0 border-slate-300 bg-white px-4 py-2 font-bold text-slate-900"
				style="box-shadow: inset 0 3px 0 0 var(--color-secondary);"
			>
				Match Play
			</div>
			<div class="ml-auto flex items-center gap-3 px-2 pb-1 text-xs text-slate-600">
				<span class="inline-flex items-center gap-1"
					><span class="h-2.5 w-2.5 rounded-full {fms.connected ? 'bg-emerald-500' : 'bg-rose-500'}"
					></span>{fms.connected ? 'Connected' : 'Connecting'}</span
				>
				<span>{matchState?.matchType ?? 'None'} #{matchState?.matchNumber ?? 0}</span>
				<span class="font-mono font-semibold"
					>{matchState ? formatTime(matchState.timeRemaining) : '0:00'}</span
				>
			</div>
		</div>
	</div>

	<main class="mx-auto flex max-w-[1700px] flex-col gap-3 px-3 py-3">
		<!-- Alliance readiness panels -->
		<div class="rounded border border-slate-300 bg-white shadow-sm">
			<div class="grid grid-cols-1 xl:grid-cols-[1fr_170px_1fr]">
				<!-- Blue Alliance -->
				<div class="alliance-blue-bg-soft border-b border-slate-300 xl:border-r xl:border-b-0">
					<div
						class="alliance-blue-border-soft flex items-center justify-between border-b px-3 py-2"
					>
						<span class="alliance-blue-text text-sm font-bold tracking-wide">BLUE ALLIANCE</span>
						<span
							class="rounded px-2 py-0.5 text-xs font-bold text-white {blueReady
								? 'bg-emerald-700'
								: 'bg-rose-700'}">{blueReady
								? readinessPositiveLabel
								: readinessNegativeLabel}</span
						>
					</div>
					<div class="overflow-x-auto p-2 text-xs">
						{@render readinessHeaderRow()}
						{#each blueStations as s, i}
							{@const idx = blueInputIndices[i]}
							<div
								class="alliance-blue-border-soft mt-1 rounded border bg-white/75 px-1.5 py-1.5 sm:hidden"
							>
								<div class="grid grid-cols-[76px_1fr] items-end gap-2">
									<label class="flex flex-col items-center gap-1 text-[10px] font-semibold tracking-wide text-slate-500 uppercase">
										<span>Bypass</span>
										<input
											type="checkbox"
											checked={s.bypassed}
											disabled={phase !== 'Idle'}
											onchange={() => fms.bypassStation(idx, !s.bypassed)}
											class="h-4 w-4 cursor-pointer"
										/>
									</label>
									<div class="min-w-0">
										<div class="alliance-blue-text mb-1 font-bold">Station {i + 1}</div>
										<input
											type="text"
											inputmode="numeric"
											pattern="[0-9]*"
											placeholder="Team"
											bind:value={inputs[idx].team}
											disabled={phase !== 'Idle'}
											class="h-8 w-full rounded border border-slate-300 bg-white px-2 text-sm"
										/>
									</div>
								</div>
								<div class="mt-2 grid grid-cols-4">
									<div class="flex flex-col items-center gap-0.5">
										<span class="text-[10px] font-semibold tracking-wide text-slate-500 uppercase">E-Stop HW</span>
										{@render readinessStatusCell(hasActiveEstopHardware(idx))}
									</div>
									<div class="flex flex-col items-center gap-0.5">
										<span class="text-[10px] font-semibold tracking-wide text-slate-500 uppercase">DS</span>
										{@render readinessStatusCell(s.dsLinked)}
									</div>
									<div class="flex flex-col items-center gap-0.5">
										<span class="text-[10px] font-semibold tracking-wide text-slate-500 uppercase">Robot</span>
										{@render readinessStatusCell(s.robotLinked)}
									</div>
									<div class="flex flex-col items-center gap-0.5">
										<span class="text-[10px] font-semibold tracking-wide text-slate-500 uppercase">Enabled</span>
										{@render robotEnabledCell(s.estop, s.astop, s.robotLinked, s.bypassed)}
									</div>
								</div>
								<div class="mt-2 flex justify-center">
									<div class="flex flex-col items-center gap-1">
										<span class="text-[10px] font-semibold tracking-wide text-slate-500 uppercase">E-Stop</span>
									{@render stopButton('E', s.estop, idx)}
									</div>
								</div>
								<!-- {@render stopButton('A', s.astop, idx)} -->
							</div>
							<div
								class="alliance-blue-border-soft mt-1 hidden grid-cols-[68px_66px_minmax(128px,1fr)_48px_44px_44px_88px_80px] items-center gap-1 rounded border bg-white/75 px-1.5 py-1.5 sm:grid"
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
										class="h-7 min-w-[8rem] w-full rounded border border-slate-300 bg-white px-2 text-xs"
									/>
								</div>
								<input
									type="checkbox"
									checked={s.bypassed}
									disabled={phase !== 'Idle'}
									onchange={() => fms.bypassStation(idx, !s.bypassed)}
									class="mx-auto h-4 w-4 cursor-pointer"
								/>
								{@render readinessStatusCell(s.dsLinked)}
								{@render readinessStatusCell(s.robotLinked)}
								{@render robotEnabledCell(s.estop, s.astop, s.robotLinked, s.bypassed)}
								{@render stopButton('E', s.estop, idx)}
								<!-- {@render stopButton('A', s.astop, idx)} -->
							</div>
						{/each}
					</div>
				</div>

				<!-- Center column -->
				<div
					class="order-first flex flex-col items-center justify-center gap-2 border-b border-slate-300 bg-slate-50 px-4 py-4 text-center xl:order-none xl:border-b-0"
				>
					<div class="text-xs font-bold tracking-widest text-slate-500 uppercase">Match Status</div>
					<div class="text-xl font-black tracking-tight text-slate-800">
						{matchState?.matchType ?? 'Test'} Match
					</div>

					<div
						class="inline-flex items-center gap-2 rounded-full border border-slate-300 bg-white px-3 py-1 shadow-sm"
					>
						<span class="text-[11px] font-bold tracking-wider text-slate-600 uppercase">
							{displayPhase}
						</span>
					</div>
				</div>

				<!-- Red Alliance -->
				<div class="alliance-red-bg-soft">
					<div
						class="alliance-red-border-soft flex items-center justify-between border-b px-3 py-2"
					>
						<span
							class="rounded px-2 py-0.5 text-xs font-bold text-white {redReady
								? 'bg-emerald-700'
								: 'bg-rose-700'}">{redReady
								? readinessPositiveLabel
								: readinessNegativeLabel}</span
						>
						<span class="alliance-red-text text-sm font-bold tracking-wide">RED ALLIANCE</span>
					</div>
					<div class="overflow-x-auto p-2 text-xs">
						{@render readinessHeaderRow()}
						{#each redStations as s, i}
							{@const idx = redInputIndices[i]}
							<div
								class="alliance-red-border-soft mt-1 rounded border bg-white/75 px-1.5 py-1.5 sm:hidden"
							>
								<div class="grid grid-cols-[76px_1fr] items-end gap-2">
									<label class="flex flex-col items-center gap-1 text-[10px] font-semibold tracking-wide text-slate-500 uppercase">
										<span>Bypass</span>
										<input
											type="checkbox"
											checked={s.bypassed}
											disabled={phase !== 'Idle'}
											onchange={() => fms.bypassStation(idx, !s.bypassed)}
											class="h-4 w-4 cursor-pointer"
										/>
									</label>
									<div class="min-w-0">
										<div class="alliance-red-text mb-1 font-bold">Station {3 - i}</div>
										<input
											type="text"
											inputmode="numeric"
											pattern="[0-9]*"
											placeholder="Team"
											bind:value={inputs[idx].team}
											disabled={phase !== 'Idle' || isConfiguring}
											class="h-8 w-full rounded border border-slate-300 bg-white px-2 text-sm"
										/>
									</div>
								</div>
								<div class="mt-2 grid grid-cols-4 gap-2">
									<div class="flex flex-col items-center gap-0.5">
										<span class="text-[10px] font-semibold tracking-wide text-slate-500 uppercase">E-Stop HW</span>
										{@render readinessStatusCell(hasActiveEstopHardware(idx))}
									</div>
									<div class="flex flex-col items-center gap-0.5">
										<span class="text-[10px] font-semibold tracking-wide text-slate-500 uppercase">DS</span>
										{@render readinessStatusCell(s.dsLinked)}
									</div>
									<div class="flex flex-col items-center gap-0.5">
										<span class="text-[10px] font-semibold tracking-wide text-slate-500 uppercase">Robot</span>
										{@render readinessStatusCell(s.robotLinked)}
									</div>
									<div class="flex flex-col items-center gap-0.5">
										<span class="text-[10px] font-semibold tracking-wide text-slate-500 uppercase">Enabled</span>
										{@render robotEnabledCell(s.estop, s.astop, s.robotLinked, s.bypassed)}
									</div>
								</div>
								<div class="mt-2 flex justify-center">
									<div class="flex flex-col items-center gap-1">
										<span class="text-[10px] font-semibold tracking-wide text-slate-500 uppercase">E-Stop</span>
									{@render stopButton('E', s.estop, idx)}
									</div>
								</div>
								<!-- {@render stopButton('A', s.astop, idx)} -->
							</div>
							<div
								class="alliance-red-border-soft mt-1 hidden grid-cols-[68px_66px_minmax(128px,1fr)_48px_44px_44px_88px_80px] items-center gap-1 rounded border bg-white/75 px-1.5 py-1.5 sm:grid"
							>
								{@render readinessStatusCell(hasActiveEstopHardware(idx))}
								<div class="alliance-red-text text-center font-bold">Station {3 - i}</div>
								<div class="flex items-center gap-1">
									<input
										type="text"
										inputmode="numeric"
										pattern="[0-9]*"
										placeholder="Team"
										bind:value={inputs[idx].team}
										disabled={phase !== 'Idle' || isConfiguring}
										class="h-7 min-w-[8rem] w-full rounded border border-slate-300 bg-white px-2 text-xs"
									/>
								</div>
								<input
									type="checkbox"
									checked={s.bypassed}
									disabled={phase !== 'Idle'}
									onchange={() => fms.bypassStation(idx, !s.bypassed)}
									class="mx-auto h-4 w-4 cursor-pointer"
								/>
								{@render readinessStatusCell(s.dsLinked)}
								{@render readinessStatusCell(s.robotLinked)}
								{@render robotEnabledCell(s.estop, s.astop, s.robotLinked, s.bypassed)}
								{@render stopButton('E', s.estop, idx)}
								<!-- {@render stopButton('A', s.astop, idx)} -->
							</div>
						{/each}
					</div>
				</div>
			</div>

			<div class="border-t border-slate-200 p-3">
				{#if matchState}
					<div class="flex flex-wrap items-center justify-center gap-3 text-sm">
						<button
							onclick={configureAccessPoint}
							disabled={isConfiguring || phase !== 'Idle'}
							aria-busy={isConfiguring}
							class="brand-secondary-bg rounded px-3 py-1.5 text-sm font-bold text-white hover:opacity-90"
						>
							{isConfiguring ? 'Configuring...' : 'Configure AP'}
						</button>
						<button
							onclick={clearAllTeams}
							disabled={isConfiguring || phase !== 'Idle'}
							class="rounded bg-slate-600 px-3 py-1.5 text-sm font-bold text-white hover:bg-slate-500"
						>
							Clear Teams
						</button>
						<span class="text-slate-600">FMS AP Status:</span>
						<span
							class="rounded px-2 py-0.5 text-xs font-bold {matchState.accessPoint.status ===
							'ACTIVE'
								? 'bg-emerald-100 text-emerald-800'
								: matchState.accessPoint.status === 'CONFIGURING'
									? 'bg-yellow-100 text-yellow-800'
									: 'bg-rose-100 text-rose-800'}">{matchState.accessPoint.status}</span
						>
						{#if configureWarning}
							<span class="text-xs font-semibold text-rose-700">{configureWarning}</span>
						{/if}
						{#if configureSuccess}
							<span class="text-xs font-semibold text-emerald-700">{configureSuccess}</span>
						{/if}
					</div>
				{/if}
			</div>
		</div>

		<!-- Match control bar -->
		<div class="app-neutral-bg rounded border border-slate-300 p-3 shadow-sm">
			<div class="flex flex-wrap items-center justify-center gap-3">
				<button
					onclick={() => fms.startPreMatch()}
					disabled={phase !== 'Idle' || !!matchState?.arenaEstop || !!matchState?.freePracticeEnabled}
					class="h-14 min-w-44 rounded px-5 text-sm font-black disabled:cursor-not-allowed disabled:opacity-40 {phase ===
					'Idle' && !matchState?.freePracticeEnabled
						? 'bg-amber-400 text-slate-900 hover:bg-amber-300'
						: 'bg-amber-800 text-white'}"
				>
					Prestart Match
				</button>
				<button
					onclick={() => fms.startMatch()}
					disabled={phase !== 'PreMatch' || !blueReady || !redReady || !!matchState?.arenaEstop}
					class="h-14 min-w-44 rounded px-5 text-sm font-black disabled:cursor-not-allowed {phase ===
						'PreMatch' &&
					blueReady &&
					redReady &&
					!matchState?.arenaEstop
						? 'bg-emerald-700 text-white hover:bg-emerald-600'
						: 'bg-[repeating-linear-gradient(-45deg,#b9bec8_0px,#b9bec8_8px,#a9aeb8_8px,#a9aeb8_16px)] text-slate-500'}"
				>
					Start Match
				</button>
				<button
					onclick={() => fms.abortMatch()}
					disabled={phase !== 'Auto' && phase !== 'AutoToTeleopTransition' && phase !== 'Teleop'}
					class="h-14 min-w-44 rounded px-5 text-sm font-black text-white disabled:cursor-not-allowed disabled:opacity-40 {phase ===
						'Auto' ||
					phase === 'AutoToTeleopTransition' ||
					phase === 'Teleop'
						? 'bg-orange-600 hover:bg-orange-500'
						: 'bg-orange-900'}"
				>
					Abort Match
				</button>
				<button
					onclick={() => fms.clearMatch()}
					disabled={(phase !== 'PostMatch' && phase !== 'PreMatch' && phase !== 'Idle') ||
						!!matchState?.arenaEstop}
					class="h-14 min-w-44 rounded bg-slate-500 px-5 text-sm font-black text-white hover:bg-slate-400 disabled:cursor-not-allowed disabled:opacity-40"
				>
					Clear
				</button>
				{#if matchState?.arenaEstop}
					<button
						class="h-14 min-w-44 rounded bg-[repeating-linear-gradient(-45deg,#e7ca4f_0px,#e7ca4f_8px,#9a9a9a_8px,#9a9a9a_16px)] px-5 text-sm font-black text-black"
					>
						Arena is E-STOPPED!
					</button>
				{:else}
					<button
						onclick={() => fms.triggerArenaEstop()}
						class="h-14 min-w-44 rounded bg-rose-700 px-5 text-sm font-black text-white hover:bg-rose-600"
					>
						Arena E-STOP
					</button>
				{/if}
			</div>
		</div>

		<!-- Tab panel -->
		<div class="rounded border border-slate-300 bg-white shadow-sm">
			<div class="flex items-center gap-0 border-b border-slate-300 px-3 pt-2 text-sm">
				<button
					onclick={() => {
						activeTab = 'score';
					}}
					class="mr-6 pb-2 {activeTab === 'score'
						? 'brand-secondary-border brand-secondary-text border-b-2 font-semibold'
						: 'text-slate-500 hover:text-slate-800'}">Score</button
				>
				<button
					onclick={() => {
						activeTab = 'status';
					}}
					class="mr-6 pb-2 {activeTab === 'status'
						? 'brand-secondary-border brand-secondary-text border-b-2 font-semibold'
						: 'text-slate-500 hover:text-slate-800'}">Status</button
				>
				<button
					onclick={() => {
						activeTab = 'field';
					}}
					class="mr-6 pb-2 {activeTab === 'field'
						? 'brand-secondary-border brand-secondary-text border-b-2 font-semibold'
						: 'text-slate-500 hover:text-slate-800'}">Field</button
				>
				<button
					onclick={() => {
						activeTab = 'options';
					}}
					class="mr-6 pb-2 {activeTab === 'options'
						? 'brand-secondary-border brand-secondary-text border-b-2 font-semibold'
						: 'text-slate-500 hover:text-slate-800'}">Options</button
				>
				<button
					onclick={() => {
						activeTab = 'log';
					}}
					class="mr-6 pb-2 {activeTab === 'log'
						? 'brand-secondary-border brand-secondary-text border-b-2 font-semibold'
						: 'text-slate-500 hover:text-slate-800'}">Log</button
				>
			</div>

			{#if activeTab === 'score'}
				<div class="p-3">
					{#if scoreWarning}
						<div
							class="mb-3 rounded border border-rose-300 bg-rose-50 px-3 py-2 text-xs font-semibold text-rose-700"
						>
							{scoreWarning}
						</div>
					{/if}
					<div class="grid grid-cols-2 gap-3">
						{@render scoreAlliancePanel('blue', blueScoreStationIndices)}
						{@render scoreAlliancePanel('red', redScoreStationIndices)}
					</div>
				</div>
			{:else if activeTab === 'status'}
				<div class="grid grid-cols-1 lg:grid-cols-2">
					<!-- Blue Alliance -->
					<div class="alliance-blue-bg-soft border-r border-slate-200 p-3">
						<div class="alliance-blue-text mb-2 text-xs font-bold tracking-wider uppercase">
							Blue Alliance
						</div>
						{#each blueStations as s, i}
							{@render stationStatusCard(s, i + 1, 'blue')}
						{/each}
					</div>

					<!-- Red Alliance -->
					<div class="alliance-red-bg-soft p-3">
						<div class="alliance-red-text mb-2 text-xs font-bold tracking-wider uppercase">
							Red Alliance
						</div>
						{#each redStations as s, i}
							{@render stationStatusCard(s, 3 - i, 'red')}
						{/each}
					</div>
				</div>
			{:else if activeTab === 'options'}
				<div class="p-4">
					{#if optionsWarning}
						<div
							class="mb-3 rounded border border-rose-300 bg-rose-50 px-3 py-2 text-xs font-semibold text-rose-700"
						>
							{optionsWarning}
						</div>
					{/if}
					{#if optionsSuccess}
						<div
							class="mb-3 rounded border border-emerald-300 bg-emerald-50 px-3 py-2 text-xs font-semibold text-emerald-700"
						>
							{optionsSuccess}
						</div>
					{/if}
					<div class="flex flex-wrap gap-3">
						<div class="min-w-[320px] rounded border border-slate-200 bg-slate-50 px-4 py-3">
							<div class="flex items-start justify-between gap-4">
								<div>
									<div class="text-sm font-bold text-slate-900">Free Practice</div>
									<div class="mt-1 max-w-xl text-xs text-slate-600">
										Stops FMS communication to driver stations while leaving AP configuration and Hub counting available.
									</div>
								</div>
								<label class="flex items-center gap-2 text-sm font-semibold text-slate-800">
									<input
										type="checkbox"
										checked={matchState?.freePracticeEnabled ?? false}
										disabled={!matchState || phase !== 'Idle' || isTogglingFreePractice}
										onchange={(event) =>
											setFreePracticeEnabled((event.currentTarget as HTMLInputElement).checked)}
										class="h-4 w-4 cursor-pointer"
									/>
									<span>{matchState?.freePracticeEnabled ? 'Enabled' : 'Disabled'}</span>
								</label>
							</div>
							<div class="mt-3 text-[11px] font-medium text-slate-500">
								{phase === 'Idle'
									? 'Free Practice can be toggled while the arena is idle.'
									: 'Return the arena to Idle before changing Free Practice.'}
							</div>
						</div>
						<div class="min-w-[420px] rounded border border-slate-200 bg-slate-50 px-4 py-3">
							<div class="text-sm font-bold text-slate-900">Match Durations</div>
							<div class="mt-1 text-xs text-slate-600">
								Configure Auto, Auto to Teleop transition, and Teleop durations in seconds. Enter 0 for instant progression.
							</div>
							<div class="mt-3 grid grid-cols-3 gap-3">
								<label class="text-xs font-semibold text-slate-700">
									<span>Auto (s)</span>
									<input
										type="number"
										min="0"
										step="1"
										bind:value={autoDurationSecondsInput}
										disabled={phase !== 'Idle' || isSavingMatchDurations}
										class="mt-1 w-full rounded border border-slate-300 bg-white px-2 py-1 text-sm"
									/>
								</label>
								<label class="text-xs font-semibold text-slate-700">
									<span>Auto→Teleop (s)</span>
									<input
										type="number"
										min="0"
										step="1"
										bind:value={autoToTeleopTransitionDurationSecondsInput}
										disabled={phase !== 'Idle' || isSavingMatchDurations}
										class="mt-1 w-full rounded border border-slate-300 bg-white px-2 py-1 text-sm"
									/>
								</label>
								<label class="text-xs font-semibold text-slate-700">
									<span>Teleop (s)</span>
									<input
										type="number"
										min="0"
										step="1"
										bind:value={teleopDurationSecondsInput}
										disabled={phase !== 'Idle' || isSavingMatchDurations}
										class="mt-1 w-full rounded border border-slate-300 bg-white px-2 py-1 text-sm"
									/>
								</label>
							</div>
							<div class="mt-3 flex items-center justify-between gap-3">
								<div class="text-[11px] font-medium text-slate-500">
									{phase === 'Idle'
										? 'Durations can be changed while the arena is idle.'
										: 'Return the arena to Idle before changing durations.'}
								</div>
								<button
									onclick={saveMatchDurations}
									disabled={phase !== 'Idle' || isSavingMatchDurations}
									class="brand-secondary-bg rounded px-3 py-1.5 text-sm font-bold text-white hover:opacity-90"
								>
									{isSavingMatchDurations ? 'Saving...' : 'Save Durations'}
								</button>
							</div>
						</div>
						{#if matchState?.arenaEstop}
							<div
								class="flex items-center gap-3 rounded border border-rose-300 bg-rose-50 px-4 py-3"
							>
								<span class="font-bold text-rose-700">Arena E-Stop is ACTIVE</span>
								<button
									onclick={() => fms.resetArenaEstop()}
									class="rounded bg-yellow-500 px-3 py-1.5 text-sm font-bold text-slate-900 hover:bg-yellow-400"
									>Reset E-Stop</button
								>
							</div>
						{/if}
					</div>
				</div>
			{:else if activeTab === 'field'}
				<div class="p-3">
					<div class="mb-3 flex items-center justify-between">
						<div class="text-xs text-slate-600">
							Connected devices: <span class="font-bold">{fieldDevices.length}</span>
						</div>
					</div>

					{#if fieldDevices.length === 0}
						<div
							class="rounded border border-slate-200 bg-slate-50 px-3 py-6 text-center text-sm text-slate-500"
						>
							No field devices connected.
						</div>
					{:else}
						<div class="overflow-x-auto rounded border border-slate-200">
							<table class="min-w-[1460px] divide-y divide-slate-200 text-left text-xs">
								<thead class="bg-slate-100 text-slate-600">
									<tr>
										<th class="px-2 py-2 font-semibold">Name</th>
										<th class="px-2 py-2 font-semibold">Type</th>
										<th class="px-2 py-2 font-semibold">Status</th>
										<th class="px-2 py-2 font-semibold">Bypass</th>
										<th class="px-2 py-2 font-semibold">Last Reply Time</th>
										<th class="px-2 py-2 font-semibold">Last Seen</th>
										<th class="px-2 py-2 font-semibold">Device-Specific Values</th>
									</tr>
								</thead>
								<tbody class="divide-y divide-slate-200 bg-white">
									{#each fieldDevices as device}
										<tr class="align-top">
											<td class="px-2 py-2 font-semibold text-slate-900">{device.name}</td>
											<td class="px-2 py-2 text-slate-700">{device.type}</td>
											<td class="px-2 py-2">
												<span
													class="rounded px-2 py-0.5 text-[10px] font-bold {statusBadgeClasses(
														device.status
													)}">{device.status}</span
												>
											</td>
											<td class="px-2 py-2">
												<input
													type="checkbox"
													checked={device.bypassed}
													onchange={() => fms.bypassFieldDevice(device.id, !device.bypassed)}
													class="h-4 w-4 cursor-pointer"
												/>
											</td>
											<td class="px-2 py-2">{@render fieldDeviceReplyTimeCell(device)}</td>
											<td class="px-2 py-2 text-[11px] text-slate-700">
												<div>{formatTimestamp(device.lastSeenUtc)}</div>
												<div class="text-slate-500">{formatAgo(device.secondsSinceLastSeen)}</div>
											</td>
											<td class="px-2 py-2">{@render fieldDeviceSpecificValuesCell(device)}</td>
										</tr>
									{/each}
								</tbody>
							</table>
						</div>
					{/if}
				</div>
			{:else if activeTab === 'log'}
				<div class="p-3">
					<div
						class="mb-3 flex flex-wrap items-end justify-between gap-3 rounded border border-slate-200 bg-slate-50 p-3"
					>
						<div class="min-w-[260px] flex-1">
							<label for="log-search" class="mb-1 block text-xs font-semibold text-slate-600"
								>Search Logs</label
							>
							<input
								id="log-search"
								type="text"
								bind:value={logSearch}
								placeholder="Keyword, category, or message"
								class="w-full rounded border border-slate-300 bg-white px-3 py-2 text-sm"
							/>
						</div>

						<div>
							<div class="mb-1 text-xs font-semibold text-slate-600">Severity Filters</div>
							<div class="flex flex-wrap gap-2">
								{#each logSeverityOptions as level}
									<label class="inline-flex items-center gap-1 px-2 py-1 text-xs text-slate-700">
										<input
											type="checkbox"
											checked={selectedLogSeverities.includes(level)}
											onchange={(e) =>
												toggleLogSeverity(level, (e.currentTarget as HTMLInputElement).checked)}
										/>
										<span>{level}</span>
									</label>
								{/each}
							</div>
						</div>

						<!-- <div class="text-xs text-slate-600">
								<div>Total buffered: <span class="font-bold text-slate-900">{fms.logEntries.length}</span></div>
								<div>Visible: <span class="font-bold text-slate-900">{filteredLogEntries.length}</span></div>
							</div> -->
					</div>

					{#if selectedLogSeverities.length === 0}
						<div
							class="rounded border border-amber-300 bg-amber-50 px-3 py-2 text-sm font-semibold text-amber-900"
						>
							Select at least one severity level to display log entries.
						</div>
					{:else if filteredLogEntries.length === 0}
						<div
							class="rounded border border-slate-200 bg-slate-50 px-3 py-6 text-center text-sm text-slate-500"
						>
							No log entries match the current filters.
						</div>
					{:else}
						<div class="space-y-2">
							{#each filteredLogEntries as entry}
								<div class="rounded border px-3 py-2 shadow-sm {logEntryClasses(entry.level)}">
									<div
										class="mb-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-[11px] font-semibold tracking-wide uppercase opacity-90"
									>
										<span>{entry.level}</span>
										<span>{formatLogTimestamp(entry.timestampUtc)}</span>
										<span class="tracking-normal normal-case">{entry.category}</span>
									</div>
									<div class="font-mono text-[12px] leading-5 break-words whitespace-pre-wrap">
										{entry.message}
									</div>
								</div>
							{/each}
						</div>
					{/if}
				</div>
			{/if}
		</div>
	</main>

	<footer
		class="app-neutral-bg fixed right-0 bottom-0 left-0 border-t border-slate-300 px-3 py-1 text-xs text-slate-600"
	>
		<div class="relative mx-auto max-w-[1700px]">
			<span
				>{matchState
					? `Loop ${matchState.loopTiming.currentMs.toFixed(2)} ms (30s max ${matchState.loopTiming.maxMs30s.toFixed(2)} ms)`
					: 'Loop — ms'}</span
			>
			<span class="absolute left-1/2 -translate-x-1/2">PossumFMS</span>
			<a href="/audience" class="brand-secondary-text absolute right-0 hover:opacity-80"
				>Audience Overlay</a
			>
		</div>
	</footer>
</div>
