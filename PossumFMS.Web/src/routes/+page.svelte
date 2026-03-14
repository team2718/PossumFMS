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
		const total = Math.ceil(secs);
		const m = Math.floor(total / 60);
		const s = total % 60;
		return `${m}:${s.toString().padStart(2, '0')}`;
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
	const blueStations = $derived(matchState?.stations.slice(3, 6) ?? []);
	const redStations = $derived(
		matchState ? [matchState.stations[2], matchState.stations[1], matchState.stations[0]] : []
	);

	const blueInputIndices = [3, 4, 5];
	const redInputIndices = [2, 1, 0];

	// A station is ready if it is bypassed, OR if both DS and robot are linked — and it has no active e-stop.
	const blueReady = $derived(
		blueStations.every((s) => (s.bypassed || (s.dsLinked && s.robotLinked)) && !s.estop)
	);
	const redReady = $derived(
		redStations.every((s) => (s.bypassed || (s.dsLinked && s.robotLinked)) && !s.estop)
	);

	let activeTab = $state('status');
</script>

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
						<div
							class="grid grid-cols-[72px_1fr_52px_46px_56px_56px_64px_100px] items-center gap-1 px-1 py-1 font-bold text-slate-600"
						>
							<div>Station</div>
							<div>Team</div>
							<div>Bypass</div>
							<div>WPA</div>
							<div>DS</div>
							<div>E-Stop</div>
							<div>A-Stop</div>
							<div>Robot</div>
						</div>
						{#each blueStations as s, i}
							{@const idx = blueInputIndices[i]}
							<div
								class="mt-1 grid grid-cols-[72px_1fr_52px_46px_56px_56px_64px_100px] items-center gap-1 rounded border border-blue-200 bg-white/75 px-1.5 py-1.5"
							>
								<div class="text-center font-bold text-blue-900">Station {i + 1}</div>
								<div class="flex items-center gap-1">
									<input
										type="number"
										placeholder="Team"
										bind:value={inputs[idx].team}
										onkeydown={(e) => e.key === 'Enter' && assignTeam(idx)}
										class="h-7 w-full rounded border border-slate-300 bg-white px-2 text-xs"
									/>
									<button
										onclick={() => assignTeam(idx)}
										class="h-7 rounded bg-emerald-700 px-2 text-[10px] font-bold text-white"
										>Set</button
									>
									<button
										onclick={() => clearTeam(idx)}
										class="h-7 rounded bg-slate-500 px-2 text-[10px] font-bold text-white"
										>Clr</button
									>
								</div>
								<input
									type="checkbox"
									checked={s.bypassed}
									onchange={() => fms.bypassStation(idx, !s.bypassed)}
									class="mx-auto h-4 w-4 cursor-pointer"
								/>
								<button class="mx-auto h-7 w-7 rounded bg-slate-400 font-bold text-white">?</button>
								<div
									class="mx-auto flex h-7 w-10 items-center justify-center rounded font-bold text-white {s.dsLinked
										? 'bg-emerald-600'
										: 'bg-rose-700'}"
								>
									{s.dsLinked ? 'OK' : 'X'}
								</div>
								<button
									onclick={() => fms.estopStation(idx)}
									class="mx-auto h-7 w-12 rounded text-[10px] font-bold text-white {s.estop
										? 'bg-rose-900'
										: 'bg-rose-700 hover:bg-rose-600'}">{s.estop ? 'LOCK' : 'E-Stop'}</button
								>
								<button
									onclick={() => fms.astopStation(idx)}
									class="mx-auto h-7 w-14 rounded text-[10px] font-bold text-white {s.astop
										? 'bg-orange-900'
										: 'bg-emerald-700 hover:bg-emerald-600'}">{s.astop ? 'LOCK' : 'A-Stop'}</button
								>
								<div
									class="mx-auto flex h-7 w-full items-center justify-center rounded bg-slate-800 text-white"
								>
									{s.robotLinked ? 'OK' : 'X'}
								</div>
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
						<div
							class="grid grid-cols-[72px_1fr_52px_46px_56px_56px_64px_100px] items-center gap-1 px-1 py-1 font-bold text-slate-600"
						>
							<div>Station</div>
							<div>Team</div>
							<div>Bypass</div>
							<div>WPA</div>
							<div>DS</div>
							<div>E-Stop</div>
							<div>A-Stop</div>
							<div>Robot</div>
						</div>
						{#each redStations as s, i}
							{@const idx = redInputIndices[i]}
							<div
								class="mt-1 grid grid-cols-[72px_1fr_52px_46px_56px_56px_64px_100px] items-center gap-1 rounded border border-rose-200 bg-white/75 px-1.5 py-1.5"
							>
								<div class="text-center font-bold text-rose-900">Station {3 - i}</div>
								<div class="flex items-center gap-1">
									<input
										type="number"
										placeholder="Team"
										bind:value={inputs[idx].team}
										onkeydown={(e) => e.key === 'Enter' && assignTeam(idx)}
										class="h-7 w-full rounded border border-slate-300 bg-white px-2 text-xs"
									/>
									<button
										onclick={() => assignTeam(idx)}
										class="h-7 rounded bg-emerald-700 px-2 text-[10px] font-bold text-white"
										>Set</button
									>
									<button
										onclick={() => clearTeam(idx)}
										class="h-7 rounded bg-slate-500 px-2 text-[10px] font-bold text-white"
										>Clr</button
									>
								</div>
								<input
									type="checkbox"
									checked={s.bypassed}
									onchange={() => fms.bypassStation(idx, !s.bypassed)}
									class="mx-auto h-4 w-4 cursor-pointer"
								/>
								<button class="mx-auto h-7 w-7 rounded bg-slate-400 font-bold text-white">?</button>
								<div
									class="mx-auto flex h-7 w-10 items-center justify-center rounded font-bold text-white {s.dsLinked
										? 'bg-emerald-600'
										: 'bg-rose-700'}"
								>
									{s.dsLinked ? 'OK' : 'X'}
								</div>
								<button
									onclick={() => fms.estopStation(idx)}
									class="mx-auto h-7 w-12 rounded text-[10px] font-bold text-white {s.estop
										? 'bg-rose-900'
										: 'bg-rose-700 hover:bg-rose-600'}">{s.estop ? 'LOCK' : 'E-Stop'}</button
								>
								<button
									onclick={() => fms.astopStation(idx)}
									class="mx-auto h-7 w-14 rounded text-[10px] font-bold text-white {s.astop
										? 'bg-orange-900'
										: 'bg-emerald-700 hover:bg-emerald-600'}">{s.astop ? 'LOCK' : 'A-Stop'}</button
								>
								<div
									class="mx-auto flex h-7 w-full items-center justify-center rounded bg-slate-800 text-white"
								>
									{s.robotLinked ? 'OK' : 'X'}
								</div>
							</div>
						{/each}
					</div>
				</div>
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
					disabled={phase !== 'PreMatch' || !blueReady || !redReady}
					class="h-14 min-w-44 rounded px-5 text-sm font-black disabled:cursor-not-allowed {phase ===
						'PreMatch' &&
					blueReady &&
					redReady
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
					disabled={phase !== 'PostMatch' && phase !== 'PreMatch'}
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
						activeTab = 'options';
					}}
					class="mr-6 pb-2 {activeTab === 'options'
						? 'border-b-2 border-blue-600 font-semibold text-blue-700'
						: 'text-slate-500 hover:text-slate-800'}">Options</button
				>
			</div>

			{#if activeTab === 'score'}
				<div class="flex min-h-48 items-center justify-center p-6 text-center text-slate-400">
					<div>
						<div class="text-sm font-semibold">Scoring not yet implemented</div>
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
							<div
								class="mb-2 rounded border p-2 text-xs {s.estop
									? 'border-rose-400 bg-rose-50'
									: 'border-blue-200 bg-white'}"
							>
								<div class="mb-1.5 flex items-center justify-between">
									<span class="font-black text-blue-900"
										>Station {i + 1} — Team {s.teamNumber || '—'}</span
									>
									<div class="flex gap-1">
										{#if s.estop}<span
												class="rounded bg-rose-700 px-1.5 py-0.5 text-[10px] font-bold text-white"
												>E-STOP</span
											>{/if}
										{#if s.astop}<span
												class="rounded bg-orange-600 px-1.5 py-0.5 text-[10px] font-bold text-white"
												>A-STOP</span
											>{/if}
										{#if s.bypassed}<span
												class="rounded bg-slate-500 px-1.5 py-0.5 text-[10px] font-bold text-white"
												>BYPASS</span
											>{/if}
										{#if s.wrongStation}<span
												class="rounded bg-yellow-600 px-1.5 py-0.5 text-[10px] font-bold text-white"
												>WRONG STN</span
											>{/if}
									</div>
								</div>
								<div class="grid grid-cols-4 gap-1">
									{#each [{ label: 'DS', ok: s.dsLinked }, { label: 'Radio', ok: s.radioLinked }, { label: 'RIO', ok: s.rioLinked }, { label: 'Robot', ok: s.robotLinked }] as item}
										<div
											class="flex flex-col items-center gap-0.5 rounded py-1 {item.ok
												? 'bg-emerald-100'
												: 'bg-slate-100'}"
										>
											<span
												class="h-2 w-2 rounded-full {item.ok ? 'bg-emerald-500' : 'bg-slate-400'}"
											></span>
											<span class="font-semibold {item.ok ? 'text-emerald-800' : 'text-slate-500'}"
												>{item.label}</span
											>
										</div>
									{/each}
								</div>
								<div class="mt-1.5 grid grid-cols-3 gap-1 text-slate-600">
									<div class="rounded bg-slate-50 px-1.5 py-1">
										<div class="text-[10px] text-slate-400">Battery</div>
										<div
											class="font-semibold {s.battery < 11 && s.robotLinked
												? 'text-yellow-600'
												: ''}"
										>
											{s.robotLinked ? s.battery.toFixed(1) + 'V' : '—'}
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
								</div>
								{#if s.wifi}
									<div class="mt-1 grid grid-cols-4 gap-1 text-slate-600">
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
									</div>
								{/if}
							</div>
						{/each}
					</div>

					<!-- Red Alliance -->
					<div class="bg-rose-50 p-3">
						<div class="mb-2 text-xs font-bold tracking-wider text-rose-800 uppercase">
							Red Alliance
						</div>
						{#each redStations as s, i}
							<div
								class="mb-2 rounded border p-2 text-xs {s.estop
									? 'border-rose-400 bg-rose-100'
									: 'border-rose-200 bg-white'}"
							>
								<div class="mb-1.5 flex items-center justify-between">
									<span class="font-black text-rose-900"
										>Station {3 - i} — Team {s.teamNumber || '—'}</span
									>
									<div class="flex gap-1">
										{#if s.estop}<span
												class="rounded bg-rose-700 px-1.5 py-0.5 text-[10px] font-bold text-white"
												>E-STOP</span
											>{/if}
										{#if s.astop}<span
												class="rounded bg-orange-600 px-1.5 py-0.5 text-[10px] font-bold text-white"
												>A-STOP</span
											>{/if}
										{#if s.bypassed}<span
												class="rounded bg-slate-500 px-1.5 py-0.5 text-[10px] font-bold text-white"
												>BYPASS</span
											>{/if}
										{#if s.wrongStation}<span
												class="rounded bg-yellow-600 px-1.5 py-0.5 text-[10px] font-bold text-white"
												>WRONG STN</span
											>{/if}
									</div>
								</div>
								<div class="grid grid-cols-4 gap-1">
									{#each [{ label: 'DS', ok: s.dsLinked }, { label: 'Radio', ok: s.radioLinked }, { label: 'RIO', ok: s.rioLinked }, { label: 'Robot', ok: s.robotLinked }] as item}
										<div
											class="flex flex-col items-center gap-0.5 rounded py-1 {item.ok
												? 'bg-emerald-100'
												: 'bg-slate-100'}"
										>
											<span
												class="h-2 w-2 rounded-full {item.ok ? 'bg-emerald-500' : 'bg-slate-400'}"
											></span>
											<span class="font-semibold {item.ok ? 'text-emerald-800' : 'text-slate-500'}"
												>{item.label}</span
											>
										</div>
									{/each}
								</div>
								<div class="mt-1.5 grid grid-cols-3 gap-1 text-slate-600">
									<div class="rounded bg-slate-50 px-1.5 py-1">
										<div class="text-[10px] text-slate-400">Battery</div>
										<div
											class="font-semibold {s.battery < 11 && s.robotLinked
												? 'text-yellow-600'
												: ''}"
										>
											{s.robotLinked ? s.battery.toFixed(1) + 'V' : '—'}
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
								</div>
								{#if s.wifi}
									<div class="mt-1 grid grid-cols-4 gap-1 text-slate-600">
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
									</div>
								{/if}
							</div>
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
					<div class="mt-4 mb-2 text-xs font-bold tracking-wider text-slate-500 uppercase">
						Access Point
					</div>
					{#if matchState}
						<div class="flex items-center gap-2 text-sm">
							<span class="text-slate-600">Status:</span>
							<span
								class="rounded px-2 py-0.5 text-xs font-bold {matchState.accessPoint.status ===
								'ACTIVE'
									? 'bg-emerald-100 text-emerald-800'
									: matchState.accessPoint.status === 'CONFIGURING'
										? 'bg-yellow-100 text-yellow-800'
										: 'bg-rose-100 text-rose-800'}">{matchState.accessPoint.status}</span
							>
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
			<span>0.4 ms</span>
			<span class="absolute left-1/2 -translate-x-1/2">POSM - Team 2718</span>
			<a href="/audience" class="absolute right-0 text-blue-700 hover:text-blue-500"
				>Audience Overlay</a
			>
		</div>
	</footer>
</div>
