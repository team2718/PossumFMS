<script lang="ts">
	import { fms } from '$lib/fms.svelte';
	import type { FieldDeviceDiagnostics, TowerEndgameLevel } from '$lib/fms.svelte';

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

	function deviceSpecificValues(device: FieldDeviceDiagnostics): Array<{ label: string; value: string }> {
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

	// A station is ready if it is bypassed, OR if both DS and robot are linked — and it has no active e-stop.
	const blueReady = $derived(
		blueStations.every((s) => (s.bypassed || (s.dsLinked && s.robotLinked)) && !s.estop)
	);
	const redReady = $derived(
		redStations.every((s) => (s.bypassed || (s.dsLinked && s.robotLinked)) && !s.estop)
	);

	let activeTab = $state('status');
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
				error instanceof Error ? error.message : 'Failed to update auto tower climb. Please try again.';
		}
	}

	async function setEndgameTowerLevel(stationIndex: number, level: TowerEndgameLevel) {
		scoreWarning = '';
		try {
			await fms.setEndgameTowerLevel(stationIndex, level);
		} catch (error) {
			scoreWarning =
				error instanceof Error ? error.message : 'Failed to update endgame tower level. Please try again.';
		}
	}
</script>

{#snippet readinessHeaderRow()}
	<div
		class="grid grid-cols-[68px_1fr_48px_44px_44px_44px_44px_62px_62px] items-center gap-1 px-1 py-1 font-bold text-slate-600"
	>
		<div>Station</div>
		<div>Team</div>
		<div>Bypass</div>
		<div>DS</div>
		<div>Radio</div>
		<div>RIO</div>
		<div>Robot</div>
		<div>E-Stop</div>
		<div>A-Stop</div>
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

{#snippet stopButton(type: 'E' | 'A', active: boolean, stationIndex: number)}
	<button
		onclick={() => (type === 'E' ? fms.estopStation(stationIndex) : fms.astopStation(stationIndex))}
		class="mx-auto h-7 w-14 cursor-pointer rounded border border-rose-900 px-1 text-[10px] font-black tracking-wide text-white shadow-sm transition active:translate-y-px {active
			? 'bg-rose-950'
			: 'bg-rose-700 hover:bg-rose-600'}"
	>{active ? 'LOCK' : `${type}-Stop`}</button
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
				<div class="mb-1 text-[10px] font-semibold text-slate-500 uppercase">{stationCode(idx)}</div>
				<select
					value={matchState?.stationClimbs?.[idx]?.endgameLevel ?? 'None'}
					onchange={(e) =>
						setEndgameTowerLevel(idx, (e.currentTarget as HTMLSelectElement).value as TowerEndgameLevel)}
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
		<div>Energized RP (100 Fuel): <span class="font-bold">{matchState?.rankingPoints[alliance].energized ? 'Yes' : 'No'}</span></div>
		<div>Supercharged RP (360 Fuel): <span class="font-bold">{matchState?.rankingPoints[alliance].supercharged ? 'Yes' : 'No'}</span></div>
		<div>Traversal RP (50 Tower): <span class="font-bold">{matchState?.rankingPoints[alliance].traversal ? 'Yes' : 'No'}</span></div>
		<div>Win/Tie RP: <span class="font-bold">{matchState?.rankingPoints[alliance].winTie ?? 0}</span></div>
		<div>Total RP: <span class="font-bold">{matchState?.rankingPoints[alliance].total ?? 0}</span></div>
	</div>
{/snippet}

{#snippet stationStatusCard(s: StationStatus, stationNumber: number, alliance: 'blue' | 'red')}
	<div
		class="mb-2 rounded border p-2 text-xs {s.estop
			? alliance === 'red'
				? 'border-rose-400 bg-rose-100'
				: 'border-rose-400 bg-rose-50'
			: alliance === 'red'
				? 'border-rose-200 bg-white'
				: 'border-blue-200 bg-white'}"
	>
		<div class="mb-1.5 flex items-center justify-between">
			<span class="font-black {alliance === 'blue' ? 'text-blue-900' : 'text-rose-900'}"
				>Station {stationNumber} — Team {s.teamNumber || '—'}</span
			>
			<div class="flex gap-1">
				{#if s.estop}<span class="rounded bg-rose-700 px-1.5 py-0.5 text-[10px] font-bold text-white">E-STOP</span>{/if}
				{#if s.astop}<span class="rounded bg-orange-600 px-1.5 py-0.5 text-[10px] font-bold text-white">A-STOP</span>{/if}
				{#if s.bypassed}<span class="rounded bg-slate-500 px-1.5 py-0.5 text-[10px] font-bold text-white">BYPASS</span>{/if}
				{#if s.wrongStation}<span class="rounded bg-yellow-600 px-1.5 py-0.5 text-[10px] font-bold text-white">WRONG STN</span>{/if}
			</div>
		</div>
		{#if s.wrongStation}
			<div class="mb-1 rounded bg-yellow-50 px-1.5 py-1 text-[10px] font-semibold text-yellow-800">
				Expected station: {s.wrongStation}
			</div>
		{/if}
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
				<div class="font-semibold {s.missedPackets > 0 ? 'text-yellow-600' : ''}">{s.missedPackets}</div>
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
			Avg {device.replyTimeStats.avgMs.toFixed(1)} ms · StdDev {device.replyTimeStats.stdDevMs.toFixed(1)} ms
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
			? 'border-blue-200 bg-blue-50'
			: 'border-rose-200 bg-rose-50'}"
	>
		<div class="mb-3 flex items-center justify-between">
			<div
				class="text-xs font-bold tracking-wider uppercase {alliance === 'blue'
					? 'text-blue-800'
					: 'text-rose-800'}"
			>
				{allianceLabel} Alliance
			</div>
			<div
				class="rounded px-2 py-0.5 text-xs font-bold text-white {alliance === 'blue'
					? 'bg-blue-700'
					: 'bg-rose-700'}"
			>
				Total {breakdown?.total ?? 0}
			</div>
		</div>

		<div
			class="mb-2 rounded border bg-white p-2 {alliance === 'blue'
				? 'border-blue-200'
				: 'border-rose-200'}"
		>
			<div class="mb-2 flex items-center justify-between text-xs font-semibold text-slate-700">
				<span>Auto Fuel</span>
				<span>{breakdown?.autoFuelPoints ?? 0}</span>
			</div>
			{@render fuelAdjustmentButtons(allianceLabel as 'Red' | 'Blue', true)}
		</div>

		<div
			class="mb-2 rounded border bg-white p-2 {alliance === 'blue'
				? 'border-blue-200'
				: 'border-rose-200'}"
		>
			<div class="mb-2 flex items-center justify-between text-xs font-semibold text-slate-700">
				<span>Teleop Fuel</span>
				<span>{breakdown?.teleopFuelPoints ?? 0}</span>
			</div>
			{@render fuelAdjustmentButtons(allianceLabel as 'Red' | 'Blue', false)}
		</div>

		<div
			class="mb-2 rounded border bg-white p-2 text-xs {alliance === 'blue'
				? 'border-blue-200'
				: 'border-rose-200'}"
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
				? 'border-blue-200'
				: 'border-rose-200'}"
		>
			<div>Fuel Combined: <span class="font-bold">{breakdown?.fuelCombined ?? 0}</span></div>
			<div>Tower Combined: <span class="font-bold">{breakdown?.towerCombined ?? 0}</span></div>
			{@render rankingPointsSummary(alliance)}
		</div>
	</div>
{/snippet}

<div class="min-h-screen bg-[#e5e7eb] text-slate-900">
	<div class="border-b border-slate-300 bg-[#f2f4f8]">
		<div class="mx-auto flex max-w-[1700px] items-end gap-1 px-3 pt-2 text-sm">
			<div
				class="rounded-t-md border border-b-0 border-slate-300 bg-white px-4 py-2 font-bold text-slate-900 shadow-[inset_0_3px_0_0_#1d4ed8]"
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
		<div class="overflow-x-auto rounded border border-slate-300 bg-white shadow-sm">
			<div class="grid min-w-[1200px] grid-cols-[1fr_170px_1fr]">
				<!-- Blue Alliance -->
				<div class="border-r border-slate-300 bg-[#dfeafc]">
					<div class="flex items-center justify-between border-b border-blue-200 px-3 py-2">
						<div class="flex items-center gap-2">
							<span
								class="rounded px-2 py-0.5 text-xs font-bold text-white {blueReady
									? 'bg-emerald-700'
									: 'bg-rose-700'}">{blueReady ? 'READY' : 'NOT READY'}</span
							>
							<span class="text-sm font-bold tracking-wide text-blue-900">BLUE ALLIANCE</span>
						</div>
						<span class="rounded-full bg-blue-700 px-3 py-0.5 text-sm font-bold text-white">0</span>
					</div>
					<div class="p-2 text-xs">
						{@render readinessHeaderRow()}
						{#each blueStations as s, i}
							{@const idx = blueInputIndices[i]}
							<div
								class="mt-1 grid grid-cols-[68px_1fr_48px_44px_44px_44px_44px_62px_62px] items-center gap-1 rounded border border-blue-200 bg-white/75 px-1.5 py-1.5"
							>
								<div class="text-center font-bold text-blue-900">Station {i + 1}</div>
								<div class="flex items-center gap-1">
									<input
										type="text"
										inputmode="numeric"
										pattern="[0-9]*"
										placeholder="Team"
										bind:value={inputs[idx].team}
										class="h-7 w-full rounded border border-slate-300 bg-white px-2 text-xs"
									/>
								</div>
								<input
									type="checkbox"
									checked={s.bypassed}
									onchange={() => fms.bypassStation(idx, !s.bypassed)}
									class="mx-auto h-4 w-4 cursor-pointer"
								/>
								{@render readinessStatusCell(s.dsLinked)}
								{@render readinessStatusCell(s.radioLinked)}
								{@render readinessStatusCell(s.rioLinked)}
								{@render readinessStatusCell(s.robotLinked)}
								{@render stopButton('E', s.estop, idx)}
								{@render stopButton('A', s.astop, idx)}
							</div>
						{/each}
					</div>
				</div>

				<!-- Center column -->
				<div
					class="flex items-center justify-center border-r border-slate-300 bg-white text-sm font-bold text-slate-700"
				>
					Test Match
				</div>

				<!-- Red Alliance -->
				<div class="bg-[#fde3e3]">
					<div class="flex items-center justify-between border-b border-rose-200 px-3 py-2">
						<span class="rounded-full bg-rose-700 px-3 py-0.5 text-sm font-bold text-white">0</span>
						<span class="text-sm font-bold tracking-wide text-rose-900">RED ALLIANCE</span>
						<span
							class="rounded px-2 py-0.5 text-xs font-bold text-white {redReady
								? 'bg-emerald-700'
								: 'bg-rose-700'}">{redReady ? 'READY' : 'NOT READY'}</span
						>
					</div>
					<div class="p-2 text-xs">
						{@render readinessHeaderRow()}
						{#each redStations as s, i}
							{@const idx = redInputIndices[i]}
							<div
								class="mt-1 grid grid-cols-[68px_1fr_48px_44px_44px_44px_44px_62px_62px] items-center gap-1 rounded border border-rose-200 bg-white/75 px-1.5 py-1.5"
							>
								<div class="text-center font-bold text-rose-900">Station {3 - i}</div>
								<div class="flex items-center gap-1">
									<input
										type="text"
										inputmode="numeric"
										pattern="[0-9]*"
										placeholder="Team"
										bind:value={inputs[idx].team}
										class="h-7 w-full rounded border border-slate-300 bg-white px-2 text-xs"
									/>
								</div>
								<input
									type="checkbox"
									checked={s.bypassed}
									onchange={() => fms.bypassStation(idx, !s.bypassed)}
									class="mx-auto h-4 w-4 cursor-pointer"
								/>
								{@render readinessStatusCell(s.dsLinked)}
								{@render readinessStatusCell(s.radioLinked)}
								{@render readinessStatusCell(s.rioLinked)}
								{@render readinessStatusCell(s.robotLinked)}
								{@render stopButton('E', s.estop, idx)}
								{@render stopButton('A', s.astop, idx)}
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
							disabled={isConfiguring}
							aria-busy={isConfiguring}
							class="rounded bg-blue-700 px-3 py-1.5 text-sm font-bold text-white hover:bg-blue-600"
						>
							{isConfiguring ? 'Configuring...' : 'Configure'}
						</button>
						<button
							onclick={clearAllTeams}
							disabled={isConfiguring}
							class="rounded bg-slate-600 px-3 py-1.5 text-sm font-bold text-white hover:bg-slate-500"
						>
							Clear All
						</button>
						{#if configureWarning}
							<span class="text-xs font-semibold text-rose-700">{configureWarning}</span>
						{/if}
						{#if configureSuccess}
							<span class="text-xs font-semibold text-emerald-700">{configureSuccess}</span>
						{/if}
						<span class="text-slate-600">FMS AP Status:</span>
						<span
							class="rounded px-2 py-0.5 text-xs font-bold {matchState.accessPoint.status === 'ACTIVE'
								? 'bg-emerald-100 text-emerald-800'
								: matchState.accessPoint.status === 'CONFIGURING'
									? 'bg-yellow-100 text-yellow-800'
									: 'bg-rose-100 text-rose-800'}"
						>{matchState.accessPoint.status}</span
						>
					</div>
				{/if}
			</div>
		</div>

		<!-- Match control bar -->
		<div class="rounded border border-slate-300 bg-[#eceef2] p-3 shadow-sm">
			<div class="flex flex-wrap items-center justify-center gap-3">
				<button
					onclick={() => fms.startPreMatch()}
					disabled={phase !== 'Idle'}
					class="h-14 min-w-44 rounded px-5 text-sm font-black disabled:cursor-not-allowed disabled:opacity-40 {phase ===
					'Idle'
						? 'bg-amber-500 text-slate-900 hover:bg-amber-400'
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
					disabled={(phase !== 'PostMatch' && phase !== 'PreMatch') || !!matchState?.arenaEstop}
					class="h-14 min-w-44 rounded bg-slate-500 px-5 text-sm font-black text-white hover:bg-slate-400 disabled:cursor-not-allowed disabled:opacity-40"
				>
					Clear Match
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
						? 'border-b-2 border-blue-600 font-semibold text-blue-700'
						: 'text-slate-500 hover:text-slate-800'}">Score</button
				>
				<button
					onclick={() => {
						activeTab = 'status';
					}}
					class="mr-6 pb-2 {activeTab === 'status'
						? 'border-b-2 border-blue-600 font-semibold text-blue-700'
						: 'text-slate-500 hover:text-slate-800'}">Status</button
				>
				<button
					onclick={() => {
						activeTab = 'field';
					}}
					class="mr-6 pb-2 {activeTab === 'field'
						? 'border-b-2 border-blue-600 font-semibold text-blue-700'
						: 'text-slate-500 hover:text-slate-800'}">Field</button
				>
				<button
					onclick={() => {
						activeTab = 'options';
					}}
					class="mr-6 pb-2 {activeTab === 'options'
						? 'border-b-2 border-blue-600 font-semibold text-blue-700'
						: 'text-slate-500 hover:text-slate-800'}">Options</button
				>
			</div>

			{#if activeTab === 'score'}
				<div class="p-3">
					{#if scoreWarning}
						<div class="mb-3 rounded border border-rose-300 bg-rose-50 px-3 py-2 text-xs font-semibold text-rose-700">
							{scoreWarning}
						</div>
					{/if}
					<div class="grid grid-cols-2 gap-3">
						{@render scoreAlliancePanel('blue', blueScoreStationIndices)}
						{@render scoreAlliancePanel('red', redScoreStationIndices)}

					</div>
				</div>
			{:else if activeTab === 'status'}
				<div class="grid grid-cols-2">
					<!-- Blue Alliance -->
					<div class="border-r border-slate-200 bg-blue-50 p-3">
						<div class="mb-2 text-xs font-bold tracking-wider text-blue-800 uppercase">
							Blue Alliance
						</div>
						{#each blueStations as s, i}
							{@render stationStatusCard(
								s,
								i + 1,
								'blue'
							)}
						{/each}
					</div>

					<!-- Red Alliance -->
					<div class="bg-rose-50 p-3">
						<div class="mb-2 text-xs font-bold tracking-wider text-rose-800 uppercase">
							Red Alliance
						</div>
						{#each redStations as s, i}
							{@render stationStatusCard(
								s,
								3 - i,
								'red'
							)}
						{/each}
					</div>
				</div>
			{:else if activeTab === 'options'}
				<div class="p-4">
					<div class="mb-3 text-xs font-bold tracking-wider text-slate-500 uppercase">
						Arena Control
					</div>
					<div class="flex flex-wrap gap-3">
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
						<div class="text-xs font-bold tracking-wider text-slate-500 uppercase">
							Field Device Diagnostics
						</div>
						<div class="text-xs text-slate-600">
							Connected devices: <span class="font-bold">{fieldDevices.length}</span>
						</div>
					</div>

					{#if fieldDevices.length === 0}
						<div class="rounded border border-slate-200 bg-slate-50 px-3 py-6 text-center text-sm text-slate-500">
							No field devices connected.
						</div>
					{:else}
						<div class="overflow-x-auto rounded border border-slate-200">
							<table class="min-w-[1400px] divide-y divide-slate-200 text-left text-xs">
								<thead class="bg-slate-100 text-slate-600">
									<tr>
										<th class="px-2 py-2 font-semibold">Name</th>
										<th class="px-2 py-2 font-semibold">Type</th>
										<th class="px-2 py-2 font-semibold">Status</th>
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
												<span class="rounded px-2 py-0.5 text-[10px] font-bold {statusBadgeClasses(device.status)}"
													>{device.status}</span
												>
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
			{/if}
		</div>
	</main>

	<footer
		class="fixed right-0 bottom-0 left-0 border-t border-slate-300 bg-[#eceff4] px-3 py-1 text-xs text-slate-600"
	>
		<div class="relative mx-auto max-w-[1700px]">
			<span
				>{matchState
					? `Loop ${matchState.loopTiming.currentMs.toFixed(2)} ms (30s max ${matchState.loopTiming.maxMs30s.toFixed(2)} ms)`
					: 'Loop — ms'}</span
			>
			<span class="absolute left-1/2 -translate-x-1/2">PossumFMS</span>
			<a href="/audience" class="absolute right-0 text-blue-700 hover:text-blue-500"
				>Audience Overlay</a
			>
		</div>
	</footer>
</div>
