<script lang="ts">
	import { formatLastRobotLink } from '$lib/utils/formatters';

	export interface StationData {
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
	}

	let {
		station,
		stationNumber,
		alliance
	} = $props<{
		station: StationData;
		stationNumber: number;
		alliance: 'blue' | 'red';
	}>();
</script>

<div
	class="mb-2 rounded border p-2 text-xs {station.estop
		? 'alliance-red-border-soft alliance-red-bg'
		: alliance === 'red'
			? 'alliance-red-border-soft bg-white'
			: 'alliance-blue-border-soft bg-white'}"
>
	<div class="mb-1.5 flex items-center justify-between">
		<span
			class="font-black {station.estop
				? 'text-white'
				: alliance === 'blue'
					? 'alliance-blue-text'
					: 'alliance-red-text'}"
		>
			Station {stationNumber} — Team {station.teamNumber || '—'}
		</span>
		<div class="flex gap-1">
			{#if station.estop}
				<span class="rounded bg-rose-700 px-1.5 py-0.5 text-[10px] font-bold text-white">E-STOP</span>
			{/if}
			{#if station.astop}
				<span class="rounded bg-orange-600 px-1.5 py-0.5 text-[10px] font-bold text-white">A-STOP</span>
			{/if}
			{#if station.bypassed}
				<span class="rounded bg-slate-500 px-1.5 py-0.5 text-[10px] font-bold text-white">BYPASS</span>
			{/if}
			{#if station.wrongStation}
				<span class="rounded bg-yellow-600 px-1.5 py-0.5 text-[10px] font-bold text-white">WRONG STN</span>
			{/if}
		</div>
	</div>

	{#if station.wrongStation}
		<div class="mb-1 rounded bg-yellow-50 px-1.5 py-1 text-[10px] font-semibold text-yellow-800">
			Expected station: {station.wrongStation}
		</div>
	{/if}

	<div class="mt-1 flex flex-wrap items-center gap-1">
		<span
			class="rounded px-1.5 py-0.5 text-[10px] font-bold text-white {station.dsLinked
				? 'bg-emerald-600'
				: 'bg-slate-400'}">DS</span
		>
		<span
			class="rounded px-1.5 py-0.5 text-[10px] font-bold text-white {station.robotLinked
				? 'bg-emerald-600'
				: 'bg-slate-400'}">Robot</span
		>
		<span
			class="rounded px-1.5 py-0.5 text-[10px] font-bold text-white {station.radioLinked
				? 'bg-emerald-600'
				: 'bg-slate-400'}">Radio</span
		>
		<span
			class="rounded px-1.5 py-0.5 text-[10px] font-bold text-white {station.rioLinked
				? 'bg-emerald-600'
				: 'bg-slate-400'}">RIO</span
		>
		{#if station.isReady}
			<span
				class="ml-1 rounded px-1.5 py-0.5 text-[10px] font-bold bg-emerald-100 text-emerald-800"
			>
				Ready
			</span>
		{/if}
	</div>

	<div class="mt-1.5 grid grid-cols-4 gap-1 text-slate-600">
		<div class="rounded bg-slate-50 px-1.5 py-1">
			<div class="text-[10px] text-slate-400">Battery</div>
			<div class="font-semibold {station.battery < 11 && station.robotLinked ? 'text-yellow-600' : ''}">
				{station.robotLinked ? station.battery.toFixed(2) + 'V' : '—'}
			</div>
		</div>
		<div class="rounded bg-slate-50 px-1.5 py-1">
			<div class="text-[10px] text-slate-400">Trip</div>
			<div class="font-semibold">{station.dsLinked ? station.tripTimeMs + ' ms' : '—'}</div>
		</div>
		<div class="rounded bg-slate-50 px-1.5 py-1">
			<div class="text-[10px] text-slate-400">Lost Pkts</div>
			<div class="font-semibold {station.missedPackets > 0 ? 'text-yellow-600' : ''}">
				{station.missedPackets}
			</div>
		</div>
		<div class="rounded bg-slate-50 px-1.5 py-1">
			<div class="text-[10px] text-slate-400">Last Robot Link</div>
			<div class="font-semibold {station.secondsSinceLastRobotLink > 3 ? 'text-yellow-600' : ''}">
				{formatLastRobotLink(station.secondsSinceLastRobotLink, station.robotLinked)}
			</div>
		</div>
	</div>

	{#if station.wifi}
		<div class="mt-1 grid grid-cols-5 gap-1 text-slate-600">
			<div class="rounded bg-slate-50 px-1.5 py-1">
				<div class="text-[10px] text-slate-400">SNR</div>
				<div class="font-semibold">{station.wifi.snr}</div>
			</div>
			<div class="rounded bg-slate-50 px-1.5 py-1">
				<div class="text-[10px] text-slate-400">Rx Mbps</div>
				<div class="font-semibold">{station.wifi.rxRateMbps.toFixed(1)}</div>
			</div>
			<div class="rounded bg-slate-50 px-1.5 py-1">
				<div class="text-[10px] text-slate-400">Tx Mbps</div>
				<div class="font-semibold">{station.wifi.txRateMbps.toFixed(1)}</div>
			</div>
			<div class="rounded bg-slate-50 px-1.5 py-1">
				<div class="text-[10px] text-slate-400">BW Mbps</div>
				<div class="font-semibold">{station.wifi.bandwidthMbps.toFixed(2)}</div>
			</div>
			<div class="rounded bg-slate-50 px-1.5 py-1">
				<div class="text-[10px] text-slate-400">WiFi Link</div>
				<div class="font-semibold {station.wifi.radioLinked ? 'text-emerald-700' : 'text-rose-700'}">
					{station.wifi.radioLinked ? 'Linked' : 'Not Linked'}
				</div>
			</div>
		</div>
	{/if}
</div>

