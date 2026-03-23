<script lang="ts">
	import { fms } from '$lib/fms.svelte';
	import Navbar from '$lib/Navbar.svelte';
	import type { FieldDeviceDiagnostics, Station } from '$lib/fms.svelte';

	$effect(() => {
		fms.connect();
	});

	const matchState = $derived(fms.matchState);
	const phase = $derived(matchState?.phase ?? 'Disconnected');
	const blueStations = $derived(matchState?.stations.slice(3, 6) ?? []);
	const redStations = $derived(
		matchState ? [matchState.stations[2], matchState.stations[1], matchState.stations[0]] : []
	);
	const fieldDevices = $derived(matchState?.fieldDevices ?? []);

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
</script>

{#snippet stationCard(s: Station, stationNumber: number, alliance: 'blue' | 'red')}
	<div
		class="mb-2 rounded border p-2 text-xs {s.estop
			? 'alliance-red-border-soft alliance-red-bg'
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
				{#if s.estop}<span class="rounded bg-rose-700 px-1.5 py-0.5 text-[10px] font-bold text-white"
						>E-STOP</span
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
		<div class="mt-1 flex flex-wrap items-center gap-1">
			<span
				class="rounded px-1.5 py-0.5 text-[10px] font-bold text-white {s.dsLinked
					? 'bg-emerald-600'
					: 'bg-slate-400'}">DS</span
			>
			<span
				class="rounded px-1.5 py-0.5 text-[10px] font-bold text-white {s.robotLinked
					? 'bg-emerald-600'
					: 'bg-slate-400'}">Robot</span
			>
			<span
				class="rounded px-1.5 py-0.5 text-[10px] font-bold text-white {s.radioLinked
					? 'bg-emerald-600'
					: 'bg-slate-400'}">Radio</span
			>
			<span
				class="rounded px-1.5 py-0.5 text-[10px] font-bold text-white {s.rioLinked
					? 'bg-emerald-600'
					: 'bg-slate-400'}">RIO</span
			>
			<span
				class="ml-1 rounded px-1.5 py-0.5 text-[10px] font-bold {s.isReady
					? 'bg-emerald-100 text-emerald-800'
					: 'bg-slate-100 text-slate-600'}">Ready</span
			>
			<span
				class="rounded px-1.5 py-0.5 text-[10px] font-bold {s.isReadyInMatch
					? 'bg-emerald-100 text-emerald-800'
					: 'bg-slate-100 text-slate-600'}">In-Match</span
			>
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
					<div
						class="font-semibold {s.wifi.radioLinked ? 'text-emerald-700' : 'text-rose-700'}"
					>
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

<div class="app-neutral-bg min-h-screen text-slate-900">
	<Navbar />

	<main class="mx-auto flex max-w-[1700px] flex-col gap-3 px-3 py-3">
		<!-- Phase banner -->
		<div class="flex items-center gap-3 rounded border border-slate-200 bg-white px-4 py-2 shadow-sm">
			<span class="text-sm font-bold text-slate-700">FTA Status</span>
			<span class="rounded-full border border-slate-300 bg-slate-50 px-3 py-0.5 text-xs font-semibold text-slate-600">
				{phase}
			</span>
			{#if matchState}
				<span class="text-xs text-slate-500">{matchState.matchType} #{matchState.matchNumber}</span>
			{/if}
		</div>

		{#if !matchState}
			<div class="rounded border border-slate-200 bg-white px-4 py-8 text-center text-sm text-slate-500 shadow-sm">
				Waiting for FMS connection…
			</div>
		{:else}
			<!-- Station status cards -->
			<div class="overflow-hidden rounded border border-slate-300 bg-white shadow-sm">
				<div class="grid grid-cols-1 lg:grid-cols-2">
					<div class="alliance-blue-bg-soft border-b border-slate-200 p-3 lg:border-r lg:border-b-0">
						<div class="alliance-blue-text mb-2 text-xs font-bold tracking-wider uppercase">
							Blue Alliance
						</div>
						{#each blueStations as s, i}
							{@render stationCard(s, i + 1, 'blue')}
						{/each}
					</div>
					<div class="alliance-red-bg-soft p-3">
						<div class="alliance-red-text mb-2 text-xs font-bold tracking-wider uppercase">
							Red Alliance
						</div>
						{#each redStations as s, i}
							{@render stationCard(s, 3 - i, 'red')}
						{/each}
					</div>
				</div>
			</div>

			<!-- Field Devices -->
			<div class="rounded border border-slate-300 bg-white shadow-sm">
				<div class="border-b border-slate-200 px-4 py-2">
					<span class="text-sm font-bold text-slate-700">Field Devices</span>
					<span class="ml-2 text-xs text-slate-500">({fieldDevices.length} connected)</span>
				</div>
				<div class="p-3">
					{#if fieldDevices.length === 0}
						<div class="rounded border border-slate-200 bg-slate-50 px-3 py-6 text-center text-sm text-slate-500">
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
			</div>
		{/if}
	</main>
</div>
