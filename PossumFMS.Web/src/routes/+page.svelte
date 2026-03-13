<script lang="ts">
	import { fms } from '$lib/fms.svelte';

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
		const m = Math.floor(secs / 60);
		const s = Math.floor(secs % 60);
		return `${m}:${s.toString().padStart(2, '0')}`;
	}

	// Pill color for the current match phase
	function phaseColor(phase: string): string {
		if (phase === 'Idle') return 'bg-gray-600';
		if (phase === 'PreMatch') return 'bg-yellow-600';
		if (phase === 'Auto' || phase === 'AutoToTeleopTransition' || phase === 'Teleop') return 'bg-green-600';
		if (phase === 'PostMatch') return 'bg-blue-700';
		return 'bg-orange-600'; // Disconnected or unknown
	}

	// Assign the team from the input field, or 0 if the field is blank
	function assignTeam(idx: number) {
		const n = parseInt(inputs[idx].team);
		fms.assignTeam(idx, isNaN(n) ? 0 : n, inputs[idx].wpa);
	}

	function clearTeam(idx: number) {
		inputs[idx].team = '';
		inputs[idx].wpa = '';
		fms.assignTeam(idx, 0);
	}

	// Derived helpers so the template isn't doing repeated fms.state?.... lookups
	const matchState = $derived(fms.matchState);
	const phase = $derived(matchState?.phase ?? 'Disconnected');
</script>

<!-- Page wrapper — dark background fills the screen -->
<div class="flex min-h-screen flex-col bg-gray-900 text-white">

	<!-- ===== HEADER BAR ===== -->
	<header class="flex items-center justify-between gap-4 border-b border-gray-700 bg-gray-800 px-4 py-3">
		<!-- Left: title + connection status -->
		<div class="flex items-center gap-3">
			<h1 class="text-xl font-bold text-cyan-400">PossumFMS</h1>
			<span class="text-xs text-gray-400">Team 2718</span>
			<!-- Green dot = connected, red = not -->
			<span
				class="inline-block h-3 w-3 rounded-full {fms.connected ? 'bg-green-500' : 'bg-red-500'}"
				title={fms.connected ? 'Connected' : 'Disconnected'}
			></span>
			<span class="text-xs {fms.connected ? 'text-gray-400' : 'text-red-400'}">
				{fms.connected ? 'Connected' : 'Connecting…'}
			</span>
		</div>

		<!-- Center: phase pill + match type/number + timer -->
		<div class="flex items-center gap-4">
			<span class="rounded-full px-3 py-1 text-sm font-semibold {phaseColor(phase)}">
				{phase}
			</span>
			{#if matchState}
				<span class="text-sm text-gray-300">{matchState.matchType} #{matchState.matchNumber}</span>
				<span class="font-mono text-2xl font-bold text-white">
					{formatTime(matchState.timeRemaining)}
				</span>
			{/if}
		</div>

		<!-- Right: Arena E-Stop -->
		<div class="flex items-center gap-2">
			{#if matchState?.arenaEstop}
				<span class="text-sm font-bold text-red-400">ARENA E-STOP ACTIVE</span>
				<button
					onclick={() => fms.resetArenaEstop()}
					class="rounded bg-yellow-600 px-3 py-1.5 text-sm font-bold hover:bg-yellow-500"
				>
					Reset E-Stop
				</button>
			{:else}
				<button
					onclick={() => fms.triggerArenaEstop()}
					class="rounded bg-red-700 px-4 py-1.5 text-sm font-bold hover:bg-red-600"
				>
					ARENA E-STOP
				</button>
			{/if}
		</div>
	</header>

	<!-- ===== MATCH CONTROLS ===== -->
	<div class="flex items-center gap-3 border-b border-gray-700 bg-gray-800/60 px-4 py-2">
		<span class="text-xs font-semibold uppercase tracking-wider text-gray-400">Match</span>

		<!-- Each button is disabled when the phase isn't right -->
		<button
			onclick={() => fms.startPreMatch()}
			disabled={phase !== 'Idle'}
			class="rounded bg-yellow-700 px-3 py-1.5 text-sm font-semibold hover:bg-yellow-600 disabled:cursor-not-allowed disabled:opacity-40"
		>
			Pre-Match
		</button>
		<button
			onclick={() => fms.startMatch()}
			disabled={phase !== 'PreMatch'}
			class="rounded bg-green-700 px-3 py-1.5 text-sm font-semibold hover:bg-green-600 disabled:cursor-not-allowed disabled:opacity-40"
		>
			Start Match
		</button>
		<button
			onclick={() => fms.abortMatch()}
			disabled={phase !== 'Auto' && phase !== 'AutoToTeleopTransition' && phase !== 'Teleop'}
			class="rounded bg-orange-700 px-3 py-1.5 text-sm font-semibold hover:bg-orange-600 disabled:cursor-not-allowed disabled:opacity-40"
		>
			Abort Match
		</button>
		<button
			onclick={() => fms.clearMatch()}
			disabled={phase !== 'PostMatch' && phase !== 'PreMatch'}
			class="rounded bg-gray-600 px-3 py-1.5 text-sm font-semibold hover:bg-gray-500 disabled:cursor-not-allowed disabled:opacity-40"
		>
			Clear Match
		</button>

		<!-- Access Point status — shown on the right of the controls bar -->
		{#if matchState}
			<div class="ml-auto flex items-center gap-2">
				<span class="text-xs text-gray-400">Access Point</span>
				<span
					class="rounded-full px-2 py-0.5 text-xs font-semibold
					{matchState.accessPoint.status === 'ACTIVE'
						? 'bg-green-800 text-green-200'
						: matchState.accessPoint.status === 'CONFIGURING'
							? 'bg-yellow-800 text-yellow-200'
							: 'bg-red-800 text-red-200'}"
				>
					{matchState.accessPoint.status}
				</span>
			</div>
		{/if}
	</div>

	<!-- ===== STATION CARDS ===== -->
	<main class="flex-1 p-4">
		<!-- Alliance labels above the grid -->
		<div class="mb-2 grid grid-cols-6 gap-3">
			<div class="col-span-3 text-center text-sm font-bold text-red-400">Red Alliance</div>
			<div class="col-span-3 text-center text-sm font-bold text-blue-400">Blue Alliance</div>
		</div>

		<!-- 6 station cards in a single row (Red1 Red2 Red3 | Blue1 Blue2 Blue3) -->
		<div class="grid grid-cols-6 gap-3">
			{#each [0, 1, 2, 3, 4, 5] as idx}
				{@const s = matchState?.stations[idx]}
				{@const isRed = idx < 3}
				{@const label = isRed ? `Red ${idx + 1}` : `Blue ${idx - 2}`}

				<div
					class="flex flex-col gap-2 rounded-lg border p-3
					{isRed ? 'border-red-900 bg-gray-800' : 'border-blue-900 bg-gray-800'}"
				>
					<!-- Station header -->
					<div
						class="rounded px-2 py-1 text-center text-sm font-bold
						{isRed ? 'bg-red-950 text-red-300' : 'bg-blue-950 text-blue-300'}"
					>
						{label}
					</div>

					<!-- Team number input -->
					<input
						type="number"
						placeholder="Team #"
						bind:value={inputs[idx].team}
						onkeydown={(e) => e.key === 'Enter' && assignTeam(idx)}
						class="w-full rounded border border-gray-600 bg-gray-700 px-2 py-1 text-sm text-white placeholder-gray-500 focus:border-cyan-500 focus:outline-none"
					/>

					<!-- WPA key input -->
					<input
						type="text"
						placeholder="WPA Key (optional)"
						bind:value={inputs[idx].wpa}
						class="rounded border border-gray-600 bg-gray-700 px-2 py-1 text-xs text-white placeholder-gray-500 focus:border-cyan-500 focus:outline-none"
					/>

					<!-- Assign / Clear buttons -->
					<div class="flex gap-1">
						<button
							onclick={() => assignTeam(idx)}
							class="flex-1 rounded bg-cyan-700 px-2 py-1 text-xs font-semibold hover:bg-cyan-600"
						>
							Assign
						</button>
						<button
							onclick={() => clearTeam(idx)}
							class="rounded bg-gray-700 px-2 py-1 text-xs hover:bg-gray-600"
						>
							Clear
						</button>
					</div>

					<!-- Status indicators: DS, RIO, Radio, Robot -->
					{#if s}
						<div class="grid grid-cols-2 gap-x-2 gap-y-1 text-xs">
							{#each [
								{ label: 'DS', ok: s.dsLinked },
								{ label: 'RIO', ok: s.rioLinked },
								{ label: 'Radio', ok: s.radioLinked },
								{ label: 'Robot', ok: s.robotLinked }
							] as item}
								<div class="flex items-center gap-1">
									<span class="h-2 w-2 rounded-full {item.ok ? 'bg-green-500' : 'bg-gray-600'}"></span>
									<span class="{item.ok ? 'text-gray-200' : 'text-gray-500'}">{item.label}</span>
								</div>
							{/each}
						</div>

						<!-- Battery voltage + round-trip time + missed packets -->
						<div class="flex justify-between text-xs text-gray-400">
							<span>
								{#if s.robotLinked}
									<span class="{s.battery < 11 ? 'text-yellow-400' : 'text-gray-300'}">
										{s.battery.toFixed(1)}V
									</span>
								{:else}
									—
								{/if}
							</span>
							<span>{s.dsLinked ? `${s.tripTimeMs}ms` : '—'}</span>
							{#if s.missedPackets > 0}
								<span class="text-yellow-500">{s.missedPackets} lost</span>
							{/if}
						</div>

						<!-- Warnings -->
						{#if s.wrongStation}
							<div class="rounded bg-yellow-800/70 px-2 py-0.5 text-center text-xs font-semibold text-yellow-300">
								Wrong Station
							</div>
						{/if}
						{#if s.bypassed}
							<div class="rounded bg-gray-700 px-2 py-0.5 text-center text-xs text-gray-400">
								Bypassed
							</div>
						{/if}

						<!-- E-Stop and A-Stop buttons -->
						<div class="mt-auto flex gap-1">
							<button
								onclick={() => fms.estopStation(idx)}
								disabled={s.estop}
								class="flex-1 rounded px-2 py-1 text-xs font-bold
								{s.estop
									? 'cursor-not-allowed bg-red-900 text-red-400'
									: 'bg-red-800 text-white hover:bg-red-700'}"
							>
								{s.estop ? 'E-STOPPED' : 'E-STOP'}
							</button>
							<button
								onclick={() => fms.astopStation(idx)}
								disabled={s.astop}
								class="flex-1 rounded px-2 py-1 text-xs font-bold
								{s.astop
									? 'cursor-not-allowed bg-orange-900 text-orange-400'
									: 'bg-orange-800 text-white hover:bg-orange-700'}"
							>
								{s.astop ? 'A-STOPPED' : 'A-STOP'}
							</button>
						</div>
					{:else}
						<!-- No state received yet (still connecting) -->
						<div class="flex-1 py-4 text-center text-xs text-gray-600">No data</div>
					{/if}
				</div>
			{/each}
		</div>

		<!-- ===== WIFI DIAGNOSTICS TABLE ===== -->
		{#if matchState}
			<div class="mt-4 rounded-lg border border-gray-700 bg-gray-800 p-3">
				<h2 class="mb-2 text-xs font-semibold uppercase tracking-wider text-gray-400">
					Wifi Diagnostics
				</h2>
				<table class="w-full text-xs">
					<thead>
						<tr class="text-left text-gray-500">
							<th class="pb-1 pr-4">Station</th>
							<th class="pb-1 pr-4">Team</th>
							<th class="pb-1 pr-4">Radio</th>
							<th class="pb-1 pr-4">Rx Mbps</th>
							<th class="pb-1 pr-4">Tx Mbps</th>
							<th class="pb-1 pr-4">SNR</th>
							<th class="pb-1 pr-4">BW Used</th>
							<th class="pb-1">Quality</th>
						</tr>
					</thead>
					<tbody>
						{#each matchState.stations as s, i}
							{@const label = i < 3 ? `Red ${i + 1}` : `Blue ${i - 2}`}
							<tr class="border-t border-gray-700">
								<td class="py-0.5 pr-4 {i < 3 ? 'text-red-400' : 'text-blue-400'}">{label}</td>
								<td class="py-0.5 pr-4 text-gray-300">{s.teamNumber || '—'}</td>
								<td class="py-0.5 pr-4">
									<span
										class="inline-block h-2 w-2 rounded-full
										{s.wifi?.radioLinked ? 'bg-green-500' : 'bg-gray-600'}"
									></span>
								</td>
								<td class="py-0.5 pr-4 text-gray-300">{s.wifi?.rxRateMbps.toFixed(1) ?? '—'}</td>
								<td class="py-0.5 pr-4 text-gray-300">{s.wifi?.txRateMbps.toFixed(1) ?? '—'}</td>
								<td class="py-0.5 pr-4 text-gray-300">{s.wifi?.snr ?? '—'}</td>
								<td class="py-0.5 pr-4 text-gray-300">{s.wifi?.bandwidthMbps.toFixed(2) ?? '—'}</td>
								<td class="py-0.5 text-gray-300">{s.wifi?.connectionQuality ?? '—'}</td>
							</tr>
						{/each}
					</tbody>
				</table>
			</div>
		{/if}
	</main>

	<!-- Footer with link to audience overlay -->
	<footer class="border-t border-gray-700 px-4 py-2 text-center text-xs text-gray-600">
		PossumFMS · FRC Team 2718 ·
		<a href="/audience" class="text-cyan-700 hover:text-cyan-500">Audience Overlay →</a>
	</footer>
</div>
